using System.Text.Json;
using System.Text.Json.Serialization;

namespace RelayHook.SqlServer.Storage;

internal static class SqlStorageJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
