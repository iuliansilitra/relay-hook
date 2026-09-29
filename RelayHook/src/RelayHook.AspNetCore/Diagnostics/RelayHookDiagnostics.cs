using System.Diagnostics;
using System.Diagnostics.Metrics;
using RelayHook.Core.Callbacks;

namespace RelayHook.AspNetCore.Diagnostics;

internal sealed class RelayHookDiagnostics : IDisposable
{
    public const string InstrumentationName = "RelayHook";
    private readonly Meter _meter = new(InstrumentationName);
    private readonly Counter<long> _enqueued;
    private readonly Counter<long> _completed;
    private readonly Counter<long> _failed;
    private readonly Counter<long> _retried;
    private readonly Histogram<double> _processingDuration;
    private readonly Histogram<double> _httpDuration;

    public RelayHookDiagnostics()
    {
        ActivitySource = new ActivitySource(InstrumentationName);
        _enqueued = _meter.CreateCounter<long>("callbacks.enqueued");
        _completed = _meter.CreateCounter<long>("callbacks.completed");
        _failed = _meter.CreateCounter<long>("callbacks.failed");
        _retried = _meter.CreateCounter<long>("callbacks.retried");
        _processingDuration = _meter.CreateHistogram<double>("callbacks.processing.duration", "ms");
        _httpDuration = _meter.CreateHistogram<double>("callbacks.http.duration", "ms");
    }

    public ActivitySource ActivitySource { get; }

    public void RecordEnqueued() => _enqueued.Add(1);

    public void RecordOutcome(CallbackStatus status, TimeSpan processingDuration, TimeSpan httpDuration)
    {
        if (status == CallbackStatus.Completed) _completed.Add(1);
        else if (status == CallbackStatus.Failed) _failed.Add(1);
        else if (status == CallbackStatus.Pending) _retried.Add(1);

        _processingDuration.Record(processingDuration.TotalMilliseconds);
        _httpDuration.Record(httpDuration.TotalMilliseconds);
    }

    public void Dispose()
    {
        ActivitySource.Dispose();
        _meter.Dispose();
    }
}
