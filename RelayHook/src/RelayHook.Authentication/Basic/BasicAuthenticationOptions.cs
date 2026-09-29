namespace RelayHook.Authentication.Basic;

/// <summary>Basic authentication settings.</summary>
public sealed class BasicAuthenticationOptions
{
    /// <summary>Basic-authentication username.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Password secret reference.</summary>
    public string PasswordSecretReference { get; set; } = string.Empty;
}
