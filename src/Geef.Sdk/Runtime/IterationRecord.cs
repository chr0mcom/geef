using Geef.Sdk.Results;

namespace Geef.Sdk.Runtime;

/// <summary>
/// Record of a single loop iteration for convergence analysis.
/// </summary>
public sealed record IterationRecord
{
    /// <summary>Iteration number (1-based).</summary>
    public required int Iteration { get; init; }

    /// <summary>Timestamp when this iteration started.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Duration of the execution phase in this iteration.</summary>
    public required TimeSpan ExecutionDuration { get; init; }

    /// <summary>The aggregated evaluation result of this iteration.</summary>
    public required EvaluationAggregate EvaluationResult { get; init; }

    /// <summary>
    /// Set of finding fingerprints in this iteration
    /// (for stagnation/regression detection).
    /// </summary>
    public IReadOnlySet<string> FindingFingerprints =>
        EvaluationResult.AllFindings.Select(f => f.Fingerprint).ToHashSet();
}
