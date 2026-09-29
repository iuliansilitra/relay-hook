using System.Text.Json;

namespace RelayHook.Core.Configuration;

internal sealed record CallbackEndpointDefinition(
    string Name,
    Uri Url,
    HttpMethod Method,
    string ContentType,
    TimeSpan Timeout,
    string AuthenticationType,
    JsonElement AuthenticationConfiguration,
    IReadOnlyDictionary<string, string> Headers);
