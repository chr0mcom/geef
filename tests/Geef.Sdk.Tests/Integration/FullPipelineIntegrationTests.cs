using FluentAssertions;
using Geef.Sdk.Context;
using Geef.Sdk.Events;
using Geef.Sdk.Exceptions;
using Geef.Sdk.Policies;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;
using Xunit;

namespace Geef.Sdk.Tests.Integration;

public sealed class FullPipelineIntegrationTests
{
    private static readonly ContextKey<string> ArtifactKey = new("test:artifact");

    private static IGroundingStep SimpleGrounding(string? extraKey = null)
    {
        return new DelegateGrounding(input =>
        {
            var ctx = (IRunContext)new RunContext().Set(ArtifactKey, "initial");
            return new GroundingResult { Context = ctx };
        });
    }

    [Fact]
    public async Task Pipeline_succeeds_on_first_attempt_when_reviewer_approves()
    {
        var pipeline = Geef.CreatePipeline<string>()
            .UseGrounding(SimpleGrounding())
            .UseExecution(new DelegateExecution((ctx, _) =>
                Task.FromResult(new ExecutionResult { UpdatedContext = ctx.Set(ArtifactKey, "generated") })))
            .AddReviewer(new DelegateReviewer("OK", (ctx, _) =>
                Task.FromResult(new ReviewResult
                {
                    ReviewerName = "OK",
                    Decision = ReviewDecision.Approved,
                    Duration = TimeSpan.FromMilliseconds(1)
                })))
            .UseFinalizer(new DelegateFinalizer<string>(ctx =>
                Task.FromResult(new FinalizeResult<string>
                {
                    Output = ctx.GetRequired(ArtifactKey),
                    FinalContext = ctx
                })))
            .Build();

        var result = await pipeline.RunAsync("test input");

        result.Success.Should().BeTrue();
        result.Output.Should().Be("generated");
        result.TotalIterations.Should().Be(1);
        result.RunId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Pipeline_retries_after_rejection_and_succeeds_on_second_iteration()
    {
        var iterationCount = 0;

        var pipeline = Geef.CreatePipeline<string>()
            .UseGrounding(SimpleGrounding())
            .UseExecution(new DelegateExecution((ctx, _) =>
            {
                iterationCount++;
                return Task.FromResult(new ExecutionResult { UpdatedContext = ctx.Set(ArtifactKey, $"v{iterationCount}") });
            }))
            .AddReviewer(new DelegateReviewer("Gatekeeper", (ctx, _) =>
            {
                var artifact = ctx.GetRequired(ArtifactKey);
                var passed = artifact == "v2";
                return Task.FromResult(new ReviewResult
                {
                    ReviewerName = "Gatekeeper",
                    Decision = passed ? ReviewDecision.Approved : ReviewDecision.Rejected,
                    Findings = passed ? Array.Empty<Finding>() : new[]
                    {
                        new Finding { ReviewerName = "Gatekeeper", Fingerprint = "needs-v2", Message = "Needs v2" }
                    },
                    Duration = TimeSpan.FromMilliseconds(1)
                });
            }))
            .UseFinalizer(new DelegateFinalizer<string>(ctx =>
                Task.FromResult(new FinalizeResult<string>
                {
                    Output = ctx.GetRequired(ArtifactKey),
                    FinalContext = ctx
                })))
            .Build();

        var result = await pipeline.RunAsync("input");

        result.Success.Should().BeTrue();
        result.Output.Should().Be("v2");
        result.TotalIterations.Should().Be(2);
    }

    [Fact]
    public async Task Pipeline_sets_PreviousFindings_for_next_execution()
    {
        IReadOnlyList<Finding>? capturedFindings = null;
        var callCount = 0;

        var pipeline = Geef.CreatePipeline<string>()
            .UseGrounding(SimpleGrounding())
            .UseExecution(new DelegateExecution((ctx, _) =>
            {
                callCount++;
                if (callCount > 1 && ctx.TryGet(GeefKeys.PreviousFindings, out var findings))
                    capturedFindings = findings;
                return Task.FromResult(new ExecutionResult { UpdatedContext = ctx.Set(ArtifactKey, $"v{callCount}") });
            }))
            .AddReviewer(new DelegateReviewer("F", (ctx, _) =>
            {
                var artifact = ctx.GetRequired(ArtifactKey);
                var ok = artifact == "v2";
                return Task.FromResult(new ReviewResult
                {
                    ReviewerName = "F",
                    Decision = ok ? ReviewDecision.Approved : ReviewDecision.Rejected,
                    Findings = ok ? Array.Empty<Finding>() : new[]
                    {
                        new Finding { ReviewerName = "F", Fingerprint = "fp1", Message = "Fix this" }
                    },
                    Duration = TimeSpan.FromMilliseconds(1)
                });
            }))
            .UseFinalizer(new DelegateFinalizer<string>(ctx =>
                Task.FromResult(new FinalizeResult<string> { Output = "done", FinalContext = ctx })))
            .Build();

        await pipeline.RunAsync("x");

        capturedFindings.Should().NotBeNull();
        capturedFindings!.Should().HaveCount(1);
        capturedFindings![0].Fingerprint.Should().Be("fp1");
    }

    [Fact]
    public async Task Pipeline_throws_ConvergenceFailedException_when_max_iterations_reached()
    {
        var pipeline = Geef.CreatePipeline<string>()
            .UseGrounding(SimpleGrounding())
            .UseExecution(new DelegateExecution((ctx, _) =>
                Task.FromResult(new ExecutionResult { UpdatedContext = ctx })))
            .AddReviewer(new DelegateReviewer("AlwaysReject", (ctx, _) =>
                Task.FromResult(new ReviewResult
                {
                    ReviewerName = "AlwaysReject",
                    Decision = ReviewDecision.Rejected,
                    Findings = new[]
                    {
                        new Finding { ReviewerName = "AlwaysReject", Fingerprint = "always-fail", Message = "Always fails" }
                    },
                    Duration = TimeSpan.FromMilliseconds(1)
                })))
            .UseFinalizer(new DelegateFinalizer<string>(ctx =>
                Task.FromResult(new FinalizeResult<string> { Output = "done", FinalContext = ctx })))
            .UseConvergencePolicy(new DefaultConvergencePolicy { MaxIterations = 3, StagnationThreshold = 99 })
            .Build();

        var act = async () => await pipeline.RunAsync("input");

        await act.Should().ThrowAsync<ConvergenceFailedException>()
            .Where(ex => ex.History.Count == 3
                      && ex.Reason == ConvergenceDecision.StopMaxAttemptsReached);
    }

    [Fact]
    public async Task Pipeline_fires_events_in_correct_order()
    {
        var events = new List<string>();

        var pipeline = Geef.CreatePipeline<string>()
            .UseGrounding(SimpleGrounding())
            .UseExecution(new DelegateExecution((ctx, _) =>
                Task.FromResult(new ExecutionResult { UpdatedContext = ctx.Set(ArtifactKey, "x") })))
            .AddReviewer(new DelegateReviewer("R", (ctx, _) =>
                Task.FromResult(new ReviewResult
                {
                    ReviewerName = "R",
                    Decision = ReviewDecision.Approved,
                    Duration = TimeSpan.FromMilliseconds(1)
                })))
            .UseFinalizer(new DelegateFinalizer<string>(ctx =>
                Task.FromResult(new FinalizeResult<string> { Output = "done", FinalContext = ctx })))
            .ConfigureEvents(e =>
            {
                e.OnPipelineStarted = _ => { events.Add(nameof(PipelineStartedEvent)); return Task.CompletedTask; };
                e.OnGroundingStarted = _ => { events.Add(nameof(GroundingStartedEvent)); return Task.CompletedTask; };
                e.OnGroundingCompleted = _ => { events.Add(nameof(GroundingCompletedEvent)); return Task.CompletedTask; };
                e.OnExecutionStarted = _ => { events.Add(nameof(ExecutionStartedEvent)); return Task.CompletedTask; };
                e.OnExecutionCompleted = _ => { events.Add(nameof(ExecutionCompletedEvent)); return Task.CompletedTask; };
                e.OnEvaluationApproved = _ => { events.Add(nameof(EvaluationApprovedEvent)); return Task.CompletedTask; };
                e.OnFinalizeStarted = _ => { events.Add(nameof(FinalizeStartedEvent)); return Task.CompletedTask; };
                e.OnFinalizeCompleted = _ => { events.Add(nameof(FinalizeCompletedEvent)); return Task.CompletedTask; };
                e.OnPipelineCompleted = _ => { events.Add(nameof(PipelineCompletedEvent)); return Task.CompletedTask; };
            })
            .Build();

        await pipeline.RunAsync("input");

        events.Should().ContainInOrder(
            nameof(PipelineStartedEvent),
            nameof(GroundingStartedEvent),
            nameof(GroundingCompletedEvent),
            nameof(ExecutionStartedEvent),
            nameof(ExecutionCompletedEvent),
            nameof(EvaluationApprovedEvent),
            nameof(FinalizeStartedEvent),
            nameof(FinalizeCompletedEvent),
            nameof(PipelineCompletedEvent));
    }

    [Fact]
    public async Task Pipeline_includes_RunId_in_result()
    {
        var pipeline = Geef.CreatePipeline<string>()
            .UseGrounding(SimpleGrounding())
            .UseExecution(new DelegateExecution((ctx, _) =>
                Task.FromResult(new ExecutionResult { UpdatedContext = ctx.Set(ArtifactKey, "x") })))
            .AddReviewer(new DelegateReviewer("R", (ctx, _) =>
                Task.FromResult(new ReviewResult { ReviewerName = "R", Decision = ReviewDecision.Approved })))
            .UseFinalizer(new DelegateFinalizer<string>(ctx =>
                Task.FromResult(new FinalizeResult<string> { Output = "out", FinalContext = ctx })))
            .Build();

        var r1 = await pipeline.RunAsync("a");
        var r2 = await pipeline.RunAsync("b");

        r1.RunId.Should().NotBe(r2.RunId);
        r1.RunId.Should().HaveLength(12);
    }

    [Fact]
    public async Task ConvergenceFailedException_wraps_provider_error()
    {
        var pipeline = Geef.CreatePipeline<string>()
            .UseGrounding(SimpleGrounding())
            .UseExecution(new DelegateExecution((ctx, _) =>
                throw new InvalidOperationException("Boom")))
            .AddReviewer(new DelegateReviewer("R", (ctx, _) =>
                Task.FromResult(new ReviewResult { ReviewerName = "R", Decision = ReviewDecision.Approved })))
            .UseFinalizer(new DelegateFinalizer<string>(ctx =>
                Task.FromResult(new FinalizeResult<string> { Output = "x", FinalContext = ctx })))
            .Build();

        var act = async () => await pipeline.RunAsync("input");

        await act.Should().ThrowAsync<ProviderException>()
            .WithInnerException<ProviderException, InvalidOperationException>();
    }

    // ── Inline test-helper implementations ───────────────────────────────────

    private sealed class DelegateGrounding : IGroundingStep
    {
        private readonly Func<string, GroundingResult> _fn;
        public DelegateGrounding(Func<string, GroundingResult> fn) => _fn = fn;
        public Task<GroundingResult> RunAsync(string input, CancellationToken ct = default)
            => Task.FromResult(_fn(input));
    }

    private sealed class DelegateExecution : IExecutionStep
    {
        private readonly Func<IRunContext, CancellationToken, Task<ExecutionResult>> _fn;
        public DelegateExecution(Func<IRunContext, CancellationToken, Task<ExecutionResult>> fn) => _fn = fn;
        public Task<ExecutionResult> RunAsync(IRunContext ctx, CancellationToken ct = default) => _fn(ctx, ct);
    }

    private sealed class DelegateReviewer : IReviewer
    {
        private readonly Func<IRunContext, CancellationToken, Task<ReviewResult>> _fn;
        public string Name { get; }
        public int Priority => 100;
        public DelegateReviewer(string name, Func<IRunContext, CancellationToken, Task<ReviewResult>> fn)
        { Name = name; _fn = fn; }
        public Task<ReviewResult> ReviewAsync(IRunContext ctx, CancellationToken ct = default) => _fn(ctx, ct);
    }

    private sealed class DelegateFinalizer<TOutput> : IFinalizer<TOutput>
    {
        private readonly Func<IRunContext, Task<FinalizeResult<TOutput>>> _fn;
        public DelegateFinalizer(Func<IRunContext, Task<FinalizeResult<TOutput>>> fn) => _fn = fn;
        public Task<FinalizeResult<TOutput>> FinalizeAsync(IRunContext ctx, CancellationToken ct = default) => _fn(ctx);
    }
}
