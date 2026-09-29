namespace RelayHook.Authentication.ApiKey;

/// <summary>API-key header authentication settings.</summary>
public sealed class ApiKeyAuthenticationOptions
{
    /// <summary>Request header containing the resolved key.</summary>
    public string HeaderName { get; set; } = "X-API-Key";

    /// <summary>Reference passed to <c>ICallbackSecretProvider</c>.</summary>
    public string SecretReference { get; set; } = string.Empty;
}
