using Geef.Sdk.Results;
using Geef.Sdk.Runtime;

namespace Geef.Sdk.Policies;

/// <summary>
/// Default convergence policy with configurable thresholds.
/// </summary>
public sealed class DefaultConvergencePolicy : IConvergencePolicy
{
    /// <summary>Maximum number of iterations. Default: 10.</summary>
    public int MaxIterations { get; init; } = 10;

    /// <summary>Maximum time budget for the entire loop. Default: 30 minutes.</summary>
    public TimeSpan MaxElapsedTime { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>Number of iterations without finding changes before stagnation is detected. Default: 3.</summary>
    public int StagnationThreshold { get; init; } = 3;

    /// <summary>Whether to abort immediately on Critical findings. Default: true.</summary>
    public bool AbortOnCritical { get; init; } = true;

    /// <summary>Whether regression should be detected and reported. Default: true.</summary>
    public bool DetectRegression { get; init; } = true;

    /// <summary>
    /// How to treat reviewers that reported an infrastructure failure (<see cref="ReviewDecision.Failed"/>).
    /// Default: <see cref="FailedReviewerHandling.Block"/> — preserves the pre-fault-isolation behaviour
    /// where a failed reviewer blocks convergence. Set to <see cref="FailedReviewerHandling.TreatAsNonBlocking"/>
    /// to allow the pipeline to converge even when some reviewers were temporarily unavailable.
    /// </summary>
    public FailedReviewerHandling FailedReviewerHandling { get; init; } = FailedReviewerHandling.Block;

    /// <inheritdoc />
    public ConvergenceDecision Evaluate(
        IterationHistory history,
        EvaluationAggregate currentAggregate,
        TimeSpan elapsed)
    {
        if (currentAggregate.HasFailedReviewers && FailedReviewerHandling == FailedReviewerHandling.Abort)
            return ConvergenceDecision.AbortReviewerUnavailable;

        var isApproved = FailedReviewerHandling == FailedReviewerHandling.TreatAsNonBlocking
            ? currentAggregate.IsApprovedIgnoringFailed
            : currentAggregate.IsFullyApproved;

        if (isApproved)
            return ConvergenceDecision.Approved;

        if (AbortOnCritical && currentAggregate.AllFindings.Any(f => f.Severity == FindingSeverity.Critical))
            return ConvergenceDecision.AbortCriticalBlocker;

        if (elapsed > MaxElapsedTime)
            return ConvergenceDecision.StopMaxAttemptsReached;

        if (history.Count >= MaxIterations)
            return ConvergenceDecision.StopMaxAttemptsReached;

        if (history.IsStagnant(StagnationThreshold))
            return ConvergenceDecision.StopStagnant;

        if (DetectRegression && history.HasRegression())
            return ConvergenceDecision.StopRegression;

        return ConvergenceDecision.Continue;
    }
}
