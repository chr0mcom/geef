namespace Geef.Sdk.Events;

/// <summary>
/// Invokes configurable delegates for events (for simple scenarios without DI).
/// Backwards-compatible with the original hook concept.
/// </summary>
public sealed class DelegateEventSink : IGeefEventSink
{
    /// <summary>Called when a pipeline starts.</summary>
    public Func<PipelineStartedEvent, Task>? OnPipelineStarted { get; set; }

    /// <summary>Called when grounding starts.</summary>
    public Func<GroundingStartedEvent, Task>? OnGroundingStarted { get; set; }

    /// <summary>Called when grounding completes.</summary>
    public Func<GroundingCompletedEvent, Task>? OnGroundingCompleted { get; set; }

    /// <summary>Called when execution starts.</summary>
    public Func<ExecutionStartedEvent, Task>? OnExecutionStarted { get; set; }

    /// <summary>Called when execution completes.</summary>
    public Func<ExecutionCompletedEvent, Task>? OnExecutionCompleted { get; set; }

    /// <summary>Called when a reviewer starts.</summary>
    public Func<ReviewerStartedEvent, Task>? OnReviewerStarted { get; set; }

    /// <summary>Called when a reviewer completes.</summary>
    public Func<ReviewerCompletedEvent, Task>? OnReviewerCompleted { get; set; }

    /// <summary>Called when evaluation results in rejection.</summary>
    public Func<EvaluationRejectedEvent, Task>? OnEvaluationRejected { get; set; }

    /// <summary>Called when evaluation results in approval.</summary>
    public Func<EvaluationApprovedEvent, Task>? OnEvaluationApproved { get; set; }

    /// <summary>Called when finalize starts.</summary>
    public Func<FinalizeStartedEvent, Task>? OnFinalizeStarted { get; set; }

    /// <summary>Called when finalize completes.</summary>
    public Func<FinalizeCompletedEvent, Task>? OnFinalizeCompleted { get; set; }

    /// <summary>Called when a pipeline completes successfully.</summary>
    public Func<PipelineCompletedEvent, Task>? OnPipelineCompleted { get; set; }

    /// <summary>Called when a pipeline fails.</summary>
    public Func<PipelineFailedEvent, Task>? OnPipelineFailed { get; set; }

    /// <summary>Called when a reviewer throws an infrastructure error and is fault-isolated.</summary>
    public Func<ReviewerFaultIsolatedEvent, Task>? OnReviewerFaultIsolated { get; set; }

    /// <summary>Called when an advisor consultation starts.</summary>
    public Func<AdvisorConsultationStartedEvent, Task>? OnAdvisorConsultationStarted { get; set; }

    /// <summary>Called when an advisor consultation completes (including degraded outcomes).</summary>
    public Func<AdvisorConsultationCompletedEvent, Task>? OnAdvisorConsultationCompleted { get; set; }

    /// <inheritdoc />
    public async ValueTask PublishAsync(IGeefEvent geefEvent, CancellationToken cancellationToken = default)
    {
        var task = geefEvent switch
        {
            PipelineStartedEvent e => OnPipelineStarted?.Invoke(e),
            GroundingStartedEvent e => OnGroundingStarted?.Invoke(e),
            GroundingCompletedEvent e => OnGroundingCompleted?.Invoke(e),
            ExecutionStartedEvent e => OnExecutionStarted?.Invoke(e),
            ExecutionCompletedEvent e => OnExecutionCompleted?.Invoke(e),
            ReviewerStartedEvent e => OnReviewerStarted?.Invoke(e),
            ReviewerCompletedEvent e => OnReviewerCompleted?.Invoke(e),
            EvaluationRejectedEvent e => OnEvaluationRejected?.Invoke(e),
            EvaluationApprovedEvent e => OnEvaluationApproved?.Invoke(e),
            FinalizeStartedEvent e => OnFinalizeStarted?.Invoke(e),
            FinalizeCompletedEvent e => OnFinalizeCompleted?.Invoke(e),
            PipelineCompletedEvent e => OnPipelineCompleted?.Invoke(e),
            PipelineFailedEvent e => OnPipelineFailed?.Invoke(e),
            ReviewerFaultIsolatedEvent e => OnReviewerFaultIsolated?.Invoke(e),
            AdvisorConsultationStartedEvent e => OnAdvisorConsultationStarted?.Invoke(e),
            AdvisorConsultationCompletedEvent e => OnAdvisorConsultationCompleted?.Invoke(e),
            _ => null
        };

        if (task is not null)
            await task;
    }
}
