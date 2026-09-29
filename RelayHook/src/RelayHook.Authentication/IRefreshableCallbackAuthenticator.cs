using RelayHook.Core.Abstractions;

namespace RelayHook.Authentication;

internal interface IRefreshableCallbackAuthenticator
{
    ValueTask InvalidateAsync(
        HttpRequestMessage rejectedRequest,
        CallbackAuthenticationContext context,
        CancellationToken cancellationToken);
}
