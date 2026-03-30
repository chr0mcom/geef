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
    public void Returns_StopMaxAttemptsReached_when_time_exceeded()
    {
        var policy = new DefaultConvergencePolicy { MaxElapsedTime = TimeSpan.FromSeconds(1) };
        var aggregate = MakeAggregate(ReviewDecision.Rejected,
            new Finding { ReviewerName = "R", Fingerprint = "fp", Message = "x" });

        var decision = policy.Evaluate(new IterationHistory(), aggregate, TimeSpan.FromSeconds(2));

        decision.Should().Be(ConvergenceDecision.StopMaxAttemptsReached);
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
}
