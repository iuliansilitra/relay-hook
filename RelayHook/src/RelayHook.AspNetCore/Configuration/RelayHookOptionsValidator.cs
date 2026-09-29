using Microsoft.Extensions.Options;
using RelayHook.Core.Configuration;

namespace RelayHook.AspNetCore.Configuration;

internal sealed class RelayHookOptionsValidator : IValidateOptions<RelayHookOptions>
{
    public ValidateOptionsResult Validate(string? name, RelayHookOptions options)
    {
        var errors = new List<string>();
        if (options.PollingInterval <= TimeSpan.Zero) errors.Add("PollingInterval must be positive.");
        if (options.WorkerFailureBackoff <= TimeSpan.Zero) errors.Add("WorkerFailureBackoff must be positive.");
        if (options.BatchSize <= 0) errors.Add("BatchSize must be positive.");
        if (options.MaxConcurrentCallbacks <= 0) errors.Add("MaxConcurrentCallbacks must be positive.");
        if (options.LeaseDuration <= TimeSpan.Zero) errors.Add("LeaseDuration must be positive.");
        if (options.MaxAttempts <= 0) errors.Add("MaxAttempts must be positive.");
        if (options.MaxPayloadBytes <= 0) errors.Add("MaxPayloadBytes must be positive.");
        if (options.MaxPersistedResponseBytes < 0) errors.Add("MaxPersistedResponseBytes cannot be negative.");
        if (options.MaxPersistedErrorCharacters <= 0) errors.Add("MaxPersistedErrorCharacters must be positive.");
        if (options.Retry is null)
        {
            errors.Add("Retry options are required.");
        }
        else
        {
            if (options.Retry.Delays is null ||
                options.Retry.Delays.Count == 0 ||
                options.Retry.Delays.Any(delay => delay < TimeSpan.Zero))
                errors.Add("Retry delays must contain at least one non-negative value.");
            if (options.Retry.JitterFactor is < 0 or > 1)
                errors.Add("Retry JitterFactor must be between 0 and 1.");
            if (options.Retry.MaximumRetryAfter <= TimeSpan.Zero)
                errors.Add("Retry MaximumRetryAfter must be positive.");
        }

        if (options.Retention is null)
        {
            errors.Add("Retention options are required.");
        }
        else if (options.Retention.Enabled)
        {
            if (options.Retention.CompletedRetention <= TimeSpan.Zero)
                errors.Add("CompletedRetention must be positive.");
            if (options.Retention.FailedRetention is { } failed && failed <= TimeSpan.Zero)
                errors.Add("FailedRetention must be positive when configured.");
            if (options.Retention.CleanupInterval <= TimeSpan.Zero)
                errors.Add("CleanupInterval must be positive.");
            if (options.Retention.BatchSize <= 0)
                errors.Add("Retention BatchSize must be positive.");
        }

        if (options.UrlSecurity?.AllowedHosts is null)
            errors.Add("URL security options and AllowedHosts are required.");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
