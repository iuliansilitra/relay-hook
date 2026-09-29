using System.Net.Http.Headers;
using RelayHook.Core.Abstractions;

namespace RelayHook.Authentication.Bearer;

internal sealed class BearerCallbackAuthenticator : ICallbackAuthenticator
{
    public async ValueTask AuthenticateAsync(
        HttpRequestMessage request,
        CallbackAuthenticationContext context,
        CancellationToken cancellationToken)
    {
        var options = AuthenticationJson.Deserialize<BearerAuthenticationOptions>(context.Configuration);
        if (string.IsNullOrWhiteSpace(options.TokenSecretReference))
        {
            throw new CallbackConfigurationException("Bearer token secret reference is required.");
        }

        var token = await context.SecretProvider
            .GetSecretAsync(options.TokenSecretReference, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token) || token.Any(char.IsControl))
        {
            throw new CallbackAuthenticationException("Bearer token could not be resolved.");
        }

        try
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        catch (FormatException exception)
        {
            throw new CallbackAuthenticationException("Bearer token format is invalid.", exception);
        }
    }
}
