using FluentAssertions;
using Geef.Sdk.Context;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;
using Geef.Sdk.Runtime;
using Xunit;

namespace Geef.Sdk.Tests.Runtime;

/// <summary>
/// Tests for Phase 2: ResilientReviewer + ITransientFaultClassifier.
/// </summary>
public sealed class ResilientReviewerTests
{
    private static IRunContext EmptyContext()
        => new RunContext();

    private static ITransientFaultClassifier AlwaysTransient()
        => new DelegateClassifier(_ => true);

    private static ITransientFaultClassifier AlwaysPermanent()
        => new DelegateClassifier(_ => false);

    private static ITransientFaultClassifier TransientFor<TException>() where TException : Exception
        => new DelegateClassifier(ex => ex is TException);

    // ── Basic behavior ────────────────────────────────────────────────────────

    [Fact]
    public async Task Returns_result_immediately_on_first_success()
    {
        var calls = 0;
        var inner = new DelegateReviewer("R", (_, _) =>
        {
            calls++;
            return Task.FromResult(new ReviewResult { ReviewerName = "R", Decision = ReviewDecision.Approved });
        });

        var resilient = new ResilientReviewer(inner, AlwaysTransient(), baseDelay: TimeSpan.Zero);
        var result = await resilient.ReviewAsync(EmptyContext());

        result.Decision.Should().Be(ReviewDecision.Approved);
        calls.Should().Be(1);
    }

    [Fact]
    public async Task Retries_transient_faults_until_success()
    {
        var calls = 0;
        var inner = new DelegateReviewer("R", (_, _) =>
        {
            calls++;
            if (calls < 3) throw new InvalidOperationException("transient");
            return Task.FromResult(new ReviewResult { ReviewerName = "R", Decision = ReviewDecision.Approved });
        });

        var resilient = new ResilientReviewer(inner, AlwaysTransient(), maxAttempts: 4, baseDelay: TimeSpan.Zero);
        var result = await resilient.ReviewAsync(EmptyContext());

        result.Decision.Should().Be(ReviewDecision.Approved);
        calls.Should().Be(3);
    }

    [Fact]
    public async Task Does_not_retry_permanent_faults()
    {
        var calls = 0;
        var inner = new DelegateReviewer("R", (_, _) =>
        {
            calls++;
            throw new UnauthorizedAccessException("401");
        });

        var resilient = new ResilientReviewer(inner, AlwaysPermanent(), maxAttempts: 4, baseDelay: TimeSpan.Zero);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => resilient.ReviewAsync(EmptyContext()));
        calls.Should().Be(1);
    }

    [Fact]
    public async Task Re_throws_after_exhausting_attempts()
    {
        var calls = 0;
        var inner = new DelegateReviewer("R", (_, _) =>
        {
            calls++;
            throw new InvalidOperationException("still failing");
        });

        var resilient = new ResilientReviewer(inner, AlwaysTransient(), maxAttempts: 3, baseDelay: TimeSpan.Zero);

        await Assert.ThrowsAsync<InvalidOperationException>(() => resilient.ReviewAsync(EmptyContext()));
        calls.Should().Be(3);
    }

    [Fact]
    public async Task Respects_cancellation_and_does_not_retry()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var calls = 0;
        var inner = new DelegateReviewer("R", (_, ct) =>
        {
            calls++;
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new ReviewResult { ReviewerName = "R", Decision = ReviewDecision.Approved });
        });

        var resilient = new ResilientReviewer(inner, AlwaysTransient(), maxAttempts: 4, baseDelay: TimeSpan.Zero);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => resilient.ReviewAsync(EmptyContext(), cts.Token));
        calls.Should().Be(1);
    }

    [Fact]
    public void Name_and_Priority_are_delegated_to_inner()
    {
        var inner = new DelegateReviewer("MyReviewer", (_, _) =>
            Task.FromResult(new ReviewResult { ReviewerName = "MyReviewer", Decision = ReviewDecision.Approved }),
            priority: 42);

        var resilient = new ResilientReviewer(inner, AlwaysTransient());

        resilient.Name.Should().Be("MyReviewer");
        resilient.Priority.Should().Be(42);
    }

    // ── ITransientFaultClassifier contract ───────────────────────────────────

    [Fact]
    public async Task Only_retries_exception_types_that_classifier_considers_transient()
    {
        var calls = 0;
        var inner = new DelegateReviewer("R", (_, _) =>
        {
            calls++;
            throw new InvalidOperationException("transient-only");
        });

        var resilient = new ResilientReviewer(inner,
            TransientFor<InvalidOperationException>(), maxAttempts: 3, baseDelay: TimeSpan.Zero);

        await Assert.ThrowsAsync<InvalidOperationException>(() => resilient.ReviewAsync(EmptyContext()));
        calls.Should().Be(3);
    }

    [Fact]
    public async Task Does_not_retry_when_classifier_returns_false_for_exception_type()
    {
        var calls = 0;
        var inner = new DelegateReviewer("R", (_, _) =>
        {
            calls++;
            throw new InvalidOperationException("not-transient");
        });

        var resilient = new ResilientReviewer(inner,
            AlwaysPermanent(), maxAttempts: 3, baseDelay: TimeSpan.Zero);

        await Assert.ThrowsAsync<InvalidOperationException>(() => resilient.ReviewAsync(EmptyContext()));
        calls.Should().Be(1);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private sealed class DelegateClassifier(Func<Exception, bool> fn) : ITransientFaultClassifier
    {
        public bool IsTransient(Exception exception) => fn(exception);
    }

    private sealed class DelegateReviewer(
        string name,
        Func<IRunContext, CancellationToken, Task<ReviewResult>> fn,
        int priority = 100) : IReviewer
    {
        public string Name => name;
        public int Priority => priority;
        public Task<ReviewResult> ReviewAsync(IRunContext ctx, CancellationToken ct = default) => fn(ctx, ct);
    }
}
