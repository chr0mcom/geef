using Geef.Sdk.Context;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;

namespace Geef.Sdk.Policies;

/// <summary>
/// Executes reviewers sequentially sorted by priority (lower value = higher priority).
/// Aborts as soon as a reviewer rejects with Severity >= Error.
/// Cheap/fast checks (syntax, formatting) run before expensive ones (AI review).
/// </summary>
public sealed class PriorityOrderedEvaluationStrategy : IEvaluationStrategy
{
    /// <inheritdoc />
    public async Task<EvaluationAggregate> ExecuteAsync(
        IReadOnlyList<IReviewer> reviewers,
        IRunContext context,
        CancellationToken cancellationToken = default)
    {
        var sorted = reviewers.OrderBy(r => r.Priority).ToList();
        var results = new List<ReviewResult>(sorted.Count);

        foreach (var reviewer in sorted)
        {
            var result = await reviewer.ReviewAsync(context, cancellationToken);
            results.Add(result);

            if (result.Decision is ReviewDecision.Rejected or ReviewDecision.Failed
                && result.Findings.Any(f => f.Severity >= FindingSeverity.Error))
            {
                break;
            }
        }

        return new EvaluationAggregate { Reviews = results };
    }
}
