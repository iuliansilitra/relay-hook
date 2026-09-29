using System.Data.Common;
using RelayHook.Core.Abstractions;
using RelayHook.Core.Callbacks;
using RelayHook.Core.Configuration;
using RelayHook.SqlServer.Migrations;

namespace RelayHook.SqlServer.Storage;

internal sealed class SqlCallbackStorage : ICallbackStorage
{
    private readonly SqlSchemaInstaller _schemaInstaller;
    private readonly SqlSchemaStatusReader _schemaStatusReader;
    private readonly SqlCallbackJobWriter _jobWriter;
    private readonly SqlCallbackJobClaimer _jobClaimer;
    private readonly SqlCallbackAttemptStore _attemptStore;
    private readonly SqlCallbackRetentionStore _retentionStore;

    public SqlCallbackStorage(SqlServerStorageOptions options, SqlSchemaInstaller schemaInstaller)
    {
        _schemaInstaller = schemaInstaller;
        var connectionFactory = new SqlConnectionFactory(options);
        _schemaStatusReader = new SqlSchemaStatusReader(connectionFactory);
        _jobWriter = new SqlCallbackJobWriter(connectionFactory, schemaInstaller);
        _jobClaimer = new SqlCallbackJobClaimer(connectionFactory);
        _attemptStore = new SqlCallbackAttemptStore(connectionFactory);
        _retentionStore = new SqlCallbackRetentionStore(connectionFactory);
    }

    public Task InitializeAsync(CancellationToken cancellationToken) =>
        _schemaInstaller.InitializeAsync(cancellationToken);

    public Task<int> GetSchemaVersionAsync(CancellationToken cancellationToken) =>
        _schemaStatusReader.GetVersionAsync(cancellationToken);

    public Task<Guid> EnqueueAsync(
        CallbackEnqueueCommand command,
        DbConnection? connection,
        DbTransaction? transaction,
        CancellationToken cancellationToken) =>
        _jobWriter.EnqueueAsync(command, connection, transaction, cancellationToken);

    public Task<IReadOnlyList<CallbackJob>> ClaimAsync(
        string workerId,
        int batchSize,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken) =>
        _jobClaimer.ClaimAsync(workerId, batchSize, now, leaseDuration, cancellationToken);

    public Task<Guid?> StartAttemptAsync(
        Guid jobId,
        string workerId,
        int attemptNumber,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken) =>
        _attemptStore.StartAsync(jobId, workerId, attemptNumber, startedAt, cancellationToken);

    public Task<bool> RenewLeaseAsync(
        Guid jobId,
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken) =>
        _attemptStore.RenewLeaseAsync(jobId, workerId, leaseDuration, cancellationToken);

    public Task<bool> CompleteAttemptAsync(
        CallbackAttemptCompletion completion,
        CancellationToken cancellationToken) =>
        _attemptStore.CompleteAsync(completion, cancellationToken);

    public Task<int> CleanupAsync(
        DateTimeOffset now,
        RetentionOptions options,
        CancellationToken cancellationToken) =>
        _retentionStore.CleanupAsync(now, options, cancellationToken);
}
