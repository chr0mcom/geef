using FluentAssertions;
using Geef.Sdk.Policies;
using Xunit;

namespace Geef.Sdk.Tests.Advisors;

public sealed class AdvisorBudgetStateTests
{
    private static AdvisorBudgetState Make(AdvisorBudget? budget = null)
        => new(budget ?? new AdvisorBudget());

    [Fact]
    public void WouldExceedBudget_returns_false_when_under_all_limits()
    {
        var state = Make();
        state.WouldExceedBudget(500).Should().BeFalse();
    }

    [Fact]
    public void WouldExceedBudget_returns_true_when_run_limit_reached()
    {
        var state = Make(new AdvisorBudget { MaxConsultationsPerRun = 2 });
        state.RecordConsumption(100, TimeSpan.FromMilliseconds(1));
        state.RecordConsumption(100, TimeSpan.FromMilliseconds(1));

        state.WouldExceedBudget(100).Should().BeTrue();
    }

    [Fact]
    public void WouldExceedBudget_returns_true_when_iteration_limit_reached()
    {
        var state = Make(new AdvisorBudget { MaxConsultationsPerIteration = 1 });
        state.StartIteration(1);
        state.RecordConsumption(100, TimeSpan.FromMilliseconds(1));

        state.WouldExceedBudget(100).Should().BeTrue();
    }

    [Fact]
    public void WouldExceedBudget_returns_true_when_total_token_budget_reached()
    {
        var state = Make(new AdvisorBudget { MaxTotalAdvisorTokens = 1000 });
        state.RecordConsumption(900, TimeSpan.FromMilliseconds(1));

        state.WouldExceedBudget(200).Should().BeTrue();
    }

    [Fact]
    public void WouldExceedBudget_returns_true_when_per_consultation_token_cap_exceeded()
    {
        var state = Make(new AdvisorBudget { MaxTokensPerConsultation = 500 });

        state.WouldExceedBudget(600).Should().BeTrue();
    }

    [Fact]
    public void WouldExceedBudget_returns_true_when_time_budget_reached()
    {
        var state = Make(new AdvisorBudget { MaxTotalAdvisorTime = TimeSpan.FromMilliseconds(10) });
        state.RecordConsumption(10, TimeSpan.FromMilliseconds(15));

        state.WouldExceedBudget(10).Should().BeTrue();
    }

    [Fact]
    public void StartIteration_resets_iteration_counter()
    {
        var state = Make(new AdvisorBudget { MaxConsultationsPerIteration = 2 });
        state.StartIteration(1);
        state.RecordConsumption(10, TimeSpan.FromMilliseconds(1));
        state.RecordConsumption(10, TimeSpan.FromMilliseconds(1));
        state.WouldExceedBudget(10).Should().BeTrue();

        state.StartIteration(2);
        state.ConsultationsThisIteration.Should().Be(0);
        state.WouldExceedBudget(10).Should().BeFalse();
    }

    [Fact]
    public void RecordConsumption_increments_counters_correctly()
    {
        var state = Make();
        state.StartIteration(1);
        state.RecordConsumption(250, TimeSpan.FromMilliseconds(100));
        state.RecordConsumption(100, TimeSpan.FromMilliseconds(50));

        state.ConsultationsTotal.Should().Be(2);
        state.ConsultationsThisIteration.Should().Be(2);
        state.TokensTotal.Should().Be(350);
        state.TimeTotal.Should().Be(TimeSpan.FromMilliseconds(150));
    }

    [Fact]
    public async Task Concurrent_RecordConsumption_is_thread_safe()
    {
        var state = Make(new AdvisorBudget
        {
            MaxConsultationsPerRun = 10_000,
            MaxTotalAdvisorTokens = 10_000_000,
            MaxConsultationsPerIteration = 10_000,
            MaxTotalAdvisorTime = TimeSpan.FromMinutes(10),
        });

        var tasks = Enumerable.Range(0, 100)
            .Select(_ => Task.Run(() =>
            {
                for (var i = 0; i < 100; i++)
                    state.RecordConsumption(1, TimeSpan.FromTicks(1));
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        state.ConsultationsTotal.Should().Be(10_000);
        state.TokensTotal.Should().Be(10_000);
    }
}
