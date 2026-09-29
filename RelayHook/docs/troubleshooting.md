# Troubleshooting

## Schema initialization fails

Verify the connection string, database availability, encryption settings, and account permissions. First deployment needs permission to create schema `[Callback]`, tables, constraints, and indexes. Startup also uses transaction-owned `sp_getapplock` resource `RelayHook.SchemaMigration`.

## SQL permissions are insufficient

Use a migration-capable identity during first deployment and package upgrades. Normal processing needs read/write access to RelayHook tables. Automatic initialization cannot currently be disabled, so a strict DBA-only migration process requires an operational step outside RelayHook.

## Database schema is newer than the package

RelayHook rejects downgrades. Deploy a package supporting the recorded `[Callback].[SchemaVersion]`. Do not edit the version row manually.

## Callback remains pending

Check that the host process is running, startup completed, SQL Server is reachable, and worker logs do not show repeated failures. Inspect `NextAttemptAt`; scheduled work and retries remain pending until that timestamp.

## Callback repeatedly retries

Review attempt HTTP status, network failures, endpoint timeout, and destination logs. HTTP 408, 425, 429, 5xx, network failures, and timeouts retry by default. A valid `Retry-After` can delay the next attempt further.

## OAuth token request fails

Verify token endpoint uses HTTPS, client ID is correct, secret reference resolves, scopes contain no whitespace, and system time is accurate. Token endpoint responses are bounded and errors are sanitized.

## Destination returns 401 or 403

Verify secret-provider output and destination permissions. OAuth delivery performs only one controlled cache invalidation and resend after 401. Static credentials do not retry authentication automatically.

## Destination URL is blocked

HTTPS is required by default. Check `UrlSecurity.AllowedHosts`, literal private-address restrictions, and any custom `ICallbackUrlPolicy`. For intentional private endpoints, combine an explicit allowlist with network egress controls.

## Duplicate callback observed

Duplicates are valid under at-least-once delivery. Deduplicate using `X-Callback-Id` or `X-Idempotency-Key`, then return a success response for repeats.

## Health check fails

`AddRelayHookHealthCheck()` registers check name `relayhook`. Failure usually means SQL connectivity failed or schema version differs from package expectations. Review logs without exposing connection strings or secrets.
