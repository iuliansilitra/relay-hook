using RelayHook.Core.Abstractions;

namespace RelayHook.AspNetCore.Delivery;

internal static class CallbackHeaderPolicy
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Proxy-Authorization", "Host", "Content-Length", "Transfer-Encoding",
        "Cookie", "Set-Cookie", "User-Agent", "X-Callback-Id", "X-Idempotency-Key",
        "X-Callback-Event", "X-Correlation-Id"
    };

    public static void Apply(HttpRequestMessage request, IReadOnlyDictionary<string, string> headers)
    {
        foreach (var header in headers)
        {
            if (Reserved.Contains(header.Key) ||
                header.Value.Any(char.IsControl) ||
                !request.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                throw new CallbackConfigurationException($"Custom callback header '{header.Key}' is not allowed.");
            }
        }
    }
}
