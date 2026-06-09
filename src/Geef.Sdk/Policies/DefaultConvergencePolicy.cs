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

    /// <summary>
    /// Base wall-clock budget for the entire loop. Default: 30 minutes.
    /// When <see cref="MinutesPerIteration"/> is non-zero this acts as a floor: the effective
    /// budget is raised to <c>MaxIterations * MinutesPerIteration</c> when that product exceeds
    /// the value set here.
    /// </summary>
    public TimeSpan MaxElapsedTime { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Per-iteration time allowance used to auto-scale <see cref="MaxElapsedTime"/>.
    /// When greater than zero the effective time budget becomes
    /// <c>max(MaxElapsedTime, TimeSpan.FromMinutes(MaxIterations * MinutesPerIteration))</c>,
    /// so the iteration cap always governs and a fixed <see cref="MaxElapsedTime"/> can only
    /// raise the budget further. Default: 0 (auto-scaling disabled).
    /// </summary>
    public double MinutesPerIteration { get; init; } = 0;

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

    /// <summary>
    /// Minimum finding severity that blocks convergence. Findings with severity at or above this
    /// threshold prevent the iteration from being treated as approved, regardless of the reviewer's
    /// own decision. Default: <see cref="FindingSeverity.Error"/>.
    /// Set to <see cref="FindingSeverity.Critical"/> to only block on critical findings, or
    /// <see cref="FindingSeverity.Warning"/> to block on warnings as well.
    /// </summary>
    public FindingSeverity BlockingSeverity { get; init; } = FindingSeverity.Error;

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

        // Only apply BlockingSeverity to findings from non-Failed reviewers — Failed-reviewer findings
        // are infrastructure diagnostics, not content issues, and are governed by FailedReviewerHandling.
        if (isApproved)
        {
            var contentFindings = currentAggregate.Reviews
                .Where(r => r.Decision != ReviewDecision.Failed)
                .SelectMany(r => r.Findings);
            if (contentFindings.Any(f => f.Severity >= BlockingSeverity))
                isApproved = false;
        }

        if (isApproved)
            return ConvergenceDecision.Approved;

        if (AbortOnCritical && currentAggregate.AllFindings.Any(f => f.Severity == FindingSeverity.Critical))
            return ConvergenceDecision.AbortCriticalBlocker;

        var effectiveMaxElapsed = MinutesPerIteration > 0
            ? TimeSpan.FromMinutes(Math.Max(MaxElapsedTime.TotalMinutes, MaxIterations * MinutesPerIteration))
            : MaxElapsedTime;

        if (elapsed > effectiveMaxElapsed)
            return ConvergenceDecision.StopTimeBudgetReached;

        if (history.Count >= MaxIterations)
            return ConvergenceDecision.StopMaxAttemptsReached;

        if (history.IsStagnant(StagnationThreshold))
            return ConvergenceDecision.StopStagnant;

        if (DetectRegression && history.HasRegression())
            return ConvergenceDecision.StopRegression;

        return ConvergenceDecision.Continue;
    }
}
