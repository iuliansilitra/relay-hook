using System.Data.Common;
using System.Text;
using RelayHook.Core.Abstractions;
using RelayHook.Core.Configuration;

namespace RelayHook.Core.Callbacks;

internal sealed class CallbackClient(
    ICallbackStorage storage,
    ICallbackDestinationResolver destinationResolver,
    ICallbackSerializer serializer,
    ICallbackUrlPolicy urlPolicy,
    RelayHookOptions options,
    TimeProvider timeProvider) : ICallbackClient, ICallbackTransactionalClient
{
    public Task<Guid> EnqueueAsync<TPayload>(
        string client,
        string eventName,
        TPayload payload,
        CancellationToken cancellationToken = default) =>
        EnqueueAsync(client, eventName, payload, new CallbackEnqueueOptions(), cancellationToken);

    public Task<Guid> EnqueueAsync<TPayload>(
        string client,
        string eventName,
        TPayload payload,
        CallbackEnqueueOptions options,
        CancellationToken cancellationToken = default) =>
        EnqueueCoreAsync(null, null, client, eventName, payload, options, cancellationToken);

    public Task<Guid> EnqueueAsync<TPayload>(
        DbConnection connection,
        DbTransaction transaction,
        string client,
        string eventName,
        TPayload payload,
        CallbackEnqueueOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        return EnqueueCoreAsync(
            connection,
            transaction,
            client,
            eventName,
            payload,
            options ?? new CallbackEnqueueOptions(),
            cancellationToken);
    }

    private async Task<Guid> EnqueueCoreAsync<TPayload>(
        DbConnection? connection,
        DbTransaction? transaction,
        string client,
        string eventName,
        TPayload payload,
        CallbackEnqueueOptions enqueueOptions,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentNullException.ThrowIfNull(enqueueOptions);

        if (client.Length > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(client), "Client name cannot exceed 200 characters.");
        }

        if (eventName.Length > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(eventName), "Event name cannot exceed 200 characters.");
        }

        ValidateHeaderValue(eventName, nameof(eventName));

        var destination = destinationResolver.Resolve(client, enqueueOptions.Endpoint);
        urlPolicy.Validate(new Uri(destination.Url, UriKind.Absolute));

        var serializedPayload = serializer.Serialize(payload);
        var payloadBytes = Encoding.UTF8.GetByteCount(serializedPayload);
        if (payloadBytes > options.MaxPayloadBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payload),
                $"Serialized payload is {payloadBytes} bytes; maximum is {options.MaxPayloadBytes} bytes.");
        }

        var now = timeProvider.GetUtcNow();
        var id = Guid.NewGuid();
        var maxAttempts = enqueueOptions.MaxAttempts ?? options.MaxAttempts;
        if (maxAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(enqueueOptions), "MaxAttempts must be greater than zero.");
        }

        var idempotencyKey = string.IsNullOrWhiteSpace(enqueueOptions.IdempotencyKey)
            ? id.ToString("N")
            : enqueueOptions.IdempotencyKey;

        if (idempotencyKey.Length > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(enqueueOptions), "IdempotencyKey cannot exceed 200 characters.");
        }

        ValidateHeaderValue(idempotencyKey, nameof(enqueueOptions.IdempotencyKey));

        if (enqueueOptions.CorrelationId?.Length > 200)
        {
            throw new ArgumentOutOfRangeException(
                nameof(enqueueOptions),
                "CorrelationId cannot exceed 200 characters.");
        }
        if (enqueueOptions.CorrelationId is { } correlationId)
        {
            ValidateHeaderValue(correlationId, nameof(enqueueOptions.CorrelationId));
        }

        var command = new CallbackEnqueueCommand(
            id,
            destination,
            eventName,
            serializedPayload,
            now,
            enqueueOptions.ScheduledAt ?? now,
            maxAttempts,
            enqueueOptions.CorrelationId,
            idempotencyKey);

        return await storage.EnqueueAsync(command, connection, transaction, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateHeaderValue(string value, string parameterName)
    {
        if (value.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Callback header values cannot contain control characters.",
                parameterName);
        }
    }
}
