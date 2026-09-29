using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using RelayHook.Core.Abstractions;
using RelayHook.Core.Callbacks;

namespace RelayHook.SqlServer.Storage;

internal sealed class SqlCallbackJobClaimer(SqlConnectionFactory connectionFactory)
{
    public async Task<IReadOnlyList<CallbackJob>> ClaimAsync(
        string workerId,
        int batchSize,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SET XACT_ABORT ON;
            SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

            UPDATE [Callback].[Job]
            SET [Status] = 3,
                [CompletedAt] = SYSUTCDATETIME(),
                [LockedBy] = NULL,
                [LockedUntil] = NULL,
                [LastError] = N'Worker lease expired after the final permitted attempt.'
            WHERE [Status] = 1
              AND [LockedUntil] <= SYSUTCDATETIME()
              AND [CompletedAttemptCount] >= [MaxAttempts];

            ;WITH [Candidates] AS
            (
                SELECT TOP (@BatchSize) *
                FROM [Callback].[Job] WITH (UPDLOCK, READPAST, READCOMMITTEDLOCK, ROWLOCK)
                WHERE
                    (([Status] = 0 AND [NextAttemptAt] <= @Now)
                     OR ([Status] = 1 AND [LockedUntil] <= SYSUTCDATETIME()))
                    AND [CompletedAttemptCount] < [MaxAttempts]
                ORDER BY [NextAttemptAt], [CreatedAt]
            )
            UPDATE [Candidates]
            SET [Status] = 1,
                [LockedBy] = @WorkerId,
                [LockedUntil] = DATEADD(
                    nanosecond,
                    @LeaseNanoseconds,
                    DATEADD(second, @LeaseSeconds, DATEADD(day, @LeaseDays, SYSUTCDATETIME())))
            OUTPUT
                inserted.[Id], inserted.[ClientId], inserted.[EndpointId], inserted.[EventName],
                inserted.[Payload], inserted.[ContentType], inserted.[EndpointSnapshot],
                inserted.[CreatedAt], inserted.[NextAttemptAt], inserted.[AttemptCount],
                inserted.[CompletedAttemptCount], inserted.[MaxAttempts],
                inserted.[CorrelationId], inserted.[IdempotencyKey],
                inserted.[LockedBy], inserted.[LockedUntil];
            """;

        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@BatchSize", SqlDbType.Int).Value = batchSize;
            command.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
            command.Parameters.Add("@WorkerId", SqlDbType.NVarChar, 200).Value = workerId;
            AddLeaseDurationParameters(command, leaseDuration);

            var jobs = new List<CallbackJob>(batchSize);
            var invalidSnapshots = new List<Guid>();
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    try
                    {
                        jobs.Add(ReadJob(reader));
                    }
                    catch (JsonException)
                    {
                        invalidSnapshots.Add(reader.GetGuid(0));
                    }
                    catch (CallbackStorageException exception) when (exception.InnerException is JsonException)
                    {
                        invalidSnapshots.Add(reader.GetGuid(0));
                    }
                }
            }

            foreach (var jobId in invalidSnapshots)
            {
                await FailInvalidSnapshotAsync(
                    connection,
                    jobId,
                    workerId,
                    now,
                    cancellationToken).ConfigureAwait(false);
            }

            return jobs;
        }
        catch (SqlException exception)
        {
            throw new CallbackStorageException("Failed to claim callback jobs.", exception);
        }
    }

    private static void AddLeaseDurationParameters(SqlCommand command, TimeSpan leaseDuration)
    {
        command.Parameters.Add("@LeaseDays", SqlDbType.Int).Value = (int)leaseDuration.TotalDays;
        command.Parameters.Add("@LeaseSeconds", SqlDbType.Int).Value =
            leaseDuration.Hours * 3600 + leaseDuration.Minutes * 60 + leaseDuration.Seconds;
        command.Parameters.Add("@LeaseNanoseconds", SqlDbType.Int).Value =
            (int)(leaseDuration.Ticks % TimeSpan.TicksPerSecond * 100);
    }

    private static async Task FailInvalidSnapshotAsync(
        SqlConnection connection,
        Guid jobId,
        string workerId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE [Callback].[Job]
            SET [Status] = 3,
                [CompletedAt] = @Now,
                [LockedBy] = NULL,
                [LockedUntil] = NULL,
                [LastError] = N'Stored callback endpoint snapshot is invalid.'
            WHERE [Id] = @JobId
              AND [Status] = 1
              AND [LockedBy] = @WorkerId;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@JobId", SqlDbType.UniqueIdentifier).Value = jobId;
        command.Parameters.Add("@WorkerId", SqlDbType.NVarChar, 200).Value = workerId;
        command.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static CallbackJob ReadJob(SqlDataReader reader)
    {
        var destination = JsonSerializer.Deserialize<CallbackDestinationSnapshot>(
                              reader.GetString(6),
                              SqlStorageJson.Options) ??
                          throw new CallbackStorageException(
                              "Stored callback endpoint snapshot is invalid.",
                              new JsonException("Endpoint snapshot deserialized to null."));

        return new CallbackJob(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            destination,
            reader.GetDateTimeOffset(7),
            reader.GetDateTimeOffset(8),
            reader.GetInt32(9),
            reader.GetInt32(10),
            reader.GetInt32(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.GetString(13),
            reader.GetString(14),
            reader.GetDateTimeOffset(15));
    }
}
