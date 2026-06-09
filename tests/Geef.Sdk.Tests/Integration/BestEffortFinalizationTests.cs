using FluentAssertions;
using Geef.Sdk.Context;
using Geef.Sdk.Exceptions;
using Geef.Sdk.Policies;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;
using Xunit;

namespace Geef.Sdk.Tests.Integration;

file sealed class DelegateGrounding(Func<string, GroundingResult> fn) : IGroundingStep
{
    public Task<GroundingResult> RunAsync(string input, CancellationToken ct) =>
        Task.FromResult(fn(input));
}

file sealed class DelegateExecution(Func<IRunContext, CancellationToken, Task<ExecutionResult>> fn) : IExecutionStep
{
    public Task<ExecutionResult> RunAsync(IRunContext ctx, CancellationToken ct) => fn(ctx, ct);
}

file sealed class DelegateReviewer(string name, Func<IRunContext, CancellationToken, Task<ReviewResult>> fn) : IReviewer
{
    public string Name => name;
    public Task<ReviewResult> ReviewAsync(IRunContext ctx, CancellationToken ct) => fn(ctx, ct);
}

file sealed class DelegateFinalizer<TOutput>(Func<IRunContext, Task<FinalizeResult<TOutput>>> fn) : IFinalizer<TOutput>
{
    public Task<FinalizeResult<TOutput>> FinalizeAsync(IRunContext ctx, CancellationToken ct) => fn(ctx);
}

/// <summary>
/// Verifies the EnableBestEffortOnNonConvergence() option:
/// when a pipeline fails to converge, the runner finalizes the best available iteration
/// and returns GeefPipelineResult{Success=false} instead of throwing.
/// </summary>
public sealed class BestEffortFinalizationTests
{
    private static readonly ContextKey<string> DraftKey = new("test:draft");

    // Reviewer that always rejects, tracking call count per iteration
    private static IReviewer AlwaysReject(string name = "Blocker")
        => new DelegateReviewer(name, (_, _) =>
            Task.FromResult(new ReviewResult
            {
                ReviewerName = name,
                Decision     = ReviewDecision.Rejected,
                Findings     = [new Finding { ReviewerName = name, Fingerprint = "always", Message = "Rejected" }],
                Duration     = TimeSpan.Zero
            }));

    private static IReviewer AlwaysApprove(string name = "OK")
        => new DelegateReviewer(name, (_, _) =>
            Task.FromResult(new ReviewResult
            {
                ReviewerName = name,
                Decision     = ReviewDecision.Approved,
                Duration     = TimeSpan.Zero
            }));

    private static IReviewer AlwaysFail(string name = "Infra")
        => new DelegateReviewer(name, (_, _) =>
            Task.FromResult(new ReviewResult
            {
                ReviewerName = name,
                Decision     = ReviewDecision.Failed,
                Duration     = TimeSpan.Zero
            }));

    private static GeefPipelineRunner<string> BuildNonConvergingPipeline(
        int maxIterations = 2,
        Action<GeefPipelineBuilder<string>>? configure = null)
    {
        var iteration = 0;
        var builder = Geef.CreatePipeline<string>()
            .UseGrounding(new DelegateGrounding(_ =>
                new GroundingResult { Context = new RunContext() }))
            .UseExecution(new DelegateExecution((ctx, _) =>
            {
                iteration++;
                return Task.FromResult(new ExecutionResult
                {
                    UpdatedContext = ctx.Set(DraftKey, $"draft-v{iteration}")
                });
            }))
            .AddReviewer(AlwaysReject())
            .UseFinalizer(new DelegateFinalizer<string>(ctx =>
                Task.FromResult(new FinalizeResult<string>
                {
                    Output       = ctx.TryGet(DraftKey, out var d) ? d ?? "" : "",
                    FinalContext = ctx
                })))
            .UseConvergencePolicy(new DefaultConvergencePolicy { MaxIterations = maxIterations })
            .EnableBestEffortOnNonConvergence();

        configure?.Invoke(builder);
        return builder.Build();
    }

    [Fact]
    public async Task BestEffort_returns_success_false_and_stop_reason_when_pipeline_fails_to_converge()
    {
        var pipeline = BuildNonConvergingPipeline(maxIterations: 2);

        var result = await pipeline.RunAsync("input");

        result.Success.Should().BeFalse();
        result.StopReason.Should().NotBeNull();
        result.StopReason.Should().Be(ConvergenceDecision.StopMaxAttemptsReached);
    }

