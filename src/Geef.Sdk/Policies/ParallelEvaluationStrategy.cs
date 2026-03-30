using Geef.Sdk.Context;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;

namespace Geef.Sdk.Policies;

/// <summary>
/// Executes all reviewers in parallel via Task.WhenAll.
/// </summary>
public sealed class ParallelEvaluationStrategy : IEvaluationStrategy
{
    /// <inheritdoc />
    public async Task<EvaluationAggregate> ExecuteAsync(
        IReadOnlyList<IReviewer> reviewers,
        IRunContext context,
        CancellationToken cancellationToken = default)
    {
        var tasks = reviewers.Select(r => r.ReviewAsync(context, cancellationToken));
        var results = await Task.WhenAll(tasks);
        return new EvaluationAggregate { Reviews = results.ToList() };
    }
}
