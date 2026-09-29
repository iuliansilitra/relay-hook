using System.Data;
using Microsoft.Data.SqlClient;
using RelayHook.Core.Callbacks;
using RelayHook.SqlServer;
using RelayHook.SqlServer.Migrations;
using RelayHook.SqlServer.Storage;

namespace RelayHook.IntegrationTests;

public sealed class SqlServerIntegrationTests
{
    private const string ConnectionStringVariable = "RELAYHOOK_SQLSERVER";

    [SqlServerFact]
    public async Task RenewLeaseAsync_WhenLeaseExpiredButNotReclaimed_ShouldRetainOwnership()
    {
        var connectionString = GetConnectionString();
        var storageOptions = new SqlServerStorageOptions(connectionString);
        using var installer = new SqlSchemaInstaller(storageOptions);
        var storage = new SqlCallbackStorage(storageOptions, installer);
        await storage.InitializeAsync(CancellationToken.None);
        var command = CreateCommand();
        await storage.EnqueueAsync(command, null, null, CancellationToken.None);

        try
        {
            var claimed = Assert.Single(await storage.ClaimAsync(
                "worker-a",
                1,
                DateTimeOffset.UtcNow.AddSeconds(1),
                TimeSpan.FromMinutes(1),
                CancellationToken.None));
            Assert.Equal(command.Id, claimed.Id);
            await ExpireLeaseAsync(connectionString, command.Id);

            var leaseDuration = TimeSpan.FromMinutes(1);
            var databaseTimeBeforeRenewal = await ReadDatabaseUtcNowAsync(connectionString);
            var renewed = await storage.RenewLeaseAsync(
                command.Id,
                "worker-a",
                leaseDuration,
                CancellationToken.None);
            var databaseTimeAfterRenewal = await ReadDatabaseUtcNowAsync(connectionString);
            var competitorClaims = await storage.ClaimAsync(
                "worker-b",
                1,
                DateTimeOffset.UtcNow,
                TimeSpan.FromMinutes(1),
                CancellationToken.None);

            Assert.True(renewed);
            Assert.Empty(competitorClaims);
            Assert.Equal("worker-a", await ReadOwnerAsync(connectionString, command.Id));
            Assert.InRange(
                await ReadLeaseDeadlineAsync(connectionString, command.Id),
                databaseTimeBeforeRenewal + leaseDuration,
                databaseTimeAfterRenewal + leaseDuration);
        }
        finally
        {
            await DeleteJobGraphAsync(connectionString, command.Id);
        }
    }

    [SqlServerFact]
    public async Task RenewLeaseAsync_WhenCompetitorReclaimsExpiredLease_ShouldRejectStaleOwner()
    {
        var connectionString = GetConnectionString();
        var storageOptions = new SqlServerStorageOptions(connectionString);
        using var installer = new SqlSchemaInstaller(storageOptions);
        var storage = new SqlCallbackStorage(storageOptions, installer);
        await storage.InitializeAsync(CancellationToken.None);
        var command = CreateCommand();
        await storage.EnqueueAsync(command, null, null, CancellationToken.None);

        try
        {
            var claimed = Assert.Single(await storage.ClaimAsync(
                "worker-a",
                1,
                DateTimeOffset.UtcNow.AddSeconds(1),
                TimeSpan.FromMinutes(1),
                CancellationToken.None));
            Assert.Equal(command.Id, claimed.Id);
            var attemptId = await storage.StartAttemptAsync(
                command.Id,
                "worker-a",
                1,
                DateTimeOffset.UtcNow,
                CancellationToken.None);
            Assert.NotNull(attemptId);
            await ExpireLeaseAsync(connectionString, command.Id);

            var competitorClaim = Assert.Single(await storage.ClaimAsync(
                "worker-b",
                1,
                DateTimeOffset.UtcNow,
                TimeSpan.FromMinutes(1),
                CancellationToken.None));
            var renewed = await storage.RenewLeaseAsync(
                command.Id,
                "worker-a",
                TimeSpan.FromMinutes(1),
                CancellationToken.None);
            var completed = await storage.CompleteAttemptAsync(
                new CallbackAttemptCompletion(
                    command.Id,
                    attemptId.Value,
                    "worker-a",
                    1,
                    1,
                    DateTimeOffset.UtcNow,
                    CallbackDeliveryOutcome.Success(204, null, TimeSpan.FromMilliseconds(1)),
                    CallbackStatus.Completed,
                    null,
                    null),
                CancellationToken.None);

            Assert.Equal(command.Id, competitorClaim.Id);
            Assert.False(renewed);
            Assert.False(completed);
            Assert.Equal("worker-b", await ReadOwnerAsync(connectionString, command.Id));
        }
        finally
        {
            await DeleteJobGraphAsync(connectionString, command.Id);
        }
    }

