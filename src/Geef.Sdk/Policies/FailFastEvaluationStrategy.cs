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

        var tasks = reviewers
            .Select(async r =>
            {
                try
                {
                    return await r.ReviewAsync(context, cts.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return new ReviewResult
                    {
                        ReviewerName = r.Name,
                        Decision = ReviewDecision.NotApplicable,
                        Findings = Array.Empty<Finding>(),
                        Duration = TimeSpan.Zero
                    };
                }
            })
            .ToList();

        var pending = new List<Task<ReviewResult>>(tasks);
        var results = new List<ReviewResult>(reviewers.Count);

        while (pending.Count > 0)
        {
            var completed = await Task.WhenAny(pending);
            pending.Remove(completed);

            var result = await completed;
            results.Add(result);

            if (result.Decision is ReviewDecision.Rejected or ReviewDecision.Failed)
            {
                cts.Cancel();
                // Observe remaining tasks non-blocking to prevent unobserved-exception faults.
                foreach (var remaining in pending)
                    _ = remaining.ContinueWith(
                        static t => _ = t.Exception,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
                break;
            }
        }

        return new EvaluationAggregate { Reviews = results };
    }
}
