using System.Text.Json;
using RelayHook.Core.Abstractions;

namespace RelayHook.Core.Serialization;

internal sealed class SystemTextJsonCallbackSerializer(JsonSerializerOptions options) : ICallbackSerializer
{
    private readonly JsonSerializerOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    public string Serialize<TPayload>(TPayload payload) => JsonSerializer.Serialize(payload, _options);
}
