using FluentAssertions;
using Geef.Sdk.Context;
using Geef.Sdk.Events;
using Geef.Sdk.Exceptions;
using Geef.Sdk.Policies;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;
using Xunit;

namespace Geef.Sdk.Tests.Runtime;

/// <summary>
/// Tests for Phase 1 reviewer fault isolation (F1):
/// - InstrumentedReviewer catches non-cancellation exceptions → Failed result (does not abort round)
/// - EvaluationAggregate.HasFailedReviewers / FailedReviewers helpers
/// - DefaultConvergencePolicy.FailedReviewerHandling options
/// </summary>
public sealed class ReviewerFaultIsolationTests
{
    private static IGroundingStep Grounding()
        => new DelegateGrounding(_ => new GroundingResult { Context = new RunContext() });

    private static IExecutionStep Execution()
        => new DelegateExecution((ctx, _) => Task.FromResult(new ExecutionResult { UpdatedContext = ctx }));

    private static IFinalizer<string> Finalizer()
        => new DelegateFinalizer<string>(ctx => Task.FromResult(new FinalizeResult<string> { Output = "done", FinalContext = ctx }));

    private static IReviewer ApproveReviewer(string name = "R")
        => new DelegateReviewer(name, (_, _) =>
            Task.FromResult(new ReviewResult { ReviewerName = name, Decision = ReviewDecision.Approved }));

    private static IReviewer ThrowingReviewer(string name = "Faulty")
        => new DelegateReviewer(name, (_, _) =>
            throw new InvalidOperationException("provider error"));

    // ── InstrumentedReviewer fault isolation ─────────────────────────────────

    [Fact]
    public async Task Throwing_reviewer_does_not_abort_round_when_another_reviewer_approves()
    {
        // TreatAsNonBlocking so the Failed from the throwing reviewer does not block convergence.
        var pipeline = Geef.CreatePipeline<string>()
            .UseGrounding(Grounding())
            .UseExecution(Execution())
            .AddReviewer(ThrowingReviewer("Faulty"))
            .AddReviewer(ApproveReviewer("Good"))
            .UseFinalizer(Finalizer())
            .UseConvergencePolicy(new DefaultConvergencePolicy
            {
                MaxIterations = 2,
                FailedReviewerHandling = FailedReviewerHandling.TreatAsNonBlocking
            })
            .Build();

        var result = await pipeline.RunAsync("input");

        result.Success.Should().BeTrue();
        result.TotalIterations.Should().Be(1);
    }

    [Fact]
    public async Task Throwing_reviewer_produces_Failed_result_with_diagnostic_finding()
    {
        var faultEvents = new List<ReviewerFaultIsolatedEvent>();

        var pipeline = Geef.CreatePipeline<string>()
            .UseGrounding(Grounding())
            .UseExecution(Execution())
            .AddReviewer(ThrowingReviewer("Faulty"))
            .AddReviewer(ApproveReviewer("Good"))
            .UseFinalizer(Finalizer())
            .UseConvergencePolicy(new DefaultConvergencePolicy
            {
                MaxIterations = 2,
                FailedReviewerHandling = FailedReviewerHandling.TreatAsNonBlocking
            })
            .ConfigureEvents(e =>
            {
                e.OnReviewerFaultIsolated = evt => { faultEvents.Add(evt); return Task.CompletedTask; };
            })
            .Build();

        await pipeline.RunAsync("input");

        faultEvents.Should().HaveCount(1);
        faultEvents[0].ReviewerName.Should().Be("Faulty");
        faultEvents[0].FaultMessage.Should().Contain("provider error");
    }

