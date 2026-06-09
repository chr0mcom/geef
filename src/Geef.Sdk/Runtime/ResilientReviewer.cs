using Geef.Sdk.Context;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;

namespace Geef.Sdk.Runtime;

/// <summary>
/// Wraps a reviewer with retry logic for transient failures. Uses an
/// <see cref="ITransientFaultClassifier"/> to decide which exceptions are worth retrying;
/// permanent errors are surfaced immediately without retrying.
/// After all attempts are exhausted the last exception is re-thrown (letting the outer
/// <see cref="InstrumentedReviewer"/> convert it to <see cref="ReviewDecision.Failed"/>).
/// </summary>
public sealed class ResilientReviewer : IReviewer
{
    private readonly IReviewer _inner;
    private readonly ITransientFaultClassifier _classifier;
    private readonly int _maxAttempts;
    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan _maxDelay;
    private const double BackoffFactor = 2.0;

    /// <summary>
    /// Wraps <paramref name="inner"/> with retry behavior.
    /// </summary>
    /// <param name="inner">The reviewer to wrap.</param>
    /// <param name="classifier">Classifies exceptions as transient or permanent.</param>
    /// <param name="maxAttempts">Maximum attempts including the initial call. Default: 4.</param>
    /// <param name="baseDelay">Initial backoff delay. Default: 1 second.</param>
    /// <param name="maxDelay">Upper bound on a single backoff wait. Default: 8 seconds.</param>
    public ResilientReviewer(
        IReviewer inner,
        ITransientFaultClassifier classifier,
        int maxAttempts = 4,
        TimeSpan? baseDelay = null,
        TimeSpan? maxDelay = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _maxAttempts = maxAttempts;
        _baseDelay = baseDelay ?? TimeSpan.FromSeconds(1);
        _maxDelay = maxDelay ?? TimeSpan.FromSeconds(8);
    }

    /// <inheritdoc />
    public string Name => _inner.Name;

    /// <inheritdoc />
    public int Priority => _inner.Priority;

    /// <inheritdoc />
    public async Task<ReviewResult> ReviewAsync(IRunContext context, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await _inner.ReviewAsync(context, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < _maxAttempts
                                        && !cancellationToken.IsCancellationRequested
                                        && _classifier.IsTransient(ex))
            {
                var delay = ComputeDelay(attempt);
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private TimeSpan ComputeDelay(int attempt)
    {
        if (_baseDelay <= TimeSpan.Zero) return TimeSpan.Zero;
        var scaled = _baseDelay.TotalMilliseconds * Math.Pow(BackoffFactor, attempt - 1);
        var capped = Math.Min(scaled, _maxDelay.TotalMilliseconds);
        var jitter = Random.Shared.Next(0, 250);
        return TimeSpan.FromMilliseconds(capped + jitter);
    }
}
