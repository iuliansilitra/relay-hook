using RelayHook.AspNetCore.Delivery;
using RelayHook.Core.Abstractions;
using RelayHook.Core.Configuration;

namespace RelayHook.AspNetCore.Tests;

public sealed class DefaultCallbackUrlPolicyTests
{
    [Fact]
    public void Validate_WhenHttpNotEnabled_ShouldRejectClearTextUrl()
    {
        var policy = new DefaultCallbackUrlPolicy(new RelayHookOptions());

        Assert.Throws<CallbackConfigurationException>(() =>
            policy.Validate(new Uri("http://example.com/callback")));
    }

    [Theory]
    [InlineData("https://127.0.0.1/callback")]
    [InlineData("https://10.0.0.1/callback")]
    [InlineData("https://192.168.1.1/callback")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://0.0.0.0/callback")]
    [InlineData("https://100.64.0.1/callback")]
    [InlineData("https://[::1]/callback")]
    [InlineData("https://[::ffff:10.0.0.1]/callback")]
    public void Validate_WhenLiteralAddressIsPrivate_ShouldReject(string value)
    {
        var policy = new DefaultCallbackUrlPolicy(new RelayHookOptions());

        Assert.Throws<CallbackConfigurationException>(() => policy.Validate(new Uri(value)));
    }

    [Fact]
    public void Validate_WhenPrivateHostExplicitlyAllowed_ShouldAccept()
    {
        var options = new RelayHookOptions();
        options.UrlSecurity.AllowedHosts.Add("127.0.0.1");
        var policy = new DefaultCallbackUrlPolicy(options);

        policy.Validate(new Uri("https://127.0.0.1/callback"));
    }
}
