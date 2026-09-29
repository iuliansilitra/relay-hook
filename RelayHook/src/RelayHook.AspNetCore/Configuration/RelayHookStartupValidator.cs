using Microsoft.Extensions.DependencyInjection;
using RelayHook.Authentication;
using RelayHook.AspNetCore.Delivery;
using RelayHook.Core.Abstractions;

namespace RelayHook.AspNetCore.Configuration;

internal sealed class RelayHookStartupValidator(
    CallbackClientRegistry clients,
    CallbackAuthenticatorRegistry authenticators,
    ICallbackUrlPolicy urlPolicy,
    IServiceProvider serviceProvider)
{
    public void Validate()
    {
        foreach (var client in clients.GetAll())
        {
            foreach (var endpoint in client.Endpoints.Values)
            {
                urlPolicy.Validate(endpoint.Url);
                _ = authenticators.Resolve(endpoint.AuthenticationType, serviceProvider);
                using var request = new HttpRequestMessage();
                CallbackHeaderPolicy.Apply(request, endpoint.Headers);
            }
        }
    }
}
