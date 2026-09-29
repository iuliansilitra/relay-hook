namespace RelayHook.Core.Abstractions;

/// <summary>Indicates invalid RelayHook or endpoint configuration.</summary>
public sealed class CallbackConfigurationException : Exception
{
    /// <summary>Creates a configuration exception.</summary>
    public CallbackConfigurationException(string message) : base(message)
    {
    }

    /// <summary>Creates a configuration exception with its cause.</summary>
    public CallbackConfigurationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
