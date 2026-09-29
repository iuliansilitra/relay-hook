using System.Net;
using RelayHook.Core.Abstractions;

namespace RelayHook.Authentication.Tests;

internal sealed class DictionarySecretProvider(IReadOnlyDictionary<string, string> secrets)
    : ICallbackSecretProvider
{
    public ValueTask<string?> GetSecretAsync(string secretReference, CancellationToken cancellationToken) =>
        ValueTask.FromResult(secrets.GetValueOrDefault(secretReference));
}

internal sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => value;
}

internal sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

internal sealed class DelegateHttpMessageHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) => send(request, cancellationToken);

    public static HttpResponseMessage Json(HttpStatusCode statusCode, string json) =>
        new(statusCode) { Content = new StringContent(json) };
}
