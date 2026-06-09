using FluentAssertions;
using Geef.Sdk.Advisors;
using Geef.Sdk.Context;
using Geef.Sdk.Exceptions;
using Geef.Sdk.Policies;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;
using Xunit;

namespace Geef.Sdk.Tests.Advisors;

public sealed class AdvisorTriggerTests
{
    private static readonly ContextKey<string> ArtifactKey = new("test:artifact");
    private static readonly ContextKey<int> IterationKey = new("test:iterations");

    // ── BeforeFirstExecution ──────────────────────────────────────────────

    [Fact]
    public async Task BeforeFirst_advisor_is_consulted_exactly_once_regardless_of_iteration_count()
    {
        var advisor = new CountingAdvisor("first-only");
        var iterationCount = 0;

        // Pipeline that rejects the first draft, approves the second.
        var pipeline = BuildPipeline()
            .AddAdvisor(advisor, AdvisorTrigger.BeforeFirstExecution)
            .UseExecution(new DelegateExecution((ctx, _) =>
            {
                iterationCount++;
                return Task.FromResult(new ExecutionResult { UpdatedContext = ctx.Set(ArtifactKey, $"v{iterationCount}") });
            }))
            .AddReviewer(new DelegateReviewer("gate", (ctx, _) =>
            {
                var val = ctx.TryGet(ArtifactKey, out var v) ? v : "";
                return Task.FromResult(new ReviewResult
                {
                    ReviewerName = "gate",
                    Decision = val == "v2" ? ReviewDecision.Approved : ReviewDecision.ApprovedWithWarnings,
                    Findings = val == "v2" ? [] : [new Finding { ReviewerName = "gate", Fingerprint = "fp", Message = "needs v2", Severity = FindingSeverity.Error }]
                });
            }))
            .Build();

        await pipeline.RunAsync("in");

        advisor.CallCount.Should().Be(1, "BeforeFirst fires only on iteration 1");
        iterationCount.Should().Be(2);
    }

    [Fact]
    public async Task BeforeFirst_advisor_context_is_set_on_iteration_1()
    {
        // BeforeFirst fires only on iteration 1. The resulting context naturally persists
        // into later iterations via immutable context propagation (run-wide guidance).
        var contextOnIteration1 = (string?)null;

        var pipeline = BuildPipeline()
            .AddAdvisor(new FixedOutputAdvisor("a", "advice-text"), AdvisorTrigger.BeforeFirstExecution)
            .UseExecution(new DelegateExecution((ctx, _) =>
            {
                ctx.TryGet(GeefKeys.AdvisorContext, out var advice);
                var iter = ctx.TryGet(GeefKeys.CurrentIteration, out var i) ? i : 0;
                if (iter == 1) contextOnIteration1 = advice;
                return Task.FromResult(new ExecutionResult { UpdatedContext = ctx.Set(ArtifactKey, $"v{iter}") });
            }))
            .AddReviewer(new DelegateReviewer("gate", (ctx, _) =>
            {
                var iter = ctx.TryGet(GeefKeys.CurrentIteration, out var i) ? i : 0;
                return Task.FromResult(new ReviewResult
                {
                    ReviewerName = "gate",
                    Decision = iter == 2 ? ReviewDecision.Approved : ReviewDecision.ApprovedWithWarnings,
                    Findings = iter == 2 ? [] : [new Finding { ReviewerName = "gate", Fingerprint = "fp", Message = "retry", Severity = FindingSeverity.Error }]
                });
            }))
            .Build();

        await pipeline.RunAsync("in");

        contextOnIteration1.Should().Contain("advice-text");
    }

    // ── BeforeEveryExecution ──────────────────────────────────────────────

