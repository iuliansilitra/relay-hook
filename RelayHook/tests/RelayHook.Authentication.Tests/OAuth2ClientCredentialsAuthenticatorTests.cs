using System.Net;
using System.Text.Json;
using RelayHook.Authentication.OAuth;
using RelayHook.Core.Abstractions;

namespace RelayHook.Authentication.Tests;

public sealed class OAuth2ClientCredentialsAuthenticatorTests
{
    [Fact]
    public async Task AuthenticateAsync_WhenCalledTwice_ShouldReuseCachedToken()
    {
        var tokenRequestCount = 0;
        var handler = new DelegateHttpMessageHandler((_, _) =>
        {
            Interlocked.Increment(ref tokenRequestCount);
            return Task.FromResult(DelegateHttpMessageHandler.Json(
                HttpStatusCode.OK,
                "{\"access_token\":\"cached-token\",\"expires_in\":300}"));
        });
        var authenticator = new OAuth2ClientCredentialsAuthenticator(
            new StubHttpClientFactory(handler),
            new OAuthTokenCache());
        var context = CreateContext();
        using var first = new HttpRequestMessage();
        using var second = new HttpRequestMessage();

        await authenticator.AuthenticateAsync(first, context, CancellationToken.None);
        await authenticator.AuthenticateAsync(second, context, CancellationToken.None);

        Assert.Equal(1, tokenRequestCount);
        Assert.Equal("cached-token", first.Headers.Authorization?.Parameter);
        Assert.Equal("cached-token", second.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task InvalidateAsync_WhenTokenCached_ShouldAcquireNewToken()
    {
        var tokenRequestCount = 0;
        var handler = new DelegateHttpMessageHandler((_, _) =>
            Task.FromResult(DelegateHttpMessageHandler.Json(
                HttpStatusCode.OK,
                $"{{\"access_token\":\"token-{Interlocked.Increment(ref tokenRequestCount)}\",\"expires_in\":300}}")));
        var authenticator = new OAuth2ClientCredentialsAuthenticator(
            new StubHttpClientFactory(handler),
            new OAuthTokenCache());
        var context = CreateContext();
        using var first = new HttpRequestMessage();
        using var second = new HttpRequestMessage();

        await authenticator.AuthenticateAsync(first, context, CancellationToken.None);
        await authenticator.InvalidateAsync(first, context, CancellationToken.None);
        await authenticator.AuthenticateAsync(second, context, CancellationToken.None);

        Assert.Equal(2, tokenRequestCount);
        Assert.Equal("token-2", second.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task AuthenticateAsync_WhenTokenRequestTimesOut_ShouldClassifyAuthenticationFailure()
    {
        var handler = new DelegateHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var authenticator = new OAuth2ClientCredentialsAuthenticator(
            new StubHttpClientFactory(handler),
            new OAuthTokenCache());
        var context = CreateContext(TimeSpan.FromMilliseconds(25));
        using var request = new HttpRequestMessage();

        var exception = await Assert.ThrowsAsync<CallbackAuthenticationException>(async () =>
            await authenticator.AuthenticateAsync(request, context, CancellationToken.None));

        Assert.Equal("OAuth token request timed out.", exception.Message);
    }

    [Fact]
    public async Task AuthenticateAsync_WhenTokenResponseIsMalformed_ShouldClassifyAuthenticationFailure()
    {
        var handler = new DelegateHttpMessageHandler((_, _) => Task.FromResult(
            DelegateHttpMessageHandler.Json(HttpStatusCode.OK, "not-json")));
        var authenticator = new OAuth2ClientCredentialsAuthenticator(
            new StubHttpClientFactory(handler),
            new OAuthTokenCache());
        using var request = new HttpRequestMessage();

        var exception = await Assert.ThrowsAsync<CallbackAuthenticationException>(async () =>
            await authenticator.AuthenticateAsync(request, CreateContext(), CancellationToken.None));

        Assert.Equal("OAuth token acquisition failed.", exception.Message);
    }

    private static CallbackAuthenticationContext CreateContext(TimeSpan? requestTimeout = null) =>
        new(
            "callback",
            JsonSerializer.SerializeToElement(
                new OAuth2ClientCredentialsOptions
                {
                    TokenEndpoint = "https://identity.example.com/token",
                    ClientId = "client",
                    ClientSecretReference = "oauth-secret",
                    Scopes = ["callbacks.write"],
                    RequestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30)
                },
                AuthenticationJson.Options),
            new DictionarySecretProvider(
                new Dictionary<string, string> { ["oauth-secret"] = "secret" }),
            new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
}
