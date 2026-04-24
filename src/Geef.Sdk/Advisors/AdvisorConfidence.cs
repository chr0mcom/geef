namespace Geef.Sdk.Advisors;

/// <summary>
/// The confidence an advisor reports in its own advice.
/// </summary>
public enum AdvisorConfidence
{
    /// <summary>Low confidence — the advice is speculative or weakly grounded.</summary>
    Low,

    /// <summary>Medium confidence — the advice is plausible and grounded in available context.</summary>
    Medium,

    /// <summary>High confidence — the advice is well grounded and the advisor expects it to hold.</summary>
    High,

    /// <summary>
    /// Uncertain — the advisor explicitly cannot quantify confidence, or has conflicting signals.
    /// Prefer this over <see cref="Low"/> when the advisor wants to flag ambiguity rather than weak evidence.
    /// </summary>
    Uncertain
}
