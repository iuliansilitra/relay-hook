namespace RelayHook.Core.Callbacks;

internal sealed record CallbackEnqueueCommand(
    Guid Id,
    CallbackDestinationSnapshot Destination,
    string EventName,
    string Payload,
    DateTimeOffset CreatedAt,
    DateTimeOffset ScheduledAt,
    int MaxAttempts,
    string? CorrelationId,
    string IdempotencyKey);
