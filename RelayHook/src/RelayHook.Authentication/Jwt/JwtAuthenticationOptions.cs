namespace RelayHook.Authentication.Jwt;

/// <summary>HS256 generated-JWT settings.</summary>
public sealed class JwtAuthenticationOptions
{
    /// <summary>JWT issuer.</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>JWT audience.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>Optional JWT subject.</summary>
    public string? Subject { get; set; }

    /// <summary>HS256 signing-key secret reference.</summary>
    public string SigningKeySecretReference { get; set; } = string.Empty;

    /// <summary>Generated token lifetime.</summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Additional non-reserved string claims.</summary>
    public IDictionary<string, string> Claims { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
}
