namespace RelayHook.Core.Configuration;

/// <summary>Retry schedule and optional jitter.</summary>
public sealed class RetryOptions
{
    /// <summary>Delays after each failed attempt, starting with attempt one.</summary>
    public IList<TimeSpan> Delays { get; set; } =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1)
    ];

    /// <summary>Maximum proportional variation. For example, 0.1 gives plus or minus ten percent.</summary>
    public double JitterFactor { get; set; } = 0.1;

    /// <summary>Maximum delay accepted from an endpoint's Retry-After header.</summary>
    public TimeSpan MaximumRetryAfter { get; set; } = TimeSpan.FromHours(24);
}
