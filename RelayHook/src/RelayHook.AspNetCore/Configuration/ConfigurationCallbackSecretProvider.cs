using Microsoft.Extensions.Configuration;
using RelayHook.Core.Abstractions;

namespace RelayHook.AspNetCore.Configuration;

internal sealed class ConfigurationCallbackSecretProvider(IConfiguration configuration) : ICallbackSecretProvider
{
    public ValueTask<string?> GetSecretAsync(string secretReference, CancellationToken cancellationToken) =>
        ValueTask.FromResult(configuration[secretReference]);
}