    [SqlServerFact]
    public async Task ClaimAsync_WhenWorkerClockIsAhead_ShouldUseDatabaseClockForLeaseDeadline()
    {
        var connectionString = GetConnectionString();
        var storageOptions = new SqlServerStorageOptions(connectionString);
        using var installer = new SqlSchemaInstaller(storageOptions);
        var storage = new SqlCallbackStorage(storageOptions, installer);
        await storage.InitializeAsync(CancellationToken.None);
        var command = CreateCommand();
        await storage.EnqueueAsync(command, null, null, CancellationToken.None);

        try
        {
            var leaseDuration = TimeSpan.FromMinutes(1);
            var databaseTimeBeforeClaim = await ReadDatabaseUtcNowAsync(connectionString);
            var claimed = Assert.Single(await storage.ClaimAsync(
                "worker-with-fast-clock",
                1,
                DateTimeOffset.UtcNow.AddDays(1),
                leaseDuration,
                CancellationToken.None));
            var databaseTimeAfterClaim = await ReadDatabaseUtcNowAsync(connectionString);

            Assert.InRange(
                claimed.LockedUntil,
                databaseTimeBeforeClaim + leaseDuration,
                databaseTimeAfterClaim + leaseDuration);
        }
        finally
        {
            await DeleteJobGraphAsync(connectionString, command.Id);
        }
    }

    [SqlServerFact]
    public async Task RenewLeaseAsync_WhenRacingReclaim_ShouldLeaveExactlyOneOwner()
    {
        var connectionString = GetConnectionString();
        var storageOptions = new SqlServerStorageOptions(connectionString);
        using var installer = new SqlSchemaInstaller(storageOptions);
        var storage = new SqlCallbackStorage(storageOptions, installer);
        await storage.InitializeAsync(CancellationToken.None);
        var command = CreateCommand();
        await storage.EnqueueAsync(command, null, null, CancellationToken.None);

        try
        {
            Assert.Single(await storage.ClaimAsync(
                "worker-a",
                1,
                DateTimeOffset.UtcNow.AddSeconds(1),
                TimeSpan.FromMinutes(1),
                CancellationToken.None));
            await ExpireLeaseAsync(connectionString, command.Id);

            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var renewalTask = Task.Run(async () =>
            {
                await start.Task;
                return await storage.RenewLeaseAsync(
                    command.Id,
                    "worker-a",
                    TimeSpan.FromMinutes(1),
                    CancellationToken.None);
            });
            var reclaimTask = Task.Run(async () =>
            {
                await start.Task;
                return await storage.ClaimAsync(
                    "worker-b",
                    1,
                    DateTimeOffset.UtcNow,
                    TimeSpan.FromMinutes(1),
                    CancellationToken.None);
            });

            start.SetResult();
            await Task.WhenAll(renewalTask, reclaimTask);

            var renewed = await renewalTask;
            var reclaimed = await reclaimTask;
            Assert.NotEqual(renewed, reclaimed.Count == 1);
            Assert.Equal(
                renewed ? "worker-a" : "worker-b",
                await ReadOwnerAsync(connectionString, command.Id));
        }
        finally
        {
            await DeleteJobGraphAsync(connectionString, command.Id);
        }
    }

