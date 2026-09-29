using System.Collections.Concurrent;

namespace RelayHook.Authentication.OAuth;

internal sealed class OAuthTokenCache
{
    private readonly ConcurrentDictionary<string, CacheSlot> _slots = new(StringComparer.Ordinal);

    public async Task<OAuthToken> GetOrCreateAsync(
        string key,
        DateTimeOffset now,
        TimeSpan refreshBeforeExpiry,
        Func<CancellationToken, Task<OAuthToken>> factory,
        CancellationToken cancellationToken)
    {
        var slot = _slots.GetOrAdd(key, static _ => new CacheSlot());
        var current = slot.Token;
        if (current is not null && slot.RefreshAt > now)
        {
            return current;
        }

        await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            current = slot.Token;
            if (current is not null && slot.RefreshAt > now)
            {
                return current;
            }

            current = await factory(cancellationToken).ConfigureAwait(false);
            slot.Token = current;
            var lifetime = current.ExpiresAt - now;
            var effectiveWindow = refreshBeforeExpiry <= lifetime / 2
                ? refreshBeforeExpiry
                : TimeSpan.FromTicks(Math.Max(0, lifetime.Ticks / 2));
            slot.RefreshAt = current.ExpiresAt - effectiveWindow;
            return current;
        }
        finally
        {
            slot.Gate.Release();
        }
    }

    public async ValueTask InvalidateAsync(
        string key,
        string rejectedAccessToken,
        CancellationToken cancellationToken)
    {
        if (!_slots.TryGetValue(key, out var slot))
        {
            return;
        }

        await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (string.Equals(
                    slot.Token?.AccessToken,
                    rejectedAccessToken,
                    StringComparison.Ordinal))
            {
                slot.Token = null;
                slot.RefreshAt = DateTimeOffset.MinValue;
            }
        }
        finally
        {
            slot.Gate.Release();
        }
    }

    private sealed class CacheSlot
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public volatile OAuthToken? Token;

        public DateTimeOffset RefreshAt;
    }
}
