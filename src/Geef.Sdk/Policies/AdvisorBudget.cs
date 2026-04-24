namespace Geef.Sdk.Policies;

/// <summary>
/// Configurable budget for advisor consultations across a pipeline run.
/// Anthropic provides per-request limits via <c>max_uses</c> but no conversation-level
/// budget; this type fills that gap on the SDK side.
/// </summary>
public sealed class AdvisorBudget
{
    /// <summary>Maximum advisor consultations across the entire run. Default: 10.</summary>
    public int MaxConsultationsPerRun { get; init; } = 10;

    /// <summary>Maximum advisor consultations per iteration. Default: 3.</summary>
    public int MaxConsultationsPerIteration { get; init; } = 3;

    /// <summary>Soft cap on tokens per consultation (for budget tracking). Default: 1000.</summary>
    public int MaxTokensPerConsultation { get; init; } = 1000;

    /// <summary>Total token budget for advisor traffic across the run. Default: 20000.</summary>
    public int MaxTotalAdvisorTokens { get; init; } = 20000;

    /// <summary>Wall-clock budget for advisor consultations across the run. Default: 5 min.</summary>
    public TimeSpan MaxTotalAdvisorTime { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Returns a builder-friendly clone with the same configuration.</summary>
    public AdvisorBudget Clone() => new()
    {
        MaxConsultationsPerRun = MaxConsultationsPerRun,
        MaxConsultationsPerIteration = MaxConsultationsPerIteration,
        MaxTokensPerConsultation = MaxTokensPerConsultation,
        MaxTotalAdvisorTokens = MaxTotalAdvisorTokens,
        MaxTotalAdvisorTime = MaxTotalAdvisorTime,
    };
}