    [SqlServerFact]
    public async Task ClaimAsync_WhenTwoWorkersRace_ShouldClaimJobOnceAndRecoverExpiredLease()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable) ??
            throw new InvalidOperationException($"{ConnectionStringVariable} is required.");

        var storageOptions = new SqlServerStorageOptions(connectionString);
        using var installer = new SqlSchemaInstaller(storageOptions);
        var storage = new SqlCallbackStorage(storageOptions, installer);
        await storage.InitializeAsync(CancellationToken.None);
        var command = CreateCommand();
        await storage.EnqueueAsync(command, null, null, CancellationToken.None);

        try
        {
            var now = DateTimeOffset.UtcNow;
            var claims = await Task.WhenAll(
                storage.ClaimAsync("worker-a", 1, now, TimeSpan.FromMinutes(1), CancellationToken.None),
                storage.ClaimAsync("worker-b", 1, now, TimeSpan.FromMinutes(1), CancellationToken.None));

            Assert.Single(claims.SelectMany(static jobs => jobs));

            await ExpireLeaseAsync(connectionString, command.Id);
            var recovered = await storage.ClaimAsync(
                "worker-c",
                1,
                DateTimeOffset.UtcNow,
                TimeSpan.FromMinutes(1),
                CancellationToken.None);

            Assert.Single(recovered);
            Assert.Equal(command.Id, recovered[0].Id);
            Assert.Equal("worker-c", recovered[0].LockedBy);
        }
        finally
        {
            await DeleteJobGraphAsync(connectionString, command.Id);
        }
    }

    [SqlServerFact]
    public async Task ClaimAsync_WhenStartedAttemptCrashes_ShouldNotConsumeDeliveryBudget()
    {
        var connectionString = GetConnectionString();
        var storageOptions = new SqlServerStorageOptions(connectionString);
        using var installer = new SqlSchemaInstaller(storageOptions);
        var storage = new SqlCallbackStorage(storageOptions, installer);
        await storage.InitializeAsync(CancellationToken.None);
        var command = CreateCommand(maxAttempts: 1);
        await storage.EnqueueAsync(command, null, null, CancellationToken.None);

        try
        {
            var now = DateTimeOffset.UtcNow;
            var firstClaim = Assert.Single(await storage.ClaimAsync(
                "worker-a", 1, now, TimeSpan.FromSeconds(1), CancellationToken.None));
            var firstAttempt = await storage.StartAttemptAsync(
                command.Id, "worker-a", 1, now, CancellationToken.None);
            Assert.NotNull(firstAttempt);

            await ExpireLeaseAsync(connectionString, command.Id);

            var recovered = Assert.Single(await storage.ClaimAsync(
                "worker-b", 1, now.AddSeconds(2), TimeSpan.FromMinutes(1), CancellationToken.None));

            Assert.Equal(1, recovered.AttemptCount);
            Assert.Equal(0, recovered.CompletedAttemptCount);
            var secondAttempt = await storage.StartAttemptAsync(
                command.Id, "worker-b", 2, now.AddSeconds(2), CancellationToken.None);
            Assert.NotNull(secondAttempt);
            var completed = await storage.CompleteAttemptAsync(
                new CallbackAttemptCompletion(
                    command.Id,
                    secondAttempt.Value,
                    "worker-b",
                    2,
                    1,
                    now.AddSeconds(3),
                    CallbackDeliveryOutcome.Success(204, null, TimeSpan.FromMilliseconds(1)),
                    CallbackStatus.Completed,
                    null,
                    null),
                CancellationToken.None);

            Assert.True(completed);
            Assert.Equal(firstClaim.Id, recovered.Id);
        }
        finally
        {
            await DeleteJobGraphAsync(connectionString, command.Id);
        }
    }

    [SqlServerFact]
    public async Task InitializeAsync_WhenManyInstancesStart_ShouldConvergeOnExpectedVersion()
    {
        var storageOptions = new SqlServerStorageOptions(GetConnectionString());
        var installers = Enumerable.Range(0, 20)
            .Select(_ => new SqlSchemaInstaller(storageOptions))
            .ToArray();

        try
        {
            await Task.WhenAll(installers.Select(installer =>
                installer.InitializeAsync(CancellationToken.None)));

            var storage = new SqlCallbackStorage(storageOptions, installers[0]);
            Assert.Equal(
                SqlSchemaInstaller.ExpectedVersion,
                await storage.GetSchemaVersionAsync(CancellationToken.None));
        }
        finally
        {
            foreach (var installer in installers)
            {
                installer.Dispose();
            }
        }
    }

    [SqlServerFact]
    public async Task TransactionalEnqueue_WhenCallerRollsBack_ShouldUseCallerConnectionAndPersistNothing()
    {
        var baseConnectionString = GetConnectionString();
        var initializedOptions = new SqlServerStorageOptions(baseConnectionString);
        using (var initializedInstaller = new SqlSchemaInstaller(initializedOptions))
        {
            await initializedInstaller.InitializeAsync(CancellationToken.None);
        }

        var builder = new SqlConnectionStringBuilder(baseConnectionString)
        {
            ApplicationName = $"RelayHook-transaction-{Guid.NewGuid():N}",
            MaxPoolSize = 1,
            ConnectTimeout = 1
        };
        var options = new SqlServerStorageOptions(builder.ConnectionString);
        using var freshInstaller = new SqlSchemaInstaller(options);
        var storage = new SqlCallbackStorage(options, freshInstaller);
        var command = CreateCommand();

        await using (var connection = new SqlConnection(builder.ConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
            var id = await storage.EnqueueAsync(command, connection, transaction, CancellationToken.None);
            Assert.Equal(command.Id, id);
            await transaction.RollbackAsync();
        }

        Assert.Equal(0, await CountJobsAsync(baseConnectionString, command.Id));
    }

    [SqlServerFact]
    public async Task ClaimAsync_WhenOneSnapshotIsCorrupt_ShouldFailOnlyThatJob()
    {
        var connectionString = GetConnectionString();
        var storageOptions = new SqlServerStorageOptions(connectionString);
        using var installer = new SqlSchemaInstaller(storageOptions);
        var storage = new SqlCallbackStorage(storageOptions, installer);
        await storage.InitializeAsync(CancellationToken.None);
        var corrupt = CreateCommand();
        var valid = CreateCommand();
        await storage.EnqueueAsync(corrupt, null, null, CancellationToken.None);
        await storage.EnqueueAsync(valid, null, null, CancellationToken.None);

        try
        {
            await SetSnapshotAsync(connectionString, corrupt.Id, "{\"timeoutSeconds\":\"invalid\"}");

            var claimed = await storage.ClaimAsync(
                "worker",
                2,
                DateTimeOffset.UtcNow.AddSeconds(1),
                TimeSpan.FromMinutes(1),
                CancellationToken.None);

            Assert.Single(claimed);
            Assert.Equal(valid.Id, claimed[0].Id);
            Assert.Equal(3, await ReadStatusAsync(connectionString, corrupt.Id));
        }
        finally
        {
            await DeleteJobGraphAsync(connectionString, corrupt.Id);
            await DeleteJobGraphAsync(connectionString, valid.Id);
        }
    }

    private static string GetConnectionString() =>
        Environment.GetEnvironmentVariable(ConnectionStringVariable) ??
        throw new InvalidOperationException($"{ConnectionStringVariable} is required.");

    private static CallbackEnqueueCommand CreateCommand(int maxAttempts = 3)
    {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        var unique = Guid.NewGuid().ToString("N");
        return new CallbackEnqueueCommand(
            id,
            new CallbackDestinationSnapshot(
                $"client-{unique}",
                "primary",
                "https://example.com/callback",
                "POST",
                "application/json",
                30,
                "None",
                System.Text.Json.JsonSerializer.SerializeToElement(new { }),
                new Dictionary<string, string>()),
            "integration.test",
            "{}",
            now,
            now,
            maxAttempts,
            null,
            id.ToString("N"));
    }

    private static async Task<int> CountJobsAsync(string connectionString, Guid jobId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT COUNT(*) FROM [Callback].[Job] WHERE [Id] = @JobId;",
            connection);
        command.Parameters.Add("@JobId", SqlDbType.UniqueIdentifier).Value = jobId;
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task SetSnapshotAsync(string connectionString, Guid jobId, string snapshot)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "UPDATE [Callback].[Job] SET [EndpointSnapshot] = @Snapshot WHERE [Id] = @JobId;",
            connection);
        command.Parameters.Add("@Snapshot", SqlDbType.NVarChar, -1).Value = snapshot;
        command.Parameters.Add("@JobId", SqlDbType.UniqueIdentifier).Value = jobId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<byte> ReadStatusAsync(string connectionString, Guid jobId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT [Status] FROM [Callback].[Job] WHERE [Id] = @JobId;",
            connection);
        command.Parameters.Add("@JobId", SqlDbType.UniqueIdentifier).Value = jobId;
        return (byte)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExpireLeaseAsync(string connectionString, Guid jobId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "UPDATE [Callback].[Job] SET [LockedUntil] = DATEADD(second, -1, SYSUTCDATETIME()) WHERE [Id] = @JobId;",
            connection);
        command.Parameters.Add("@JobId", SqlDbType.UniqueIdentifier).Value = jobId;
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<string?> ReadOwnerAsync(string connectionString, Guid jobId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT [LockedBy] FROM [Callback].[Job] WHERE [Id] = @JobId;",
            connection);
        command.Parameters.Add("@JobId", SqlDbType.UniqueIdentifier).Value = jobId;
        return await command.ExecuteScalarAsync() as string;
    }

    private static async Task<DateTimeOffset> ReadDatabaseUtcNowAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00');",
            connection);
        return (DateTimeOffset)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<DateTimeOffset> ReadLeaseDeadlineAsync(
        string connectionString,
        Guid jobId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT [LockedUntil] FROM [Callback].[Job] WHERE [Id] = @JobId;",
            connection);
        command.Parameters.Add("@JobId", SqlDbType.UniqueIdentifier).Value = jobId;
        return (DateTimeOffset)(await command.ExecuteScalarAsync())!;
    }

    private static async Task DeleteJobGraphAsync(string connectionString, Guid jobId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        const string sql = """
            DECLARE @ClientId uniqueidentifier;
            DECLARE @EndpointId uniqueidentifier;
            SELECT @ClientId = [ClientId], @EndpointId = [EndpointId]
            FROM [Callback].[Job] WHERE [Id] = @JobId;
            DELETE FROM [Callback].[Job] WHERE [Id] = @JobId;
            DELETE FROM [Callback].[Endpoint] WHERE [Id] = @EndpointId;
            DELETE FROM [Callback].[Client] WHERE [Id] = @ClientId;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@JobId", SqlDbType.UniqueIdentifier).Value = jobId;
        await command.ExecuteNonQueryAsync();
    }
}
