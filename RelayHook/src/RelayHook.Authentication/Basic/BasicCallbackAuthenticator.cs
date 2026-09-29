using System.Net.Http.Headers;
using System.Text;
using RelayHook.Core.Abstractions;

namespace RelayHook.Authentication.Basic;

internal sealed class BasicCallbackAuthenticator : ICallbackAuthenticator
{
    public async ValueTask AuthenticateAsync(
        HttpRequestMessage request,
        CallbackAuthenticationContext context,
        CancellationToken cancellationToken)
    {
        var options = AuthenticationJson.Deserialize<BasicAuthenticationOptions>(context.Configuration);
        if (string.IsNullOrWhiteSpace(options.Username) ||
            string.IsNullOrWhiteSpace(options.PasswordSecretReference) ||
            options.Username.Contains(':', StringComparison.Ordinal))
        {
            throw new CallbackConfigurationException(
                "Basic authentication requires a username without ':' and a password secret reference.");
        }

        var password = await context.SecretProvider
            .GetSecretAsync(options.PasswordSecretReference, cancellationToken)
            .ConfigureAwait(false);
        if (password is null)
        {
            throw new CallbackAuthenticationException("Basic authentication password could not be resolved.");
        }

        var bytes = Encoding.UTF8.GetBytes($"{options.Username}:{password}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes));
    }
}
