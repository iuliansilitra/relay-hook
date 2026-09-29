using System.Text.Json;
using RelayHook.Authentication;

namespace RelayHook.AspNetCore.Configuration;

internal sealed class RelayHookRegistration
{
    public CallbackClientRegistry Clients { get; } = new();

    public CallbackAuthenticatorRegistry Authenticators { get; } = new();

    public JsonSerializerOptions JsonSerializerOptions { get; } = new(JsonSerializerDefaults.Web);
}
