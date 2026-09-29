namespace RelayHook.Core.Abstractions;

/// <summary>Applies one authentication strategy to an outbound callback request.</summary>
public interface ICallbackAuthenticator
{
    /// <summary>Applies authentication without logging or persisting resolved secrets.</summary>
    ValueTask AuthenticateAsync(
        HttpRequestMessage request,
        CallbackAuthenticationContext context,
        CancellationToken cancellationToken);
}
