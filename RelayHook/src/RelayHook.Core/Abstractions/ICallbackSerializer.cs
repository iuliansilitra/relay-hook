namespace RelayHook.Core.Abstractions;

/// <summary>Serializes callback payloads at enqueue time.</summary>
public interface ICallbackSerializer
{
    /// <summary>Serializes a payload into its durable representation.</summary>
    string Serialize<TPayload>(TPayload payload);
}
