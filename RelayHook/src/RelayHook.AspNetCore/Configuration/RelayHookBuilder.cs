using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace RelayHook.AspNetCore.Configuration;

/// <summary>Fluent RelayHook registration builder.</summary>
public sealed class RelayHookBuilder
{
    internal RelayHookBuilder(IServiceCollection services, RelayHookRegistration registration)
    {
        Services = services;
        Registration = registration;
    }

    internal IServiceCollection Services { get; }

    internal RelayHookRegistration Registration { get; }

    /// <summary>Adds a logical callback client.</summary>
    /// <remarks>Client names are case-insensitive and must be unique.</remarks>
    /// <param name="name">Stable client name used by enqueue calls.</param>
    /// <param name="configure">Endpoint configuration.</param>
    /// <returns>The same builder for further configuration.</returns>
    public RelayHookBuilder AddClient(string name, Action<CallbackClientBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new CallbackClientBuilder(name);
        configure(builder);
        Registration.Clients.Add(builder.Build());
        return this;
    }

    /// <summary>Configures the JSON serializer used to freeze payloads at enqueue time.</summary>
    /// <param name="configure">Serializer option configuration.</param>
    /// <returns>The same builder for further configuration.</returns>
    public RelayHookBuilder ConfigureJson(Action<JsonSerializerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(Registration.JsonSerializerOptions);
        return this;
    }
}
