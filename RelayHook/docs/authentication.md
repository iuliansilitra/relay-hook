# Authentication

Authentication is a named strategy resolved from each job's endpoint snapshot. Built-ins are `None`, API key, Basic, static Bearer, generated HS256 JWT, and OAuth2 client credentials. Consumers can register another `ICallbackAuthenticator` under a custom name.

Configuration snapshots contain secret references, never secret values. `ICallbackSecretProvider` resolves values immediately before sending. The default provider reads `IConfiguration`; vault integrations can replace it.

OAuth tokens are cached per normalized configuration until the configured pre-expiry refresh window. A per-key semaphore prevents token stampedes. Token endpoint calls have an independent `RequestTimeout` (30 seconds by default), bounded response buffering, and sanitized failure classification. A 401 invalidates only the exact rejected token, so a late 401 cannot evict a token another request already refreshed. Each delivery permits one controlled resend; a second 401 is final.

Generated JWTs use the Microsoft IdentityModel implementation with an explicit HS256 signing credential, `TimeProvider` timestamps, and a minimum 32-byte UTF-8 secret. Issuer and audience are required, reserved claims cannot be overridden, and lifetime is limited to greater than zero through one hour.

## No authentication

```csharp
endpoint.UseNoAuthentication();
```

## API key

```csharp
endpoint.UseApiKey(authentication =>
{
    authentication.HeaderName = "X-API-Key";
    authentication.SecretReference = "Callbacks:PartnerA:ApiKey";
});
```

The header name must pass RelayHook's reserved-header policy. The referenced value is resolved for every delivery.

## Basic authentication

```csharp
endpoint.UseBasicAuthentication(authentication =>
{
    authentication.Username = "relayhook";
    authentication.PasswordSecretReference = "Callbacks:PartnerA:Password";
});
```

Usernames cannot contain `:`. Use Basic authentication only over HTTPS; RelayHook requires HTTPS by default.

## Static Bearer token

```csharp
endpoint.UseBearerToken(authentication =>
{
    authentication.TokenSecretReference = "Callbacks:PartnerA:BearerToken";
});
```

The token is read at delivery time, so secret rotation does not require rewriting queued jobs.

## Generated JWT

```csharp
endpoint.UseGeneratedJwt(authentication =>
{
    authentication.Issuer = "https://service.example.com";
    authentication.Audience = "partner-api";
    authentication.Subject = "relayhook";
    authentication.SigningKeySecretReference = "Callbacks:PartnerA:JwtSigningKey";
    authentication.Lifetime = TimeSpan.FromMinutes(5);
    authentication.Claims["tenant"] = "partner-a";
});
```

RelayHook generates HS256 tokens only. Signing secret must contain at least 32 UTF-8 bytes. Token lifetime must be greater than zero and no more than one hour. Additional claims cannot replace reserved JWT claims.

## OAuth2 client credentials

```csharp
endpoint.UseOAuth2ClientCredentials(authentication =>
{
    authentication.TokenEndpoint = "https://identity.example.com/oauth/token";
    authentication.ClientId = "relayhook";
    authentication.ClientSecretReference = "Callbacks:PartnerA:OAuthClientSecret";
    authentication.Scopes = ["callbacks.write"];
    authentication.RefreshBeforeExpiry = TimeSpan.FromSeconds(30);
    authentication.RequestTimeout = TimeSpan.FromSeconds(30);
});
```

Token endpoint must use HTTPS. Tokens are cached by normalized configuration. A per-key lock prevents concurrent refresh stampedes. Tokens refresh inside `RefreshBeforeExpiry`. One delivery-side 401 invalidates the exact rejected cached token and permits one controlled resend; a second 401 is final.

## Secret references

The default provider resolves a reference by calling `IConfiguration[secretReference]`. A reference such as `Callbacks:PartnerA:ApiKey` maps to environment variable `Callbacks__PartnerA__ApiKey` through standard .NET configuration.

An `appsettings.json` value is useful only for disposable local development. Do not commit production secrets. Prefer environment variables, mounted Kubernetes secrets, Vault, Azure Key Vault, CyberArk, or a custom provider.

Register a custom provider before RelayHook so `TryAdd` preserves it:

```csharp
builder.Services.AddSingleton<ICallbackSecretProvider, VaultSecretProvider>();
builder.Services.AddRelayHook();
```

## Custom authenticator

Register a strategy under a stable type name:

```csharp
builder.Services
    .AddRelayHook()
    .AddCallbackAuthenticator<SignatureAuthenticator>("Signature");
```

Select it with non-secret options:

```csharp
endpoint.UseAuthentication(
    "Signature",
    new SignatureOptions { SecretReference = "Callbacks:PartnerA:SigningSecret" });
```

Minimal implementation:

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RelayHook.Core.Abstractions;

public sealed record SignatureOptions
{
    public string SecretReference { get; init; } = string.Empty;
}

public sealed class SignatureAuthenticator : ICallbackAuthenticator
{
    public async ValueTask AuthenticateAsync(
        HttpRequestMessage request,
        CallbackAuthenticationContext context,
        CancellationToken cancellationToken)
    {
        var options = context.Configuration.Deserialize<SignatureOptions>() ??
            throw new CallbackAuthenticationException("Signature configuration is invalid.");
        var secret = await context.SecretProvider.GetSecretAsync(
            options.SecretReference,
            cancellationToken) ?? throw new CallbackAuthenticationException("Signing secret was not found.");

        var signature = Convert.ToHexStringLower(
            HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(secret),
                Encoding.UTF8.GetBytes(context.CallbackId)));
        request.Headers.TryAddWithoutValidation("X-Signature", signature);
    }
}
```

Custom strategy configuration is snapshotted. Store secret references only, never secret values.
