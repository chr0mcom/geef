using Microsoft.Extensions.Logging;

namespace Geef.Sdk.Events;

/// <summary>
/// Logs events via ILogger (Microsoft.Extensions.Logging).
/// </summary>
public sealed class LoggingEventSink : IGeefEventSink
{
    private readonly ILogger _logger;

    /// <summary>Initializes a new <see cref="LoggingEventSink"/> with the given logger.</summary>
    public LoggingEventSink(ILogger logger) => _logger = logger;

    /// <inheritdoc />
    public ValueTask PublishAsync(IGeefEvent geefEvent, CancellationToken cancellationToken = default)
    {
        switch (geefEvent)
        {
            case PipelineStartedEvent e:
                _logger.LogInformation("[{RunId}] Pipeline started with input: {Input}", e.RunId, e.Input);
                break;
            case GroundingStartedEvent e:
                _logger.LogInformation("[{RunId}] Grounding started", e.RunId);
                break;
            case GroundingCompletedEvent e:
                _logger.LogInformation("[{RunId}] Grounding completed in {Duration}", e.RunId, e.Duration);
                break;
            case ExecutionStartedEvent e:
                _logger.LogInformation("[{RunId}] Execution started (iteration {Iteration})", e.RunId, e.Iteration);
                break;
            case ExecutionCompletedEvent e:
                _logger.LogInformation("[{RunId}] Execution completed (iteration {Iteration}) in {Duration}", e.RunId, e.Iteration, e.Duration);
                break;
            case ReviewerStartedEvent e:
                _logger.LogInformation("[{RunId}] Reviewer '{ReviewerName}' started (iteration {Iteration})", e.RunId, e.ReviewerName, e.Iteration);
                break;
            case ReviewerCompletedEvent e:
                _logger.LogInformation("[{RunId}] Reviewer '{ReviewerName}' completed: {Decision} (iteration {Iteration})",
                    e.RunId, e.Result.ReviewerName, e.Result.Decision, e.Iteration);
                break;
            case EvaluationRejectedEvent e:
                _logger.LogWarning("[{RunId}] Evaluation rejected (iteration {Iteration}): {FindingCount} findings, decision: {Decision}",
                    e.RunId, e.Iteration, e.Aggregate.AllFindings.Count, e.Decision);
                break;
            case EvaluationApprovedEvent e:
                _logger.LogInformation("[{RunId}] Evaluation approved (iteration {Iteration})", e.RunId, e.Iteration);
                break;
            case FinalizeStartedEvent e:
                _logger.LogInformation("[{RunId}] Finalize started", e.RunId);
                break;
            case FinalizeCompletedEvent e:
                _logger.LogInformation("[{RunId}] Finalize completed in {Duration}", e.RunId, e.Duration);
                break;
            case PipelineCompletedEvent e:
                _logger.LogInformation("[{RunId}] Pipeline completed: {Iterations} iterations in {Duration}",
                    e.RunId, e.TotalIterations, e.TotalDuration);
                break;
            case PipelineFailedEvent e:
                _logger.LogError("[{RunId}] Pipeline failed after {Iterations} iterations: {Reason}",
                    e.RunId, e.TotalIterations, e.Reason);
                break;
            default:
                _logger.LogDebug("[{RunId}] Unknown event: {EventType}", geefEvent.RunId, geefEvent.GetType().Name);
                break;
        }

        return ValueTask.CompletedTask;
    }
}
