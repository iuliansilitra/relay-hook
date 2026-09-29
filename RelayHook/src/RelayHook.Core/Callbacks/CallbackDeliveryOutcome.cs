namespace RelayHook.Core.Callbacks;

/// <summary>Sanitized result supplied to retry policy implementations.</summary>
public sealed record CallbackDeliveryOutcome(
    bool Succeeded,
    CallbackFailureType FailureType,
    int? HttpStatusCode,
    string? ErrorMessage,
    string? ResponseBody,
    TimeSpan Duration)
{
    /// <summary>Server-requested minimum delay parsed from Retry-After, when valid.</summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>Creates a successful HTTP outcome.</summary>
    public static CallbackDeliveryOutcome Success(int statusCode, string? responseBody, TimeSpan duration) =>
        new(true, CallbackFailureType.None, statusCode, null, responseBody, duration);

    /// <summary>Creates a sanitized failed outcome.</summary>
    public static CallbackDeliveryOutcome Failure(
        CallbackFailureType failureType,
        int? statusCode,
        string errorMessage,
        string? responseBody,
        TimeSpan duration,
        TimeSpan? retryAfter = null) =>
        new(false, failureType, statusCode, errorMessage, responseBody, duration)
        {
            RetryAfter = retryAfter
        };
}
