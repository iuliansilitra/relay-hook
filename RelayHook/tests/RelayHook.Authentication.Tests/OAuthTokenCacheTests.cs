using RelayHook.Authentication.OAuth;

namespace RelayHook.Authentication.Tests;

public sealed class OAuthTokenCacheTests
{
    [Fact]
    public async Task GetOrCreateAsync_WhenManyCallersMiss_ShouldAcquireOneToken()
    {
        var cache = new OAuthTokenCache();
        var now = DateTimeOffset.UtcNow;
        var acquisitionCount = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<OAuthToken> Acquire(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref acquisitionCount);
            await gate.Task.WaitAsync(cancellationToken);
            return new OAuthToken("token", now.AddMinutes(5));
        }

        var tasks = Enumerable.Range(0, 100)
            .Select(_ => cache.GetOrCreateAsync(
                "configuration",
                now,
                TimeSpan.FromSeconds(30),
                Acquire,
                CancellationToken.None))
            .ToArray();
        gate.SetResult();

        var tokens = await Task.WhenAll(tasks);

        Assert.Equal(1, acquisitionCount);
        Assert.All(tokens, token => Assert.Equal("token", token.AccessToken));
    }

    [Fact]
    public async Task Invalidate_WhenTokenCached_ShouldForceRefresh()
    {
        var cache = new OAuthTokenCache();
        var now = DateTimeOffset.UtcNow;
        var count = 0;
        Task<OAuthToken> Acquire(CancellationToken _) =>
            Task.FromResult(new OAuthToken($"token-{Interlocked.Increment(ref count)}", now.AddMinutes(5)));

        var first = await cache.GetOrCreateAsync(
            "configuration", now, TimeSpan.Zero, Acquire, CancellationToken.None);
        await cache.InvalidateAsync("configuration", "token-1", CancellationToken.None);
        var second = await cache.GetOrCreateAsync(
            "configuration", now, TimeSpan.Zero, Acquire, CancellationToken.None);

        Assert.Equal("token-1", first.AccessToken);
        Assert.Equal("token-2", second.AccessToken);
    }

    [Fact]
    public async Task InvalidateAsync_WhenRejectionIsForOldToken_ShouldKeepReplacement()
    {
        var cache = new OAuthTokenCache();
        var now = DateTimeOffset.UtcNow;
        var count = 0;
        Task<OAuthToken> Acquire(CancellationToken _) =>
            Task.FromResult(new OAuthToken($"token-{Interlocked.Increment(ref count)}", now.AddMinutes(5)));

        await cache.GetOrCreateAsync(
            "configuration", now, TimeSpan.Zero, Acquire, CancellationToken.None);
        await cache.InvalidateAsync("configuration", "token-1", CancellationToken.None);
        var replacement = await cache.GetOrCreateAsync(
            "configuration", now, TimeSpan.Zero, Acquire, CancellationToken.None);
        await cache.InvalidateAsync("configuration", "token-1", CancellationToken.None);
        var afterStaleRejection = await cache.GetOrCreateAsync(
            "configuration", now, TimeSpan.Zero, Acquire, CancellationToken.None);

        Assert.Same(replacement, afterStaleRejection);
        Assert.Equal(2, count);
    }
}
