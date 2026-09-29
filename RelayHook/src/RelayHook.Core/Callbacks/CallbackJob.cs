namespace RelayHook.Core.Callbacks;

internal sealed record CallbackJob(
    Guid Id,
    Guid ClientId,
    Guid EndpointId,
    string EventName,
    string Payload,
    string ContentType,
    CallbackDestinationSnapshot Destination,
    DateTimeOffset CreatedAt,
    DateTimeOffset NextAttemptAt,
    int AttemptCount,
    int CompletedAttemptCount,
    int MaxAttempts,
    string? CorrelationId,
    string IdempotencyKey,
    string LockedBy,
    DateTimeOffset LockedUntil);
