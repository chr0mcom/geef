namespace Geef.Sdk.Advisors;

/// <summary>
/// The kind of advice an advisor provides. Corresponds to the advisor typology
/// introduced in the GEEF Advisor follow-up article.
/// </summary>
public enum AdvisorKind
{
    /// <summary>Provides strategic guidance on direction and approach (e.g. "what's my plan?").</summary>
    Strategic,

    /// <summary>Provides critical perspective, surfacing risks, gaps, and weaknesses.</summary>
    Critical,

    /// <summary>Provides Socratic questioning that challenges assumptions rather than giving answers.</summary>
    Socratic,

    /// <summary>Provides calibration against prior evidence, baselines, or standards.</summary>
    Calibration,

    /// <summary>Provides memory-backed advice drawn from accumulated context, notes, or prior runs.</summary>
    Memory
}