    [Fact]
    public async Task BeforeEvery_advisor_is_consulted_every_iteration()
    {
        var advisor = new CountingAdvisor("every");
        var iterationCount = 0;

        var pipeline = BuildPipeline()
            .AddAdvisor(advisor, AdvisorTrigger.BeforeEveryExecution)
            .UseExecution(new DelegateExecution((ctx, _) =>
            {
                iterationCount++;
                return Task.FromResult(new ExecutionResult { UpdatedContext = ctx.Set(ArtifactKey, $"v{iterationCount}") });
            }))
            .AddReviewer(new DelegateReviewer("gate", (ctx, _) =>
            {
                var val = ctx.TryGet(ArtifactKey, out var v) ? v : "";
                return Task.FromResult(new ReviewResult
                {
                    ReviewerName = "gate",
                    Decision = val == "v3" ? ReviewDecision.Approved : ReviewDecision.ApprovedWithWarnings,
                    Findings = val == "v3" ? [] : [new Finding { ReviewerName = "gate", Fingerprint = "fp", Message = "not v3", Severity = FindingSeverity.Error }]
                });
            }))
            .Build();

        await pipeline.RunAsync("in");

        advisor.CallCount.Should().Be(3, "BeforeEvery fires on each of the 3 iterations");
    }

    [Fact]
    public async Task BeforeEvery_advisor_context_is_present_on_every_iteration()
    {
        var advisorContextSeen = new List<bool>();

        var pipeline = BuildPipeline()
            .AddAdvisor(new FixedOutputAdvisor("b", "guidance"), AdvisorTrigger.BeforeEveryExecution)
            .UseExecution(new DelegateExecution((ctx, _) =>
            {
                advisorContextSeen.Add(ctx.TryGet(GeefKeys.AdvisorContext, out var v) && !string.IsNullOrEmpty(v));
                var iter = ctx.TryGet(GeefKeys.CurrentIteration, out var i) ? i : 0;
                return Task.FromResult(new ExecutionResult { UpdatedContext = ctx.Set(ArtifactKey, $"v{iter}") });
            }))
            .AddReviewer(new DelegateReviewer("gate", (ctx, _) =>
            {
                var iter = ctx.TryGet(GeefKeys.CurrentIteration, out var i) ? i : 0;
                return Task.FromResult(new ReviewResult
                {
                    ReviewerName = "gate",
                    Decision = iter == 2 ? ReviewDecision.Approved : ReviewDecision.ApprovedWithWarnings,
                    Findings = iter == 2 ? [] : [new Finding { ReviewerName = "gate", Fingerprint = "fp", Message = "retry", Severity = FindingSeverity.Error }]
                });
            }))
            .Build();

        await pipeline.RunAsync("in");

        advisorContextSeen.Should().AllSatisfy(seen => seen.Should().BeTrue(),
            "advisor context must be present on every iteration");
    }

    // ── AdvisorContext format ─────────────────────────────────────────────

    [Fact]
    public async Task AdvisorContext_key_contains_advisor_name_and_advice_text()
    {
        string? capturedContext = null;

        var pipeline = BuildPipeline()
            .AddAdvisor(new FixedOutputAdvisor("my-advisor", "do this and that"), AdvisorTrigger.BeforeEveryExecution)
            .UseExecution(new DelegateExecution((ctx, _) =>
            {
                ctx.TryGet(GeefKeys.AdvisorContext, out capturedContext);
                return Task.FromResult(new ExecutionResult { UpdatedContext = ctx.Set(ArtifactKey, "done") });
            }))
            .AddReviewer(new DelegateReviewer("ok", (_, _) => Task.FromResult(new ReviewResult
                { ReviewerName = "ok", Decision = ReviewDecision.Approved })))
            .Build();

        await pipeline.RunAsync("in");

        capturedContext.Should().Contain("my-advisor");
        capturedContext.Should().Contain("do this and that");
    }

    // ── OnConvergenceFailure ──────────────────────────────────────────────

