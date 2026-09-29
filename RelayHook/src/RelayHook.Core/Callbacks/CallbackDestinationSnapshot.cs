using System.Text.Json;

namespace RelayHook.Core.Callbacks;

internal sealed record CallbackDestinationSnapshot(
    string ClientName,
    string EndpointName,
    string Url,
    string Method,
    string ContentType,
    double TimeoutSeconds,
    string AuthenticationType,
    JsonElement AuthenticationConfiguration,
    IReadOnlyDictionary<string, string> Headers);
