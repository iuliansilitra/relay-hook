namespace RelayHook.Core.Abstractions;

/// <summary>Indicates that callback authentication could not be applied.</summary>
public sealed class CallbackAuthenticationException : Exception
{
    /// <summary>Creates an authentication exception.</summary>
    public CallbackAuthenticationException(string message) : base(message)
    {
    }

    /// <summary>Creates an authentication exception with its cause.</summary>
    public CallbackAuthenticationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
