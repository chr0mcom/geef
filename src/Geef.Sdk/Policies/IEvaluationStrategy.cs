using Geef.Sdk.Context;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;

namespace Geef.Sdk.Policies;

/// <summary>
/// Controls HOW reviewers are executed (parallel, sequential, fail-fast, etc.).
/// </summary>
public interface IEvaluationStrategy
{
    /// <summary>
    /// Executes the reviewers according to the strategy and returns the aggregated result.
    /// </summary>
    Task<EvaluationAggregate> ExecuteAsync(
        IReadOnlyList<IReviewer> reviewers,
        IRunContext context,
        CancellationToken cancellationToken = default);
}
