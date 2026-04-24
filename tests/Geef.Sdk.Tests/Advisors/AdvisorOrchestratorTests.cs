using FluentAssertions;
using Geef.Sdk.Advisors;
using Geef.Sdk.Context;
using Geef.Sdk.Events;
using Geef.Sdk.Middleware;
using Geef.Sdk.Policies;
using Xunit;

namespace Geef.Sdk.Tests.Advisors;

public sealed class AdvisorOrchestratorTests
{
    private static readonly IRunContext EmptyContext = new RunContext();

    private static AdvisorQuery NewQuery(int? maxTokens = null) => new()
    {
        Question = "q",
        Character = AdvisorQueryCharacter.Diagnostic,
        MaxTokens = maxTokens,
    };

    private static AdvisorOrchestrator NewOrchestrator(
        IReadOnlyList<IAdvisor>? advisors = null,
        IAdvisorPolicy? policy = null,
        AdvisorBudget? budget = null,
        IGeefEventSink? sink = null)
        => new(
            advisors ?? Array.Empty<IAdvisor>(),
            policy ?? new DefaultAdvisorPolicy(),
            budget ?? new AdvisorBudget(),
            sink ?? new CollectingEventSink(),
            "runid000000");

    [Fact]
    public async Task No_advisor_registered_returns_NoApplicableAdvice_without_provenance_or_events()
    {
        var sink = new CollectingEventSink();
        var orch = NewOrchestrator(sink: sink);

        var response = await orch.ConsultAsync(NewQuery(), EmptyContext);

        response.Outcome.Should().Be(AdvisorOutcome.NoApplicableAdvice);
        orch.Provenance.Consultations.Should().BeEmpty();
        sink.Events.Should().BeEmpty();
        orch.HasAdvisor.Should().BeFalse();
    }

