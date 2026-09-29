namespace RelayHook.Core.Abstractions;

/// <summary>Queues durable callbacks for later delivery.</summary>
public interface ICallbackClient
{
    /// <summary>Serializes and durably queues a callback without sending it synchronously.</summary>
    /// <param name="client">Configured logical client name.</param>
    /// <param name="eventName">Non-secret event name sent in callback headers.</param>
    /// <param name="payload">Payload serialized at enqueue time.</param>
    /// <param name="cancellationToken">Cancellation token for serialization and persistence.</param>
    /// <typeparam name="TPayload">Payload type handled by the configured serializer.</typeparam>
    /// <returns>Stable callback identifier retained across delivery attempts.</returns>
    Task<Guid> EnqueueAsync<TPayload>(
        string client,
        string eventName,
        TPayload payload,
        CancellationToken cancellationToken = default);

    /// <summary>Serializes and durably queues a callback with scheduling and identity options.</summary>
    /// <param name="client">Configured logical client name.</param>
    /// <param name="eventName">Non-secret event name sent in callback headers.</param>
    /// <param name="payload">Payload serialized at enqueue time.</param>
    /// <param name="options">Endpoint, schedule, attempt, correlation, and idempotency options.</param>
    /// <param name="cancellationToken">Cancellation token for serialization and persistence.</param>
    /// <typeparam name="TPayload">Payload type handled by the configured serializer.</typeparam>
    /// <returns>Stable callback identifier retained across delivery attempts.</returns>
    Task<Guid> EnqueueAsync<TPayload>(
        string client,
        string eventName,
        TPayload payload,
        CallbackEnqueueOptions options,
        CancellationToken cancellationToken = default);
}
