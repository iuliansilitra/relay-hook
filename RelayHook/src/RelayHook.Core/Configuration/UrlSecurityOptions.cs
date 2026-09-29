namespace RelayHook.Core.Configuration;

/// <summary>Default outbound URL restrictions.</summary>
public sealed class UrlSecurityOptions
{
    /// <summary>Allows clear-text HTTP endpoints. HTTPS remains the default.</summary>
    public bool AllowHttp { get; set; }

    /// <summary>Optional case-insensitive host allowlist. Empty permits public hosts.</summary>
    public ISet<string> AllowedHosts { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
