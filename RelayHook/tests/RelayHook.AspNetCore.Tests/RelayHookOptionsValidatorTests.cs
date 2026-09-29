using RelayHook.AspNetCore.Configuration;
using RelayHook.Core.Configuration;

namespace RelayHook.AspNetCore.Tests;

public sealed class RelayHookOptionsValidatorTests
{
    [Fact]
    public void Validate_WhenConcurrencyIsZero_ShouldFailWithUsefulMessage()
    {
        var options = new RelayHookOptions { MaxConcurrentCallbacks = 0 };

        var result = new RelayHookOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("MaxConcurrentCallbacks", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WhenDefaultsUsed_ShouldSucceed()
    {
        var result = new RelayHookOptionsValidator().Validate(null, new RelayHookOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WhenWorkerFailureBackoffIsZero_ShouldFail()
    {
        var options = new RelayHookOptions { WorkerFailureBackoff = TimeSpan.Zero };

        var result = new RelayHookOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure =>
            failure.Contains("WorkerFailureBackoff", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WhenMaximumRetryAfterIsZero_ShouldFail()
    {
        var options = new RelayHookOptions();
        options.Retry.MaximumRetryAfter = TimeSpan.Zero;

        var result = new RelayHookOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure =>
            failure.Contains("MaximumRetryAfter", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WhenNestedOptionsAreNull_ShouldFailWithoutThrowing()
    {
        var options = new RelayHookOptions
        {
            Retry = null!,
            Retention = null!,
            UrlSecurity = null!
        };

        var result = new RelayHookOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Equal(3, result.Failures.Count());
    }
}
