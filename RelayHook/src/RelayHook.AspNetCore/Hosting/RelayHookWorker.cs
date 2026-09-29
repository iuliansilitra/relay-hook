using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RelayHook.AspNetCore.Configuration;
using RelayHook.AspNetCore.Delivery;
using RelayHook.AspNetCore.Diagnostics;
using RelayHook.Core.Abstractions;
using RelayHook.Core.Configuration;

namespace RelayHook.AspNetCore.Hosting;

internal sealed class RelayHookWorker(
    ICallbackStorage storage,
    CallbackDeliveryProcessor processor,
    RelayHookStartupValidator startupValidator,
    RelayHookOptions options,
    TimeProvider timeProvider,
    ILogger<RelayHookWorker> logger) : BackgroundService
{
    private readonly string _workerId =
        $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        startupValidator.Validate();
        await storage.InitializeAsync(stoppingToken).ConfigureAwait(false);
        var nextCleanup = timeProvider.GetUtcNow() + options.Retention.CleanupInterval;

        RelayHookLog.WorkerStarted(logger, _workerId);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var jobs = await storage.ClaimAsync(
                    _workerId,
                    Math.Min(options.BatchSize, options.MaxConcurrentCallbacks),
                    timeProvider.GetUtcNow(),
                    options.LeaseDuration,
                    stoppingToken).ConfigureAwait(false);

                if (jobs.Count > 0)
                {
                    await Parallel.ForEachAsync(
                        jobs,
                        new ParallelOptions
                        {
                            MaxDegreeOfParallelism = options.MaxConcurrentCallbacks,
                            CancellationToken = stoppingToken
                        },
                        (job, cancellationToken) =>
                            new ValueTask(processor.ProcessAsync(job, _workerId, cancellationToken)))
                        .ConfigureAwait(false);
                }
                else
                {
                    await Task.Delay(options.PollingInterval, timeProvider, stoppingToken).ConfigureAwait(false);
                }

                if (options.Retention.Enabled && timeProvider.GetUtcNow() >= nextCleanup)
                {
                    await RunCleanupAsync(stoppingToken).ConfigureAwait(false);
                    nextCleanup = timeProvider.GetUtcNow() + options.Retention.CleanupInterval;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                RelayHookLog.WorkerCycleFailed(
                    logger,
                    _workerId,
                    exception.GetType().FullName ?? exception.GetType().Name);
                await Task.Delay(options.WorkerFailureBackoff, timeProvider, stoppingToken).ConfigureAwait(false);
            }
        }

        RelayHookLog.WorkerStopped(logger, _workerId);
    }

    private async Task RunCleanupAsync(CancellationToken cancellationToken)
    {
        var deleted = await storage.CleanupAsync(
            timeProvider.GetUtcNow(),
            options.Retention,
            cancellationToken).ConfigureAwait(false);
        if (deleted > 0)
        {
            RelayHookLog.RetentionDeleted(logger, deleted);
        }
    }
}
