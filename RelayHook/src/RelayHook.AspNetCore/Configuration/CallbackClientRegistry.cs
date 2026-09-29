using RelayHook.Core.Abstractions;
using RelayHook.Core.Callbacks;
using RelayHook.Core.Configuration;

namespace RelayHook.AspNetCore.Configuration;

internal sealed class CallbackClientRegistry : ICallbackDestinationResolver
{
    private readonly Dictionary<string, CallbackClientDefinition> _clients =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public void Add(CallbackClientDefinition client)
    {
        lock (_sync)
        {
            if (!_clients.TryAdd(client.Name, client))
            {
                throw new CallbackConfigurationException(
                    $"Callback client '{client.Name}' is already configured.");
            }
        }
    }

    public CallbackDestinationSnapshot Resolve(string clientName, string? endpointName)
    {
        CallbackClientDefinition client;
        lock (_sync)
        {
            if (!_clients.TryGetValue(clientName, out client!))
            {
                throw new CallbackConfigurationException(
                    $"Callback client '{clientName}' is not configured.");
            }
        }

        CallbackEndpointDefinition endpoint;
        if (string.IsNullOrWhiteSpace(endpointName))
        {
            if (client.Endpoints.Count != 1)
            {
                throw new CallbackConfigurationException(
                    $"Callback client '{clientName}' has multiple endpoints; specify CallbackEnqueueOptions.Endpoint.");
            }

            endpoint = client.Endpoints.Values.Single();
        }
        else if (!client.Endpoints.TryGetValue(endpointName, out endpoint!))
        {
            throw new CallbackConfigurationException(
                $"Callback endpoint '{endpointName}' is not configured for client '{clientName}'.");
        }

        return new CallbackDestinationSnapshot(
            client.Name,
            endpoint.Name,
            endpoint.Url.AbsoluteUri,
            endpoint.Method.Method,
            endpoint.ContentType,
            endpoint.Timeout.TotalSeconds,
            endpoint.AuthenticationType,
            endpoint.AuthenticationConfiguration.Clone(),
            endpoint.Headers);
    }

    public IReadOnlyCollection<CallbackClientDefinition> GetAll()
    {
        lock (_sync)
        {
            return [.. _clients.Values];
        }
    }
}
