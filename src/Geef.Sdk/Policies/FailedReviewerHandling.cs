namespace Geef.Sdk.Policies;

/// <summary>
/// Determines how reviewer infrastructure failures (isolated by the pipeline as <c>Failed</c> results)
/// affect convergence. A reviewer with <c>Failed</c> did not evaluate the document — it was
/// unavailable. The choice is whether to treat its absence as blocking or non-blocking.
/// </summary>
public enum FailedReviewerHandling
{
    /// <summary>
    /// <c>Failed</c> results are treated as blocking: the pipeline cannot converge as long as any
    /// reviewer reports <c>Failed</c>. This is equivalent to the SDK behaviour before fault isolation
    /// was introduced and is the default for consumers that want strict coverage guarantees.
    /// </summary>
    Block,

    /// <summary>
    /// <c>Failed</c> results are treated as non-blocking: a reviewer that could not run is recorded
    /// in the evaluation aggregate but does not prevent convergence. Use this when brief provider
    /// outages should not abort long-running runs. The failure is still visible in the result.
    /// </summary>
    TreatAsNonBlocking,

    /// <summary>
    /// Any <c>Failed</c> result causes the pipeline to abort immediately with
    /// <see cref="ConvergenceDecision.AbortReviewerUnavailable"/>.
    /// </summary>
    Abort
}
