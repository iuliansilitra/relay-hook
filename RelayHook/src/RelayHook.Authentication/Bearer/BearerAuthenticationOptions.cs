namespace RelayHook.Authentication.Bearer;

/// <summary>Static bearer-token authentication settings.</summary>
public sealed class BearerAuthenticationOptions
{
    /// <summary>Static bearer-token secret reference.</summary>
    public string TokenSecretReference { get; set; } = string.Empty;
}
