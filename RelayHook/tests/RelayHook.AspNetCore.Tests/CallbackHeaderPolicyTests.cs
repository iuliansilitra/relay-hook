using RelayHook.AspNetCore.Delivery;
using RelayHook.Core.Abstractions;

namespace RelayHook.AspNetCore.Tests;

public sealed class CallbackHeaderPolicyTests
{
    [Theory]
    [InlineData("Authorization")]
    [InlineData("X-Callback-Id")]
    [InlineData("Content-Length")]
    public void Apply_WhenHeaderIsReserved_ShouldReject(string name)
    {
        using var request = new HttpRequestMessage();

        Assert.Throws<CallbackConfigurationException>(() =>
            CallbackHeaderPolicy.Apply(request, new Dictionary<string, string> { [name] = "value" }));
    }

    [Fact]
    public void Apply_WhenValueContainsNewline_ShouldReject()
    {
        using var request = new HttpRequestMessage();

        Assert.Throws<CallbackConfigurationException>(() =>
            CallbackHeaderPolicy.Apply(
                request,
                new Dictionary<string, string> { ["X-Custom"] = "safe\r\ninjected" }));
    }

    [Fact]
    public void Apply_WhenValueContainsOtherControlCharacter_ShouldReject()
    {
        using var request = new HttpRequestMessage();

        Assert.Throws<CallbackConfigurationException>(() =>
            CallbackHeaderPolicy.Apply(
                request,
                new Dictionary<string, string> { ["X-Custom"] = "safe\0unsafe" }));
    }
}
