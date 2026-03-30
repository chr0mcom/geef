using Geef.Sdk.Context;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;

namespace Geef.Sdk.Policies;

/// <summary>
/// Executes reviewers sequentially. Simplest strategy.
/// </summary>
public sealed class SequentialEvaluationStrategy : IEvaluationStrategy
{
    /// <inheritdoc />
    public async Task<EvaluationAggregate> ExecuteAsync(
        IReadOnlyList<IReviewer> reviewers,
        IRunContext context,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ReviewResult>(reviewers.Count);
        foreach (var reviewer in reviewers)
        {
            var result = await reviewer.ReviewAsync(context, cancellationToken);
            results.Add(result);
        }
        return new EvaluationAggregate { Reviews = results };
    }
}
