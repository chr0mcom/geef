using Geef.Sdk.Advisors;
using Geef.Sdk.Middleware;

namespace Geef.Sdk.Policies;

/// <summary>
/// Decides whether an advisor consultation is allowed at a given point in the run.
/// A policy MAY restrict consultations (e.g. "no advisor in iteration 1") but it
/// CANNOT force a consultation that the provider did not request.
///
/// Precedence rule: Budget &gt; Policy &gt; Provider. The budget always wins; the
/// policy may further restrict; the provider decides within what is allowed.
/// </summary>
public interface IAdvisorPolicy
{
    /// <summary>Returns true if the consultation should be allowed.</summary>
    /// <param name="consultationContext">The context of the consultation about to happen.</param>
    bool IsConsultationAllowed(AdvisorConsultationContext consultationContext);
}

/// <summary>
/// Read-only context passed to <see cref="IAdvisorPolicy.IsConsultationAllowed"/>.
/// </summary>
public sealed record AdvisorConsultationContext
{
    /// <summary>The phase in which the consultation is requested.</summary>
    public required GeefPhase Phase { get; init; }

    /// <summary>The current iteration (null for Grounding/Finalize).</summary>
    public int? Iteration { get; init; }

    /// <summary>The advisor that is about to be consulted.</summary>
    public required IAdvisor Advisor { get; init; }

    /// <summary>The query that is about to be sent.</summary>
    public required AdvisorQuery Query { get; init; }

    /// <summary>How many consultations have already happened in this run.</summary>
    public required int ConsultationsSoFar { get; init; }

    /// <summary>How many consultations have already happened in the current iteration.</summary>
    public required int ConsultationsInThisIteration { get; init; }
}
