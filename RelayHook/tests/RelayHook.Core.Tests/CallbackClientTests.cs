using System.Data.Common;
using System.Text.Json;
using RelayHook.Core.Abstractions;
using RelayHook.Core.Callbacks;
using RelayHook.Core.Configuration;
using RelayHook.Core.Serialization;

namespace RelayHook.Core.Tests;

public sealed class CallbackClientTests
{
    [Fact]
    public async Task EnqueueAsync_ShouldPersistExactSerializedPayloadAndStableIdentity()
    {
        var storage = new CapturingStorage();
        var client = CreateClient(storage, maximumPayloadBytes: 1_024);
        var payload = new MutablePayload { Value = "before" };

        var id = await client.EnqueueAsync(
            "client",
            "event.created",
            payload,
            new CallbackEnqueueOptions { IdempotencyKey = "business-key" },
            CancellationToken.None);
        payload.Value = "after";

        Assert.Equal(id, storage.Command!.Id);
        Assert.Equal("{\"Value\":\"before\"}", storage.Command.Payload);
        Assert.Equal("business-key", storage.Command.IdempotencyKey);
    }

    [Fact]
    public async Task EnqueueAsync_WhenPayloadExceedsLimit_ShouldRejectBeforeStorage()
    {
        var storage = new CapturingStorage();
        var client = CreateClient(storage, maximumPayloadBytes: 8);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.EnqueueAsync(
            "client",
            "event.created",
            new { Value = "payload-is-too-large" },
            CancellationToken.None));

        Assert.Null(storage.Command);
    }

    [Theory]
    [InlineData("event\r\ninjected", null, null)]
    [InlineData("event.created", "key\nmalicious", null)]
    [InlineData("event.created", null, "correlation\u0000bad")]
    public async Task EnqueueAsync_WhenOutboundHeaderValueContainsControlCharacter_ShouldReject(
        string eventName,
        string? idempotencyKey,
        string? correlationId)
    {
        var storage = new CapturingStorage();
        var client = CreateClient(storage, maximumPayloadBytes: 1_024);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => client.EnqueueAsync(
            "client",
            eventName,
            new { Value = 1 },
            new CallbackEnqueueOptions
            {
                IdempotencyKey = idempotencyKey,
                CorrelationId = correlationId
            },
            CancellationToken.None));

        Assert.Null(storage.Command);
    }

    private static CallbackClient CreateClient(CapturingStorage storage, int maximumPayloadBytes)
    {
        var options = new RelayHookOptions { MaxPayloadBytes = maximumPayloadBytes };
        return new CallbackClient(
            storage,
            new FixedDestinationResolver(),
            new SystemTextJsonCallbackSerializer(new JsonSerializerOptions()),
            new PermissiveUrlPolicy(),
            options,
            TimeProvider.System);
    }

    private sealed class MutablePayload
    {
        public string Value { get; set; } = string.Empty;
    }

    private sealed class FixedDestinationResolver : ICallbackDestinationResolver
    {
        public CallbackDestinationSnapshot Resolve(string clientName, string? endpointName) =>
            new(
                clientName,
                endpointName ?? "primary",
                "https://example.com/callback",
                "POST",
                "application/json",
                30,
                "None",
                JsonSerializer.SerializeToElement(new { }),
                new Dictionary<string, string>());
    }

    private sealed class PermissiveUrlPolicy : ICallbackUrlPolicy
    {
        public void Validate(Uri destination)
        {
        }
    }

    private sealed class CapturingStorage : ICallbackStorage
    {
        public CallbackEnqueueCommand? Command { get; private set; }

        public Task<Guid> EnqueueAsync(
            CallbackEnqueueCommand command,
            DbConnection? connection,
            DbTransaction? transaction,
            CancellationToken cancellationToken)
        {
            Command = command;
            return Task.FromResult(command.Id);
        }

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<int> GetSchemaVersionAsync(CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<IReadOnlyList<CallbackJob>> ClaimAsync(
            string workerId,
            int batchSize,
            DateTimeOffset now,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Guid?> StartAttemptAsync(
            Guid jobId,
            string workerId,
            int attemptNumber,
            DateTimeOffset startedAt,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> RenewLeaseAsync(
            Guid jobId,
            string workerId,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> CompleteAttemptAsync(
            CallbackAttemptCompletion completion,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> CleanupAsync(
            DateTimeOffset now,
            RetentionOptions options,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
