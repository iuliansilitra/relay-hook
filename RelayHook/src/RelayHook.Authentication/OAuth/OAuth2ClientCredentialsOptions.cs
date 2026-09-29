namespace RelayHook.Authentication.OAuth;

/// <summary>OAuth2 client-credentials settings.</summary>
public sealed class OAuth2ClientCredentialsOptions
{
    /// <summary>HTTPS OAuth token endpoint.</summary>
    public string TokenEndpoint { get; set; } = string.Empty;

    /// <summary>OAuth client identifier.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>OAuth client-secret reference.</summary>
    public string ClientSecretReference { get; set; } = string.Empty;

    /// <summary>Requested OAuth scopes.</summary>
    public IList<string> Scopes { get; set; } = [];

    /// <summary>Window before expiry in which a token is refreshed.</summary>
    public TimeSpan RefreshBeforeExpiry { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Maximum duration of one token endpoint request.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
