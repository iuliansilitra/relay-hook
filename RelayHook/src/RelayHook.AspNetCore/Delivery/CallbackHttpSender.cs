using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using RelayHook.Authentication;
using RelayHook.AspNetCore.Diagnostics;
using RelayHook.Core.Abstractions;
using RelayHook.Core.Callbacks;
using RelayHook.Core.Configuration;

namespace RelayHook.AspNetCore.Delivery;

internal sealed class CallbackHttpSender(
    IHttpClientFactory httpClientFactory,
    CallbackAuthenticatorRegistry authenticatorRegistry,
    IServiceProvider serviceProvider,
    ICallbackSecretProvider secretProvider,
    ICallbackUrlPolicy urlPolicy,
    RelayHookOptions options,
    TimeProvider timeProvider,
    RelayHookDiagnostics diagnostics)
{
    private const string HttpClientName = "RelayHook.Delivery";

    public async Task<CallbackDeliveryOutcome> SendAsync(
        CallbackJob job,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var authenticator = authenticatorRegistry.Resolve(
                job.Destination.AuthenticationType,
                serviceProvider);
            var authenticationContext = new CallbackAuthenticationContext(
                job.Id.ToString("N"),
                job.Destination.AuthenticationConfiguration,
                secretProvider,
                timeProvider);

            for (var sendNumber = 0; sendNumber < 2; sendNumber++)
            {
                using var request = CreateRequest(job);
                await authenticator.AuthenticateAsync(request, authenticationContext, cancellationToken)
                    .ConfigureAwait(false);

                using var timeoutCts = new CancellationTokenSource(
                    TimeSpan.FromSeconds(job.Destination.TimeoutSeconds),
                    timeProvider);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeoutCts.Token);

                HttpResponseMessage response;
                try
                {
                    using var activity = StartHttpActivity(job);
                    response = await httpClientFactory.CreateClient(HttpClientName)
                        .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token)
                        .ConfigureAwait(false);
                    activity?.SetTag("http.response.status_code", (int)response.StatusCode);
                }
                catch (OperationCanceledException) when (
                    timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    return CallbackDeliveryOutcome.Failure(
                        CallbackFailureType.Timeout,
                        null,
                        "Callback request timed out.",
                        null,
                        Stopwatch.GetElapsedTime(started));
                }
                catch (HttpRequestException)
                {
                    return CallbackDeliveryOutcome.Failure(
                        CallbackFailureType.Network,
                        null,
                        "Callback HTTP transport failed.",
                        null,
                        Stopwatch.GetElapsedTime(started));
                }

                using (response)
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized &&
                        sendNumber == 0 &&
                        authenticator is IRefreshableCallbackAuthenticator refreshable)
                    {
                        await refreshable.InvalidateAsync(request, authenticationContext, cancellationToken)
                            .ConfigureAwait(false);
                        continue;
                    }

                    string? responseBody;
                    try
                    {
                        responseBody = await ReadResponseBodyAsync(
                            response.Content,
                            options.MaxPersistedResponseBytes,
                            linkedCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (
                        timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                    {
                        return CallbackDeliveryOutcome.Failure(
                            CallbackFailureType.Timeout,
                            null,
                            "Callback response timed out.",
                            null,
                            Stopwatch.GetElapsedTime(started));
                    }
                    catch (Exception exception) when (exception is HttpRequestException or IOException)
                    {
                        return CallbackDeliveryOutcome.Failure(
                            CallbackFailureType.Network,
                            null,
                            "Callback response transport failed.",
                            null,
                            Stopwatch.GetElapsedTime(started));
                    }
                    var duration = Stopwatch.GetElapsedTime(started);
                    if (response.IsSuccessStatusCode)
                    {
                        return CallbackDeliveryOutcome.Success(
                            (int)response.StatusCode,
                            responseBody,
                            duration);
                    }

                    return CallbackDeliveryOutcome.Failure(
                        response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                            ? CallbackFailureType.Authentication
                            : CallbackFailureType.Http,
                        (int)response.StatusCode,
                        $"Callback endpoint returned HTTP {(int)response.StatusCode}.",
                        responseBody,
                        duration,
                        ParseRetryAfter(response, timeProvider.GetUtcNow()));
                }
            }

            throw new UnreachableException();
        }
        catch (CallbackAuthenticationException)
        {
            return CallbackDeliveryOutcome.Failure(
                CallbackFailureType.Authentication,
                null,
                "Callback authentication failed.",
                null,
                Stopwatch.GetElapsedTime(started));
        }
        catch (CallbackConfigurationException)
        {
            return CallbackDeliveryOutcome.Failure(
                CallbackFailureType.Configuration,
                null,
                "Callback configuration is invalid.",
                null,
                Stopwatch.GetElapsedTime(started));
        }
    }

    private static TimeSpan? ParseRetryAfter(HttpResponseMessage response, DateTimeOffset now)
    {
        if (!response.Headers.TryGetValues("Retry-After", out var values))
        {
            return null;
        }

        var value = values.FirstOrDefault();
        if (value is null || !RetryConditionHeaderValue.TryParse(value, out var parsed))
        {
            return null;
        }

        if (parsed.Delta is { } delta)
        {
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }

        if (parsed.Date is { } date)
        {
            var delay = date - now;
            return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
        }

        return null;
    }

    private HttpRequestMessage CreateRequest(CallbackJob job)
    {
        var destination = new Uri(job.Destination.Url, UriKind.Absolute);
        urlPolicy.Validate(destination);

        var request = new HttpRequestMessage(new HttpMethod(job.Destination.Method), destination)
        {
            Content = new StringContent(job.Payload, Encoding.UTF8)
        };

        if (!MediaTypeHeaderValue.TryParse(job.ContentType, out var contentType))
        {
            request.Dispose();
            throw new CallbackConfigurationException("Callback content type is invalid.");
        }

        request.Content.Headers.ContentType = contentType;
        request.Headers.UserAgent.ParseAdd("RelayHook/1.0");
        request.Headers.Add("X-Callback-Id", job.Id.ToString("N"));
        request.Headers.Add("X-Idempotency-Key", job.IdempotencyKey);
        request.Headers.Add("X-Callback-Event", job.EventName);
        if (!string.IsNullOrWhiteSpace(job.CorrelationId))
        {
            request.Headers.Add("X-Correlation-Id", job.CorrelationId);
        }

        try
        {
            CallbackHeaderPolicy.Apply(request, job.Destination.Headers);
            return request;
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }

    private Activity? StartHttpActivity(CallbackJob job)
    {
        var activity = diagnostics.ActivitySource.StartActivity("callback.http.send", ActivityKind.Client);
        activity?.SetTag("callback.id", job.Id);
        activity?.SetTag("callback.event", job.EventName);
        activity?.SetTag("server.address", new Uri(job.Destination.Url).Host);
        return activity;
    }

    private static async Task<string?> ReadResponseBodyAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (maximumBytes == 0)
        {
            return null;
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[maximumBytes];
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            offset += read;
        }

        return Encoding.UTF8.GetString(buffer, 0, offset);
    }
}
