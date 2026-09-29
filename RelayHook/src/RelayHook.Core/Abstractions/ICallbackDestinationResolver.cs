using RelayHook.Core.Callbacks;

namespace RelayHook.Core.Abstractions;

internal interface ICallbackDestinationResolver
{
    CallbackDestinationSnapshot Resolve(string clientName, string? endpointName);
}
