using RelayHook.Core.Abstractions;
using RelayHook.Core.Callbacks;
using RelayHook.Core.Configuration;

namespace RelayHook.Core.Retry;

internal sealed class DefaultRetryPolicy : IRetryPolicy
{
    private readonly TimeSpan[] _delays;
    private readonly double _jitterFactor;
    private readonly TimeSpan _maximumRetryAfter;
    private readonly Func<double> _nextRandom;

    public DefaultRetryPolicy(RetryOptions options)
        : this(options, () => Random.Shared.NextDouble())
    {
    }

    internal DefaultRetryPolicy(RetryOptions options, Func<double> nextRandom)
    {
        ArgumentNullException.ThrowIfNull(options);
        _delays = [.. options.Delays];
        _jitterFactor = options.JitterFactor;
        _maximumRetryAfter = options.MaximumRetryAfter;
        _nextRandom = nextRandom;
    }

    public RetryDecision Decide(
        CallbackDeliveryOutcome outcome,
        int completedAttemptCount,
        int maxAttempts,
        DateTimeOffset now)
    {
        if (outcome.Succeeded)
        {
            return RetryDecision.Completed();
        }

        if (completedAttemptCount >= maxAttempts || !IsRetryable(outcome))
        {
            return RetryDecision.Failed();
        }

        var delayIndex = Math.Min(completedAttemptCount - 1, _delays.Length - 1);
        var delay = _delays[delayIndex];
        if (_jitterFactor > 0)
        {
            var multiplier = 1 + (((_nextRandom() * 2) - 1) * _jitterFactor);
            delay = TimeSpan.FromTicks(Math.Max(0, (long)(delay.Ticks * multiplier)));
        }

        if (outcome.RetryAfter is { } retryAfter)
        {
            var boundedRetryAfter = retryAfter < TimeSpan.Zero
                ? TimeSpan.Zero
                : retryAfter > _maximumRetryAfter
                    ? _maximumRetryAfter
                    : retryAfter;
            if (boundedRetryAfter > delay)
            {
                delay = boundedRetryAfter;
            }
        }

        return RetryDecision.RetryAt(now + delay);
    }

    private static bool IsRetryable(CallbackDeliveryOutcome outcome)
    {
        if (outcome.FailureType is CallbackFailureType.Network or CallbackFailureType.Timeout or CallbackFailureType.Storage)
        {
            return true;
        }

        return outcome.FailureType == CallbackFailureType.Http &&
               (outcome.HttpStatusCode is 408 or 425 or 429 ||
                outcome.HttpStatusCode is >= 500 and <= 599);
    }
}
