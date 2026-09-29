# Configuration reference

RelayHook runtime options are configured by the `Action<RelayHookOptions>` passed to `AddRelayHook`. RelayHook does not currently bind these runtime options from `IConfiguration`. Secret references are resolved from `IConfiguration` by the default secret provider.

## Worker and persistence options

| Name | Type | Default | Constraint | Meaning and when to change it |
|---|---|---:|---|---|
| `PollingInterval` | `TimeSpan` | 5 seconds | Greater than zero | Delay after a poll finds no work. Reduce for lower idle latency at the cost of more SQL polling. |
| `WorkerFailureBackoff` | `TimeSpan` | 5 seconds | Greater than zero | Delay after worker infrastructure failure. Increase when repeated SQL outages should back off more slowly. |
| `BatchSize` | `int` | 50 | Greater than zero | Upper claim request size. Actual claim size is capped at `MaxConcurrentCallbacks`. |
| `MaxConcurrentCallbacks` | `int` | 10 | Greater than zero | Maximum simultaneous deliveries in one process. Tune with destination and host capacity. |
| `LeaseDuration` | `TimeSpan` | 2 minutes | Greater than zero | Worker ownership duration. Active delivery renews every third of this value. SQL Server UTC controls deadlines. |
| `MaxAttempts` | `int` | 5 | Greater than zero | Default total completed delivery attempts. Per-callback options can override it. |
| `MaxPayloadBytes` | `int` | 1,048,576 | Greater than zero | Maximum UTF-8 serialized payload accepted by enqueue. |
| `MaxPersistedResponseBytes` | `int` | 16,384 | Zero or greater | Maximum response-body bytes retained for one attempt. Zero disables response-body retention. |
| `MaxPersistedErrorCharacters` | `int` | 2,048 | Greater than zero | Maximum sanitized error text retained for one attempt. |

Endpoint timeout defaults to 30 seconds. Default lease duration is 2 minutes and renews every 40 seconds, so a normal request does not approach its initial lease deadline. Long endpoint timeouts remain safe because active work renews its lease.

## Retry options

| Name | Type | Default | Constraint | Meaning and when to change it |
|---|---|---:|---|---|
| `Retry.Delays` | `IList<TimeSpan>` | 1 minute, 5 minutes, 15 minutes, 1 hour | At least one non-negative value | Delay after each failed completed attempt. Attempts beyond the list reuse its last value. |
| `Retry.JitterFactor` | `double` | 0.1 | 0 through 1 | Proportional random variation. `0.1` means plus or minus 10%. |
| `Retry.MaximumRetryAfter` | `TimeSpan` | 24 hours | Greater than zero | Upper bound for a valid remote `Retry-After` value. |

Network failures, timeouts, HTTP 408, 425, 429, and 5xx retry by default. Other HTTP responses fail permanently. A valid `Retry-After` can extend, but never shorten, configured backoff. Register an `IRetryPolicy` replacement for other classification rules.

## Retention options

| Name | Type | Default | Constraint | Meaning and when to change it |
|---|---|---:|---|---|
| `Retention.Enabled` | `bool` | `true` | Boolean | Enables asynchronous terminal-job cleanup. |
| `Retention.CompletedRetention` | `TimeSpan` | 30 days | Greater than zero when enabled | Age after which completed jobs can be deleted. |
| `Retention.FailedRetention` | `TimeSpan?` | `null` | `null` or greater than zero | Failed-job retention. `null` keeps failed jobs indefinitely. |
| `Retention.CleanupInterval` | `TimeSpan` | 1 hour | Greater than zero when enabled | Delay between cleanup passes. |
| `Retention.BatchSize` | `int` | 500 | Greater than zero when enabled | Maximum jobs deleted per pass. Attempt rows cascade with jobs. |

## URL security options

| Name | Type | Default | Constraint | Meaning and when to change it |
|---|---|---:|---|---|
| `UrlSecurity.AllowHttp` | `bool` | `false` | Boolean | Allows clear-text HTTP destinations. Keep disabled outside controlled development networks. |
| `UrlSecurity.AllowedHosts` | `ISet<string>` | Empty, case-insensitive | Must not be null | Optional host allowlist. An empty set permits public hosts while private/literal addresses remain blocked. |

The default URL policy rejects relative URLs, non-HTTP schemes, user information, loopback addresses, link-local addresses, and private literal IP addresses unless a host is explicitly allowlisted. It cannot eliminate DNS rebinding; use network egress controls or replace `ICallbackUrlPolicy` where DNS enforcement is required.

## Endpoint options

| Name | Type | Default | Constraint | Meaning |
|---|---|---:|---|---|
| `Url` | `Uri?` | `null` | Required absolute URL, at most 2,048 characters | Callback destination. |
| `Method` | `HttpMethod` | `POST` | `POST`, `PUT`, or `PATCH` | Delivery method. |
| `ContentType` | `string` | `application/json` | Non-empty, at most 100 characters | Payload media type. |
| `Timeout` | `TimeSpan` | 30 seconds | Greater than zero | Per-delivery HTTP timeout. |

`AddHeader` accepts non-sensitive custom headers. RelayHook rejects authorization, identity, framing, callback identity, and control-character header values. Use an authenticator for credentials.

## Enqueue options

| Name | Type | Default | Constraint | Meaning |
|---|---|---:|---|---|
| `Endpoint` | `string?` | `null` | Required when a client has multiple endpoints | Named endpoint selection. |
| `ScheduledAt` | `DateTimeOffset?` | Current UTC time | Optional | Earliest delivery time. |
| `MaxAttempts` | `int?` | Runtime default | Greater than zero when set | Callback-specific attempt limit. |
| `CorrelationId` | `string?` | `null` | At most 200 characters; no control characters | Trace or business identifier. Do not store secrets here. |
| `IdempotencyKey` | `string?` | Generated callback ID | At most 200 characters; no control characters | Stable receiver deduplication key. |

Client names, endpoint names, and event names are non-empty and limited to 200 characters. Client and endpoint lookup is case-insensitive. Duplicate registrations fail immediately.

## Example

```csharp
builder.Services.AddRelayHook(options =>
{
    options.PollingInterval = TimeSpan.FromSeconds(2);
    options.MaxConcurrentCallbacks = 20;
    options.LeaseDuration = TimeSpan.FromMinutes(3);
    options.Retry.MaximumRetryAfter = TimeSpan.FromHours(6);
    options.Retention.FailedRetention = TimeSpan.FromDays(90);
    options.UrlSecurity.AllowedHosts.Add("partner.example.com");
});
```

Invalid runtime options fail host startup through `ValidateOnStart`. Invalid clients and endpoints fail during service registration or startup validation.
