using RelayHook.Core.Abstractions;

namespace RelayHook.Authentication.ApiKey;

internal sealed class ApiKeyCallbackAuthenticator : ICallbackAuthenticator
{
    private static readonly HashSet<string> ReservedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Proxy-Authorization", "Host", "Content-Length", "Transfer-Encoding",
        "Cookie", "Set-Cookie", "User-Agent", "X-Callback-Id", "X-Idempotency-Key",
        "X-Callback-Event", "X-Correlation-Id"
    };

    public async ValueTask AuthenticateAsync(
        HttpRequestMessage request,
        CallbackAuthenticationContext context,
        CancellationToken cancellationToken)
    {
        var options = AuthenticationJson.Deserialize<ApiKeyAuthenticationOptions>(context.Configuration);
        if (string.IsNullOrWhiteSpace(options.HeaderName) || string.IsNullOrWhiteSpace(options.SecretReference))
        {
            throw new CallbackConfigurationException("API-key header name and secret reference are required.");
        }

        if (ReservedHeaders.Contains(options.HeaderName))
        {
            throw new CallbackConfigurationException("API-key authentication header is reserved.");
        }

        var secret = await context.SecretProvider.GetSecretAsync(options.SecretReference, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrEmpty(secret) || secret.Any(char.IsControl))
        {
            throw new CallbackAuthenticationException("API-key secret could not be resolved.");
        }

        if (!request.Headers.TryAddWithoutValidation(options.HeaderName, secret))
        {
            throw new CallbackConfigurationException("API-key header name is invalid.");
        }
    }
}
