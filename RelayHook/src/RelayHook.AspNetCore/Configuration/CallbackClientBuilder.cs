using RelayHook.Core.Abstractions;
using RelayHook.Core.Configuration;

namespace RelayHook.AspNetCore.Configuration;

/// <summary>Builds one logical callback client.</summary>
public sealed class CallbackClientBuilder
{
    private readonly string _name;
    private readonly Dictionary<string, CallbackEndpointDefinition> _endpoints =
        new(StringComparer.OrdinalIgnoreCase);

    internal CallbackClientBuilder(string name) => _name = name;

    /// <summary>Adds a named callback endpoint.</summary>
    /// <remarks>Endpoint names are case-insensitive and must be unique inside the client.</remarks>
    /// <param name="name">Endpoint name selected by <c>CallbackEnqueueOptions.Endpoint</c>.</param>
    /// <param name="configure">Destination, timeout, headers, and authentication configuration.</param>
    /// <returns>The same client builder.</returns>
    public CallbackClientBuilder AddEndpoint(string name, Action<CallbackEndpointBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);
        if (name.Length > 200)
        {
            throw new CallbackConfigurationException("Endpoint name cannot exceed 200 characters.");
        }

        var builder = new CallbackEndpointBuilder(name);
        configure(builder);
        if (!_endpoints.TryAdd(name, builder.Build()))
        {
            throw new CallbackConfigurationException(
                $"Client '{_name}' contains duplicate endpoint '{name}'.");
        }

        return this;
    }

    internal CallbackClientDefinition Build()
    {
        if (_name.Length > 200)
        {
            throw new CallbackConfigurationException("Client name cannot exceed 200 characters.");
        }

        if (_endpoints.Count == 0)
        {
            throw new CallbackConfigurationException($"Client '{_name}' must contain at least one endpoint.");
        }

        return new CallbackClientDefinition(
            _name,
            new Dictionary<string, CallbackEndpointDefinition>(_endpoints, StringComparer.OrdinalIgnoreCase));
    }
}
