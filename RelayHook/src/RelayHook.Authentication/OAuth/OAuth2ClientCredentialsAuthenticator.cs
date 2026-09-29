using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RelayHook.Core.Abstractions;

namespace RelayHook.Authentication.OAuth;

internal sealed class OAuth2ClientCredentialsAuthenticator(
    IHttpClientFactory httpClientFactory,
    OAuthTokenCache tokenCache) : ICallbackAuthenticator, IRefreshableCallbackAuthenticator
{
    private const string HttpClientName = "RelayHook.OAuth";

    public async ValueTask AuthenticateAsync(
        HttpRequestMessage request,
        CallbackAuthenticationContext context,
        CancellationToken cancellationToken)
    {
        var options = GetAndValidateOptions(context);
        var key = GetCacheKey(context);
        OAuthToken token;
        try
        {
            token = await tokenCache.GetOrCreateAsync(
                key,
                context.TimeProvider.GetUtcNow(),
                options.RefreshBeforeExpiry,
                ct => AcquireTokenAsync(options, context, ct),
                cancellationToken).ConfigureAwait(false);
        }
        catch (CallbackAuthenticationException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            throw new CallbackAuthenticationException("OAuth token acquisition failed.", exception);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
    }

    public async ValueTask InvalidateAsync(
        HttpRequestMessage rejectedRequest,
        CallbackAuthenticationContext context,
        CancellationToken cancellationToken)
    {
        var rejectedToken = rejectedRequest.Headers.Authorization?.Parameter;
        if (rejectedToken is not null)
        {
            await tokenCache.InvalidateAsync(
                GetCacheKey(context),
                rejectedToken,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<OAuthToken> AcquireTokenAsync(
        OAuth2ClientCredentialsOptions options,
        CallbackAuthenticationContext context,
        CancellationToken cancellationToken)
    {
        var secret = await context.SecretProvider
            .GetSecretAsync(options.ClientSecretReference, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrEmpty(secret))
        {
            throw new CallbackAuthenticationException("OAuth client secret could not be resolved.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, options.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(
            [
                new("grant_type", "client_credentials"),
                new("client_id", options.ClientId),
                new("client_secret", secret),
                new("scope", string.Join(' ', options.Scopes))
            ])
        };

        using var timeoutCts = new CancellationTokenSource(options.RequestTimeout, context.TimeProvider);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token);

        HttpResponseMessage response;
        try
        {
            response = await httpClientFactory.CreateClient(HttpClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new CallbackAuthenticationException("OAuth token request timed out.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new CallbackAuthenticationException(
                    $"OAuth token endpoint returned HTTP {(int)response.StatusCode}.");
            }

            await response.Content.LoadIntoBufferAsync(65_536, linkedCts.Token).ConfigureAwait(false);
            await using var stream = await response.Content.ReadAsStreamAsync(linkedCts.Token).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: linkedCts.Token)
                .ConfigureAwait(false);
            if (!document.RootElement.TryGetProperty("access_token", out var accessTokenElement) ||
                string.IsNullOrWhiteSpace(accessTokenElement.GetString()))
            {
                throw new CallbackAuthenticationException("OAuth token response did not contain access_token.");
            }

            var expiresIn = document.RootElement.TryGetProperty("expires_in", out var expiryElement) &&
                            expiryElement.TryGetInt32(out var seconds)
                ? Math.Max(1, seconds)
                : 300;

            return new OAuthToken(
                accessTokenElement.GetString()!,
                context.TimeProvider.GetUtcNow().AddSeconds(expiresIn));
        }
    }

    private static OAuth2ClientCredentialsOptions GetAndValidateOptions(CallbackAuthenticationContext context)
    {
        var options = AuthenticationJson.Deserialize<OAuth2ClientCredentialsOptions>(context.Configuration);
        if (!Uri.TryCreate(options.TokenEndpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(options.ClientId) ||
            string.IsNullOrWhiteSpace(options.ClientSecretReference) ||
            options.RefreshBeforeExpiry < TimeSpan.Zero ||
            options.RequestTimeout <= TimeSpan.Zero ||
            options.Scopes is null ||
            options.Scopes.Any(scope =>
                string.IsNullOrWhiteSpace(scope) || scope.Any(char.IsWhiteSpace)))
        {
            throw new CallbackConfigurationException(
                "OAuth requires an HTTPS token endpoint, client ID, client-secret reference, valid scope tokens, non-negative refresh window, and positive request timeout.");
        }

        return options;
    }

    private static string GetCacheKey(CallbackAuthenticationContext context)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(context.Configuration.GetRawText()));
        return Convert.ToHexString(bytes);
    }
}
