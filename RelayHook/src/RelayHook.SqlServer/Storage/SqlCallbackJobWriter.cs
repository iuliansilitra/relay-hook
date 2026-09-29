using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using RelayHook.Core.Abstractions;
using RelayHook.Core.Callbacks;
using RelayHook.SqlServer.Migrations;

namespace RelayHook.SqlServer.Storage;

internal sealed class SqlCallbackJobWriter(
    SqlConnectionFactory connectionFactory,
    SqlSchemaInstaller schemaInstaller)
{
    public async Task<Guid> EnqueueAsync(
        CallbackEnqueueCommand command,
        DbConnection? connection,
        DbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (connection is not null || transaction is not null)
        {
            if (connection is not SqlConnection sqlConnection ||
                transaction is not SqlTransaction sqlTransaction ||
                !ReferenceEquals(sqlTransaction.Connection, sqlConnection))
            {
                throw new ArgumentException(
                    "Transactional enqueue requires a matching Microsoft.Data.SqlClient SqlConnection and SqlTransaction.");
            }

            if (sqlConnection.State != ConnectionState.Open)
            {
                throw new ArgumentException("Transactional enqueue requires an open SQL connection.");
            }

            try
            {
                await SqlSchemaInstaller.EnsureCompatibleWithCallerTransactionAsync(
                    sqlConnection,
                    sqlTransaction,
                    cancellationToken).ConfigureAwait(false);
                return await EnqueueCoreAsync(command, sqlConnection, sqlTransaction, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (SqlException exception)
            {
                throw new CallbackStorageException("Failed to persist a callback job.", exception);
            }
        }

        await schemaInstaller.InitializeAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var ownedConnection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var ownedTransaction = (SqlTransaction)await ownedConnection
                .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                .ConfigureAwait(false);
            try
            {
                var id = await EnqueueCoreAsync(command, ownedConnection, ownedTransaction, cancellationToken)
                    .ConfigureAwait(false);
                await ownedTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return id;
            }
            catch
            {
                await ownedTransaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }
        catch (SqlException exception)
        {
            throw new CallbackStorageException("Failed to persist a callback job.", exception);
        }
    }

    private static async Task<Guid> EnqueueCoreAsync(
        CallbackEnqueueCommand enqueue,
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var snapshotJson = JsonSerializer.Serialize(enqueue.Destination, SqlStorageJson.Options);
        var configurationHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshotJson)));
        var clientId = await GetOrCreateClientAsync(
            enqueue.Destination.ClientName, enqueue.CreatedAt, connection, transaction, cancellationToken)
            .ConfigureAwait(false);
        var endpointId = await GetOrCreateEndpointAsync(
            clientId, enqueue.Destination, configurationHash, enqueue.CreatedAt,
            connection, transaction, cancellationToken).ConfigureAwait(false);

        const string sql = """
            INSERT INTO [Callback].[Job]
            (
                [Id], [ClientId], [EndpointId], [EventName], [Payload], [ContentType],
                [EndpointSnapshot], [Status], [CreatedAt], [NextAttemptAt], [AttemptCount],
                [MaxAttempts], [CorrelationId], [IdempotencyKey]
            )
            VALUES
            (
                @Id, @ClientId, @EndpointId, @EventName, @Payload, @ContentType,
                @EndpointSnapshot, 0, @CreatedAt, @NextAttemptAt, 0,
                @MaxAttempts, @CorrelationId, @IdempotencyKey
            );
            """;

        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = enqueue.Id;
        command.Parameters.Add("@ClientId", SqlDbType.UniqueIdentifier).Value = clientId;
        command.Parameters.Add("@EndpointId", SqlDbType.UniqueIdentifier).Value = endpointId;
        command.Parameters.Add("@EventName", SqlDbType.NVarChar, 200).Value = enqueue.EventName;
        command.Parameters.Add("@Payload", SqlDbType.NVarChar, -1).Value = enqueue.Payload;
        command.Parameters.Add("@ContentType", SqlDbType.NVarChar, 100).Value = enqueue.Destination.ContentType;
        command.Parameters.Add("@EndpointSnapshot", SqlDbType.NVarChar, -1).Value = snapshotJson;
        command.Parameters.Add("@CreatedAt", SqlDbType.DateTimeOffset).Value = enqueue.CreatedAt;
        command.Parameters.Add("@NextAttemptAt", SqlDbType.DateTimeOffset).Value = enqueue.ScheduledAt;
        command.Parameters.Add("@MaxAttempts", SqlDbType.Int).Value = enqueue.MaxAttempts;
        command.Parameters.Add("@CorrelationId", SqlDbType.NVarChar, 200).Value =
            (object?)enqueue.CorrelationId ?? DBNull.Value;
        command.Parameters.Add("@IdempotencyKey", SqlDbType.NVarChar, 200).Value = enqueue.IdempotencyKey;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return enqueue.Id;
    }

    private static async Task<Guid> GetOrCreateClientAsync(
        string name,
        DateTimeOffset now,
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string select = "SELECT [Id] FROM [Callback].[Client] WITH (UPDLOCK, HOLDLOCK) WHERE [Name] = @Name;";
        await using var selectCommand = new SqlCommand(select, connection, transaction);
        selectCommand.Parameters.Add("@Name", SqlDbType.NVarChar, 200).Value = name;
        var existing = await selectCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (existing is Guid existingId) return existingId;

        var id = Guid.NewGuid();
        const string insert = """
            INSERT INTO [Callback].[Client] ([Id], [Name], [CreatedAt], [UpdatedAt])
            VALUES (@Id, @Name, @Now, @Now);
            """;
        await using var command = new SqlCommand(insert, connection, transaction);
        command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = id;
        command.Parameters.Add("@Name", SqlDbType.NVarChar, 200).Value = name;
        command.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return id;
    }

    private static async Task<Guid> GetOrCreateEndpointAsync(
        Guid clientId,
        CallbackDestinationSnapshot destination,
        string configurationHash,
        DateTimeOffset now,
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string select = """
            SELECT [Id] FROM [Callback].[Endpoint] WITH (UPDLOCK, HOLDLOCK)
            WHERE [ClientId] = @ClientId AND [Name] = @Name;
            """;
        await using var selectCommand = new SqlCommand(select, connection, transaction);
        selectCommand.Parameters.Add("@ClientId", SqlDbType.UniqueIdentifier).Value = clientId;
        selectCommand.Parameters.Add("@Name", SqlDbType.NVarChar, 200).Value = destination.EndpointName;
        var existing = await selectCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (existing is Guid existingId)
        {
            const string update = """
                UPDATE [Callback].[Endpoint]
                SET [Url] = @Url, [HttpMethod] = @HttpMethod,
                    [ConfigurationHash] = @ConfigurationHash, [UpdatedAt] = @Now
                WHERE [Id] = @Id;
                """;
            await using var command = new SqlCommand(update, connection, transaction);
            AddEndpointParameters(command, existingId, destination, configurationHash, now);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return existingId;
        }

        var id = Guid.NewGuid();
        const string insert = """
            INSERT INTO [Callback].[Endpoint]
                ([Id], [ClientId], [Name], [Url], [HttpMethod], [ConfigurationHash], [CreatedAt], [UpdatedAt])
            VALUES
                (@Id, @ClientId, @Name, @Url, @HttpMethod, @ConfigurationHash, @Now, @Now);
            """;
        await using var insertCommand = new SqlCommand(insert, connection, transaction);
        insertCommand.Parameters.Add("@ClientId", SqlDbType.UniqueIdentifier).Value = clientId;
        insertCommand.Parameters.Add("@Name", SqlDbType.NVarChar, 200).Value = destination.EndpointName;
        AddEndpointParameters(insertCommand, id, destination, configurationHash, now);
        await insertCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return id;
    }

    private static void AddEndpointParameters(
        SqlCommand command,
        Guid id,
        CallbackDestinationSnapshot destination,
        string configurationHash,
        DateTimeOffset now)
    {
        command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = id;
        command.Parameters.Add("@Url", SqlDbType.NVarChar, 2048).Value = destination.Url;
        command.Parameters.Add("@HttpMethod", SqlDbType.VarChar, 10).Value = destination.Method;
        command.Parameters.Add("@ConfigurationHash", SqlDbType.Char, 64).Value = configurationHash;
        command.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
    }
}
