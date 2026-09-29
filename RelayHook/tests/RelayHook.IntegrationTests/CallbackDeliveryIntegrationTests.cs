using System.Data;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RelayHook.Core.Abstractions;
using RelayHook.SqlServer;
using RelayHook.SqlServer.Migrations;
using RelayHook.SqlServer.Storage;

namespace RelayHook.IntegrationTests;

public sealed class CallbackDeliveryIntegrationTests
{
    private const string ConnectionStringVariable = "RELAYHOOK_SQLSERVER";

    [SqlServerFact]
    public Task Worker_WhenEndpointReturns500Then204_ShouldRetryAndComplete() =>
        VerifyRetryableStatusAsync(HttpStatusCode.InternalServerError);

    [SqlServerFact]
    public Task Worker_WhenEndpointReturns429Then204_ShouldRetryAndComplete() =>
        VerifyRetryableStatusAsync(HttpStatusCode.TooManyRequests);

    [SqlServerFact]
    public async Task Worker_WhenEndpointReturns400_ShouldFailWithoutRetry()
    {
        var requests = 0;
        var firstRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await RunDeliveryAsync(
            endpoint =>
            {
                Interlocked.Increment(ref requests);
                firstRequest.TrySetResult();
                endpoint.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                return Task.CompletedTask;
            },
            TimeSpan.FromSeconds(1),
            async (connectionString, jobId) =>
            {
                await firstRequest.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await WaitForStatusAsync(connectionString, jobId, expectedStatus: 3);
                Assert.Equal(1, requests);
                Assert.Equal(1, await ReadAttemptCountAsync(connectionString, jobId));
            });
    }

    [SqlServerFact]
    public async Task Worker_WhenFirstRequestTimesOut_ShouldRetryAndComplete()
    {
        var requests = 0;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await RunDeliveryAsync(
            async endpoint =>
            {
                var count = Interlocked.Increment(ref requests);
                if (count == 1)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250));
                }

