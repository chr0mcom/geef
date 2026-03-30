using Geef.Sdk.Context;
using Geef.Sdk.Results;

namespace Geef.Sdk.Providers;

/// <summary>
/// An independent reviewer that checks the artifacts in the context.
/// Any number of reviewers can be registered.
/// Reviewers MUST only READ the RunContext, not modify it.
/// Infrastructure errors (e.g. API timeouts) should be returned as ReviewDecision.Failed,
/// NOT thrown as exceptions (except for fatal errors).
/// </summary>
public interface IReviewer
{
    /// <summary>Unique name of the reviewer (for logging, events, finding attribution).</summary>
    string Name { get; }

    /// <summary>
    /// Priority for scheduling. Lower values = higher priority.
    /// Reviewers with higher priority run first (with PriorityOrdered strategy).
    /// Default: 100.
    /// </summary>
    int Priority => 100;

    /// <summary>
    /// Reviews the artifacts in the context and returns a result.
    /// </summary>
    Task<ReviewResult> ReviewAsync(
        IRunContext context,
        CancellationToken cancellationToken = default);
}
