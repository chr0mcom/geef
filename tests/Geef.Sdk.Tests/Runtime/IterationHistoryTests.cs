using FluentAssertions;
using Geef.Sdk.Results;
using Geef.Sdk.Runtime;
using Xunit;

namespace Geef.Sdk.Tests.Runtime;

public sealed class IterationHistoryTests
{
    private static IterationRecord MakeRecord(int iteration, params string[] fingerprints)
    {
        var findings = fingerprints.Select(fp => new Finding
        {
            ReviewerName = "TestReviewer",
            Fingerprint = fp,
            Message = $"Finding {fp}"
        }).ToList();

        var review = new ReviewResult
        {
            ReviewerName = "TestReviewer",
            Decision = ReviewDecision.Rejected,
            Findings = findings
        };

        return new IterationRecord
        {
            Iteration = iteration,
            StartedAt = DateTimeOffset.UtcNow,
            ExecutionDuration = TimeSpan.FromSeconds(1),
            EvaluationResult = new EvaluationAggregate { Reviews = new[] { review } }
        };
    }

    [Fact]
    public void IsStagnant_returns_false_when_fewer_records_than_lookback()
    {
        var history = new IterationHistory();
        history.Add(MakeRecord(1, "fp1"));
        history.Add(MakeRecord(2, "fp1"));

        history.IsStagnant(3).Should().BeFalse();
    }

    [Fact]
    public void IsStagnant_returns_true_when_same_fingerprints_across_lookback()
    {
        var history = new IterationHistory();
        history.Add(MakeRecord(1, "fp1", "fp2"));
        history.Add(MakeRecord(2, "fp1", "fp2"));
        history.Add(MakeRecord(3, "fp1", "fp2"));

        history.IsStagnant(3).Should().BeTrue();
    }

    [Fact]
    public void IsStagnant_returns_false_when_fingerprints_change()
    {
        var history = new IterationHistory();
        history.Add(MakeRecord(1, "fp1", "fp2"));
        history.Add(MakeRecord(2, "fp1"));
        history.Add(MakeRecord(3, "fp1"));

        history.IsStagnant(3).Should().BeFalse();
    }

    [Fact]
    public void HasRegression_returns_false_when_fewer_than_3_records()
    {
        var history = new IterationHistory();
        history.Add(MakeRecord(1, "fp1"));
        history.Add(MakeRecord(2, "fp1"));

        history.HasRegression().Should().BeFalse();
    }

    [Fact]
    public void HasRegression_detects_reappearing_fingerprint()
    {
        var history = new IterationHistory();
        history.Add(MakeRecord(1, "fp1", "fp2")); // fp2 present
        history.Add(MakeRecord(2, "fp1"));        // fp2 fixed
        history.Add(MakeRecord(3, "fp1", "fp2")); // fp2 regressed

        history.HasRegression().Should().BeTrue();
    }

    [Fact]
    public void HasRegression_returns_false_when_no_regression()
    {
        var history = new IterationHistory();
        history.Add(MakeRecord(1, "fp1", "fp2"));
        history.Add(MakeRecord(2, "fp1"));
        history.Add(MakeRecord(3));               // all fixed

        history.HasRegression().Should().BeFalse();
    }

    [Fact]
    public void Count_reflects_added_records()
    {
        var history = new IterationHistory();
        history.Count.Should().Be(0);
        history.Add(MakeRecord(1));
        history.Count.Should().Be(1);
        history.Add(MakeRecord(2));
        history.Count.Should().Be(2);
    }
}
