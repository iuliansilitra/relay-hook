using System.Data;
using System.Globalization;
using System.Reflection;
using Microsoft.Data.SqlClient;
using RelayHook.Core.Abstractions;

namespace RelayHook.SqlServer.Migrations;

internal sealed class SqlSchemaInstaller(SqlServerStorageOptions options) : IDisposable
{
    public const int ExpectedVersion = 2;
    private static readonly string[] MigrationResourceSuffixes =
    [
        ".Migrations.001_Initial.sql",
        ".Migrations.002_CompletedAttemptCountAndRetention.sql"
    ];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _initialized;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            await using var connection = new SqlConnection(options.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqlTransaction)await connection
                .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
                .ConfigureAwait(false);

            try
            {
                await AcquireMigrationLockAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
                await BootstrapVersionTableAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
                var version = await ReadVersionAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
                if (version > ExpectedVersion)
                {
                    throw new CallbackStorageException(
                        $"Callback schema version {version} is newer than supported version {ExpectedVersion}.",
                        new InvalidOperationException("Database downgrade is not supported."));
                }

                for (var targetVersion = version + 1; targetVersion <= ExpectedVersion; targetVersion++)
                {
                    await ExecuteMigrationAsync(
                        connection,
                        transaction,
                        targetVersion,
                        cancellationToken).ConfigureAwait(false);
                    await SetVersionAsync(connection, transaction, targetVersion, cancellationToken)
                        .ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                _initialized = true;
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }
        catch (CallbackStorageException)
        {
            throw;
        }
        catch (SqlException exception)
        {
            throw new CallbackStorageException("Failed to install or upgrade the Callback SQL schema.", exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    public static async Task EnsureCompatibleWithCallerTransactionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        const string sql = """
            IF OBJECT_ID(N'[Callback].[SchemaVersion]', N'U') IS NULL
                SELECT CAST(NULL AS int);
            ELSE
                SELECT [Version] FROM [Callback].[SchemaVersion] WHERE [Id] = 1;
            """;

        try
        {
            await using var command = new SqlCommand(sql, connection, transaction);
            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (result is null || result is DBNull ||
                Convert.ToInt32(result, CultureInfo.InvariantCulture) != ExpectedVersion)
            {
                throw new CallbackStorageException(
                    $"Transactional enqueue requires Callback schema version {ExpectedVersion}. Start the RelayHook host before using the caller-owned transaction.",
                    new InvalidOperationException("Callback schema is missing or incompatible."));
            }
        }
        catch (CallbackStorageException)
        {
            throw;
        }
        catch (SqlException exception)
        {
            throw new CallbackStorageException(
                "Failed to verify the Callback schema using the caller-owned transaction.",
                exception);
        }
    }

    private static async Task AcquireMigrationLockAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            DECLARE @Result int;
            EXEC @Result = sys.sp_getapplock
                @Resource = N'RelayHook.SchemaMigration',
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 60000;
            SELECT @Result;
            """;

        await using var command = new SqlCommand(sql, connection, transaction);
        var result = Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
        if (result < 0)
        {
            throw new CallbackStorageException(
                "Could not acquire the Callback schema migration lock.",
                new TimeoutException($"sp_getapplock returned {result}."));
        }
    }

    private static async Task BootstrapVersionTableAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            IF SCHEMA_ID(N'Callback') IS NULL
                EXEC(N'CREATE SCHEMA [Callback] AUTHORIZATION [dbo]');

            IF OBJECT_ID(N'[Callback].[SchemaVersion]', N'U') IS NULL
            BEGIN
                CREATE TABLE [Callback].[SchemaVersion]
                (
                    [Id] tinyint NOT NULL CONSTRAINT [PK_Callback_SchemaVersion] PRIMARY KEY,
                    [Version] int NOT NULL,
                    [AppliedAt] datetimeoffset(7) NOT NULL,
                    CONSTRAINT [CK_Callback_SchemaVersion_Singleton] CHECK ([Id] = 1)
                );
                INSERT INTO [Callback].[SchemaVersion] ([Id], [Version], [AppliedAt])
                VALUES (1, 0, SYSUTCDATETIME());
            END;
            """;

        await using var command = new SqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> ReadVersionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT [Version] FROM [Callback].[SchemaVersion] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = 1;";
        await using var command = new SqlCommand(sql, connection, transaction);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteMigrationAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int version,
        CancellationToken cancellationToken)
    {
        var suffix = MigrationResourceSuffixes[version - 1];
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(suffix, StringComparison.Ordinal));
        await using var stream = assembly.GetManifestResourceStream(resourceName) ??
                                 throw new InvalidOperationException("Embedded SQL migration is missing.");
        using var reader = new StreamReader(stream);
        var sql = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task SetVersionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int version,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE [Callback].[SchemaVersion]
            SET [Version] = @Version, [AppliedAt] = SYSUTCDATETIME()
            WHERE [Id] = 1;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@Version", SqlDbType.Int).Value = version;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() => _gate.Dispose();
}
