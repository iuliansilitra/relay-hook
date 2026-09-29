using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RelayHook.Authentication.Jwt;
using RelayHook.Core.Abstractions;

namespace RelayHook.Authentication.Tests;

public sealed class JwtCallbackAuthenticatorTests
{
    [Fact]
    public async Task AuthenticateAsync_WhenConfigured_ShouldCreateVerifiableHs256Token()
    {
        const string signingKey = "01234567890123456789012345678901";
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var context = new CallbackAuthenticationContext(
            "callback-id",
            JsonSerializer.SerializeToElement(
                new JwtAuthenticationOptions
                {
                    Issuer = "issuer",
                    Audience = "audience",
                    Subject = "subject",
                    SigningKeySecretReference = "jwt-key",
                    Lifetime = TimeSpan.FromMinutes(5)
                },
                AuthenticationJson.Options),
            new DictionarySecretProvider(new Dictionary<string, string> { ["jwt-key"] = signingKey }),
            new FixedTimeProvider(now));
        using var request = new HttpRequestMessage();

        await new JwtCallbackAuthenticator().AuthenticateAsync(request, context, CancellationToken.None);

        var token = request.Headers.Authorization!.Parameter!;
        var segments = token.Split('.');
        Assert.Equal(3, segments.Length);
        var expectedSignature = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(signingKey),
            Encoding.ASCII.GetBytes($"{segments[0]}.{segments[1]}"));
        Assert.Equal(Base64Url(expectedSignature), segments[2]);

        using var payload = JsonDocument.Parse(Base64UrlDecode(segments[1]));
        Assert.Equal("issuer", payload.RootElement.GetProperty("iss").GetString());
        Assert.Equal("audience", payload.RootElement.GetProperty("aud").GetString());
        Assert.Equal(now.AddMinutes(5).ToUnixTimeSeconds(), payload.RootElement.GetProperty("exp").GetInt64());
        Assert.Equal("callback-id", payload.RootElement.GetProperty("jti").GetString());
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Convert.FromBase64String(padded);
    }
}
