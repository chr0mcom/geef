using Geef.Sdk.Context;
using Geef.Sdk.Diagnostics;
using Geef.Sdk.Events;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;

namespace Geef.Sdk.Runtime;

/// <summary>
/// Wraps a reviewer to emit structured events and tracing spans around each review call.
/// </summary>
internal sealed class InstrumentedReviewer : IReviewer
{
    private readonly IReviewer _inner;
    private readonly string _runId;
    private readonly int _iteration;
    private readonly IGeefEventSink _eventSink;

    internal InstrumentedReviewer(IReviewer inner, string runId, int iteration, IGeefEventSink eventSink)
    {
        _inner = inner;
        _runId = runId;
        _iteration = iteration;
        _eventSink = eventSink;
    }

    /// <inheritdoc />
    public string Name => _inner.Name;

    /// <inheritdoc />
    public int Priority => _inner.Priority;

    /// <inheritdoc />
    public async Task<ReviewResult> ReviewAsync(IRunContext context, CancellationToken cancellationToken = default)
    {
        await _eventSink.PublishAsync(
            new ReviewerStartedEvent(_runId, _iteration, _inner.Name, DateTimeOffset.UtcNow),
            cancellationToken);

        using var activity = GeefDiagnostics.ActivitySource.StartActivity("geef.review");
        activity?.SetTag("geef.run_id", _runId);
        activity?.SetTag("geef.iteration", _iteration);
        activity?.SetTag("geef.reviewer", _inner.Name);

        var result = await _inner.ReviewAsync(context, cancellationToken);

        activity?.SetTag("geef.review.decision", result.Decision.ToString());
        activity?.SetTag("geef.review.finding_count", result.Findings.Count);

        await _eventSink.PublishAsync(
            new ReviewerCompletedEvent(_runId, _iteration, result, DateTimeOffset.UtcNow),
            cancellationToken);

        return result;
    }
}
