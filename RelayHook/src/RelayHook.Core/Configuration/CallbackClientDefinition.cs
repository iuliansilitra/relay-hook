namespace RelayHook.Core.Configuration;

internal sealed record CallbackClientDefinition(
    string Name,
    IReadOnlyDictionary<string, CallbackEndpointDefinition> Endpoints);
