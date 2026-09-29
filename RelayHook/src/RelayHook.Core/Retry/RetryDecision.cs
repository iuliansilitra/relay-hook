namespace RelayHook.Core.Retry;

/// <summary>Retry policy decision for a completed delivery attempt.</summary>
public sealed record RetryDecision(RetryAction Action, DateTimeOffset? NextAttemptAt)
{
    /// <summary>Creates a completion decision.</summary>
    public static RetryDecision Completed() => new(RetryAction.Complete, null);

    /// <summary>Creates a permanent failure decision.</summary>
    public static RetryDecision Failed() => new(RetryAction.Fail, null);

    /// <summary>Creates a retry decision for an absolute UTC time.</summary>
    public static RetryDecision RetryAt(DateTimeOffset nextAttemptAt) =>
        new(RetryAction.Retry, nextAttemptAt);
}