    [Fact]
    public async Task OnConvergenceFailure_advisor_enables_exactly_one_recovery_pass()
    {
        var advisor = new CountingAdvisor("recovery");
        var iterationCount = 0;

        // Policy: max 2 iterations. On failure, advisor fires once; recovery iteration succeeds.
        var pipeline = BuildPipeline(maxIterations: 2)
            .AddAdvisor(advisor, AdvisorTrigger.OnConvergenceFailure)
            .UseExecution(new DelegateExecution((ctx, _) =>
            {
                iterationCount++;
                return Task.FromResult(new ExecutionResult { UpdatedContext = ctx.Set(ArtifactKey, $"v{iterationCount}") });
            }))
            .AddReviewer(new DelegateReviewer("gate", (ctx, _) =>
            {
                // Iterations 1+2 fail; iteration 3 (recovery pass) succeeds.
                var val = ctx.TryGet(ArtifactKey, out var v) ? v : "";
                var approved = val == "v3";
                return Task.FromResult(new ReviewResult
                {
                    ReviewerName = "gate",
                    Decision = approved ? ReviewDecision.Approved : ReviewDecision.ApprovedWithWarnings,
                    Findings = approved ? [] : [new Finding { ReviewerName = "gate", Fingerprint = "fp", Message = "retry", Severity = FindingSeverity.Error }]
                });
            }))
            .Build();

        var result = await pipeline.RunAsync("in");

        result.Success.Should().BeTrue("recovery pass succeeded");
        advisor.CallCount.Should().Be(1, "recovery advisor fires exactly once");
        iterationCount.Should().Be(3, "2 normal + 1 recovery");
    }

    [Fact]
    public async Task OnConvergenceFailure_advisor_context_is_injected_during_recovery_pass()
    {
        string? recoveryContext = null;
        var advisorAdvice = "critical revision needed";

        var pipeline = BuildPipeline(maxIterations: 1)
            .AddAdvisor(new FixedOutputAdvisor("recovery-advisor", advisorAdvice), AdvisorTrigger.OnConvergenceFailure)
            .UseExecution(new DelegateExecution((ctx, _) =>
            {
                var iter = ctx.TryGet(GeefKeys.CurrentIteration, out var i) ? i : 0;
                if (iter == 2) // recovery pass
                    ctx.TryGet(GeefKeys.AdvisorContext, out recoveryContext);
                return Task.FromResult(new ExecutionResult { UpdatedContext = ctx.Set(ArtifactKey, iter == 2 ? "ok" : "bad") });
            }))
            .AddReviewer(new DelegateReviewer("gate", (ctx, _) =>
            {
                var val = ctx.TryGet(ArtifactKey, out var v) ? v : "";
                var approved = val == "ok";
                return Task.FromResult(new ReviewResult
                {
                    ReviewerName = "gate",
                    Decision = approved ? ReviewDecision.Approved : ReviewDecision.ApprovedWithWarnings,
                    Findings = approved ? [] : [new Finding { ReviewerName = "gate", Fingerprint = "fp", Message = "bad", Severity = FindingSeverity.Error }]
                });
            }))
            .Build();

        await pipeline.RunAsync("in");

        recoveryContext.Should().Contain(advisorAdvice);
    }

    [Fact]
    public async Task Without_OnConvergenceFailure_advisor_pipeline_throws_immediately_on_non_convergence()
    {
        var pipeline = BuildPipeline(maxIterations: 1)
            .UseExecution(new DelegateExecution((ctx, _) =>
                Task.FromResult(new ExecutionResult { UpdatedContext = ctx.Set(ArtifactKey, "bad") })))
            .AddReviewer(new DelegateReviewer("blocker", (ctx, _) =>
                Task.FromResult(new ReviewResult
                {
                    ReviewerName = "blocker",
                    Decision = ReviewDecision.ApprovedWithWarnings,
                    Findings = [new Finding { ReviewerName = "blocker", Fingerprint = "fp", Message = "always fails", Severity = FindingSeverity.Error }]
                })))
            .Build();

        await pipeline.Invoking(p => p.RunAsync("in"))
            .Should().ThrowAsync<ConvergenceFailedException>();
    }

    [Fact]
    public async Task Recovery_does_not_fire_second_time_when_recovery_pass_also_fails()
    {
        var advisor = new CountingAdvisor("r");

        var pipeline = BuildPipeline(maxIterations: 1)
            .AddAdvisor(advisor, AdvisorTrigger.OnConvergenceFailure)
            .UseExecution(new DelegateExecution((ctx, _) =>
                Task.FromResult(new ExecutionResult { UpdatedContext = ctx.Set(ArtifactKey, "always-bad") })))
            .AddReviewer(new DelegateReviewer("blocker", (ctx, _) =>
                Task.FromResult(new ReviewResult
                {
                    ReviewerName = "blocker",
                    Decision = ReviewDecision.ApprovedWithWarnings,
                    Findings = [new Finding { ReviewerName = "blocker", Fingerprint = "fp", Message = "still fails", Severity = FindingSeverity.Error }]
                })))
            .Build();

        await pipeline.Invoking(p => p.RunAsync("in"))
            .Should().ThrowAsync<ConvergenceFailedException>();

        advisor.CallCount.Should().Be(1, "recovery advisor fires at most once per run");
    }

