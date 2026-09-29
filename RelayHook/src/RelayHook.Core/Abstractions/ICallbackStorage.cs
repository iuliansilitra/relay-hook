using System.Data.Common;
using RelayHook.Core.Callbacks;
using RelayHook.Core.Configuration;

namespace RelayHook.Core.Abstractions;

internal interface ICallbackStorage
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<int> GetSchemaVersionAsync(CancellationToken cancellationToken);

    Task<Guid> EnqueueAsync(
        CallbackEnqueueCommand command,
        DbConnection? connection,
        DbTransaction? transaction,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CallbackJob>> ClaimAsync(
        string workerId,
        int batchSize,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<Guid?> StartAttemptAsync(
        Guid jobId,
        string workerId,
        int attemptNumber,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken);

    Task<bool> RenewLeaseAsync(
        Guid jobId,
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<bool> CompleteAttemptAsync(
        CallbackAttemptCompletion completion,
        CancellationToken cancellationToken);

    Task<int> CleanupAsync(
        DateTimeOffset now,
        RetentionOptions options,
        CancellationToken cancellationToken);
}
