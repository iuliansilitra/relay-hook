using System.Text.Json;
using System.Text.Json.Serialization;
using RelayHook.Core.Serialization;

namespace RelayHook.Core.Tests;

public sealed class SystemTextJsonCallbackSerializerTests
{
    [Fact]
    public void Serialize_WhenOptionsConfigured_ShouldHonorHostOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        var serializer = new SystemTextJsonCallbackSerializer(options);

        var json = serializer.Serialize(new Payload("42", null));

        Assert.Equal("{\"identifier\":\"42\"}", json);
    }

    private sealed record Payload(string Identifier, string? Optional);
}
