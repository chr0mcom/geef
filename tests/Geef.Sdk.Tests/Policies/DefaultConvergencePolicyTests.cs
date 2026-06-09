using FluentAssertions;
using Geef.Sdk.Policies;
using Geef.Sdk.Results;
using Geef.Sdk.Runtime;
using Xunit;

namespace Geef.Sdk.Tests.Policies;

public sealed class DefaultConvergencePolicyTests
{
    private static EvaluationAggregate MakeAggregate(ReviewDecision decision, params Finding[] findings)
    {
        var review = new ReviewResult
        {
            ReviewerName = "R",
            Decision = decision,
            Findings = findings
        };
        return new EvaluationAggregate { Reviews = new[] { review } };
    }

    private static IterationHistory MakeHistory(int count)
    {
        var h = new IterationHistory();
        for (var i = 1; i <= count; i++)
            h.Add(new IterationRecord
            {
                Iteration = i,
                StartedAt = DateTimeOffset.UtcNow,
                ExecutionDuration = TimeSpan.FromSeconds(1),
                EvaluationResult = MakeAggregate(ReviewDecision.Rejected,
                    new Finding { ReviewerName = "R", Fingerprint = $"fp{i}", Message = "x" })
            });
        return h;
    }

    [Fact]
    public void Returns_Approved_when_fully_approved()
    {
        var policy = new DefaultConvergencePolicy();
        var aggregate = MakeAggregate(ReviewDecision.Approved);

        var decision = policy.Evaluate(new IterationHistory(), aggregate, TimeSpan.Zero);

        decision.Should().Be(ConvergenceDecision.Approved);
    }

    [Fact]
    public void Returns_AbortCriticalBlocker_on_critical_finding()
    {
        var policy = new DefaultConvergencePolicy { AbortOnCritical = true };
        var criticalFinding = new Finding
        {
            ReviewerName = "R",
            Fingerprint = "critical-fp",
            Message = "Critical issue",
            Severity = FindingSeverity.Critical
        };
        var aggregate = MakeAggregate(ReviewDecision.Rejected, criticalFinding);

        var decision = policy.Evaluate(new IterationHistory(), aggregate, TimeSpan.Zero);

        decision.Should().Be(ConvergenceDecision.AbortCriticalBlocker);
    }

    [Fact]
    public void Returns_StopTimeBudgetReached_when_time_exceeded()
    {
        var policy = new DefaultConvergencePolicy { MaxElapsedTime = TimeSpan.FromSeconds(1) };
        var aggregate = MakeAggregate(ReviewDecision.Rejected,
            new Finding { ReviewerName = "R", Fingerprint = "fp", Message = "x" });

        var decision = policy.Evaluate(new IterationHistory(), aggregate, TimeSpan.FromSeconds(2));

        decision.Should().Be(ConvergenceDecision.StopTimeBudgetReached);
    }

    [Fact]
    public void Returns_StopMaxAttemptsReached_when_max_iterations_reached()
    {
        var policy = new DefaultConvergencePolicy { MaxIterations = 3 };
        var history = MakeHistory(3);
        var aggregate = MakeAggregate(ReviewDecision.Rejected,
            new Finding { ReviewerName = "R", Fingerprint = "fp", Message = "x" });

        var decision = policy.Evaluate(history, aggregate, TimeSpan.Zero);

        decision.Should().Be(ConvergenceDecision.StopMaxAttemptsReached);
    }

    [Fact]
    public void Returns_StopStagnant_when_same_findings_repeat()
    {
        var policy = new DefaultConvergencePolicy { StagnationThreshold = 3 };
        var h = new IterationHistory();
        for (var i = 1; i <= 3; i++)
            h.Add(new IterationRecord
            {
                Iteration = i,
                StartedAt = DateTimeOffset.UtcNow,
                ExecutionDuration = TimeSpan.FromSeconds(1),
                EvaluationResult = MakeAggregate(ReviewDecision.Rejected,
                    new Finding { ReviewerName = "R", Fingerprint = "same-fp", Message = "x" })
            });

        var aggregate = MakeAggregate(ReviewDecision.Rejected,
            new Finding { ReviewerName = "R", Fingerprint = "same-fp", Message = "x" });

        var decision = policy.Evaluate(h, aggregate, TimeSpan.Zero);

        decision.Should().Be(ConvergenceDecision.StopStagnant);
    }

    [Fact]
    public void Returns_Continue_when_progress_being_made()
    {
        var policy = new DefaultConvergencePolicy { MaxIterations = 10 };
        var h = MakeHistory(2);
        var aggregate = MakeAggregate(ReviewDecision.Rejected,
            new Finding { ReviewerName = "R", Fingerprint = "fp3", Message = "x" });

        var decision = policy.Evaluate(h, aggregate, TimeSpan.Zero);

        decision.Should().Be(ConvergenceDecision.Continue);
    }

    [Fact]
    public void AbortOnCritical_false_does_not_abort_on_critical()
    {
        var policy = new DefaultConvergencePolicy { AbortOnCritical = false, MaxIterations = 10 };
        var criticalFinding = new Finding
        {
            ReviewerName = "R",
            Fingerprint = "fp",
            Message = "Critical issue",
            Severity = FindingSeverity.Critical
        };
        var aggregate = MakeAggregate(ReviewDecision.Rejected, criticalFinding);

        var decision = policy.Evaluate(new IterationHistory(), aggregate, TimeSpan.Zero);

        decision.Should().Be(ConvergenceDecision.Continue);
    }

    // ── Phase 3: StopTimeBudgetReached vs StopMaxAttemptsReached ─────────────

    [Fact]
    public void StopTimeBudgetReached_is_distinct_from_StopMaxAttemptsReached()
    {
        ConvergenceDecision.StopTimeBudgetReached.Should().NotBe(ConvergenceDecision.StopMaxAttemptsReached);
    }

