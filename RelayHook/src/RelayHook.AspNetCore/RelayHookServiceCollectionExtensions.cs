using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using RelayHook.Authentication;
using RelayHook.Authentication.ApiKey;
using RelayHook.Authentication.Basic;
using RelayHook.Authentication.Bearer;
using RelayHook.Authentication.Jwt;
using RelayHook.Authentication.None;
using RelayHook.Authentication.OAuth;
using RelayHook.AspNetCore.Configuration;
using RelayHook.AspNetCore.Delivery;
using RelayHook.AspNetCore.Diagnostics;
using RelayHook.AspNetCore.Health;
using RelayHook.AspNetCore.Hosting;
using RelayHook.Core.Abstractions;
using RelayHook.Core.Callbacks;
using RelayHook.Core.Configuration;
using RelayHook.Core.Retry;
using RelayHook.Core.Serialization;
using RelayHook.SqlServer;
using RelayHook.SqlServer.Migrations;
using RelayHook.SqlServer.Storage;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers RelayHook in an ASP.NET Core application.</summary>
public static class RelayHookServiceCollectionExtensions
{
    /// <summary>Adds RelayHook services, options validation, HTTP clients, and the hosted delivery worker.</summary>
    /// <remarks>
    /// Call <see cref="UseSqlServer(RelayHookBuilder, string)"/> on the returned builder before starting the host.
    /// Calling this method more than once for one service collection throws <see cref="InvalidOperationException"/>.
    /// </remarks>
    /// <param name="services">Application service collection.</param>
    /// <param name="configure">Optional runtime option configuration.</param>
    /// <returns>A builder used to select storage and register callback clients.</returns>
    public static RelayHookBuilder AddRelayHook(
        this IServiceCollection services,
        Action<RelayHookOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(RelayHookRegistration)))
        {
            throw new InvalidOperationException("AddRelayHook can only be called once per service collection.");
        }

        var registration = new RelayHookRegistration();
        services.AddSingleton(registration);
        services.AddOptions<RelayHookOptions>()
            .Configure(options => configure?.Invoke(options))
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<RelayHookOptions>, RelayHookOptionsValidator>());

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(registration.Clients);
        services.TryAddSingleton<ICallbackDestinationResolver>(registration.Clients);
        services.TryAddSingleton(registration.Authenticators);
        services.TryAddSingleton<RelayHookDiagnostics>();

        services.TryAddSingleton<NoneCallbackAuthenticator>();
        services.TryAddSingleton<ApiKeyCallbackAuthenticator>();
        services.TryAddSingleton<BasicCallbackAuthenticator>();
        services.TryAddSingleton<BearerCallbackAuthenticator>();
        services.TryAddSingleton<JwtCallbackAuthenticator>();
        services.TryAddSingleton<OAuthTokenCache>();
        services.TryAddSingleton<OAuth2ClientCredentialsAuthenticator>();
        RegisterBuiltInAuthenticators(registration);

        services.AddHttpClient("RelayHook.Delivery")
            .ConfigurePrimaryHttpMessageHandler(CreateSecureHandler);
        services.AddHttpClient("RelayHook.OAuth")
            .ConfigurePrimaryHttpMessageHandler(CreateSecureHandler);

        services.TryAddSingleton<ICallbackStorage, MissingCallbackStorage>();
        services.TryAddSingleton<ICallbackSecretProvider, ConfigurationCallbackSecretProvider>();
        services.TryAddSingleton<ICallbackSerializer>(serviceProvider =>
            new SystemTextJsonCallbackSerializer(
                serviceProvider.GetRequiredService<RelayHookRegistration>().JsonSerializerOptions));
        services.TryAddSingleton<ICallbackUrlPolicy>(serviceProvider =>
            new DefaultCallbackUrlPolicy(
                serviceProvider.GetRequiredService<IOptions<RelayHookOptions>>().Value));
        services.TryAddSingleton<IRetryPolicy>(serviceProvider =>
            new DefaultRetryPolicy(
                serviceProvider.GetRequiredService<IOptions<RelayHookOptions>>().Value.Retry));
        services.TryAddSingleton(serviceProvider =>
            serviceProvider.GetRequiredService<IOptions<RelayHookOptions>>().Value);

        services.TryAddSingleton<CallbackClient>();
        services.TryAddSingleton<InstrumentedCallbackClient>();
        services.TryAddSingleton<ICallbackClient>(serviceProvider =>
            serviceProvider.GetRequiredService<InstrumentedCallbackClient>());
        services.TryAddSingleton<ICallbackTransactionalClient>(serviceProvider =>
            serviceProvider.GetRequiredService<InstrumentedCallbackClient>());
        services.TryAddSingleton<CallbackHttpSender>();
        services.TryAddSingleton<CallbackDeliveryProcessor>();
        services.TryAddSingleton<RelayHookStartupValidator>();
        services.AddHostedService<RelayHookWorker>();

        return new RelayHookBuilder(services, registration);
    }

    /// <summary>Uses the supplied SQL Server database for RelayHook persistence.</summary>
    /// <remarks>
    /// RelayHook creates or upgrades schema <c>Callback</c> when its hosted worker starts.
    /// The connection string is retained in process configuration and is not stored in callback jobs.
    /// </remarks>
    /// <param name="builder">RelayHook registration builder.</param>
    /// <param name="connectionString">SQL Server connection string.</param>
    /// <returns>The same builder for further configuration.</returns>
    public static RelayHookBuilder UseSqlServer(
        this RelayHookBuilder builder,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var storageOptions = new SqlServerStorageOptions(connectionString);
        builder.Services.AddSingleton(storageOptions);
        builder.Services.TryAddSingleton<SqlSchemaInstaller>();
        builder.Services.TryAddSingleton<SqlCallbackStorage>();
        builder.Services.Replace(
            ServiceDescriptor.Singleton<ICallbackStorage>(serviceProvider =>
                serviceProvider.GetRequiredService<SqlCallbackStorage>()));
        return builder;
    }

    /// <summary>Registers a singleton custom authentication strategy under a stable type name.</summary>
    /// <param name="builder">RelayHook registration builder.</param>
    /// <param name="type">Stable type name stored in non-secret endpoint snapshots.</param>
    /// <typeparam name="TAuthenticator">Authenticator implementation resolved from dependency injection.</typeparam>
    /// <returns>The same builder for further configuration.</returns>
    public static RelayHookBuilder AddCallbackAuthenticator<TAuthenticator>(
        this RelayHookBuilder builder,
        string type)
        where TAuthenticator : class, ICallbackAuthenticator
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        builder.Services.TryAddSingleton<TAuthenticator>();
        builder.Registration.Authenticators.Register(
            type,
            serviceProvider => serviceProvider.GetRequiredService<TAuthenticator>());
        return builder;
    }

    /// <summary>Adds the <c>relayhook</c> database-connectivity and schema-compatibility health check.</summary>
    /// <param name="services">Application service collection.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddRelayHookHealthCheck(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddHealthChecks().AddCheck<RelayHookHealthCheck>("relayhook");
        return services;
    }

    private static void RegisterBuiltInAuthenticators(RelayHookRegistration registration)
    {
        registration.Authenticators.Register(
            AuthenticationTypeNames.None,
            serviceProvider => serviceProvider.GetRequiredService<NoneCallbackAuthenticator>());
        registration.Authenticators.Register(
            AuthenticationTypeNames.ApiKey,
            serviceProvider => serviceProvider.GetRequiredService<ApiKeyCallbackAuthenticator>());
        registration.Authenticators.Register(
            AuthenticationTypeNames.Basic,
            serviceProvider => serviceProvider.GetRequiredService<BasicCallbackAuthenticator>());
        registration.Authenticators.Register(
            AuthenticationTypeNames.Bearer,
            serviceProvider => serviceProvider.GetRequiredService<BearerCallbackAuthenticator>());
        registration.Authenticators.Register(
            AuthenticationTypeNames.Jwt,
            serviceProvider => serviceProvider.GetRequiredService<JwtCallbackAuthenticator>());
        registration.Authenticators.Register(
            AuthenticationTypeNames.OAuth2ClientCredentials,
            serviceProvider => serviceProvider.GetRequiredService<OAuth2ClientCredentialsAuthenticator>());
    }

    private static SocketsHttpHandler CreateSecureHandler() => new()
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = System.Net.DecompressionMethods.GZip |
                                 System.Net.DecompressionMethods.Deflate,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        ConnectTimeout = TimeSpan.FromSeconds(10)
    };
}
