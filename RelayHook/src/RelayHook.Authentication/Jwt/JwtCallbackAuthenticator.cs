using System.Net.Http.Headers;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using RelayHook.Core.Abstractions;

namespace RelayHook.Authentication.Jwt;

internal sealed class JwtCallbackAuthenticator : ICallbackAuthenticator
{
    private static readonly HashSet<string> ReservedClaims = new(StringComparer.OrdinalIgnoreCase)
    {
        "iss", "aud", "sub", "iat", "nbf", "exp", "jti"
    };

    public async ValueTask AuthenticateAsync(
        HttpRequestMessage request,
        CallbackAuthenticationContext context,
        CancellationToken cancellationToken)
    {
        var options = AuthenticationJson.Deserialize<JwtAuthenticationOptions>(context.Configuration);
        Validate(options);

        var signingKey = await context.SecretProvider
            .GetSecretAsync(options.SigningKeySecretReference, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            throw new CallbackAuthenticationException("JWT signing key could not be resolved.");
        }

        if (Encoding.UTF8.GetByteCount(signingKey) < 32)
        {
            throw new CallbackAuthenticationException("JWT signing key must contain at least 32 UTF-8 bytes.");
        }

        var now = context.TimeProvider.GetUtcNow();
        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["jti"] = context.CallbackId
        };

        if (!string.IsNullOrWhiteSpace(options.Subject))
        {
            claims["sub"] = options.Subject;
        }

        foreach (var claim in options.Claims)
        {
            if (ReservedClaims.Contains(claim.Key) || !claims.TryAdd(claim.Key, claim.Value))
            {
                throw new CallbackConfigurationException($"JWT claim '{claim.Key}' is reserved.");
            }
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            Claims = claims,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = now.Add(options.Lifetime).UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                SecurityAlgorithms.HmacSha256)
        };
        var handler = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false };
        var token = handler.CreateToken(descriptor);

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    private static void Validate(JwtAuthenticationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Issuer) ||
            string.IsNullOrWhiteSpace(options.Audience) ||
            string.IsNullOrWhiteSpace(options.SigningKeySecretReference) ||
            options.Lifetime <= TimeSpan.Zero ||
            options.Lifetime > TimeSpan.FromHours(1) ||
            options.Claims is null)
        {
            throw new CallbackConfigurationException(
                "JWT issuer, audience, signing-key secret reference, and lifetime from greater than zero through one hour are required.");
        }
    }
}
