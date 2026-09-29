using RelayHook.Core.Callbacks;
using RelayHook.Core.Retry;

namespace RelayHook.Core.Abstractions;

/// <summary>Classifies a delivery outcome and selects its next durable state.</summary>
public interface IRetryPolicy
{
    /// <summary>Returns a terminal or retry decision.</summary>
    RetryDecision Decide(
        CallbackDeliveryOutcome outcome,
        int completedAttemptCount,
        int maxAttempts,
        DateTimeOffset now);
}
