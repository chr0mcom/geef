using FluentAssertions;
using Geef.Sdk.Advisors;
using Geef.Sdk.Context;
using Geef.Sdk.Exceptions;
using Geef.Sdk.Policies;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;
using Xunit;

namespace Geef.Sdk.Tests.Integration;

public sealed class AdvisorIntegrationTests
{
    private static readonly ContextKey<string> ArtifactKey = new("test:artifact");
    private static readonly ContextKey<string> AdviceKey = new("test:advice");

    [Fact]
    public async Task Pipeline_records_grounding_consultation_in_result()
    {
        var advisor = new FakeAdvisor("strategist");
        var grounding = new AdvisorAwareGrounding(async (input, advisorRef, ct) =>
        {
            var ctx = (IRunContext)new RunContext().Set(ArtifactKey, "initial");
            if (advisorRef is not null)
            {
                var response = await advisorRef.ConsultAsync(
                    new AdvisorQuery { Question = "plan?", Character = AdvisorQueryCharacter.DecisionSupport },
                    ctx, ct);
                ctx = ctx.Set(AdviceKey, response.AdviceText);
            }
            return new GroundingResult { Context = ctx };
        });

        var pipeline = BuildPipeline(grounding)
            .AddAdvisor(advisor)
            .Build();

        var result = await pipeline.RunAsync("in");

        result.Success.Should().BeTrue();
        result.AdvisorConsultations.Should().HaveCount(1);
        result.AdvisorConsultations[0].AdvisorName.Should().Be("strategist");
        result.FinalContext.TryGet(AdviceKey, out var advice).Should().BeTrue();
        advice.Should().Be("ok");
        advisor.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Pipeline_without_advisor_returns_empty_advisor_lists()
    {
        var pipeline = BuildPipeline(SimpleGrounding()).Build();

        var result = await pipeline.RunAsync("in");

        result.Success.Should().BeTrue();
        result.AdvisorConsultations.Should().BeEmpty();
        result.AdvisorAttributions.Should().BeEmpty();
    }

    [Fact]
    public void Builder_throws_when_two_advisors_with_same_name_registered()
    {
        var act = () => BuildPipeline(SimpleGrounding())
            .AddAdvisor(new FakeAdvisor("same"))
            .AddAdvisor(new FakeAdvisor("same"));

        act.Should().Throw<PipelineConfigurationException>().WithMessage("*same*");
    }

    [Fact]
    public async Task AttributeArtifactToConsultation_is_reflected_in_result()
    {
        var advisor = new FakeAdvisor("a");
        var grounding = new AdvisorAwareGrounding(async (input, advisorRef, ct) =>
        {
            var ctx = (IRunContext)new RunContext().Set(ArtifactKey, "initial");
            var response = await advisorRef!.ConsultAsync(
                new AdvisorQuery { Question = "q", Character = AdvisorQueryCharacter.Diagnostic },
                ctx, ct);
            advisorRef.AttributeArtifactToConsultation("test:artifact", response.ConsultationId!);
            return new GroundingResult { Context = ctx };
        });

        var pipeline = BuildPipeline(grounding).AddAdvisor(advisor).Build();

        var result = await pipeline.RunAsync("in");

        result.AdvisorAttributions.Should().HaveCount(1);
        result.AdvisorAttributions[0].ArtifactContextKey.Should().Be("test:artifact");
        result.AdvisorAttributions[0].ConsultationId
            .Should().Be(result.AdvisorConsultations[0].ConsultationId);
    }

    [Fact]
    public async Task Budget_enforcement_triggers_BudgetExceeded_across_iterations()
    {
        var advisor = new FakeAdvisor("a");
        var executionCount = 0;

        var execution = new AdvisorAwareExecution(async (ctx, advisorRef, ct) =>
        {
            executionCount++;
            var response = await advisorRef!.ConsultAsync(
                new AdvisorQuery { Question = "q", Character = AdvisorQueryCharacter.Diagnostic, MaxTokens = 1 },
                ctx, ct);
            return new ExecutionResult { UpdatedContext = ctx.Set(ArtifactKey, $"v{executionCount}:{response.Outcome}") };
        });

        var reviewCount = 0;
        var reviewer = new DelegateReviewer("R", (ctx, _) =>
        {
            reviewCount++;
            var approved = reviewCount >= 5;
            return Task.FromResult(new ReviewResult
            {
                ReviewerName = "R",
                Decision = approved ? ReviewDecision.Approved : ReviewDecision.Rejected,
                Findings = approved ? Array.Empty<Finding>() : new[]
                {
                    new Finding { ReviewerName = "R", Fingerprint = "f", Message = "retry" }
                },
                Duration = TimeSpan.FromMilliseconds(1),
            });
        });

        var pipeline = Geef.CreatePipeline<string>()
            .UseGrounding(SimpleGrounding())
            .UseExecution(execution)
            .AddReviewer(reviewer)
            .UseFinalizer(new DelegateFinalizer<string>(ctx =>
                Task.FromResult(new FinalizeResult<string> { Output = ctx.GetRequired(ArtifactKey), FinalContext = ctx })))
            .UseConvergencePolicy(new DefaultConvergencePolicy { MaxIterations = 10, StagnationThreshold = 99 })
            .AddAdvisor(advisor)
            .UseAdvisorBudget(new AdvisorBudget
            {
                MaxConsultationsPerRun = 2,
                MaxConsultationsPerIteration = 1,
                MaxTokensPerConsultation = 10,
                MaxTotalAdvisorTokens = 100,
                MaxTotalAdvisorTime = TimeSpan.FromMinutes(1),
            })
            .Build();

        var result = await pipeline.RunAsync("in");

        result.Success.Should().BeTrue();
        result.TotalIterations.Should().Be(5);
        advisor.CallCount.Should().Be(2);
        result.AdvisorConsultations.Should().HaveCount(5);
        result.AdvisorConsultations.Count(c => c.Response.Outcome == AdvisorOutcome.Success).Should().Be(2);
        result.AdvisorConsultations.Count(c => c.Response.Outcome == AdvisorOutcome.BudgetExceeded).Should().Be(3);
    }

    [Fact]
    public async Task Custom_policy_blocks_consultation_in_specific_iteration()
    {
        var advisor = new FakeAdvisor("a");
        var execution = new AdvisorAwareExecution(async (ctx, advisorRef, ct) =>
        {
            var response = await advisorRef!.ConsultAsync(
                new AdvisorQuery { Question = "q", Character = AdvisorQueryCharacter.Diagnostic },
                ctx, ct);
            return new ExecutionResult { UpdatedContext = ctx.Set(ArtifactKey, response.Outcome.ToString()) };
        });

        var reviewCount = 0;
        var reviewer = new DelegateReviewer("R", (ctx, _) =>
        {
            reviewCount++;
            var approved = reviewCount >= 2;
            return Task.FromResult(new ReviewResult
            {
                ReviewerName = "R",
                Decision = approved ? ReviewDecision.Approved : ReviewDecision.Rejected,
                Findings = approved ? Array.Empty<Finding>() : new[]
                {
                    new Finding { ReviewerName = "R", Fingerprint = "f", Message = "retry" }
                },
                Duration = TimeSpan.FromMilliseconds(1),
            });
        });

        var pipeline = Geef.CreatePipeline<string>()
            .UseGrounding(SimpleGrounding())
            .UseExecution(execution)
            .AddReviewer(reviewer)
            .UseFinalizer(new DelegateFinalizer<string>(ctx =>
                Task.FromResult(new FinalizeResult<string> { Output = ctx.GetRequired(ArtifactKey), FinalContext = ctx })))
            .AddAdvisor(advisor)
            .UseAdvisorPolicy(new BlockOnIterationPolicy(blockedIteration: 1))
            .Build();

        var result = await pipeline.RunAsync("in");

        result.Success.Should().BeTrue();
        result.AdvisorConsultations.Should().HaveCount(2);
        result.AdvisorConsultations[0].Iteration.Should().Be(1);
        result.AdvisorConsultations[0].Response.Outcome.Should().Be(AdvisorOutcome.NoApplicableAdvice);
        result.AdvisorConsultations[1].Iteration.Should().Be(2);
        result.AdvisorConsultations[1].Response.Outcome.Should().Be(AdvisorOutcome.Success);
    }

    [Fact]
    public async Task Parallel_runs_with_same_provider_instance_have_isolated_budget_states()
    {
        var advisor = new FakeAdvisor("a");
        var grounding = new AdvisorAwareGrounding(async (input, advisorRef, ct) =>
        {
            var ctx = (IRunContext)new RunContext().Set(ArtifactKey, "x");
            for (var i = 0; i < 3; i++)
            {
                await advisorRef!.ConsultAsync(
                    new AdvisorQuery { Question = "q", Character = AdvisorQueryCharacter.Diagnostic, MaxTokens = 1 },
                    ctx, ct);
            }
            return new GroundingResult { Context = ctx };
        });

        var pipeline = BuildPipeline(grounding)
            .AddAdvisor(advisor)
            .UseAdvisorBudget(new AdvisorBudget
            {
                MaxConsultationsPerRun = 5,
                MaxTokensPerConsultation = 10,
                MaxTotalAdvisorTokens = 100,
                MaxTotalAdvisorTime = TimeSpan.FromMinutes(1),
            })
            .Build();

        var t1 = pipeline.RunAsync("a");
        var t2 = pipeline.RunAsync("b");
        var results = await Task.WhenAll(t1, t2);

        foreach (var r in results)
        {
            r.AdvisorConsultations.Should().HaveCount(3);
            r.AdvisorConsultations.Should().AllSatisfy(c => c.Response.Outcome.Should().Be(AdvisorOutcome.Success));
        }
    }

    // ── Pipeline scaffolding ─────────────────────────────────────────────────

    private static GeefPipelineBuilder<string> BuildPipeline(IGroundingStep grounding)
        => Geef.CreatePipeline<string>()
            .UseGrounding(grounding)
            .UseExecution(new DelegateExecution((ctx, _) =>
                Task.FromResult(new ExecutionResult { UpdatedContext = ctx.Set(ArtifactKey, "generated") })))
            .AddReviewer(new DelegateReviewer("R", (ctx, _) =>
                Task.FromResult(new ReviewResult
                {
                    ReviewerName = "R",
                    Decision = ReviewDecision.Approved,
                    Duration = TimeSpan.FromMilliseconds(1),
                })))
            .UseFinalizer(new DelegateFinalizer<string>(ctx =>
                Task.FromResult(new FinalizeResult<string>
                {
                    Output = ctx.GetRequired(ArtifactKey),
                    FinalContext = ctx,
                })));

    private static IGroundingStep SimpleGrounding()
        => new DelegateGrounding(input => new GroundingResult
        {
            Context = new RunContext().Set(ArtifactKey, "initial"),
        });

    // ── Helpers ──────────────────────────────────────────────────────────────

    private sealed class FakeAdvisor : IAdvisor
    {
        private int _callCount;
        public int CallCount => _callCount;
        public string Name { get; }
        public AdvisorKind Kind => AdvisorKind.Strategic;

        public FakeAdvisor(string name) => Name = name;

        public Task<AdvisorResponse> ConsultAsync(
            AdvisorQuery query,
            IRunContext context,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(new AdvisorResponse
            {
                AdviceText = "ok",
                Confidence = AdvisorConfidence.Medium,
                ApproximateTokenCount = 1,
            });
        }
    }

    private sealed class BlockOnIterationPolicy : IAdvisorPolicy
    {
        private readonly int _blockedIteration;
        public BlockOnIterationPolicy(int blockedIteration) => _blockedIteration = blockedIteration;
        public bool IsConsultationAllowed(AdvisorConsultationContext context)
            => context.Iteration != _blockedIteration;
    }

    private sealed class DelegateGrounding : IGroundingStep
    {
        private readonly Func<string, GroundingResult> _fn;
        public DelegateGrounding(Func<string, GroundingResult> fn) => _fn = fn;
        public Task<GroundingResult> RunAsync(string input, CancellationToken ct = default)
            => Task.FromResult(_fn(input));
    }

    private sealed class AdvisorAwareGrounding : AdvisorAwareProviderBase, IGroundingStep
    {
        private readonly Func<string, IAdvisorOrchestrator?, CancellationToken, Task<GroundingResult>> _fn;

        public AdvisorAwareGrounding(
            Func<string, IAdvisorOrchestrator?, CancellationToken, Task<GroundingResult>> fn) => _fn = fn;

        public Task<GroundingResult> RunAsync(string input, CancellationToken ct = default)
            => _fn(input, Advisor, ct);
    }

    private sealed class DelegateExecution : IExecutionStep
    {
        private readonly Func<IRunContext, CancellationToken, Task<ExecutionResult>> _fn;
        public DelegateExecution(Func<IRunContext, CancellationToken, Task<ExecutionResult>> fn) => _fn = fn;
        public Task<ExecutionResult> RunAsync(IRunContext ctx, CancellationToken ct = default) => _fn(ctx, ct);
    }

    private sealed class AdvisorAwareExecution : AdvisorAwareProviderBase, IExecutionStep
    {
        private readonly Func<IRunContext, IAdvisorOrchestrator?, CancellationToken, Task<ExecutionResult>> _fn;

        public AdvisorAwareExecution(
            Func<IRunContext, IAdvisorOrchestrator?, CancellationToken, Task<ExecutionResult>> fn) => _fn = fn;

        public Task<ExecutionResult> RunAsync(IRunContext ctx, CancellationToken ct = default)
            => _fn(ctx, Advisor, ct);
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
