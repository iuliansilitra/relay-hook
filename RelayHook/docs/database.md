# Database

RelayHook owns schema `[Callback]` in the application's SQL Server database.

## Tables

- `SchemaVersion`: singleton current version.
- `Client`: stable logical client identity and display name.
- `Endpoint`: stable named endpoint identity plus current URL/method/configuration hash.
- `Job`: exact payload and non-secret endpoint snapshot, schedule, status, idempotency data, and lease.
- `Attempt`: append-oriented audit record for each started processing attempt.

Secrets are references resolved at send time and never stored as secret values.

## Installation and upgrades

Startup acquires transaction-owned `sp_getapplock` resource `RelayHook.SchemaMigration`, bootstraps the version table, and applies ordered embedded migrations in one transaction. A database newer than the package fails startup. Concurrent application starts serialize on the application lock.

Automatic initialization runs from the hosted worker and cannot currently be disabled. First deployment requires permission to create schema `[Callback]`, tables, constraints, and indexes. Normal operation requires `SELECT`, `INSERT`, `UPDATE`, and `DELETE` on RelayHook objects. Package upgrades can introduce new migrations and therefore need DDL permission again. Environments with strict DBA-only migrations must coordinate those permissions or execute reviewed migration logic before application rollout; RelayHook does not currently ship a separate migration CLI.

Schema version 2 adds `CompletedAttemptCount`. `AttemptCount` remains the append-only sequence of started audit attempts, while `CompletedAttemptCount` is the retry-budget counter. Existing databases derive the latter from completed attempt rows during migration. Migration SQL and version advancement commit atomically under the same transaction-owned application lock.

## Claim query and indexes

Claiming sets its dedicated connection to `READ COMMITTED`, then uses one `UPDATE ... OUTPUT` over a `TOP` CTE with `UPDLOCK`, `READPAST`, `READCOMMITTEDLOCK`, and `ROWLOCK`. `READCOMMITTEDLOCK` keeps `READPAST` valid when the host database enables `READ_COMMITTED_SNAPSHOT`. Selection and state transition are one statement. Competing workers therefore either update the row first or skip its update lock; they cannot both acquire the same lease. SQL Server's `SYSUTCDATETIME()` determines lease deadlines and expiry. An expired lease is eligible for reclaim, but remains renewable by its owner until a competing claim changes `LockedBy`. Every renewal and completion checks `LockedBy`, which fences the old worker after takeover. Eligibility uses completed attempts, so an incomplete crash attempt cannot silently exhaust the job. The claim index starts with `(Status, NextAttemptAt, LockedUntil)`. A second index supports client/time queries. Attempt lookup uses `(JobId, AttemptNumber)`.

Retention selects and deletes at most one configured batch with `READPAST`/`ROWLOCK`; concurrent pods may clean disjoint rows. Only terminal completed/failed jobs matching configured cutoffs are eligible. `(Status, CompletedAt)` supports this scan. Attempt rows follow parent retention through cascade delete.