    [Fact]
    public async Task BestEffort_output_is_non_null_and_contains_best_iteration_content()
    {
        var pipeline = BuildNonConvergingPipeline(maxIterations: 3);

        var result = await pipeline.RunAsync("input");

        result.Success.Should().BeFalse();
        result.Output.Should().NotBeNullOrEmpty();
        // The finalizer returns the draft text; with best-effort we get the last iteration's draft
        result.Output.Should().StartWith("draft-v");
    }

    [Fact]
    public async Task BestEffort_selects_iteration_with_fewest_rejected_reviews()
    {
        // Iteration 1: 2 rejections; Iteration 2: 1 rejection; Iteration 3: 2 rejections
        // → iteration 2 should be selected (fewest rejections)
        var iterationCount = 0;
        var pipeline = Geef.CreatePipeline<string>()
            .UseGrounding(new DelegateGrounding(_ => new GroundingResult { Context = new RunContext() }))
            .UseExecution(new DelegateExecution((ctx, _) =>
            {
                iterationCount++;
                return Task.FromResult(new ExecutionResult
                {
                    UpdatedContext = ctx.Set(DraftKey, $"iteration-{iterationCount}")
                });
            }))
            .AddReviewer(new DelegateReviewer("R1", (ctx, _) =>
            {
                var draft = ctx.TryGet(DraftKey, out var d) ? d ?? "" : "";
                // Only rejects on iterations 1 and 3
                var reject = draft is "iteration-1" or "iteration-3";
                return Task.FromResult(new ReviewResult
                {
                    ReviewerName = "R1",
                    Decision     = reject ? ReviewDecision.Rejected : ReviewDecision.Approved,
                    Findings     = reject ? [new Finding { ReviewerName = "R1", Fingerprint = "r1", Message = "x" }] : [],
                    Duration     = TimeSpan.Zero
                });
            }))
            .AddReviewer(AlwaysReject("R2"))
            .UseFinalizer(new DelegateFinalizer<string>(ctx =>
                Task.FromResult(new FinalizeResult<string>
                {
                    Output       = ctx.TryGet(DraftKey, out var d) ? d ?? "" : "",
                    FinalContext = ctx
                })))
            .UseConvergencePolicy(new DefaultConvergencePolicy { MaxIterations = 3 })
            .EnableBestEffortOnNonConvergence()
            .Build();

        var result = await pipeline.RunAsync("input");

        result.Success.Should().BeFalse();
        // Iteration 2 has 1 rejection (R2 only), iterations 1 and 3 have 2 rejections each
        result.Output.Should().Be("iteration-2");
    }

    [Fact]
    public async Task BestEffort_uses_most_recent_iteration_when_rejections_are_tied()
    {
        // All iterations have the same number of rejections → most recent (last) wins
        var pipeline = BuildNonConvergingPipeline(maxIterations: 3);

        var result = await pipeline.RunAsync("input");

        result.Success.Should().BeFalse();
        // Last iteration is 3, so best draft should be draft-v3
        result.Output.Should().Be("draft-v3");
    }

    [Fact]
    public async Task BestEffort_disabled_throws_ConvergenceFailedException()
    {
        // Without EnableBestEffortOnNonConvergence(), the pipeline must still throw
        var iteration = 0;
        var pipeline = Geef.CreatePipeline<string>()
            .UseGrounding(new DelegateGrounding(_ => new GroundingResult { Context = new RunContext() }))
            .UseExecution(new DelegateExecution((ctx, _) =>
            {
                iteration++;
                return Task.FromResult(new ExecutionResult { UpdatedContext = ctx });
            }))
            .AddReviewer(AlwaysReject())
            .UseFinalizer(new DelegateFinalizer<string>(ctx =>
                Task.FromResult(new FinalizeResult<string> { Output = "", FinalContext = ctx })))
            .UseConvergencePolicy(new DefaultConvergencePolicy { MaxIterations = 2 })
            .Build();

        Func<Task> act = () => pipeline.RunAsync("input");

        await act.Should().ThrowAsync<ConvergenceFailedException>();
    }

