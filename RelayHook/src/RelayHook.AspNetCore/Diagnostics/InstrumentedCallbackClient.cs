using System.Data.Common;
using RelayHook.Core;
using RelayHook.Core.Abstractions;

namespace RelayHook.AspNetCore.Diagnostics;

internal sealed class InstrumentedCallbackClient(
    RelayHook.Core.Callbacks.CallbackClient inner,
    RelayHookDiagnostics diagnostics) : ICallbackClient, ICallbackTransactionalClient
{
    public async Task<Guid> EnqueueAsync<TPayload>(
        string client,
        string eventName,
        TPayload payload,
        CancellationToken cancellationToken = default)
    {
        var id = await inner.EnqueueAsync(client, eventName, payload, cancellationToken).ConfigureAwait(false);
        diagnostics.RecordEnqueued();
        return id;
    }

    public async Task<Guid> EnqueueAsync<TPayload>(
        string client,
        string eventName,
        TPayload payload,
        CallbackEnqueueOptions options,
        CancellationToken cancellationToken = default)
    {
        var id = await inner.EnqueueAsync(client, eventName, payload, options, cancellationToken).ConfigureAwait(false);
        diagnostics.RecordEnqueued();
        return id;
    }

    public async Task<Guid> EnqueueAsync<TPayload>(
        DbConnection connection,
        DbTransaction transaction,
        string client,
        string eventName,
        TPayload payload,
        CallbackEnqueueOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var id = await inner.EnqueueAsync(
            connection,
            transaction,
            client,
            eventName,
            payload,
            options,
            cancellationToken).ConfigureAwait(false);
        diagnostics.RecordEnqueued();
        return id;
    }
}
