namespace Geef.Sdk.Advisors;

/// <summary>
/// The character of an advisor consultation — what kind of answer the caller expects.
/// Used for routing, policy decisions, and telemetry. See article section 2.5.
/// </summary>
public enum AdvisorQueryCharacter
{
    /// <summary>
    /// Diagnostic consultation: the caller asks "what is going on?" or "what am I missing?"
    /// The advisor is expected to observe and report, not to prescribe.
    /// </summary>
    Diagnostic,

    /// <summary>
    /// Heuristic consultation: the caller asks for rules of thumb, patterns, or analogies.
    /// The advisor brings experience-based guidance rather than formal analysis.
    /// </summary>
    Heuristic,

    /// <summary>
    /// Decision-support consultation: the caller is at a branching point and wants the advisor
    /// to weigh options. The advisor may recommend, but does not decide.
    /// </summary>
    DecisionSupport,

    /// <summary>
    /// Risk-assessment consultation: the caller asks "what could go wrong here?"
    /// The advisor enumerates risks and their relative severity.
    /// </summary>
    RiskAssessment
}
