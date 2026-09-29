namespace RelayHook.Core.Configuration;

/// <summary>Completed and failed callback retention.</summary>
public sealed class RetentionOptions
{
    /// <summary>Whether asynchronous cleanup runs.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How long completed jobs are retained.</summary>
    public TimeSpan CompletedRetention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Failed-job retention. Null keeps failed callbacks indefinitely.</summary>
    public TimeSpan? FailedRetention { get; set; }

    /// <summary>Delay between cleanup runs.</summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Maximum jobs deleted per cleanup run.</summary>
    public int BatchSize { get; set; } = 500;
}
