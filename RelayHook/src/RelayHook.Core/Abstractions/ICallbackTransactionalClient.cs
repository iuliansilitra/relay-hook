using System.Data.Common;

namespace RelayHook.Core.Abstractions;

/// <summary>Queues a callback in an existing SQL transaction.</summary>
public interface ICallbackTransactionalClient
{
    /// <summary>
    /// Queues a callback using the supplied open connection and transaction. Both must belong to the configured RelayHook SQL Server database.
    /// </summary>
    /// <remarks>RelayHook does not commit, roll back, close, or dispose caller-owned database objects.</remarks>
    /// <param name="connection">Open SQL Server connection.</param>
    /// <param name="transaction">Active transaction created from <paramref name="connection"/>.</param>
    /// <param name="client">Configured logical client name.</param>
    /// <param name="eventName">Non-secret event name.</param>
    /// <param name="payload">Payload serialized inside the caller's transaction scope.</param>
    /// <param name="options">Optional endpoint, schedule, attempt, correlation, and idempotency options.</param>
    /// <param name="cancellationToken">Cancellation token for schema verification and persistence.</param>
    /// <typeparam name="TPayload">Payload type handled by the configured serializer.</typeparam>
    /// <returns>Stable callback identifier retained across delivery attempts.</returns>
    Task<Guid> EnqueueAsync<TPayload>(
        DbConnection connection,
        DbTransaction transaction,
        string client,
        string eventName,
        TPayload payload,
        CallbackEnqueueOptions? options = null,
        CancellationToken cancellationToken = default);
}