                endpoint.Response.StatusCode = (int)HttpStatusCode.NoContent;
                if (count >= 2) completed.TrySetResult();
            },
            TimeSpan.FromMilliseconds(50),
            async (connectionString, jobId) =>
            {
                await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await WaitForStatusAsync(connectionString, jobId, expectedStatus: 2);
                Assert.Equal(2, requests);
                Assert.Equal(2, await ReadAttemptCountAsync(connectionString, jobId));
            });
    }

    [SqlServerFact]
    public async Task Worker_WhenBatchExceedsConcurrency_ShouldClaimOnlyProcessingCapacity()
    {
        var connectionString = GetConnectionString();
        var firstRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverBuilder = WebApplication.CreateSlimBuilder();
        serverBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var server = serverBuilder.Build();
        server.MapPost("/callbacks", async context =>
        {
            firstRequest.TrySetResult();
            await release.Task.WaitAsync(context.RequestAborted);
            context.Response.StatusCode = StatusCodes.Status204NoContent;
        });
        await server.StartAsync();
        var address = GetServerAddress(server);
        var clientName = $"batch-{Guid.NewGuid():N}";
        var hostBuilder = Host.CreateApplicationBuilder();
        hostBuilder.Services
            .AddRelayHook(options =>
            {
                options.PollingInterval = TimeSpan.FromMilliseconds(20);
                options.BatchSize = 100;
                options.MaxConcurrentCallbacks = 1;
                options.LeaseDuration = TimeSpan.FromSeconds(2);
                options.UrlSecurity.AllowHttp = true;
                options.UrlSecurity.AllowedHosts.Add("127.0.0.1");
            })
            .UseSqlServer(connectionString)
            .AddClient(clientName, client => client.AddEndpoint("primary", endpoint =>
            {
                endpoint.Url = new Uri($"{address}/callbacks");
                endpoint.UseNoAuthentication();
            }));

        using var host = hostBuilder.Build();
        await host.StartAsync();
        var callbackClient = host.Services.GetRequiredService<ICallbackClient>();
        var jobIds = new List<Guid>();
        for (var index = 0; index < 5; index++)
        {
            jobIds.Add(await callbackClient.EnqueueAsync(
                clientName,
                "batch.test",
                new { Index = index },
                CancellationToken.None));
        }

        try
        {
            await firstRequest.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var counts = await ReadStatusCountsAsync(connectionString, clientName);
            Assert.Equal(1, counts.Processing);
            Assert.Equal(4, counts.Pending);

            release.SetResult();
            await WaitForAllStatusAsync(connectionString, clientName, expectedStatus: 2, expectedCount: 5);
        }
        finally
        {
            release.TrySetResult();
            await host.StopAsync();
            await server.StopAsync();
            await DeleteClientGraphAsync(connectionString, clientName);
        }
    }

    [SqlServerFact]
    public async Task Worker_WhenCallbackExceedsInitialLease_ShouldRenewOwnership()
    {
        var connectionString = GetConnectionString();
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverBuilder = WebApplication.CreateSlimBuilder();
        serverBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var server = serverBuilder.Build();
        server.MapPost("/callbacks", async context =>
        {
            requestStarted.TrySetResult();
            await release.Task.WaitAsync(context.RequestAborted);
            context.Response.StatusCode = StatusCodes.Status204NoContent;
        });
        await server.StartAsync();
        var address = GetServerAddress(server);
        var clientName = $"lease-{Guid.NewGuid():N}";
        var hostBuilder = Host.CreateApplicationBuilder();
        hostBuilder.Services
            .AddRelayHook(options =>
            {
                options.PollingInterval = TimeSpan.FromMilliseconds(20);
                options.LeaseDuration = TimeSpan.FromMilliseconds(300);
                options.UrlSecurity.AllowHttp = true;
                options.UrlSecurity.AllowedHosts.Add("127.0.0.1");
            })
            .UseSqlServer(connectionString)
            .AddClient(clientName, client => client.AddEndpoint("primary", endpoint =>
            {
                endpoint.Url = new Uri($"{address}/callbacks");
                endpoint.Timeout = TimeSpan.FromSeconds(5);
                endpoint.UseNoAuthentication();
            }));

        using var host = hostBuilder.Build();
        await host.StartAsync();
        var jobId = await host.Services.GetRequiredService<ICallbackClient>().EnqueueAsync(
            clientName,
            "lease.test",
            new { Value = 42 },
            CancellationToken.None);

        try
        {
            await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var originalLockedUntil = await ReadLockedUntilAsync(connectionString, jobId);
            var renewedLockedUntil = await WaitForLeaseRenewalAsync(
                connectionString,
                jobId,
                originalLockedUntil);
            Assert.True(renewedLockedUntil > originalLockedUntil);

            var storageOptions = new SqlServerStorageOptions(connectionString);
            using var installer = new SqlSchemaInstaller(storageOptions);
            var competitor = new SqlCallbackStorage(storageOptions, installer);
            await competitor.InitializeAsync(CancellationToken.None);
            var claims = await competitor.ClaimAsync(
                "competitor",
                1,
                originalLockedUntil.AddMilliseconds(1),
                TimeSpan.FromMinutes(1),
                CancellationToken.None);
            Assert.Empty(claims);

            release.SetResult();
            await WaitForStatusAsync(connectionString, jobId, expectedStatus: 2);
        }
        finally
        {
            release.TrySetResult();
            await host.StopAsync();
            await server.StopAsync();
            await DeleteJobGraphAsync(connectionString, jobId);
        }
    }

    private static async Task VerifyRetryableStatusAsync(HttpStatusCode firstStatus)
    {
        var requests = 0;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await RunDeliveryAsync(
            endpoint =>
            {
                var count = Interlocked.Increment(ref requests);
                endpoint.Response.StatusCode = count == 1
                    ? (int)firstStatus
                    : (int)HttpStatusCode.NoContent;
                if (count >= 2) completed.TrySetResult();
                return Task.CompletedTask;
            },
            TimeSpan.FromSeconds(1),
            async (connectionString, jobId) =>
            {
                await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await WaitForStatusAsync(connectionString, jobId, expectedStatus: 2);
                Assert.Equal(2, requests);
                Assert.Equal(2, await ReadAttemptCountAsync(connectionString, jobId));
            });
    }

    private static async Task RunDeliveryAsync(
        RequestDelegate endpoint,
        TimeSpan requestTimeout,
        Func<string, Guid, Task> assert)
    {
        var connectionString = GetConnectionString();
        var serverBuilder = WebApplication.CreateSlimBuilder();
        serverBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var server = serverBuilder.Build();
        server.MapPost("/callbacks", endpoint);
        await server.StartAsync();
        var address = GetServerAddress(server);

        var clientName = $"integration-{Guid.NewGuid():N}";
        var hostBuilder = Host.CreateApplicationBuilder();
        hostBuilder.Services
            .AddRelayHook(options =>
            {
                options.PollingInterval = TimeSpan.FromMilliseconds(20);
                options.LeaseDuration = TimeSpan.FromSeconds(5);
                options.Retry.Delays = [TimeSpan.FromMilliseconds(50)];
                options.Retry.JitterFactor = 0;
                options.UrlSecurity.AllowHttp = true;
                options.UrlSecurity.AllowedHosts.Add("127.0.0.1");
            })
            .UseSqlServer(connectionString)
            .AddClient(clientName, client => client.AddEndpoint("primary", configuredEndpoint =>
            {
                configuredEndpoint.Url = new Uri($"{address}/callbacks");
                configuredEndpoint.Timeout = requestTimeout;
                configuredEndpoint.UseNoAuthentication();
            }));

        using var host = hostBuilder.Build();
        await host.StartAsync();
        var jobId = await host.Services.GetRequiredService<ICallbackClient>().EnqueueAsync(
            clientName,
            "integration.test",
            new { Value = 42 },
            CancellationToken.None);
        try
        {
            await assert(connectionString, jobId);
        }
        finally
        {
            await host.StopAsync();
            await server.StopAsync();
            await DeleteJobGraphAsync(connectionString, jobId);
        }
    }

    private static string GetConnectionString() =>
        Environment.GetEnvironmentVariable(ConnectionStringVariable) ??
        throw new InvalidOperationException($"{ConnectionStringVariable} is required.");

    private static string GetServerAddress(WebApplication server) =>
        server.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single();

    private static async Task<(int Pending, int Processing)> ReadStatusCountsAsync(
        string connectionString,
        string clientName)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        const string sql = """
            SELECT
                SUM(CASE WHEN [Job].[Status] = 0 THEN 1 ELSE 0 END),
                SUM(CASE WHEN [Job].[Status] = 1 THEN 1 ELSE 0 END)
            FROM [Callback].[Job] AS [Job]
            INNER JOIN [Callback].[Client] AS [Client] ON [Client].[Id] = [Job].[ClientId]
            WHERE [Client].[Name] = @ClientName;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@ClientName", SqlDbType.NVarChar, 200).Value = clientName;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    private static async Task WaitForAllStatusAsync(
        string connectionString,
        string clientName,
        byte expectedStatus,
        int expectedCount)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!timeout.IsCancellationRequested)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(timeout.Token);
            const string sql = """
                SELECT COUNT(*)
                FROM [Callback].[Job] AS [Job]
                INNER JOIN [Callback].[Client] AS [Client] ON [Client].[Id] = [Job].[ClientId]
                WHERE [Client].[Name] = @ClientName AND [Job].[Status] = @Status;
                """;
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@ClientName", SqlDbType.NVarChar, 200).Value = clientName;
            command.Parameters.Add("@Status", SqlDbType.TinyInt).Value = expectedStatus;
            if (Convert.ToInt32(
                    await command.ExecuteScalarAsync(timeout.Token),
                    System.Globalization.CultureInfo.InvariantCulture) == expectedCount)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }

        throw new TimeoutException($"Callbacks for {clientName} did not reach status {expectedStatus}.");
    }

    private static async Task<DateTimeOffset> ReadLockedUntilAsync(string connectionString, Guid jobId)
    {
        var result = await ExecuteScalarAsync(
            connectionString,
            "SELECT [LockedUntil] FROM [Callback].[Job] WHERE [Id] = @JobId;",
            jobId);
        return (DateTimeOffset)result;
    }

    private static async Task<DateTimeOffset> WaitForLeaseRenewalAsync(
        string connectionString,
        Guid jobId,
        DateTimeOffset originalLockedUntil)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!timeout.IsCancellationRequested)
        {
            var current = await ReadLockedUntilAsync(connectionString, jobId);
            if (current > originalLockedUntil)
            {
                return current;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }

        throw new TimeoutException($"Callback {jobId} did not renew its lease.");
    }

    private static async Task WaitForStatusAsync(string connectionString, Guid jobId, byte expectedStatus)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!timeout.IsCancellationRequested)
        {
            if (await ReadStatusAsync(connectionString, jobId) == expectedStatus) return;
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }

        throw new TimeoutException($"Callback {jobId} did not reach status {expectedStatus}.");
    }

    private static async Task<int> ReadAttemptCountAsync(string connectionString, Guid jobId)
    {
        var result = await ExecuteScalarAsync(
            connectionString,
            "SELECT COUNT(*) FROM [Callback].[Attempt] WHERE [JobId] = @JobId;",
            jobId);
        return (int)result;
    }

    private static async Task<byte> ReadStatusAsync(string connectionString, Guid jobId)
    {
        var result = await ExecuteScalarAsync(
            connectionString,
            "SELECT [Status] FROM [Callback].[Job] WHERE [Id] = @JobId;",
            jobId);
        return (byte)result;
    }

    private static async Task<object> ExecuteScalarAsync(string connectionString, string sql, Guid jobId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@JobId", SqlDbType.UniqueIdentifier).Value = jobId;
        return (await command.ExecuteScalarAsync())!;
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

    private static async Task DeleteClientGraphAsync(string connectionString, string clientName)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        const string sql = """
            DECLARE @ClientId uniqueidentifier;
            SELECT @ClientId = [Id] FROM [Callback].[Client] WHERE [Name] = @ClientName;
            DELETE FROM [Callback].[Job] WHERE [ClientId] = @ClientId;
            DELETE FROM [Callback].[Endpoint] WHERE [ClientId] = @ClientId;
            DELETE FROM [Callback].[Client] WHERE [Id] = @ClientId;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@ClientName", SqlDbType.NVarChar, 200).Value = clientName;
        await command.ExecuteNonQueryAsync();
    }
}
