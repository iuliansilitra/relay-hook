# RelayHook sample

Small ASP.NET Core sample showing SQL Server registration, unauthenticated, API-key, and OAuth2 clients, health checks, and durable enqueue.

Set secrets through environment variables or another configuration provider:

```powershell
$env:Callbacks__ApiKeyPartner__ApiKey = "development-only-value"
$env:Callbacks__OAuthPartner__ClientSecret = "development-only-value"
dotnet run
```

Do not commit production secrets. Change endpoint URLs and connection string before running deliveries.
