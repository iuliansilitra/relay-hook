using System.Text.Json;

namespace RelayHook.Core.Abstractions;

/// <summary>Non-secret configuration and secret resolver supplied to an authentication strategy.</summary>
public sealed class CallbackAuthenticationContext
{
    internal CallbackAuthenticationContext(
        string callbackId,
        JsonElement configuration,
        ICallbackSecretProvider secretProvider,
        TimeProvider timeProvider)
    {
        CallbackId = callbackId;
        Configuration = configuration;
        SecretProvider = secretProvider;
        TimeProvider = timeProvider;
    }

    /// <summary>Stable callback identifier.</summary>
    public string CallbackId { get; }

    /// <summary>Strategy-specific, non-secret configuration.</summary>
    public JsonElement Configuration { get; }

    /// <summary>Secret-reference resolver.</summary>
    public ICallbackSecretProvider SecretProvider { get; }

    /// <summary>Time source used by expiring credentials.</summary>
    public TimeProvider TimeProvider { get; }
}
