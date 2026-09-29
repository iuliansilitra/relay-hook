namespace RelayHook.Core.Configuration;

/// <summary>Runtime settings for durable callback processing.</summary>
public sealed class RelayHookOptions
{
    /// <summary>Delay between empty polling cycles.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Delay after a worker infrastructure failure, separate from callback retry timing.</summary>
    public TimeSpan WorkerFailureBackoff { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Maximum jobs claimed per polling cycle.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>Maximum parallel deliveries in one process.</summary>
    public int MaxConcurrentCallbacks { get; set; } = 10;

    /// <summary>Duration of an ownership lease before another worker can recover a job.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Default total attempts per callback.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Maximum UTF-8 payload size accepted at enqueue time.</summary>
    public int MaxPayloadBytes { get; set; } = 1_048_576;

    /// <summary>Maximum response bytes persisted per attempt.</summary>
    public int MaxPersistedResponseBytes { get; set; } = 16_384;

    /// <summary>Maximum error characters persisted per attempt.</summary>
    public int MaxPersistedErrorCharacters { get; set; } = 2_048;

    /// <summary>Retry schedule.</summary>
    public RetryOptions Retry { get; set; } = new();

    /// <summary>Data-retention settings.</summary>
    public RetentionOptions Retention { get; set; } = new();

    /// <summary>Outbound URL restrictions.</summary>
    public UrlSecurityOptions UrlSecurity { get; set; } = new();
}
