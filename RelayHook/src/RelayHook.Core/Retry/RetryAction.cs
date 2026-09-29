namespace RelayHook.Core.Retry;

/// <summary>Durable action selected by a retry policy.</summary>
public enum RetryAction
{
    /// <summary>Mark the callback completed.</summary>
    Complete = 0,
    /// <summary>Schedule another attempt.</summary>
    Retry = 1,
    /// <summary>Mark the callback failed.</summary>
    Fail = 2
}
