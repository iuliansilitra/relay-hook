using System.Globalization;
using Microsoft.Data.SqlClient;
using RelayHook.Core.Abstractions;

namespace RelayHook.SqlServer.Storage;

internal sealed class SqlSchemaStatusReader(SqlConnectionFactory connectionFactory)
{
    public async Task<int> GetVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var existsCommand = new SqlCommand(
                "SELECT OBJECT_ID(N'[Callback].[SchemaVersion]', N'U');",
                connection);
            var objectId = await existsCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (objectId is null or DBNull) return 0;

            await using var versionCommand = new SqlCommand(
                "SELECT [Version] FROM [Callback].[SchemaVersion] WHERE [Id] = 1;",
                connection);
            return Convert.ToInt32(
                await versionCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture);
        }
        catch (SqlException exception)
        {
            throw new CallbackStorageException("Failed to read the Callback schema version.", exception);
        }
    }
}