    [Fact]
    public async Task DegradedIterations_is_zero_for_fully_successful_run()
    {
        var pipeline = Geef.CreatePipeline<string>()
            .UseGrounding(new DelegateGrounding(_ => new GroundingResult { Context = new RunContext() }))
            .UseExecution(new DelegateExecution((ctx, _) =>
                Task.FromResult(new ExecutionResult { UpdatedContext = ctx })))
            .AddReviewer(AlwaysApprove())
            .UseFinalizer(new DelegateFinalizer<string>(ctx =>
                Task.FromResult(new FinalizeResult<string> { Output = "ok", FinalContext = ctx })))
            .Build();

        var result = await pipeline.RunAsync("input");

        result.Success.Should().BeTrue();
        result.DegradedIterations.Should().Be(0);
    }

    [Fact]
    public async Task DegradedIterations_counts_iterations_with_failed_reviewers()
    {
        // Iteration 1: reviewer fails (infra); iteration 2: reviewer approves
        var iterationCount = 0;
        var pipeline = Geef.CreatePipeline<string>()
            .UseGrounding(new DelegateGrounding(_ => new GroundingResult { Context = new RunContext() }))
            .UseExecution(new DelegateExecution((ctx, _) =>
            {
                iterationCount++;
                return Task.FromResult(new ExecutionResult { UpdatedContext = ctx });
            }))
            .AddReviewer(new DelegateReviewer("Flaky", (_, _) =>
            {
                var decision = iterationCount == 1 ? ReviewDecision.Failed : ReviewDecision.Approved;
                return Task.FromResult(new ReviewResult
                {
                    ReviewerName = "Flaky",
                    Decision     = decision,
                    Duration     = TimeSpan.Zero
                });
            }))
            .UseFinalizer(new DelegateFinalizer<string>(ctx =>
                Task.FromResult(new FinalizeResult<string> { Output = "ok", FinalContext = ctx })))
            .UseConvergencePolicy(new DefaultConvergencePolicy
            {
                MaxIterations = 3,
                FailedReviewerHandling = FailedReviewerHandling.TreatAsNonBlocking
            })
            .Build();

        var result = await pipeline.RunAsync("input");

        result.Success.Should().BeTrue();
        result.DegradedIterations.Should().Be(1);
    }

    [Fact]
    public async Task IterationRecord_context_is_captured_when_BestEffort_is_enabled()
    {
        var pipeline = BuildNonConvergingPipeline(maxIterations: 2);

        var result = await pipeline.RunAsync("input");

        result.History.Records.Should().NotBeEmpty();
        result.History.Records.Should().OnlyContain(r => r.Context != null);
    }

    [Fact]
    public async Task IterationRecord_context_is_null_when_BestEffort_is_disabled()
    {
        // Without best-effort, context is not stored in records (avoids memory overhead)
        var iteration = 0;
        var pipeline = Geef.CreatePipeline<string>()
            .UseGrounding(new DelegateGrounding(_ => new GroundingResult { Context = new RunContext() }))
            .UseExecution(new DelegateExecution((ctx, _) =>
            {
                iteration++;
                return Task.FromResult(new ExecutionResult { UpdatedContext = ctx });
            }))
            .AddReviewer(AlwaysApprove())
            .UseFinalizer(new DelegateFinalizer<string>(ctx =>
                Task.FromResult(new FinalizeResult<string> { Output = "ok", FinalContext = ctx })))
            .Build();

        var result = await pipeline.RunAsync("input");

        result.History.Records.Should().NotBeEmpty();
        result.History.Records.Should().OnlyContain(r => r.Context == null);
    }

    [Fact]
    public async Task BestEffort_stop_reason_is_null_for_successful_run()
    {
        var pipeline = Geef.CreatePipeline<string>()
            .UseGrounding(new DelegateGrounding(_ => new GroundingResult { Context = new RunContext() }))
            .UseExecution(new DelegateExecution((ctx, _) =>
                Task.FromResult(new ExecutionResult { UpdatedContext = ctx })))
            .AddReviewer(AlwaysApprove())
            .UseFinalizer(new DelegateFinalizer<string>(ctx =>
                Task.FromResult(new FinalizeResult<string> { Output = "ok", FinalContext = ctx })))
            .EnableBestEffortOnNonConvergence()
            .Build();

        var result = await pipeline.RunAsync("input");

        result.Success.Should().BeTrue();
        result.StopReason.Should().BeNull();
    }
}
