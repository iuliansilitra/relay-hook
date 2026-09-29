# RelayHook

RelayHook provides reliable, persistent, at-least-once outbound callback/webhook delivery for ASP.NET Core applications backed by SQL Server.

Applications enqueue callbacks into their own database. A bounded hosted worker delivers them with retries, authentication, crash recovery, health checks, logs, traces, and metrics. RelayHook is an embedded library, not a central webhook service.

> RelayHook provides **at-least-once delivery**. A receiver can observe the same callback more than once after some crash or network-failure sequences. Receivers must deduplicate by callback ID or idempotency key.

## Installation

For a standard ASP.NET Core application using SQL Server, install one package:

```bash
dotnet add package RelayHook.AspNetCore
```

`RelayHook.AspNetCore` depends on `RelayHook.Core`, `RelayHook.Authentication`, and `RelayHook.SqlServer`. Install those lower-level packages directly only when building custom composition or infrastructure.

RelayHook targets .NET 10.

## Five-minute quick start

Add a SQL Server connection string:

```json
{
  "ConnectionStrings": {
    "RelayHook": "Server=localhost;Database=App;Integrated Security=true;TrustServerCertificate=true"
  }
}
```

Register RelayHook and one callback client in `Program.cs`:

```csharp
builder.Services
    .AddRelayHook()
    .UseSqlServer(builder.Configuration.GetConnectionString("RelayHook")!)
    .AddClient("PartnerA", client =>
    {
        client.AddEndpoint("primary", endpoint =>
        {
            endpoint.Url = new Uri("https://partner.example.com/webhooks");
            endpoint.UseNoAuthentication();
        });
    });

builder.Services.AddRelayHookHealthCheck();
```

Inject `ICallbackClient` and enqueue work:

```csharp
using RelayHook.Core.Abstractions;

public sealed class PaymentService(ICallbackClient callbacks)
{
    public Task<Guid> NotifyAsync(Guid paymentId, CancellationToken cancellationToken) =>
        callbacks.EnqueueAsync(
            "PartnerA",
            "payment.completed",
            new { PaymentId = paymentId, Status = "Completed" },
            cancellationToken);
}
```

`EnqueueAsync` serializes and persists work. It does not wait for the remote endpoint to receive the callback.

## Configure SQL Server

`UseSqlServer(connectionString)` stores jobs in the configured database. RelayHook never stores the connection string in callback data.

The hosted worker creates and upgrades schema `[Callback]` during startup. Concurrent application instances serialize migration work with `sp_getapplock`. Startup fails when the database schema is newer than the installed package. Automatic schema initialization cannot currently be disabled.

