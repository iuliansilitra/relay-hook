using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RelayHook.AspNetCore.Diagnostics;
using RelayHook.Core.Abstractions;
using RelayHook.Core.Callbacks;
using RelayHook.Core.Configuration;
using RelayHook.Core.Retry;

namespace RelayHook.AspNetCore.Delivery;

internal sealed class CallbackDeliveryProcessor(
    ICallbackStorage storage,
    CallbackHttpSender sender,
    IRetryPolicy retryPolicy,
    RelayHookOptions options,
    TimeProvider timeProvider,
    RelayHookDiagnostics diagnostics,
    ILogger<CallbackDeliveryProcessor> logger)
{
    public async Task ProcessAsync(
        CallbackJob job,
        string workerId,
        CancellationToken stoppingToken)
    {
        var processingStarted = Stopwatch.GetTimestamp();
        var attemptNumber = job.AttemptCount + 1;
        var attemptStartedAt = timeProvider.GetUtcNow();
        var attemptId = await storage.StartAttemptAsync(
            job.Id,
            workerId,
            attemptNumber,
            attemptStartedAt,
            stoppingToken).ConfigureAwait(false);
        if (attemptId is null)
        {
            RelayHookLog.LeaseLostBeforeAttempt(logger, job.Id, workerId);
            return;
        }

        using var processingCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        using var renewalCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var renewalTask = RenewLeaseUntilCancelledAsync(
            job.Id,
            workerId,
            processingCts,
            renewalCts.Token);

        try
        {
            using var activity = diagnostics.ActivitySource.StartActivity("callback.process", ActivityKind.Consumer);
            activity?.SetTag("callback.id", job.Id);
            activity?.SetTag("callback.event", job.EventName);
            activity?.SetTag("callback.attempt", attemptNumber);

            CallbackDeliveryOutcome outcome;
            try
            {
                outcome = await sender.SendAsync(job, processingCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                RelayHookLog.ProcessingStoppedAfterLeaseLoss(logger, job.Id, workerId);
                return;
            }
            catch (Exception exception)
            {
                RelayHookLog.UnexpectedDeliveryFailure(
                    logger,
                    job.Id,
                    workerId,
                    exception.GetType().FullName ?? exception.GetType().Name);
                outcome = CallbackDeliveryOutcome.Failure(
                    CallbackFailureType.Unknown,
                    null,
                    "Unexpected callback delivery failure.",
                    null,
                    Stopwatch.GetElapsedTime(processingStarted));
            }

            var completedAt = timeProvider.GetUtcNow();
            var completedAttemptCount = job.CompletedAttemptCount + 1;
            var decision = retryPolicy.Decide(outcome, completedAttemptCount, job.MaxAttempts, completedAt);
            var nextStatus = decision.Action switch
            {
                RetryAction.Complete => CallbackStatus.Completed,
                RetryAction.Retry => CallbackStatus.Pending,
                RetryAction.Fail => CallbackStatus.Failed,
                _ => throw new InvalidOperationException("Retry policy returned an unknown action.")
            };
            var error = outcome.ErrorMessage is null
                ? null
                : outcome.ErrorMessage[..Math.Min(outcome.ErrorMessage.Length, options.MaxPersistedErrorCharacters)];
            var completion = new CallbackAttemptCompletion(
                job.Id,
                attemptId.Value,
                workerId,
                attemptNumber,
                completedAttemptCount,
                completedAt,
                outcome with
                {
                    ResponseBody = Utf8Truncator.Truncate(
                        outcome.ResponseBody,
                        options.MaxPersistedResponseBytes),
                    ErrorMessage = error
                },
                nextStatus,
                decision.NextAttemptAt,
                error);

            var completed = await storage.CompleteAttemptAsync(completion, processingCts.Token)
                .ConfigureAwait(false);
            if (!completed)
            {
                RelayHookLog.CompletionRejected(logger, job.Id, workerId);
                return;
            }

            diagnostics.RecordOutcome(
                nextStatus,
                Stopwatch.GetElapsedTime(processingStarted),
                outcome.Duration);
            RelayHookLog.AttemptCompleted(
                logger,
                job.Id,
                job.Destination.ClientName,
                job.EndpointId,
                job.Destination.EndpointName,
                job.EventName,
                job.CorrelationId,
                attemptNumber,
                nextStatus,
                outcome.FailureType,
                outcome.HttpStatusCode,
                outcome.Duration.TotalMilliseconds,
                workerId);
        }
        finally
        {
            renewalCts.Cancel();
            try
            {
                await renewalTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task RenewLeaseUntilCancelledAsync(
        Guid jobId,
        string workerId,
        CancellationTokenSource processingCts,
        CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromTicks(Math.Max(1, options.LeaseDuration.Ticks / 3));
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(interval, timeProvider, cancellationToken).ConfigureAwait(false);
            try
            {
                var renewed = await storage.RenewLeaseAsync(
                    jobId,
                    workerId,
                    options.LeaseDuration,
                    cancellationToken).ConfigureAwait(false);
                if (!renewed)
                {
                    processingCts.Cancel();
                    return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                RelayHookLog.LeaseRenewalFailed(
                    logger,
                    jobId,
                    workerId,
                    exception.GetType().FullName ?? exception.GetType().Name);
                processingCts.Cancel();
                return;
            }
        }
    }
}
