using System.Text.Json;

namespace RelayHook.Authentication;

internal static class AuthenticationJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static T Deserialize<T>(JsonElement element) where T : class =>
        element.Deserialize<T>(Options) ??
        throw new RelayHook.Core.Abstractions.CallbackConfigurationException(
            $"Authentication configuration for {typeof(T).Name} is missing.");
}
