using Microsoft.Extensions.Logging;
using RelayHook.Core.Callbacks;

namespace RelayHook.AspNetCore.Diagnostics;

internal static partial class RelayHookLog
{
    [LoggerMessage(1, LogLevel.Information, "RelayHook worker started. WorkerId={WorkerId}")]
    public static partial void WorkerStarted(ILogger logger, string workerId);

    [LoggerMessage(2, LogLevel.Error, "RelayHook worker cycle failed. WorkerId={WorkerId} ExceptionType={ExceptionType}")]
    public static partial void WorkerCycleFailed(ILogger logger, string workerId, string exceptionType);

    [LoggerMessage(3, LogLevel.Information, "RelayHook worker stopped. WorkerId={WorkerId}")]
    public static partial void WorkerStopped(ILogger logger, string workerId);

    [LoggerMessage(4, LogLevel.Information, "RelayHook retention deleted {DeletedCount} callback jobs.")]
    public static partial void RetentionDeleted(ILogger logger, int deletedCount);

    [LoggerMessage(10, LogLevel.Warning, "Callback lease lost before attempt start. CallbackId={CallbackId} WorkerId={WorkerId}")]
    public static partial void LeaseLostBeforeAttempt(ILogger logger, Guid callbackId, string workerId);

    [LoggerMessage(11, LogLevel.Warning, "Callback processing stopped after lease loss. CallbackId={CallbackId} WorkerId={WorkerId}")]
    public static partial void ProcessingStoppedAfterLeaseLoss(ILogger logger, Guid callbackId, string workerId);

    [LoggerMessage(12, LogLevel.Error, "Unexpected callback processing failure. CallbackId={CallbackId} WorkerId={WorkerId} ExceptionType={ExceptionType}")]
    public static partial void UnexpectedDeliveryFailure(
        ILogger logger,
        Guid callbackId,
        string workerId,
        string exceptionType);

    [LoggerMessage(13, LogLevel.Warning, "Callback completion rejected because lease ownership changed. CallbackId={CallbackId} WorkerId={WorkerId}")]
    public static partial void CompletionRejected(ILogger logger, Guid callbackId, string workerId);

    [LoggerMessage(14, LogLevel.Information, "Callback attempt completed. CallbackId={CallbackId} Client={Client} EndpointId={EndpointId} Endpoint={Endpoint} EventName={EventName} CorrelationId={CorrelationId} Attempt={Attempt} Status={Status} FailureType={FailureType} HttpStatusCode={HttpStatusCode} DurationMs={DurationMs} WorkerId={WorkerId}")]
    public static partial void AttemptCompleted(
        ILogger logger,
        Guid callbackId,
        string client,
        Guid endpointId,
        string endpoint,
        string eventName,
        string? correlationId,
        int attempt,
        CallbackStatus status,
        CallbackFailureType failureType,
        int? httpStatusCode,
        double durationMs,
        string workerId);

    [LoggerMessage(15, LogLevel.Error, "Callback lease renewal failed. CallbackId={CallbackId} WorkerId={WorkerId} ExceptionType={ExceptionType}")]
    public static partial void LeaseRenewalFailed(
        ILogger logger,
        Guid callbackId,
        string workerId,
        string exceptionType);
}
