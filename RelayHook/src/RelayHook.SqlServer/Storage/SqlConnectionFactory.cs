using Microsoft.Data.SqlClient;

namespace RelayHook.SqlServer.Storage;

internal sealed class SqlConnectionFactory(SqlServerStorageOptions options)
{
    public async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(options.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
