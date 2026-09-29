using System.Net;
using RelayHook.Core.Abstractions;
using RelayHook.Core.Configuration;

namespace RelayHook.AspNetCore.Delivery;

internal sealed class DefaultCallbackUrlPolicy(RelayHookOptions options) : ICallbackUrlPolicy
{
    public void Validate(Uri destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.IsAbsoluteUri ||
            (destination.Scheme != Uri.UriSchemeHttps &&
             !(options.UrlSecurity.AllowHttp && destination.Scheme == Uri.UriSchemeHttp)))
        {
            throw new CallbackConfigurationException("Callback URL must use HTTPS unless HTTP is explicitly enabled.");
        }

        if (!string.IsNullOrEmpty(destination.UserInfo))
        {
            throw new CallbackConfigurationException("Callback URL cannot contain user information.");
        }

        var explicitlyAllowed = options.UrlSecurity.AllowedHosts.Contains(destination.IdnHost);
        if (options.UrlSecurity.AllowedHosts.Count > 0 && !explicitlyAllowed)
        {
            throw new CallbackConfigurationException($"Callback host '{destination.IdnHost}' is not allowed.");
        }

        if (!explicitlyAllowed &&
            (destination.IsLoopback || destination.IdnHost.Equals("localhost", StringComparison.OrdinalIgnoreCase)))
        {
            throw new CallbackConfigurationException("Loopback callback destinations are not allowed.");
        }

        if (!explicitlyAllowed && IPAddress.TryParse(destination.IdnHost, out var address) && IsPrivate(address))
        {
            throw new CallbackConfigurationException("Private or link-local callback destinations are not allowed.");
        }
    }

    private static bool IsPrivate(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            return IsPrivate(address.MapToIPv4());
        }

        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal)
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return bytes[0] == 0 ||
                   bytes[0] == 10 ||
                   bytes[0] == 127 ||
                   bytes[0] == 100 && bytes[1] is >= 64 and <= 127 ||
                   bytes[0] == 169 && bytes[1] == 254 ||
                   bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
                   bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0 ||
                   bytes[0] == 192 && bytes[1] == 168 ||
                   bytes[0] == 198 && bytes[1] is 18 or 19 ||
                   bytes[0] >= 224;
        }

        return bytes.Length == 16 &&
               ((bytes[0] & 0xFE) == 0xFC ||
                bytes[0] == 0xFF ||
                bytes.All(static value => value == 0));
    }
}
