using System.Data;
using Microsoft.Data.SqlClient;
using RelayHook.Core.Abstractions;
using RelayHook.Core.Configuration;

namespace RelayHook.SqlServer.Storage;

internal sealed class SqlCallbackRetentionStore(SqlConnectionFactory connectionFactory)
{
    public async Task<int> CleanupAsync(
        DateTimeOffset now,
        RetentionOptions retention,
        CancellationToken cancellationToken)
    {
        const string sql = """
            ;WITH [Expired] AS
            (
                SELECT TOP (@BatchSize) [Id]
                FROM [Callback].[Job] WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE ([Status] = 2 AND [CompletedAt] < @CompletedCutoff)
                   OR ([Status] = 3 AND @FailedCutoff IS NOT NULL AND [CompletedAt] < @FailedCutoff)
                ORDER BY [CompletedAt], [Id]
            )
            DELETE [Job]
            FROM [Callback].[Job] AS [Job]
            INNER JOIN [Expired] ON [Expired].[Id] = [Job].[Id];
            """;

        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@BatchSize", SqlDbType.Int).Value = retention.BatchSize;
            command.Parameters.Add("@CompletedCutoff", SqlDbType.DateTimeOffset).Value =
                now - retention.CompletedRetention;
            command.Parameters.Add("@FailedCutoff", SqlDbType.DateTimeOffset).Value =
                retention.FailedRetention is { } failedRetention
                    ? now - failedRetention
                    : DBNull.Value;
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqlException exception)
        {
            throw new CallbackStorageException("Failed to clean up retained callback jobs.", exception);
        }
    }
}
