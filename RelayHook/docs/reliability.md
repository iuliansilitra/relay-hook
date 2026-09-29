# Reliability

RelayHook provides at-least-once delivery. Exactly-once delivery is impossible because a process can crash after a receiver accepts a request but before RelayHook persists completion.

Each job keeps one callback ID and idempotency key across retries. Receivers should persist that key and return success for duplicates.

Workers atomically claim due jobs with a renewable lease. SQL Server's UTC clock is authoritative for lease deadlines and expiry, so clock skew between application instances cannot shorten or prolong ownership. A crashed worker's lease expires and another instance reclaims the job. Completion updates require the current worker ID, so a worker that lost its lease cannot overwrite a newer owner.

A polling cycle claims at most `min(BatchSize, MaxConcurrentCallbacks)`. RelayHook does not pre-claim a large in-memory backlog whose leases could expire before processing begins. Active deliveries renew their lease every third of `LeaseDuration`. Expiry makes a job eligible for reclaim; it does not revoke the current worker by itself. If that worker renews before a competitor reclaims, it retains ownership. If a competitor reclaims first, the old worker's renewal and completion are rejected, local processing is cancelled, and no stale completion is written. An HTTP request already accepted by the remote endpoint cannot be recalled, so recovery can still produce a duplicate.

Started attempts and completed delivery attempts are counted separately. A crash after the audit attempt is inserted but before a delivery result is persisted leaves an incomplete attempt; it remains visible but does not consume `MaxAttempts`. This prevents repeated pod crashes from failing a callback that was never proven to have received its allowed delivery attempts.

Default retry delays are 1 minute, 5 minutes, 15 minutes, and 1 hour after the initial attempt, with optional bounded jitter. DNS/connection/response-stream errors, timeouts, HTTP 408, 425, 429, and 5xx retry. HTTP 400, 401, 403, 404, 409, and 422 are permanent by default. OAuth 401 receives one in-memory token invalidation and resend before permanent classification. A custom `IRetryPolicy` can change domain-specific classification.

Valid `Retry-After` delta-seconds and HTTP-date values can extend the configured delay. They cannot shorten local backoff and are clamped to `Retry.MaximumRetryAfter`, which defaults to 24 hours. Invalid values are ignored.

Worker infrastructure failures such as SQL unavailability delay the next polling cycle by `WorkerFailureBackoff`; this is intentionally separate from callback retry timing and prevents a tight failure loop.

A database row whose endpoint snapshot is syntactically JSON but cannot be materialized is isolated and marked failed with a sanitized error. Other jobs from the same claim continue; one corrupted row cannot poison every worker cycle.

## Crash matrix

- Claimed before request: lease expiry makes the job claimable again.
- Attempt row inserted before request: the incomplete audit attempt is retained; retry budget is preserved.
- Request in flight: lease expiry and recovery are safe, but the remote side may have accepted the request.
- Remote 2xx before local completion: a duplicate is possible after recovery; callback ID and idempotency key remain stable.
- Completion transaction committed: job and attempt result remain terminal together.
- HTTP result obtained while SQL completion fails: the processing lease eventually expires and the job is recoverable; duplicate delivery is possible.

`EnqueueAsync` is not atomic with an unrelated business transaction. Use `ICallbackTransactionalClient` with the business SQL connection and transaction when both writes target the same SQL Server database. RelayHook neither opens a hidden connection nor owns commit/rollback in this path. The Callback schema must already be current. This is the provided outbox-compatible seam.
