using RelayHook.Core.Callbacks;
using RelayHook.Core.Configuration;
using RelayHook.Core.Retry;

namespace RelayHook.Core.Tests;

public sealed class DefaultRetryPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Decide_WhenDeliverySucceeded_ShouldComplete()
    {
        var policy = CreatePolicy();

        var decision = policy.Decide(
            CallbackDeliveryOutcome.Success(204, null, TimeSpan.FromMilliseconds(10)),
            1,
            5,
            Now);

        Assert.Equal(RetryAction.Complete, decision.Action);
        Assert.Null(decision.NextAttemptAt);
    }

    [Theory]
    [InlineData(408)]
    [InlineData(425)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public void Decide_WhenHttpFailureIsTransient_ShouldUseConfiguredDelay(int statusCode)
    {
        var policy = CreatePolicy();
        var outcome = CallbackDeliveryOutcome.Failure(
            CallbackFailureType.Http,
            statusCode,
            "failed",
            null,
            TimeSpan.Zero);

        var decision = policy.Decide(outcome, 1, 5, Now);

        Assert.Equal(RetryAction.Retry, decision.Action);
        Assert.Equal(Now.AddMinutes(1), decision.NextAttemptAt);
    }

    [Fact]
    public void Decide_WhenHttpFailureIsPermanent_ShouldFail()
    {
        var policy = CreatePolicy();
        var outcome = CallbackDeliveryOutcome.Failure(
            CallbackFailureType.Http,
            400,
            "failed",
            null,
            TimeSpan.Zero);

        var decision = policy.Decide(outcome, 1, 5, Now);

        Assert.Equal(RetryAction.Fail, decision.Action);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(422)]
    public void Decide_WhenHttpFailureIsNonTransient_ShouldFail(int statusCode)
    {
        var policy = CreatePolicy();
        var outcome = CallbackDeliveryOutcome.Failure(
            CallbackFailureType.Http,
            statusCode,
            "failed",
            null,
            TimeSpan.Zero);

        var decision = policy.Decide(outcome, 1, 5, Now);

        Assert.Equal(RetryAction.Fail, decision.Action);
    }

    [Fact]
    public void Decide_WhenRetryAfterIsLater_ShouldRespectIt()
    {
        var policy = CreatePolicy();
        var outcome = CallbackDeliveryOutcome.Failure(
            CallbackFailureType.Http,
            429,
            "failed",
            null,
            TimeSpan.Zero,
            TimeSpan.FromMinutes(10));

        var decision = policy.Decide(outcome, 1, 5, Now);

        Assert.Equal(Now.AddMinutes(10), decision.NextAttemptAt);
    }

    [Fact]
    public void Decide_WhenRetryAfterIsExcessive_ShouldClampIt()
    {
        var policy = new DefaultRetryPolicy(
            new RetryOptions
            {
                JitterFactor = 0,
                MaximumRetryAfter = TimeSpan.FromHours(2)
            },
            () => 0.5);
        var outcome = CallbackDeliveryOutcome.Failure(
            CallbackFailureType.Http,
            503,
            "failed",
            null,
            TimeSpan.Zero,
            TimeSpan.FromDays(30));

        var decision = policy.Decide(outcome, 1, 5, Now);

        Assert.Equal(Now.AddHours(2), decision.NextAttemptAt);
    }

    [Fact]
    public void Decide_WhenAttemptsAreExhausted_ShouldFailWithoutScheduling()
    {
        var policy = CreatePolicy();
        var outcome = CallbackDeliveryOutcome.Failure(
            CallbackFailureType.Network,
            null,
            "failed",
            null,
            TimeSpan.Zero);

        var decision = policy.Decide(outcome, 5, 5, Now);

        Assert.Equal(RetryAction.Fail, decision.Action);
        Assert.Null(decision.NextAttemptAt);
    }

    [Fact]
    public void Decide_WhenAttemptExceedsSchedule_ShouldUseLastDelay()
    {
        var policy = CreatePolicy();
        var outcome = CallbackDeliveryOutcome.Failure(
            CallbackFailureType.Timeout,
            null,
            "failed",
            null,
            TimeSpan.Zero);

        var decision = policy.Decide(outcome, 4, 10, Now);

        Assert.Equal(Now.AddHours(1), decision.NextAttemptAt);
    }

    private static DefaultRetryPolicy CreatePolicy() =>
        new(
            new RetryOptions { JitterFactor = 0 },
            () => 0.5);
}
