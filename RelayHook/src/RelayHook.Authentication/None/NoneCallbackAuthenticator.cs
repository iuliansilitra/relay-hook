using RelayHook.Core.Abstractions;

namespace RelayHook.Authentication.None;

internal sealed class NoneCallbackAuthenticator : ICallbackAuthenticator
{
    public ValueTask AuthenticateAsync(
        HttpRequestMessage request,
        CallbackAuthenticationContext context,
        CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