First deployment needs permission to create schema objects, tables, constraints, and indexes. Normal operation needs read/write access to `[Callback]`; future package upgrades may need DDL permission again. See [database documentation](https://github.com/iuliann20/relay-hook/blob/main/RelayHook/docs/database.md).

## Configure callback clients

Client and endpoint names are case-insensitive, non-empty, and limited to 200 characters. Duplicate names fail during registration. A client must contain at least one endpoint. When a client has multiple endpoints, select one with `CallbackEnqueueOptions.Endpoint`.

Endpoint configuration is snapshotted with each job, so queued callbacks retain their original non-secret delivery settings. Secret values are never snapshotted.

## Authentication

Built-in endpoint authentication supports API key, Basic, static Bearer, generated HS256 JWT, and OAuth2 client credentials. All secret-bearing options use configuration references, not literal secrets:

```csharp
endpoint.UseApiKey(authentication =>
{
    authentication.HeaderName = "X-API-Key";
    authentication.SecretReference = "Callbacks:PartnerA:ApiKey";
});
```

The default `ICallbackSecretProvider` reads `IConfiguration`. For local development, `Callbacks:PartnerA:ApiKey` can be supplied as environment variable `Callbacks__PartnerA__ApiKey`. Use Kubernetes secrets, Azure Key Vault, HashiCorp Vault, CyberArk, or a custom provider in production. Never commit production secrets to `appsettings.json`.

OAuth tokens are cached until their pre-expiry refresh window. One controlled token refresh and resend is allowed after a 401. Generated JWT support is HS256 only and requires a signing secret of at least 32 UTF-8 bytes.

See [authentication documentation](https://github.com/iuliann20/relay-hook/blob/main/RelayHook/docs/authentication.md) for Basic, Bearer, JWT, OAuth2, and custom authenticator examples.

## Retry behavior

Default retry delays after the initial attempt are 1 minute, 5 minutes, 15 minutes, and 1 hour, with 10% jitter. Network failures, timeouts, HTTP 408, 425, 429, and 5xx responses retry. Other HTTP responses fail permanently. A valid `Retry-After` can extend, but not shorten, local backoff and is capped at 24 hours by default.

`MaxAttempts` defaults to 5. Exhausted or permanent failures remain in `Failed` state. Replace `IRetryPolicy` for domain-specific classification.

## Delivery guarantee and idempotency

Every retry keeps stable `X-Callback-Id` and `X-Idempotency-Key` headers. Persist one of these values at the receiver and return success for duplicates. Supply a business key when useful:

```csharp
using RelayHook.Core;

await callbacks.EnqueueAsync(
    "PartnerA",
    "payment.completed",
    payload,
    new CallbackEnqueueOptions
    {
        CorrelationId = paymentId.ToString("N"),
        IdempotencyKey = $"payment-{paymentId:N}"
    },
    cancellationToken);
```

See [reliability documentation](https://github.com/iuliann20/relay-hook/blob/main/RelayHook/docs/reliability.md) for crash behavior and retry accounting.

## Configuration reference

Runtime options are configured in code through `AddRelayHook`; direct `IConfiguration` binding is not currently provided.

| Option | Default | Constraint | Purpose |
|---|---:|---|---|
| `PollingInterval` | 5 seconds | Greater than zero | Delay after an empty poll |
| `WorkerFailureBackoff` | 5 seconds | Greater than zero | Delay after infrastructure failure |
| `BatchSize` | 50 | Greater than zero | Maximum jobs considered per poll |
| `MaxConcurrentCallbacks` | 10 | Greater than zero | Parallel deliveries per process |
| `LeaseDuration` | 2 minutes | Greater than zero | Recoverable ownership period |
| `MaxAttempts` | 5 | Greater than zero | Default delivery attempt limit |
| `MaxPayloadBytes` | 1,048,576 | Greater than zero | Maximum serialized UTF-8 payload |
| `MaxPersistedResponseBytes` | 16,384 | Zero or greater | Maximum stored response bytes |
| `MaxPersistedErrorCharacters` | 2,048 | Greater than zero | Maximum stored sanitized error text |
| `Retry.JitterFactor` | 0.1 | 0 through 1 | Proportional retry jitter |
| `Retry.MaximumRetryAfter` | 24 hours | Greater than zero | `Retry-After` cap |
| `Retention.CompletedRetention` | 30 days | Greater than zero when enabled | Completed-job retention |
| `Retention.FailedRetention` | `null` | `null` or greater than zero | Failed-job retention; `null` keeps forever |
| `Retention.CleanupInterval` | 1 hour | Greater than zero when enabled | Cleanup cadence |
| `Retention.BatchSize` | 500 | Greater than zero when enabled | Rows deleted per cleanup pass |
| `UrlSecurity.AllowHttp` | `false` | Boolean | Explicit clear-text HTTP opt-in |
| `UrlSecurity.AllowedHosts` | Empty | Non-null set | Optional case-insensitive host allowlist |

See the [complete configuration reference](https://github.com/iuliann20/relay-hook/blob/main/RelayHook/docs/configuration.md), including endpoint and enqueue options.

## Database and schema

RelayHook owns `[Callback]` tables for clients, endpoints, jobs, attempts, and schema version. It uses direct `Microsoft.Data.SqlClient` access; it does not modify the host `DbContext` or host migrations. SQL Server UTC is authoritative for lease timing.

See [database documentation](https://github.com/iuliann20/relay-hook/blob/main/RelayHook/docs/database.md).

## Transactional enqueue

When business data and RelayHook share the same SQL Server database, use `ICallbackTransactionalClient` with an existing open `SqlConnection` and `SqlTransaction`:

```csharp
var callbackId = await transactionalCallbacks.EnqueueAsync(
    connection,
    transaction,
    "PartnerA",
    "payment.completed",
    payload,
    cancellationToken: cancellationToken);
```

RelayHook verifies schema compatibility but does not open, commit, or roll back the caller-owned transaction. Start a RelayHook host first so schema initialization completes.

## Observability

RelayHook provides:

- structured `ILogger` events without payloads or resolved secrets;
- `ActivitySource` name `RelayHook` with `callback.process` and `callback.http.send` activities;
- `System.Diagnostics.Metrics` meter name `RelayHook`;
- health check name `relayhook` after `AddRelayHookHealthCheck()`.

OpenTelemetry is optional. Subscribe to standard .NET activities and metrics when desired.

## Security

HTTPS is required by default. Redirects are disabled. Literal private, loopback, and link-local addresses are blocked unless explicitly allowlisted. DNS rebinding requires environment-specific egress controls or a custom `ICallbackUrlPolicy`. Payloads and persisted responses are bounded. RelayHook does not log or persist resolved authentication secrets.

See [security guidance](https://github.com/iuliann20/relay-hook/blob/main/RelayHook/docs/security.md).

## Multi-instance deployment

Multiple pods can share one database. Atomic SQL claims prevent concurrent ownership, renewable leases recover crashed work, and schema upgrades serialize across pods. Receivers still need idempotency because no database lease can recall an HTTP request already accepted remotely.

## Troubleshooting

- Startup says storage is missing: call `UseSqlServer` after `AddRelayHook`.
- Schema initialization fails: verify connection, DDL permissions, and migration-lock access.
- Schema is newer than package: deploy a compatible/newer RelayHook version; downgrades are blocked.
- Callback remains pending: inspect `NextAttemptAt`, worker logs, database connectivity, and host process health.
- Callback retries repeatedly: inspect HTTP status, timeout, destination logs, and `Retry-After`.
- OAuth token request fails: verify HTTPS token endpoint, client ID, secret reference, scopes, and system clock.
- Destination returns 401/403: verify secret provider output and destination authorization policy.
- URL is blocked: review HTTPS, `AllowedHosts`, private-address policy, and custom URL policy.

See [full troubleshooting guidance](https://github.com/iuliann20/relay-hook/blob/main/RelayHook/docs/troubleshooting.md).

## Advanced customization

Public extension points include `ICallbackAuthenticator`, `ICallbackSecretProvider`, `ICallbackSerializer`, `ICallbackUrlPolicy`, and `IRetryPolicy`. Register replacements in DI before `AddRelayHook` when RelayHook uses `TryAdd`, or use standard DI replacement methods.

Architecture details: [architecture](https://github.com/iuliann20/relay-hook/blob/main/RelayHook/docs/architecture.md).

## Development and packaging

```bash
dotnet restore RelayHook.slnx
dotnet restore samples/RelayHook.Sample/RelayHook.Sample.csproj
dotnet build RelayHook.slnx -c Release --no-restore
dotnet build samples/RelayHook.Sample/RelayHook.Sample.csproj -c Release --no-restore
dotnet test RelayHook.slnx -c Release --no-build --no-restore
dotnet pack RelayHook.slnx -c Release --no-build --no-restore -p:Version=0.1.0-alpha.1
```

Packages and symbols are written to `artifacts/packages/`. See [packaging and release guidance](https://github.com/iuliann20/relay-hook/blob/main/RelayHook/docs/packaging.md).

## License

RelayHook is licensed under the [MIT License](https://github.com/iuliann20/relay-hook/blob/main/LICENSE).