    // ── Phase 3: MinutesPerIteration auto-scale ───────────────────────────────

    [Fact]
    public void MinutesPerIteration_raises_effective_budget_above_MaxElapsedTime()
    {
        // MaxElapsedTime=1min but MinutesPerIteration=5 with MaxIterations=10 → effective=50min
        var policy = new DefaultConvergencePolicy
        {
            MaxElapsedTime = TimeSpan.FromMinutes(1),
            MinutesPerIteration = 5,
            MaxIterations = 10
        };
        var aggregate = MakeAggregate(ReviewDecision.Rejected,
            new Finding { ReviewerName = "R", Fingerprint = "fp", Message = "x" });

        // elapsed=5min should NOT stop (effective budget is 50min)
        var decision = policy.Evaluate(new IterationHistory(), aggregate, TimeSpan.FromMinutes(5));

        decision.Should().Be(ConvergenceDecision.Continue);
    }

    [Fact]
    public void MinutesPerIteration_disabled_when_zero_leaves_MaxElapsedTime_unchanged()
    {
        var policy = new DefaultConvergencePolicy
        {
            MaxElapsedTime = TimeSpan.FromMinutes(1),
            MinutesPerIteration = 0,
            MaxIterations = 10
        };
        var aggregate = MakeAggregate(ReviewDecision.Rejected,
            new Finding { ReviewerName = "R", Fingerprint = "fp", Message = "x" });

        // elapsed=2min should stop — no auto-scale, MaxElapsedTime=1min applies
        var decision = policy.Evaluate(new IterationHistory(), aggregate, TimeSpan.FromMinutes(2));

        decision.Should().Be(ConvergenceDecision.StopTimeBudgetReached);
    }

    [Fact]
    public void MinutesPerIteration_honors_explicit_MaxElapsedTime_when_larger()
    {
        // MaxElapsedTime=120min, MinutesPerIteration=5, MaxIterations=10 → effective=max(120,50)=120min
        var policy = new DefaultConvergencePolicy
        {
            MaxElapsedTime = TimeSpan.FromMinutes(120),
            MinutesPerIteration = 5,
            MaxIterations = 10
        };
        var aggregate = MakeAggregate(ReviewDecision.Rejected,
            new Finding { ReviewerName = "R", Fingerprint = "fp", Message = "x" });

        // elapsed=90min should NOT stop (effective budget is 120min)
        var decision = policy.Evaluate(new IterationHistory(), aggregate, TimeSpan.FromMinutes(90));

        decision.Should().Be(ConvergenceDecision.Continue);
    }

    // ── Phase 3: BlockingSeverity ─────────────────────────────────────────────

    [Fact]
    public void BlockingSeverity_Error_prevents_approval_when_error_findings_exist()
    {
        var policy = new DefaultConvergencePolicy
        {
            BlockingSeverity = FindingSeverity.Error,
            MaxIterations = 10
        };
        var errorFinding = new Finding
        {
            ReviewerName = "R",
            Fingerprint = "fp-error",
            Message = "Error finding",
            Severity = FindingSeverity.Error
        };
        // Reviewer says Approved but there's an Error finding — policy should block
        var aggregate = MakeAggregate(ReviewDecision.ApprovedWithWarnings, errorFinding);

        var decision = policy.Evaluate(new IterationHistory(), aggregate, TimeSpan.Zero);

        decision.Should().NotBe(ConvergenceDecision.Approved);
    }

    [Fact]
    public void BlockingSeverity_Warning_also_blocks_on_warning_findings()
    {
        var policy = new DefaultConvergencePolicy
        {
            BlockingSeverity = FindingSeverity.Warning,
            MaxIterations = 10
        };
        var warningFinding = new Finding
        {
            ReviewerName = "R",
            Fingerprint = "fp-warn",
            Message = "Warning finding",
            Severity = FindingSeverity.Warning
        };
        var aggregate = MakeAggregate(ReviewDecision.ApprovedWithWarnings, warningFinding);

        var decision = policy.Evaluate(new IterationHistory(), aggregate, TimeSpan.Zero);

        decision.Should().NotBe(ConvergenceDecision.Approved);
    }

    [Fact]
    public void BlockingSeverity_Critical_allows_approval_with_error_findings()
    {
        var policy = new DefaultConvergencePolicy
        {
            BlockingSeverity = FindingSeverity.Critical,
            AbortOnCritical = false,
            MaxIterations = 10
        };
        var errorFinding = new Finding
        {
            ReviewerName = "R",
            Fingerprint = "fp-error",
            Message = "Error finding",
            Severity = FindingSeverity.Error
        };
        // BlockingSeverity=Critical means Error findings don't block
        var aggregate = MakeAggregate(ReviewDecision.ApprovedWithWarnings, errorFinding);

        var decision = policy.Evaluate(new IterationHistory(), aggregate, TimeSpan.Zero);

        decision.Should().Be(ConvergenceDecision.Approved);
    }

    [Fact]
    public void BlockingSeverity_Info_findings_do_not_block_with_default_Error_threshold()
    {
        var policy = new DefaultConvergencePolicy { MaxIterations = 10 }; // default BlockingSeverity = Error
        var infoFinding = new Finding
        {
            ReviewerName = "R",
            Fingerprint = "fp-info",
            Message = "Info finding",
            Severity = FindingSeverity.Info
        };
        var aggregate = MakeAggregate(ReviewDecision.ApprovedWithWarnings, infoFinding);

        var decision = policy.Evaluate(new IterationHistory(), aggregate, TimeSpan.Zero);

        decision.Should().Be(ConvergenceDecision.Approved);
    }
}
