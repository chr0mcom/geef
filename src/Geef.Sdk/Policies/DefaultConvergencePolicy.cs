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

    /// <inheritdoc />
    public ConvergenceDecision Evaluate(
        IterationHistory history,
        EvaluationAggregate currentAggregate,
        TimeSpan elapsed)
    {
        if (currentAggregate.IsFullyApproved)
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
