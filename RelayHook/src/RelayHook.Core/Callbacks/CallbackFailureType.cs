namespace RelayHook.Core.Callbacks;

/// <summary>Stable classification used by retry policies and attempt auditing.</summary>
public enum CallbackFailureType : byte
{
    /// <summary>No failure.</summary>
    None = 0,
    /// <summary>Network transport failure.</summary>
    Network = 1,
    /// <summary>Request timeout.</summary>
    Timeout = 2,
    /// <summary>Non-success HTTP response.</summary>
    Http = 3,
    /// <summary>Credential or authorization failure.</summary>
    Authentication = 4,
    /// <summary>Invalid endpoint or engine configuration.</summary>
    Configuration = 5,
    /// <summary>Payload serialization failure.</summary>
    Serialization = 6,
    /// <summary>Durable storage failure.</summary>
    Storage = 7,
    /// <summary>Unclassified failure.</summary>
    Unknown = 8
}