    [Fact]
    public async Task Named_advisor_not_found_returns_NoApplicableAdvice_without_provenance()
    {
        var sink = new CollectingEventSink();
        var advisor = new FakeAdvisor("known");
        var orch = NewOrchestrator(new IAdvisor[] { advisor }, sink: sink);

        var response = await orch.ConsultAsync("unknown", NewQuery(), EmptyContext);

        response.Outcome.Should().Be(AdvisorOutcome.NoApplicableAdvice);
        orch.Provenance.Consultations.Should().BeEmpty();
        sink.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Consult_with_available_advisor_returns_Success_and_writes_provenance_and_events()
    {
        var sink = new CollectingEventSink();
        var advisor = new FakeAdvisor("a");
        var orch = NewOrchestrator(new IAdvisor[] { advisor }, sink: sink);

        var response = await orch.ConsultAsync(NewQuery(), EmptyContext);

        response.Outcome.Should().Be(AdvisorOutcome.Success);
        response.AdvisorName.Should().Be("a");
        response.ConsultationId.Should().NotBeNullOrEmpty();
        orch.Provenance.Consultations.Should().HaveCount(1);
        sink.Events.Should().HaveCount(2);
        sink.Events[0].Should().BeOfType<AdvisorConsultationStartedEvent>();
        sink.Events[1].Should().BeOfType<AdvisorConsultationCompletedEvent>();
    }

    [Fact]
    public async Task Budget_per_run_cap_returns_BudgetExceeded_without_invoking_advisor()
    {
        var sink = new CollectingEventSink();
        var advisor = new FakeAdvisor("a");
        var budget = new AdvisorBudget { MaxConsultationsPerRun = 1, MaxTokensPerConsultation = 100_000 };
        var orch = NewOrchestrator(new IAdvisor[] { advisor }, budget: budget, sink: sink);

        var first = await orch.ConsultAsync(NewQuery(100), EmptyContext);
        var second = await orch.ConsultAsync(NewQuery(100), EmptyContext);

        first.Outcome.Should().Be(AdvisorOutcome.Success);
        second.Outcome.Should().Be(AdvisorOutcome.BudgetExceeded);
        advisor.CallCount.Should().Be(1);
        orch.Provenance.Consultations.Should().HaveCount(2);
        sink.Events.OfType<AdvisorConsultationStartedEvent>().Should().HaveCount(1);
        sink.Events.OfType<AdvisorConsultationCompletedEvent>().Should().HaveCount(2);
    }

    [Fact]
    public async Task Budget_per_iteration_cap_returns_BudgetExceeded()
    {
        var advisor = new FakeAdvisor("a");
        var budget = new AdvisorBudget { MaxConsultationsPerIteration = 1, MaxTokensPerConsultation = 100_000 };
        var orch = NewOrchestrator(new IAdvisor[] { advisor }, budget: budget);
        orch.StartIteration(1);

        var first = await orch.ConsultAsync(NewQuery(100), EmptyContext);
        var second = await orch.ConsultAsync(NewQuery(100), EmptyContext);

        first.Outcome.Should().Be(AdvisorOutcome.Success);
        second.Outcome.Should().Be(AdvisorOutcome.BudgetExceeded);
    }

    [Fact]
    public async Task Budget_per_consultation_token_cap_returns_BudgetExceeded()
    {
        var advisor = new FakeAdvisor("a");
        var budget = new AdvisorBudget { MaxTokensPerConsultation = 100 };
        var orch = NewOrchestrator(new IAdvisor[] { advisor }, budget: budget);

        var response = await orch.ConsultAsync(NewQuery(200), EmptyContext);

        response.Outcome.Should().Be(AdvisorOutcome.BudgetExceeded);
        advisor.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Policy_rejection_returns_NoApplicableAdvice_with_provenance_but_no_started_event()
    {
        var sink = new CollectingEventSink();
        var advisor = new FakeAdvisor("a");
        var policy = new FakePolicy(_ => false);
        var orch = NewOrchestrator(new IAdvisor[] { advisor }, policy: policy, sink: sink);

        var response = await orch.ConsultAsync(NewQuery(), EmptyContext);

        response.Outcome.Should().Be(AdvisorOutcome.NoApplicableAdvice);
        advisor.CallCount.Should().Be(0);
        orch.Provenance.Consultations.Should().HaveCount(1);
        sink.Events.OfType<AdvisorConsultationStartedEvent>().Should().BeEmpty();
        sink.Events.OfType<AdvisorConsultationCompletedEvent>().Should().HaveCount(1);
    }

    [Fact]
    public async Task Advisor_throws_returns_InfrastructureFailure_and_counts_budget()
    {
        var sink = new CollectingEventSink();
        var advisor = new FakeAdvisor("a", throwOn: true);
        var orch = NewOrchestrator(new IAdvisor[] { advisor }, sink: sink);

        var response = await orch.ConsultAsync(NewQuery(123), EmptyContext);

        response.Outcome.Should().Be(AdvisorOutcome.InfrastructureFailure);
        response.AdvisorName.Should().Be("a");
        response.ConsultationId.Should().NotBeNullOrEmpty();
        orch.Provenance.Consultations.Should().HaveCount(1);
        orch.BudgetState.ConsultationsTotal.Should().Be(1);
        orch.BudgetState.TokensTotal.Should().Be(123);
        sink.Events.OfType<AdvisorConsultationStartedEvent>().Should().HaveCount(1);
        sink.Events.OfType<AdvisorConsultationCompletedEvent>().Should().HaveCount(1);
    }

    [Fact]
    public async Task OperationCanceledException_propagates_from_advisor()
    {
        var advisor = new FakeAdvisor("a", cancelOn: true);
        var orch = NewOrchestrator(new IAdvisor[] { advisor });

        var act = async () => await orch.ConsultAsync(NewQuery(), EmptyContext);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Constructor_throws_on_duplicate_advisor_names()
    {
        var advisors = new IAdvisor[] { new FakeAdvisor("same"), new FakeAdvisor("same") };

        var act = () => NewOrchestrator(advisors);

        act.Should().Throw<ArgumentException>().WithMessage("*same*");
    }

    [Fact]
    public async Task ConsultationIds_are_unique_across_calls()
    {
        var orch = NewOrchestrator(new IAdvisor[] { new FakeAdvisor("a") });

        var r1 = await orch.ConsultAsync(NewQuery(), EmptyContext);
        var r2 = await orch.ConsultAsync(NewQuery(), EmptyContext);
        var r3 = await orch.ConsultAsync(NewQuery(), EmptyContext);

        new[] { r1.ConsultationId, r2.ConsultationId, r3.ConsultationId }
            .Distinct()
            .Should().HaveCount(3);
    }

    [Fact]
    public async Task AttributeArtifactToConsultation_records_attribution()
    {
        var orch = NewOrchestrator(new IAdvisor[] { new FakeAdvisor("a") });
        var response = await orch.ConsultAsync(NewQuery(), EmptyContext);

        orch.AttributeArtifactToConsultation("artifact:key", response.ConsultationId!);

        orch.Provenance.Attributions.Should().HaveCount(1);
        orch.Provenance.Attributions[0].ArtifactContextKey.Should().Be("artifact:key");
        orch.Provenance.Attributions[0].ConsultationId.Should().Be(response.ConsultationId);
    }

    [Fact]
    public async Task SetCurrentPhase_flows_into_provenance_record()
    {
        var orch = NewOrchestrator(new IAdvisor[] { new FakeAdvisor("a") });
        orch.SetCurrentPhase(GeefPhase.Execution, 2);

        await orch.ConsultAsync(NewQuery(), EmptyContext);

        orch.Provenance.Consultations[0].Phase.Should().Be(GeefPhase.Execution);
        orch.Provenance.Consultations[0].Iteration.Should().Be(2);
    }

    [Fact]
    public async Task Concurrent_consultations_are_thread_safe()
    {
        var advisor = new FakeAdvisor("a");
        var budget = new AdvisorBudget
        {
            MaxConsultationsPerRun = 1000,
            MaxConsultationsPerIteration = 1000,
            MaxTokensPerConsultation = 100_000,
            MaxTotalAdvisorTokens = 10_000_000,
            MaxTotalAdvisorTime = TimeSpan.FromMinutes(10),
        };
        var orch = NewOrchestrator(new IAdvisor[] { advisor }, budget: budget);

        var tasks = Enumerable.Range(0, 50)
            .Select(_ => Task.Run(async () =>
            {
                await orch.ConsultAsync(NewQuery(1), EmptyContext);
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        orch.Provenance.Consultations.Should().HaveCount(50);
        orch.BudgetState.ConsultationsTotal.Should().Be(50);
        orch.Provenance.Consultations
            .Select(c => c.ConsultationId)
            .Distinct()
            .Should().HaveCount(50);
    }

    [Fact]
    public async Task HasAdvisor_is_true_when_any_advisor_registered()
    {
        var orch = NewOrchestrator(new IAdvisor[] { new FakeAdvisor("a") });
        orch.HasAdvisor.Should().BeTrue();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Response_Duration_is_populated_on_success()
    {
        var advisor = new FakeAdvisor("a", delay: TimeSpan.FromMilliseconds(5));
        var orch = NewOrchestrator(new IAdvisor[] { advisor });

        var response = await orch.ConsultAsync(NewQuery(), EmptyContext);

        response.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task Budget_is_not_counted_for_BudgetExceeded_or_policy_rejections()
    {
        var advisor = new FakeAdvisor("a");
        var policy = new FakePolicy(_ => false);
        var orch = NewOrchestrator(new IAdvisor[] { advisor }, policy: policy);

        await orch.ConsultAsync(NewQuery(100), EmptyContext);
        await orch.ConsultAsync(NewQuery(100), EmptyContext);

        orch.BudgetState.ConsultationsTotal.Should().Be(0);
        orch.BudgetState.TokensTotal.Should().Be(0);
    }

    [Fact]
    public async Task Total_token_budget_cap_returns_BudgetExceeded()
    {
        var advisor = new FakeAdvisor("a");
        var budget = new AdvisorBudget { MaxTotalAdvisorTokens = 150, MaxTokensPerConsultation = 100 };
        var orch = NewOrchestrator(new IAdvisor[] { advisor }, budget: budget);

        var first = await orch.ConsultAsync(NewQuery(100), EmptyContext);
        var second = await orch.ConsultAsync(NewQuery(100), EmptyContext);

        first.Outcome.Should().Be(AdvisorOutcome.Success);
        second.Outcome.Should().Be(AdvisorOutcome.BudgetExceeded);
    }

    [Fact]
    public async Task ApproximateTokenCount_from_response_is_used_for_budget_accounting()
    {
        var advisor = new FakeAdvisor("a", approximateTokens: 42);
        var orch = NewOrchestrator(new IAdvisor[] { advisor });

        await orch.ConsultAsync(NewQuery(100), EmptyContext);

        orch.BudgetState.TokensTotal.Should().Be(42);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private sealed class FakeAdvisor : IAdvisor
    {
        private readonly bool _throwOn;
        private readonly bool _cancelOn;
        private readonly TimeSpan _delay;
        private readonly int? _approximateTokens;
        public int CallCount { get; private set; }
        public string Name { get; }
        public AdvisorKind Kind => AdvisorKind.Strategic;

        public FakeAdvisor(
            string name,
            bool throwOn = false,
            bool cancelOn = false,
            TimeSpan? delay = null,
            int? approximateTokens = null)
        {
            Name = name;
            _throwOn = throwOn;
            _cancelOn = cancelOn;
            _delay = delay ?? TimeSpan.Zero;
            _approximateTokens = approximateTokens;
        }

        public async Task<AdvisorResponse> ConsultAsync(
            AdvisorQuery query,
            IRunContext context,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (_cancelOn) throw new OperationCanceledException();
            if (_throwOn) throw new InvalidOperationException("boom");
            if (_delay > TimeSpan.Zero) await Task.Delay(_delay, cancellationToken);

            return new AdvisorResponse
            {
                AdviceText = "ok",
                Confidence = AdvisorConfidence.Medium,
                ApproximateTokenCount = _approximateTokens,
            };
        }
    }

    private sealed class FakePolicy : IAdvisorPolicy
    {
        private readonly Func<AdvisorConsultationContext, bool> _fn;
        public FakePolicy(Func<AdvisorConsultationContext, bool> fn) => _fn = fn;
        public bool IsConsultationAllowed(AdvisorConsultationContext context) => _fn(context);
    }

    private sealed class CollectingEventSink : IGeefEventSink
    {
        private readonly object _lock = new();
        private readonly List<IGeefEvent> _events = new();

        public IReadOnlyList<IGeefEvent> Events
        {
            get { lock (_lock) return _events.ToArray(); }
        }

        public ValueTask PublishAsync(IGeefEvent geefEvent, CancellationToken cancellationToken = default)
        {
            lock (_lock) _events.Add(geefEvent);
            return ValueTask.CompletedTask;
        }
    }
}
