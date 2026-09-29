using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using RelayHook.Core.Abstractions;
using RelayHook.Core.Callbacks;

namespace RelayHook.SqlServer.Storage;

internal sealed class SqlCallbackAttemptStore(SqlConnectionFactory connectionFactory)
{
    public async Task<Guid?> StartAsync(
        Guid jobId,
        string workerId,
        int attemptNumber,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;

            UPDATE [Callback].[Job]
            SET [AttemptCount] = @AttemptNumber
            WHERE [Id] = @JobId
              AND [Status] = 1
              AND [LockedBy] = @WorkerId
              AND [LockedUntil] > SYSUTCDATETIME()
              AND [AttemptCount] = @AttemptNumber - 1;

            IF @@ROWCOUNT = 1
            BEGIN
                INSERT INTO [Callback].[Attempt]
                    ([Id], [JobId], [AttemptNumber], [StartedAt], [WorkerId])
                VALUES
                    (@AttemptId, @JobId, @AttemptNumber, @StartedAt, @WorkerId);
                SELECT @AttemptId;
            END;

            COMMIT TRANSACTION;
            """;

        try
        {
            var attemptId = Guid.NewGuid();
            await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@JobId", SqlDbType.UniqueIdentifier).Value = jobId;
            command.Parameters.Add("@AttemptId", SqlDbType.UniqueIdentifier).Value = attemptId;
            command.Parameters.Add("@AttemptNumber", SqlDbType.Int).Value = attemptNumber;
            command.Parameters.Add("@StartedAt", SqlDbType.DateTimeOffset).Value = startedAt;
            command.Parameters.Add("@WorkerId", SqlDbType.NVarChar, 200).Value = workerId;
            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result is Guid id ? id : null;
        }
        catch (SqlException exception)
        {
            throw new CallbackStorageException("Failed to start a callback attempt.", exception);
        }
    }

    public async Task<bool> RenewLeaseAsync(
        Guid jobId,
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE [Callback].[Job]
            SET [LockedUntil] = DATEADD(
                nanosecond,
                @LeaseNanoseconds,
                DATEADD(second, @LeaseSeconds, DATEADD(day, @LeaseDays, SYSUTCDATETIME())))
            WHERE [Id] = @JobId AND [Status] = 1 AND [LockedBy] = @WorkerId;
            """;

        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@JobId", SqlDbType.UniqueIdentifier).Value = jobId;
            command.Parameters.Add("@WorkerId", SqlDbType.NVarChar, 200).Value = workerId;
            AddLeaseDurationParameters(command, leaseDuration);
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
        }
        catch (SqlException exception)
        {
            throw new CallbackStorageException("Failed to renew a callback lease.", exception);
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

    public async Task<bool> CompleteAsync(
        CallbackAttemptCompletion completion,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;

            UPDATE [Callback].[Job]
            SET [Status] = @Status,
                [CompletedAttemptCount] = @CompletedAttemptCount,
                [NextAttemptAt] = COALESCE(@NextAttemptAt, [NextAttemptAt]),
                [CompletedAt] = CASE WHEN @Status IN (2, 3) THEN @CompletedAt ELSE NULL END,
                [LockedBy] = NULL,
                [LockedUntil] = NULL,
                [LastError] = @LastError
            WHERE [Id] = @JobId
              AND [Status] = 1
              AND [LockedBy] = @WorkerId
              AND [AttemptCount] = @AttemptNumber
              AND [CompletedAttemptCount] = @CompletedAttemptCount - 1;

            DECLARE @Updated int = @@ROWCOUNT;
            IF @Updated = 1
            BEGIN
                UPDATE [Callback].[Attempt]
                SET [CompletedAt] = @CompletedAt,
                    [DurationMs] = @DurationMs,
                    [HttpStatusCode] = @HttpStatusCode,
                    [FailureType] = @FailureType,
                    [ErrorMessage] = @ErrorMessage,
                    [ResponseBody] = @ResponseBody
                WHERE [Id] = @AttemptId AND [JobId] = @JobId;

                IF @@ROWCOUNT <> 1
                    THROW 51000, 'Callback attempt row was not found during completion.', 1;
            END;

            COMMIT TRANSACTION;
            SELECT @Updated;
            """;

        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(sql, connection);
            AddCompletionParameters(command, completion);
            return Convert.ToInt32(
                await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture) == 1;
        }
        catch (SqlException exception)
        {
            throw new CallbackStorageException("Failed to complete a callback attempt.", exception);
        }
    }

    private static void AddCompletionParameters(
        SqlCommand command,
        CallbackAttemptCompletion completion)
    {
        command.Parameters.Add("@JobId", SqlDbType.UniqueIdentifier).Value = completion.JobId;
        command.Parameters.Add("@AttemptId", SqlDbType.UniqueIdentifier).Value = completion.AttemptId;
        command.Parameters.Add("@WorkerId", SqlDbType.NVarChar, 200).Value = completion.WorkerId;
        command.Parameters.Add("@AttemptNumber", SqlDbType.Int).Value = completion.AttemptNumber;
        command.Parameters.Add("@CompletedAttemptCount", SqlDbType.Int).Value =
            completion.CompletedAttemptCount;
        command.Parameters.Add("@Status", SqlDbType.TinyInt).Value = (byte)completion.JobStatus;
        command.Parameters.Add("@NextAttemptAt", SqlDbType.DateTimeOffset).Value =
            (object?)completion.NextAttemptAt ?? DBNull.Value;
        command.Parameters.Add("@CompletedAt", SqlDbType.DateTimeOffset).Value = completion.CompletedAt;
        command.Parameters.Add("@LastError", SqlDbType.NVarChar, 2048).Value =
            (object?)completion.LastError ?? DBNull.Value;
        command.Parameters.Add("@DurationMs", SqlDbType.BigInt).Value =
            Math.Max(0, (long)completion.Outcome.Duration.TotalMilliseconds);
        command.Parameters.Add("@HttpStatusCode", SqlDbType.Int).Value =
            (object?)completion.Outcome.HttpStatusCode ?? DBNull.Value;
        command.Parameters.Add("@FailureType", SqlDbType.TinyInt).Value =
            (byte)completion.Outcome.FailureType;
        command.Parameters.Add("@ErrorMessage", SqlDbType.NVarChar, 2048).Value =
            (object?)completion.Outcome.ErrorMessage ?? DBNull.Value;
        command.Parameters.Add("@ResponseBody", SqlDbType.NVarChar, -1).Value =
            (object?)completion.Outcome.ResponseBody ?? DBNull.Value;
    }
}
