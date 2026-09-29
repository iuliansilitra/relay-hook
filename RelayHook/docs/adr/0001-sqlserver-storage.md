# ADR 0001: Direct SQL Server storage

**Status:** Accepted

Use `Microsoft.Data.SqlClient` behind a Core storage port. The engine owns `[Callback]`; the host does not add entities or migrations to its `DbContext`. Direct SQL keeps claim and lease statements explicit and avoids an EF Core dependency for a small, query-specific persistence model.
