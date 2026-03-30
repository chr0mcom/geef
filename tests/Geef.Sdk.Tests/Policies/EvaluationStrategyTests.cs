using FluentAssertions;
using Geef.Sdk.Context;
using Geef.Sdk.Policies;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;
using NSubstitute;
using Xunit;

namespace Geef.Sdk.Tests.Policies;

public sealed class EvaluationStrategyTests
{
    private static IReviewer MakeReviewer(string name, ReviewDecision decision, int priority = 100)
    {
        var reviewer = Substitute.For<IReviewer>();
        reviewer.Name.Returns(name);
        reviewer.Priority.Returns(priority);
        reviewer.ReviewAsync(Arg.Any<IRunContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ReviewResult
            {
                ReviewerName = name,
                Decision = decision,
                Duration = TimeSpan.FromMilliseconds(10)
            }));
        return reviewer;
    }

    private static IRunContext EmptyContext() => new RunContext();

    [Fact]
    public async Task Sequential_runs_all_reviewers_in_order()
    {
        var r1 = MakeReviewer("R1", ReviewDecision.Approved);
        var r2 = MakeReviewer("R2", ReviewDecision.Approved);
        var strategy = new SequentialEvaluationStrategy();

        var result = await strategy.ExecuteAsync(new[] { r1, r2 }, EmptyContext());

        result.Reviews.Should().HaveCount(2);
        result.Reviews[0].ReviewerName.Should().Be("R1");
        result.Reviews[1].ReviewerName.Should().Be("R2");
    }

    [Fact]
    public async Task Parallel_runs_all_reviewers()
    {
        var r1 = MakeReviewer("R1", ReviewDecision.Approved);
        var r2 = MakeReviewer("R2", ReviewDecision.Rejected);
        var strategy = new ParallelEvaluationStrategy();

        var result = await strategy.ExecuteAsync(new[] { r1, r2 }, EmptyContext());

        result.Reviews.Should().HaveCount(2);
    }

    [Fact]
    public async Task FailFast_stops_after_first_rejection()
    {
        var r1 = MakeReviewer("R1", ReviewDecision.Rejected);

        var slowReviewer = Substitute.For<IReviewer>();
        slowReviewer.Name.Returns("Slow");
        slowReviewer.Priority.Returns(100);
        slowReviewer.ReviewAsync(Arg.Any<IRunContext>(), Arg.Any<CancellationToken>())
            .Returns(async (callInfo) =>
            {
                await Task.Delay(2000, (CancellationToken)callInfo[1]);
                return new ReviewResult { ReviewerName = "Slow", Decision = ReviewDecision.Approved, Duration = TimeSpan.Zero };
            });

        var strategy = new FailFastEvaluationStrategy();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await strategy.ExecuteAsync(new[] { r1, slowReviewer }, EmptyContext());
        sw.Stop();

        result.HasBlockingIssues.Should().BeTrue();
        result.Reviews.Should().Contain(r => r.ReviewerName == "R1");
        sw.ElapsedMilliseconds.Should().BeLessThan(500, "FailFast must abort before the slow reviewer finishes");
    }

    [Fact]
    public async Task PriorityOrdered_runs_in_priority_order()
    {
        var callOrder = new List<string>();

        var r1 = Substitute.For<IReviewer>();
        r1.Name.Returns("Low");
        r1.Priority.Returns(50);
        r1.ReviewAsync(Arg.Any<IRunContext>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                callOrder.Add("Low");
                await Task.Delay(1);
                return new ReviewResult { ReviewerName = "Low", Decision = ReviewDecision.Approved, Duration = TimeSpan.Zero };
            });

        var r2 = Substitute.For<IReviewer>();
        r2.Name.Returns("High");
        r2.Priority.Returns(10);
        r2.ReviewAsync(Arg.Any<IRunContext>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                callOrder.Add("High");
                await Task.Delay(1);
                return new ReviewResult { ReviewerName = "High", Decision = ReviewDecision.Approved, Duration = TimeSpan.Zero };
            });

        var strategy = new PriorityOrderedEvaluationStrategy();
        await strategy.ExecuteAsync(new[] { r1, r2 }, EmptyContext());

        callOrder[0].Should().Be("High");
        callOrder[1].Should().Be("Low");
    }

    [Fact]
    public async Task PriorityOrdered_stops_on_rejection_with_error_finding()
    {
        var r1 = Substitute.For<IReviewer>();
        r1.Name.Returns("First");
        r1.Priority.Returns(1);
        r1.ReviewAsync(Arg.Any<IRunContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ReviewResult
            {
                ReviewerName = "First",
                Decision = ReviewDecision.Rejected,
                Findings = new[]
                {
                    new Finding { ReviewerName = "First", Fingerprint = "fp1", Message = "Error", Severity = FindingSeverity.Error }
                },
                Duration = TimeSpan.Zero
            }));

        var r2 = MakeReviewer("Second", ReviewDecision.Approved, priority: 2);
        var strategy = new PriorityOrderedEvaluationStrategy();

        var result = await strategy.ExecuteAsync(new[] { r1, r2 }, EmptyContext());

        result.Reviews.Should().HaveCount(1);
        result.Reviews[0].ReviewerName.Should().Be("First");
    }

    [Fact]
    public async Task EvaluationAggregate_IsFullyApproved_when_all_approved()
    {
        var r1 = MakeReviewer("R1", ReviewDecision.Approved);
        var r2 = MakeReviewer("R2", ReviewDecision.ApprovedWithWarnings);
        var r3 = MakeReviewer("R3", ReviewDecision.NotApplicable);
        var strategy = new SequentialEvaluationStrategy();

        var result = await strategy.ExecuteAsync(new[] { r1, r2, r3 }, EmptyContext());

        result.IsFullyApproved.Should().BeTrue();
        result.HasBlockingIssues.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluationAggregate_HasBlockingIssues_when_one_rejected()
    {
        var r1 = MakeReviewer("R1", ReviewDecision.Approved);
        var r2 = MakeReviewer("R2", ReviewDecision.Rejected);
        var strategy = new SequentialEvaluationStrategy();

        var result = await strategy.ExecuteAsync(new[] { r1, r2 }, EmptyContext());

        result.HasBlockingIssues.Should().BeTrue();
        result.IsFullyApproved.Should().BeFalse();
    }
}
