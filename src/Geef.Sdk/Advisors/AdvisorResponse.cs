namespace Geef.Sdk.Advisors;

/// <summary>
/// The structured response of an advisor. Combines free-form advice text with
/// optional structured fields (risks, suggested actions, confidence).
/// </summary>
public sealed record AdvisorResponse
{
    /// <summary>Free-form advice text. Always present, even on degraded outcomes.</summary>
    public required string AdviceText { get; init; }

    /// <summary>Confidence the advisor has in its own advice.</summary>
    public required AdvisorConfidence Confidence { get; init; }

    /// <summary>Outcome of the consultation. Use this instead of exceptions for advisor failures.</summary>
    public AdvisorOutcome Outcome { get; init; } = AdvisorOutcome.Success;

    /// <summary>Optional list of identified risks.</summary>
    public IReadOnlyList<string> Risks { get; init; } = Array.Empty<string>();

    /// <summary>Optional list of concrete suggested actions.</summary>
    public IReadOnlyList<string> SuggestedActions { get; init; } = Array.Empty<string>();

    /// <summary>How long the consultation took.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>The name of the advisor that produced this response (set automatically by the orchestrator).</summary>
    public string? AdvisorName { get; init; }

    /// <summary>
    /// Unique ID for this consultation, set automatically by the orchestrator.
    /// Providers pass this value to <see cref="IAdvisorOrchestrator.AttributeArtifactToConsultation"/>
    /// to record that a particular artifact was influenced by this advice.
    /// </summary>
    public string? ConsultationId { get; init; }

    /// <summary>Approximate token count of the advice (input + output), for budget tracking.</summary>
    public int? ApproximateTokenCount { get; init; }
}
