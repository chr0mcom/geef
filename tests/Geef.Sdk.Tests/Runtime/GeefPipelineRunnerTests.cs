using FluentAssertions;
using Geef.Sdk.Context;
using Geef.Sdk.Events;
using Geef.Sdk.Exceptions;
using Geef.Sdk.Middleware;
using Geef.Sdk.Policies;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;
using Xunit;

namespace Geef.Sdk.Tests.Runtime;

public sealed class GeefPipelineRunnerTests
{
    private static readonly ContextKey<string> ArtifactKey = new("test:artifact");

    private static IGroundingStep ApproveGrounding()
        => new DelegateGrounding(_ => new GroundingResult { Context = new RunContext() });

    private static IExecutionStep PassThroughExecution()
        => new DelegateExecution((ctx, _) =>
            Task.FromResult(new ExecutionResult { UpdatedContext = ctx.Set(ArtifactKey, "ok") }));

    private static IReviewer ApproveReviewer(string name = "R")
        => new DelegateReviewer(name, (_, _) =>
            Task.FromResult(new ReviewResult { ReviewerName = name, Decision = ReviewDecision.Approved }));

    private static IFinalizer<string> StringFinalizer()
        => new DelegateFinalizer<string>(ctx =>
            Task.FromResult(new FinalizeResult<string> { Output = "done", FinalContext = ctx }));

    [Fact]
    public async Task ProviderException_wraps_grounding_error_with_correct_phase_and_provider_name()
    {
        var groundingName = typeof(ThrowingGrounding).Name;

        var pipeline = Geef.CreatePipeline<string>()
            .UseGrounding(new ThrowingGrounding())
            .UseExecution(PassThroughExecution())
            .AddReviewer(ApproveReviewer())
            .UseFinalizer(StringFinalizer())
            .Build();

        var ex = await Assert.ThrowsAsync<ProviderException>(() => pipeline.RunAsync("input"));

        ex.Phase.Should().Be(GeefPhase.Grounding);
        ex.ProviderName.Should().Be(groundingName);
        ex.InnerException.Should().BeOfType<InvalidOperationException>();
        ex.RunId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ProviderException_wraps_execution_error_with_correct_phase_and_iteration()
    {
        var pipeline = Geef.CreatePipeline<string>()
            .UseGrounding(ApproveGrounding())
            .UseExecution(new ThrowingExecution())
            .AddReviewer(ApproveReviewer())
            .UseFinalizer(StringFinalizer())
            .Build();

        var ex = await Assert.ThrowsAsync<ProviderException>(() => pipeline.RunAsync("input"));

        ex.Phase.Should().Be(GeefPhase.Execution);
        ex.ProviderName.Should().Be(typeof(ThrowingExecution).Name);
        ex.InnerException.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task ProviderException_wraps_finalize_error()
    {
        var pipeline = Geef.CreatePipeline<string>()
            .UseGrounding(ApproveGrounding())
            .UseExecution(PassThroughExecution())
            .AddReviewer(ApproveReviewer())
            .UseFinalizer(new ThrowingFinalizer<string>())
            .Build();

        var ex = await Assert.ThrowsAsync<ProviderException>(() => pipeline.RunAsync("input"));

        ex.Phase.Should().Be(GeefPhase.Finalize);
        ex.InnerException.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task PipelineFailedEvent_is_published_when_execution_throws()
    {
        var failedEvents = new List<PipelineFailedEvent>();

        var pipeline = Geef.CreatePipeline<string>()
            .UseGrounding(ApproveGrounding())
            .UseExecution(new ThrowingExecution())
            .AddReviewer(ApproveReviewer())
            .UseFinalizer(StringFinalizer())
            .ConfigureEvents(e =>
            {
                e.OnPipelineFailed = evt => { failedEvents.Add(evt); return Task.CompletedTask; };
            })
            .Build();

        await Assert.ThrowsAsync<ProviderException>(() => pipeline.RunAsync("input"));

        failedEvents.Should().HaveCount(1);
        failedEvents[0].RunId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task PipelineFailedEvent_is_published_when_convergence_fails()
    {
        var failedEvents = new List<PipelineFailedEvent>();

        var pipeline = Geef.CreatePipeline<string>()
            .UseGrounding(ApproveGrounding())
            .UseExecution(PassThroughExecution())
            .AddReviewer(new DelegateReviewer("AlwaysReject", (_, _) =>
                Task.FromResult(new ReviewResult
                {
                    ReviewerName = "AlwaysReject",
                    Decision = ReviewDecision.Rejected,
                    Findings = new[] { new Finding { ReviewerName = "AlwaysReject", Fingerprint = "fp", Message = "x" } }
                })))
            .UseFinalizer(StringFinalizer())
            .UseConvergencePolicy(new DefaultConvergencePolicy { MaxIterations = 2, StagnationThreshold = 99 })
            .ConfigureEvents(e =>
            {
                e.OnPipelineFailed = evt => { failedEvents.Add(evt); return Task.CompletedTask; };
            })
            .Build();

        await Assert.ThrowsAsync<ConvergenceFailedException>(() => pipeline.RunAsync("input"));

        failedEvents.Should().HaveCount(1);
    }

    [Fact]
    public async Task Cancellation_propagates_as_OperationCanceledException()
    {
        using var cts = new CancellationTokenSource();

        var pipeline = Geef.CreatePipeline<string>()
            .UseGrounding(new BlockingGrounding(cts))
            .UseExecution(PassThroughExecution())
            .AddReviewer(ApproveReviewer())
            .UseFinalizer(StringFinalizer())
            .Build();

        var act = async () => await pipeline.RunAsync("input", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RunId_is_propagated_into_ProviderException()
    {
        var pipeline = Geef.CreatePipeline<string>()
            .UseGrounding(new ThrowingGrounding())
            .UseExecution(PassThroughExecution())
            .AddReviewer(ApproveReviewer())
            .UseFinalizer(StringFinalizer())
            .Build();

        var ex = await Assert.ThrowsAsync<ProviderException>(() => pipeline.RunAsync("input"));

        ex.RunId.Should().NotBeNullOrEmpty();
        ex.RunId.Should().HaveLength(12);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private sealed class ThrowingGrounding : IGroundingStep
    {
        public Task<GroundingResult> RunAsync(string input, CancellationToken ct = default)
            => throw new InvalidOperationException("Grounding failed");
    }

    private sealed class ThrowingExecution : IExecutionStep
    {
        public Task<ExecutionResult> RunAsync(IRunContext ctx, CancellationToken ct = default)
            => throw new InvalidOperationException("Execution failed");
    }

    private sealed class ThrowingFinalizer<TOutput> : IFinalizer<TOutput>
    {
        public Task<FinalizeResult<TOutput>> FinalizeAsync(IRunContext ctx, CancellationToken ct = default)
            => throw new InvalidOperationException("Finalize failed");
    }

    private sealed class BlockingGrounding : IGroundingStep
    {
        private readonly CancellationTokenSource _cts;

        public BlockingGrounding(CancellationTokenSource cts) => _cts = cts;

        public async Task<GroundingResult> RunAsync(string input, CancellationToken ct = default)
        {
            _cts.CancelAfter(50);
            await Task.Delay(5000, ct);
            return new GroundingResult { Context = new RunContext() };
        }
    }

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