    [Fact]
    public async Task Cancellation_from_throwing_reviewer_is_not_caught_but_re_thrown()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var pipeline = Geef.CreatePipeline<string>()
            .UseGrounding(Grounding())
            .UseExecution(Execution())
            .AddReviewer(new DelegateReviewer("CancelReviewer", (_, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(new ReviewResult { ReviewerName = "CancelReviewer", Decision = ReviewDecision.Approved });
            }))
            .UseFinalizer(Finalizer())
            .UseConvergencePolicy(new DefaultConvergencePolicy
            {
                FailedReviewerHandling = FailedReviewerHandling.TreatAsNonBlocking
            })
            .Build();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pipeline.RunAsync("input", cts.Token));
    }

    // ── EvaluationAggregate helpers ──────────────────────────────────────────

    [Fact]
    public void HasFailedReviewers_is_false_when_all_approved()
    {
        var agg = new EvaluationAggregate
        {
            Reviews = new[]
            {
                new ReviewResult { ReviewerName = "R1", Decision = ReviewDecision.Approved },
                new ReviewResult { ReviewerName = "R2", Decision = ReviewDecision.ApprovedWithWarnings }
            }
        };

        agg.HasFailedReviewers.Should().BeFalse();
        agg.FailedReviewers.Should().BeEmpty();
        agg.IsApprovedIgnoringFailed.Should().BeTrue();
    }

    [Fact]
    public void HasFailedReviewers_is_true_when_one_failed()
    {
        var agg = new EvaluationAggregate
        {
            Reviews = new[]
            {
                new ReviewResult { ReviewerName = "R1", Decision = ReviewDecision.Approved },
                new ReviewResult { ReviewerName = "Faulty", Decision = ReviewDecision.Failed }
            }
        };

        agg.HasFailedReviewers.Should().BeTrue();
        agg.FailedReviewers.Should().HaveCount(1);
        agg.FailedReviewers[0].ReviewerName.Should().Be("Faulty");
        agg.IsApprovedIgnoringFailed.Should().BeTrue();  // non-failed reviewer approved
        agg.IsFullyApproved.Should().BeFalse();           // strict: Failed blocks
    }

    // ── DefaultConvergencePolicy.FailedReviewerHandling ──────────────────────

    [Fact]
    public async Task Block_mode_prevents_convergence_while_reviewer_keeps_failing()
    {
        // With Block (default) a Failed reviewer prevents convergence → StopMaxAttemptsReached
        var pipeline = Geef.CreatePipeline<string>()
            .UseGrounding(Grounding())
            .UseExecution(Execution())
            .AddReviewer(ThrowingReviewer("AlwaysFails"))
            .UseFinalizer(Finalizer())
            .UseConvergencePolicy(new DefaultConvergencePolicy
            {
                MaxIterations = 2,
                StagnationThreshold = 99,
                FailedReviewerHandling = FailedReviewerHandling.Block
            })
            .Build();

        var ex = await Assert.ThrowsAsync<ConvergenceFailedException>(() => pipeline.RunAsync("input"));
        ex.Reason.Should().Be(ConvergenceDecision.StopMaxAttemptsReached);
    }

    [Fact]
    public async Task TreatAsNonBlocking_mode_converges_despite_failing_reviewer()
    {
        var pipeline = Geef.CreatePipeline<string>()
            .UseGrounding(Grounding())
            .UseExecution(Execution())
            .AddReviewer(ThrowingReviewer("AlwaysFails"))
            .AddReviewer(ApproveReviewer("Good"))
            .UseFinalizer(Finalizer())
            .UseConvergencePolicy(new DefaultConvergencePolicy
            {
                MaxIterations = 3,
                FailedReviewerHandling = FailedReviewerHandling.TreatAsNonBlocking
            })
            .Build();

        var result = await pipeline.RunAsync("input");
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Abort_mode_throws_AbortReviewerUnavailable_immediately()
    {
        var pipeline = Geef.CreatePipeline<string>()
            .UseGrounding(Grounding())
            .UseExecution(Execution())
            .AddReviewer(ThrowingReviewer("AlwaysFails"))
            .UseFinalizer(Finalizer())
            .UseConvergencePolicy(new DefaultConvergencePolicy
            {
                FailedReviewerHandling = FailedReviewerHandling.Abort
            })
            .Build();

        var ex = await Assert.ThrowsAsync<ConvergenceFailedException>(() => pipeline.RunAsync("input"));
        ex.Reason.Should().Be(ConvergenceDecision.AbortReviewerUnavailable);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private sealed class DelegateGrounding(Func<string, GroundingResult> fn) : IGroundingStep
    {
        public Task<GroundingResult> RunAsync(string input, CancellationToken ct = default)
            => Task.FromResult(fn(input));
    }

    private sealed class DelegateExecution(Func<IRunContext, CancellationToken, Task<ExecutionResult>> fn) : IExecutionStep
    {
        public Task<ExecutionResult> RunAsync(IRunContext ctx, CancellationToken ct = default) => fn(ctx, ct);
    }

    private sealed class DelegateReviewer(string name, Func<IRunContext, CancellationToken, Task<ReviewResult>> fn) : IReviewer
    {
        public string Name => name;
        public int Priority => 100;
        public Task<ReviewResult> ReviewAsync(IRunContext ctx, CancellationToken ct = default) => fn(ctx, ct);
    }

    private sealed class DelegateFinalizer<TOutput>(Func<IRunContext, Task<FinalizeResult<TOutput>>> fn) : IFinalizer<TOutput>
    {
        public Task<FinalizeResult<TOutput>> FinalizeAsync(IRunContext ctx, CancellationToken ct = default) => fn(ctx);
    }
}