    // ── Builder name uniqueness ───────────────────────────────────────────

    [Fact]
    public void Builder_throws_when_triggered_advisor_has_same_name_as_existing_advisor()
    {
        var act = () => BuildPipeline()
            .AddAdvisor(new CountingAdvisor("shared"))
            .AddAdvisor(new CountingAdvisor("shared"), AdvisorTrigger.BeforeEveryExecution);

        act.Should().Throw<PipelineConfigurationException>().WithMessage("*shared*");
    }

    [Fact]
    public void Builder_throws_when_provider_advisor_has_same_name_as_triggered_advisor()
    {
        var act = () => BuildPipeline()
            .AddAdvisor(new CountingAdvisor("shared"), AdvisorTrigger.BeforeFirstExecution)
            .AddAdvisor(new CountingAdvisor("shared"));

        act.Should().Throw<PipelineConfigurationException>().WithMessage("*shared*");
    }

    [Fact]
    public void Builder_throws_when_two_triggered_advisors_have_same_name()
    {
        var act = () => BuildPipeline()
            .AddAdvisor(new CountingAdvisor("dup"), AdvisorTrigger.BeforeFirstExecution)
            .AddAdvisor(new CountingAdvisor("dup"), AdvisorTrigger.BeforeEveryExecution);

        act.Should().Throw<PipelineConfigurationException>().WithMessage("*dup*");
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static GeefPipelineBuilder<string> BuildPipeline(int maxIterations = 10)
    {
        return Geef.CreatePipeline<string>()
            .UseGrounding(new DelegateGrounding(_ => new GroundingResult
                { Context = new RunContext().Set(ArtifactKey, "initial") }))
            .UseConvergencePolicy(new DefaultConvergencePolicy
            {
                MaxIterations = maxIterations,
                BlockingSeverity = FindingSeverity.Error,
                FailedReviewerHandling = FailedReviewerHandling.TreatAsNonBlocking
            })
            .UseFinalizer(new DelegateFinalizer<string>(ctx =>
                Task.FromResult(new FinalizeResult<string>
                {
                    Output = ctx.TryGet(ArtifactKey, out var v) ? v ?? "" : "",
                    FinalContext = ctx
                })));
    }

    private sealed class CountingAdvisor(string name) : IAdvisor
    {
        private int _callCount;
        public int CallCount => _callCount;
        public string Name { get; } = name;
        public AdvisorKind Kind => AdvisorKind.Strategic;

        public Task<AdvisorResponse> ConsultAsync(AdvisorQuery query, IRunContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(new AdvisorResponse
            {
                AdviceText = "generic advice",
                Confidence = AdvisorConfidence.Medium,
                Outcome = AdvisorOutcome.Success
            });
        }
    }

    private sealed class FixedOutputAdvisor(string name, string output) : IAdvisor
    {
        public string Name { get; } = name;
        public AdvisorKind Kind => AdvisorKind.Strategic;

        public Task<AdvisorResponse> ConsultAsync(AdvisorQuery query, IRunContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new AdvisorResponse
            {
                AdviceText = output,
                Confidence = AdvisorConfidence.High,
                Outcome = AdvisorOutcome.Success
            });
    }

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
        public string Name { get; } = name;
        public int Priority => 0;
        public Task<ReviewResult> ReviewAsync(IRunContext ctx, CancellationToken ct = default) => fn(ctx, ct);
    }

    private sealed class DelegateFinalizer<T>(Func<IRunContext, Task<FinalizeResult<T>>> fn) : IFinalizer<T>
    {
        public Task<FinalizeResult<T>> FinalizeAsync(IRunContext ctx, CancellationToken ct = default) => fn(ctx);
    }
}
