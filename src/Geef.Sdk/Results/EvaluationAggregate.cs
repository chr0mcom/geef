namespace Geef.Sdk.Results;

/// <summary>
/// Aggregated result of all reviewers in an evaluation round.
/// Produced by the EvaluationStrategy.
/// </summary>
public sealed record EvaluationAggregate
{
    /// <summary>All individual review results.</summary>
    public required IReadOnlyList<ReviewResult> Reviews { get; init; }

    /// <summary>All findings from all reviewers, combined.</summary>
    public IReadOnlyList<Finding> AllFindings =>
        Reviews.SelectMany(r => r.Findings).ToList();

    /// <summary>True if at least one reviewer is Rejected or Failed.</summary>
    public bool HasBlockingIssues =>
        Reviews.Any(r => r.Decision is ReviewDecision.Rejected or ReviewDecision.Failed);

    /// <summary>True if all reviewers are Approved, ApprovedWithWarnings, or NotApplicable.</summary>
    public bool IsFullyApproved =>
        Reviews.All(r => r.Decision is ReviewDecision.Approved
            or ReviewDecision.ApprovedWithWarnings
            or ReviewDecision.NotApplicable);

    /// <summary>Total duration of all reviews.</summary>
    public TimeSpan TotalDuration =>
        Reviews.Aggregate(TimeSpan.Zero, (sum, r) => sum + r.Duration);
}
