using System.Data.Common;
using RelayHook.Core.Abstractions;
using RelayHook.Core.Callbacks;
using RelayHook.Core.Configuration;

namespace RelayHook.AspNetCore.Configuration;

internal sealed class MissingCallbackStorage : ICallbackStorage
{
    private static CallbackConfigurationException Missing() =>
        new("RelayHook storage is not configured. Call UseSqlServer during service registration.");

    public Task InitializeAsync(CancellationToken cancellationToken) => throw Missing();
    public Task<int> GetSchemaVersionAsync(CancellationToken cancellationToken) => throw Missing();
    public Task<Guid> EnqueueAsync(CallbackEnqueueCommand command, DbConnection? connection, DbTransaction? transaction, CancellationToken cancellationToken) => throw Missing();
    public Task<IReadOnlyList<CallbackJob>> ClaimAsync(string workerId, int batchSize, DateTimeOffset now, TimeSpan leaseDuration, CancellationToken cancellationToken) => throw Missing();
    public Task<Guid?> StartAttemptAsync(Guid jobId, string workerId, int attemptNumber, DateTimeOffset startedAt, CancellationToken cancellationToken) => throw Missing();
    public Task<bool> RenewLeaseAsync(Guid jobId, string workerId, TimeSpan leaseDuration, CancellationToken cancellationToken) => throw Missing();
    public Task<bool> CompleteAttemptAsync(CallbackAttemptCompletion completion, CancellationToken cancellationToken) => throw Missing();
    public Task<int> CleanupAsync(DateTimeOffset now, RetentionOptions options, CancellationToken cancellationToken) => throw Missing();
}
