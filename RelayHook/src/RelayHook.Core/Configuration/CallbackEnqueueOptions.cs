namespace RelayHook.Core;

/// <summary>Per-callback scheduling and identity options.</summary>
public sealed class CallbackEnqueueOptions
{
    /// <summary>Named endpoint. Omit only when the client has exactly one endpoint.</summary>
    public string? Endpoint { get; set; }

    /// <summary>Earliest delivery time. Defaults to now.</summary>
    public DateTimeOffset? ScheduledAt { get; set; }

    /// <summary>Attempt limit override. Defaults to the engine setting.</summary>
    public int? MaxAttempts { get; set; }

    /// <summary>Trace or business correlation identifier.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>Receiver deduplication key. A stable generated value is used when omitted.</summary>
    public string? IdempotencyKey { get; set; }
}
