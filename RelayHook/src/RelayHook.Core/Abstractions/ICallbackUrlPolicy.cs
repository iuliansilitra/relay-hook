namespace RelayHook.Core.Abstractions;

/// <summary>Validates outbound callback destinations.</summary>
public interface ICallbackUrlPolicy
{
    /// <summary>Throws when a URL is not permitted for outbound delivery.</summary>
    void Validate(Uri destination);
}
