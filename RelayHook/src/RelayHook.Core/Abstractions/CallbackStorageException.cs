namespace RelayHook.Core.Abstractions;

/// <summary>Indicates a durable callback storage failure.</summary>
public sealed class CallbackStorageException : Exception
{
    /// <summary>Creates a storage exception with its cause.</summary>
    public CallbackStorageException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
