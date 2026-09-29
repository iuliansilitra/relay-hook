using RelayHook.Core.Abstractions;

namespace RelayHook.Authentication;

internal sealed class CallbackAuthenticatorRegistry
{
    private readonly Dictionary<string, Func<IServiceProvider, ICallbackAuthenticator>> _factories =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly object _sync = new();

    public void Register(string type, Func<IServiceProvider, ICallbackAuthenticator> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(factory);

        lock (_sync)
        {
            if (!_factories.TryAdd(type, factory))
            {
                throw new CallbackConfigurationException(
                    $"A callback authenticator named '{type}' is already registered.");
            }
        }
    }

    public ICallbackAuthenticator Resolve(string type, IServiceProvider serviceProvider)
    {
        Func<IServiceProvider, ICallbackAuthenticator>? factory;
        lock (_sync)
        {
            _factories.TryGetValue(type, out factory);
        }

        return factory?.Invoke(serviceProvider) ??
            throw new CallbackConfigurationException(
                $"No callback authenticator is registered for type '{type}'.");
    }
}
