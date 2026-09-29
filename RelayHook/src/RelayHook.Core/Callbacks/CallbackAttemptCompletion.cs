namespace RelayHook.Core.Callbacks;

internal sealed record CallbackAttemptCompletion(
    Guid JobId,
    Guid AttemptId,
    string WorkerId,
    int AttemptNumber,
    int CompletedAttemptCount,
    DateTimeOffset CompletedAt,
    CallbackDeliveryOutcome Outcome,
    CallbackStatus JobStatus,
    DateTimeOffset? NextAttemptAt,
    string? LastError);
