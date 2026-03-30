using Geef.Sdk.Context;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;

namespace Geef.Sdk.Policies;

/// <summary>
/// Executes reviewers in parallel, but cancels remaining reviewers
/// as soon as the first reviewer returns Rejected or Failed.
/// Saves cost with expensive reviewers.
/// </summary>
public sealed class FailFastEvaluationStrategy : IEvaluationStrategy
{
    /// <inheritdoc />
    public async Task<EvaluationAggregate> ExecuteAsync(
        IReadOnlyList<IReviewer> reviewers,
        IRunContext context,
        CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var linkedToken = cts.Token;
        var results = new List<ReviewResult>(reviewers.Count);
        var tasks = reviewers.Select(async reviewer =>
        {
            try
            {
                return await reviewer.ReviewAsync(context, linkedToken);
            }
            catch (OperationCanceledException) when (linkedToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return new ReviewResult
                {
                    ReviewerName = reviewer.Name,
                    Decision = ReviewDecision.NotApplicable,
                    Findings = Array.Empty<Finding>(),
                    Duration = TimeSpan.Zero
                };
            }
        }).ToList();

        var remaining = new List<Task<ReviewResult>>(tasks);

        while (remaining.Count > 0)
        {
            var completed = await Task.WhenAny(remaining);
            remaining.Remove(completed);
            var result = await completed;
            results.Add(result);

            if (result.Decision is ReviewDecision.Rejected or ReviewDecision.Failed)
            {
                cts.Cancel();
                foreach (var task in remaining)
                {
                    try { await task; }
                    catch (OperationCanceledException) { }
                    if (task.IsCompletedSuccessfully)
                        results.Add(task.Result);
                }
                break;
            }
        }

        return new EvaluationAggregate { Reviews = results };
    }
}
