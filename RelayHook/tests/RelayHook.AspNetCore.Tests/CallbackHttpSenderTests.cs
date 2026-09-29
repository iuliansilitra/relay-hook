using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using RelayHook.AspNetCore.Delivery;
using RelayHook.Core.Abstractions;
using RelayHook.Core.Callbacks;

namespace RelayHook.AspNetCore.Tests;

public sealed class CallbackHttpSenderTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task SendAsync_WhenEndpointReturnsSuccess_ShouldComplete(HttpStatusCode statusCode)
    {
        var handler = new DelegateHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(statusCode)));
        await using var provider = CreateProvider(handler);

        var outcome = await provider.GetRequiredService<CallbackHttpSender>()
            .SendAsync(CreateJob(), CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.Equal((int)statusCode, outcome.HttpStatusCode);
    }

    [Theory]
    [InlineData("120", 120)]
    [InlineData("Thu, 01 Jan 2026 00:03:00 GMT", 180)]
    public async Task SendAsync_WhenRetryAfterIsValid_ShouldCaptureDelay(
        string header,
        int expectedSeconds)
    {
        var handler = new DelegateHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            response.Headers.TryAddWithoutValidation("Retry-After", header);
            return Task.FromResult(response);
        });
        await using var provider = CreateProvider(handler);

        var outcome = await provider.GetRequiredService<CallbackHttpSender>()
            .SendAsync(CreateJob(), CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), outcome.RetryAfter);
    }

    [Fact]
    public async Task SendAsync_WhenRetryAfterIsInvalid_ShouldIgnoreIt()
    {
        var handler = new DelegateHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.TryAddWithoutValidation("Retry-After", "not-a-delay");
            return Task.FromResult(response);
        });
        await using var provider = CreateProvider(handler);

        var outcome = await provider.GetRequiredService<CallbackHttpSender>()
            .SendAsync(CreateJob(), CancellationToken.None);

        Assert.Null(outcome.RetryAfter);
    }

    [Fact]
    public async Task SendAsync_WhenResponseIsLarge_ShouldReadOnlyConfiguredBytes()
    {
        var stream = new CountingStream(new byte[1_024]);
        var handler = new DelegateHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(stream)
            }));
        await using var provider = CreateProvider(handler, maximumResponseBytes: 32);

        var outcome = await provider.GetRequiredService<CallbackHttpSender>()
            .SendAsync(CreateJob(), CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.Equal(32, outcome.ResponseBody!.Length);
        Assert.Equal(32, stream.BytesRead);
    }

    [Fact]
    public async Task SendAsync_WhenResponseStreamFails_ShouldClassifyNetworkFailure()
    {
        var handler = new DelegateHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new FailingStream())
            }));
        await using var provider = CreateProvider(handler);

        var outcome = await provider.GetRequiredService<CallbackHttpSender>()
            .SendAsync(CreateJob(), CancellationToken.None);

        Assert.Equal(CallbackFailureType.Network, outcome.FailureType);
        Assert.Equal("Callback response transport failed.", outcome.ErrorMessage);
    }

    [Fact]
    public async Task SendAsync_WhenConnectionFails_ShouldClassifyAndRedactNetworkFailure()
    {
        var handler = new DelegateHandler((_, _) =>
            throw new HttpRequestException("transport included super-secret-token"));
        await using var provider = CreateProvider(handler);

        var outcome = await provider.GetRequiredService<CallbackHttpSender>()
            .SendAsync(CreateJob(), CancellationToken.None);

        Assert.Equal(CallbackFailureType.Network, outcome.FailureType);
        Assert.Equal("Callback HTTP transport failed.", outcome.ErrorMessage);
        Assert.DoesNotContain("super-secret-token", outcome.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_WhenHostCancellationRequested_ShouldPropagateCancellation()
    {
        var handler = new DelegateHandler((_, cancellationToken) =>
            Task.FromCanceled<HttpResponseMessage>(cancellationToken));
        await using var provider = CreateProvider(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.GetRequiredService<CallbackHttpSender>()
                .SendAsync(CreateJob(), cancellation.Token));
    }

    [Fact]
    public async Task SendAsync_WhenAuthenticatorLeaksInException_ShouldRedactSecret()
    {
        var handler = new DelegateHandler((_, _) =>
            throw new InvalidOperationException("HTTP must not be called."));
        await using var provider = CreateProvider(handler, authenticationType: "Throwing");

        var outcome = await provider.GetRequiredService<CallbackHttpSender>()
            .SendAsync(CreateJob("Throwing"), CancellationToken.None);

        Assert.Equal(CallbackFailureType.Authentication, outcome.FailureType);
        Assert.Equal("Callback authentication failed.", outcome.ErrorMessage);
        Assert.DoesNotContain(ThrowingAuthenticator.Secret, outcome.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_WhenConfigurationExceptionContainsSecret_ShouldRedactSecret()
    {
        var handler = new DelegateHandler((_, _) =>
            throw new InvalidOperationException("HTTP must not be called."));
        await using var provider = CreateProvider(handler, authenticationType: "BadConfiguration");

        var outcome = await provider.GetRequiredService<CallbackHttpSender>()
            .SendAsync(CreateJob("BadConfiguration"), CancellationToken.None);

        Assert.Equal(CallbackFailureType.Configuration, outcome.FailureType);
        Assert.Equal("Callback configuration is invalid.", outcome.ErrorMessage);
        Assert.DoesNotContain(ThrowingAuthenticator.Secret, outcome.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_WhenEndpointRedirects_ShouldNotFollowRedirect()
    {
        var collectorRequests = 0;
        var collectorBuilder = WebApplication.CreateSlimBuilder();
        collectorBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var collector = collectorBuilder.Build();
        collector.MapPost("/collect", context =>
        {
            Interlocked.Increment(ref collectorRequests);
            context.Response.StatusCode = 204;
            return Task.CompletedTask;
        });
        await collector.StartAsync();
        var collectorAddress = GetAddress(collector);

        var redirectBuilder = WebApplication.CreateSlimBuilder();
        redirectBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var redirect = redirectBuilder.Build();
        redirect.MapPost("/callback", context =>
        {
            context.Response.StatusCode = 302;
            context.Response.Headers.Location = $"{collectorAddress}/collect";
            return Task.CompletedTask;
        });
        await redirect.StartAsync();

        await using var provider = CreateProvider(handler: null, allowHttp: true);
        var outcome = await provider.GetRequiredService<CallbackHttpSender>()
            .SendAsync(CreateJob(url: $"{GetAddress(redirect)}/callback"), CancellationToken.None);

        Assert.Equal(302, outcome.HttpStatusCode);
        Assert.Equal(0, collectorRequests);

        await redirect.StopAsync();
        await collector.StopAsync();
    }

    [Fact]
    public async Task SendAsync_WhenRefreshableAuthenticationGets401_ShouldRefreshOnlyOnce()
    {
        var requests = 0;
        var handler = new DelegateHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(Interlocked.Increment(ref requests) == 1
                ? HttpStatusCode.Unauthorized
                : HttpStatusCode.NoContent)));
        await using var provider = CreateProvider(handler, authenticationType: "Refreshing");

        var outcome = await provider.GetRequiredService<CallbackHttpSender>()
            .SendAsync(CreateJob("Refreshing"), CancellationToken.None);
        var authenticator = provider.GetRequiredService<RefreshingAuthenticator>();

        Assert.True(outcome.Succeeded);
        Assert.Equal(2, requests);
        Assert.Equal(2, authenticator.AuthenticationCount);
        Assert.Equal(1, authenticator.InvalidationCount);
    }

    private static ServiceProvider CreateProvider(
        HttpMessageHandler? handler,
        int maximumResponseBytes = 16_384,
        string authenticationType = "Test",
        bool allowHttp = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        services.AddSingleton<ICallbackSecretProvider, EmptySecretProvider>();
        var builder = services.AddRelayHook(options =>
        {
            options.MaxPersistedResponseBytes = maximumResponseBytes;
            options.UrlSecurity.AllowHttp = allowHttp;
            options.UrlSecurity.AllowedHosts.Add(allowHttp ? "127.0.0.1" : "example.com");
        });
        builder.AddCallbackAuthenticator<TestAuthenticator>("Test");
        if (authenticationType == "Throwing")
        {
            builder.AddCallbackAuthenticator<ThrowingAuthenticator>("Throwing");
        }
        else if (authenticationType == "Refreshing")
        {
            builder.AddCallbackAuthenticator<RefreshingAuthenticator>("Refreshing");
        }
        else if (authenticationType == "BadConfiguration")
        {
            builder.AddCallbackAuthenticator<BadConfigurationAuthenticator>("BadConfiguration");
        }

        if (handler is not null)
        {
            services.AddHttpClient("RelayHook.Delivery")
                .ConfigurePrimaryHttpMessageHandler(() => handler);
        }

        return services.BuildServiceProvider();
    }

    private static CallbackJob CreateJob(
        string authenticationType = "Test",
        string url = "https://example.com/callback")
    {
        var id = Guid.NewGuid();
        return new CallbackJob(
            id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "event.created",
            "{}",
            "application/json",
            new CallbackDestinationSnapshot(
                "client",
                "primary",
                url,
                "POST",
                "application/json",
                30,
                authenticationType,
                System.Text.Json.JsonSerializer.SerializeToElement(new { }),
                new Dictionary<string, string>()),
            Now,
            Now,
            0,
            0,
            5,
            "correlation",
            id.ToString("N"),
            "worker",
            Now.AddMinutes(1));
    }

    private static string GetAddress(WebApplication application) =>
        application.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single();

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class EmptySecretProvider : ICallbackSecretProvider
    {
        public ValueTask<string?> GetSecretAsync(
            string secretReference,
            CancellationToken cancellationToken) => ValueTask.FromResult<string?>(null);
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private sealed class TestAuthenticator : ICallbackAuthenticator
    {
        public ValueTask AuthenticateAsync(
            HttpRequestMessage request,
            CallbackAuthenticationContext context,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class ThrowingAuthenticator : ICallbackAuthenticator
    {
        public const string Secret = "super-secret-token";

        public ValueTask AuthenticateAsync(
            HttpRequestMessage request,
            CallbackAuthenticationContext context,
            CancellationToken cancellationToken) =>
            throw new CallbackAuthenticationException($"Failed with {Secret}.");
    }

    private sealed class RefreshingAuthenticator :
        ICallbackAuthenticator,
        RelayHook.Authentication.IRefreshableCallbackAuthenticator
    {
        private int _authenticationCount;
        private int _invalidationCount;

        public int AuthenticationCount => _authenticationCount;

        public int InvalidationCount => _invalidationCount;

        public ValueTask AuthenticateAsync(
            HttpRequestMessage request,
            CallbackAuthenticationContext context,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _authenticationCount);
            return ValueTask.CompletedTask;
        }

        public ValueTask InvalidateAsync(
            HttpRequestMessage rejectedRequest,
            CallbackAuthenticationContext context,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _invalidationCount);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BadConfigurationAuthenticator : ICallbackAuthenticator
    {
        public ValueTask AuthenticateAsync(
            HttpRequestMessage request,
            CallbackAuthenticationContext context,
            CancellationToken cancellationToken) =>
            throw new CallbackConfigurationException(
                $"Configuration accidentally included {ThrowingAuthenticator.Secret}.");
    }

    private sealed class CountingStream(byte[] buffer) : MemoryStream(buffer)
    {
        public int BytesRead { get; private set; }

        public override ValueTask<int> ReadAsync(
            Memory<byte> destination,
            CancellationToken cancellationToken = default)
        {
            var read = base.Read(destination.Span);
            BytesRead += read;
            return ValueTask.FromResult(read);
        }
    }

    private sealed class FailingStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("sensitive transport detail"));
    }
}
