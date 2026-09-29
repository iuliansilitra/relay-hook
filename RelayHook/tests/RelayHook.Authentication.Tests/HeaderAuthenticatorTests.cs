using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using RelayHook.Authentication.ApiKey;
using RelayHook.Authentication.Basic;
using RelayHook.Authentication.Bearer;
using RelayHook.Core.Abstractions;

namespace RelayHook.Authentication.Tests;

public sealed class HeaderAuthenticatorTests
{
    private static readonly ICallbackSecretProvider Secrets = new DictionarySecretProvider(
        new Dictionary<string, string>
        {
            ["api-key"] = "key-value",
            ["password"] = "p@ss",
            ["token"] = "bearer-value"
        });

    [Fact]
    public async Task ApiKey_WhenSecretExists_ShouldAddConfiguredHeader()
    {
        var authenticator = new ApiKeyCallbackAuthenticator();
        using var request = new HttpRequestMessage();
        var context = CreateContext(new ApiKeyAuthenticationOptions
        {
            HeaderName = "X-Partner-Key",
            SecretReference = "api-key"
        });

        await authenticator.AuthenticateAsync(request, context, CancellationToken.None);

        Assert.Equal("key-value", request.Headers.GetValues("X-Partner-Key").Single());
    }

    [Fact]
    public async Task Basic_WhenSecretExists_ShouldEncodeUsernameAndPassword()
    {
        var authenticator = new BasicCallbackAuthenticator();
        using var request = new HttpRequestMessage();
        var context = CreateContext(new BasicAuthenticationOptions
        {
            Username = "partner",
            PasswordSecretReference = "password"
        });

        await authenticator.AuthenticateAsync(request, context, CancellationToken.None);

        Assert.Equal("Basic", request.Headers.Authorization?.Scheme);
        Assert.Equal(
            Convert.ToBase64String(Encoding.UTF8.GetBytes("partner:p@ss")),
            request.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task Bearer_WhenSecretExists_ShouldSetAuthorizationHeader()
    {
        var authenticator = new BearerCallbackAuthenticator();
        using var request = new HttpRequestMessage();
        var context = CreateContext(new BearerAuthenticationOptions
        {
            TokenSecretReference = "token"
        });

        await authenticator.AuthenticateAsync(request, context, CancellationToken.None);

        Assert.Equal(new AuthenticationHeaderValue("Bearer", "bearer-value"), request.Headers.Authorization);
    }

    [Fact]
    public async Task ApiKey_WhenHeaderIsReserved_ShouldRejectConfiguration()
    {
        var authenticator = new ApiKeyCallbackAuthenticator();
        using var request = new HttpRequestMessage();
        var context = CreateContext(new ApiKeyAuthenticationOptions
        {
            HeaderName = "Host",
            SecretReference = "api-key"
        });

        await Assert.ThrowsAsync<CallbackConfigurationException>(async () =>
            await authenticator.AuthenticateAsync(request, context, CancellationToken.None));
    }

    private static CallbackAuthenticationContext CreateContext<T>(T options) where T : class =>
        new(
            "callback",
            JsonSerializer.SerializeToElement(options, AuthenticationJson.Options),
            Secrets,
            new FixedTimeProvider(DateTimeOffset.UnixEpoch));
}
