using RelayHook.AspNetCore.Configuration;

namespace RelayHook.AspNetCore.Tests;

public sealed class CallbackClientRegistryTests
{
    [Fact]
    public void Resolve_WhenClientHasOneEndpoint_ShouldCreateStableNonSecretSnapshot()
    {
        var clientBuilder = new CallbackClientBuilder("Partner");
        clientBuilder.AddEndpoint("primary", endpoint =>
        {
            endpoint.Url = new Uri("https://example.com/callback");
            endpoint.Timeout = TimeSpan.FromSeconds(15);
            endpoint.UseApiKey(options =>
            {
                options.HeaderName = "X-Partner-Key";
                options.SecretReference = "Secrets:Partner";
            });
        });
        var registry = new CallbackClientRegistry();
        registry.Add(clientBuilder.Build());

        var snapshot = registry.Resolve("partner", null);

        Assert.Equal("Partner", snapshot.ClientName);
        Assert.Equal("primary", snapshot.EndpointName);
        Assert.Equal("https://example.com/callback", snapshot.Url);
        Assert.Equal(15, snapshot.TimeoutSeconds);
        Assert.Contains("Secrets:Partner", snapshot.AuthenticationConfiguration.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", snapshot.AuthenticationConfiguration.GetRawText(), StringComparison.Ordinal);
    }
}
