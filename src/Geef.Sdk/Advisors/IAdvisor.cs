using Geef.Sdk.Context;

namespace Geef.Sdk.Advisors;

/// <summary>
/// A consultative agent that can be invoked mid-work by a provider (grounding,
/// execution, reviewer, or finalizer) to obtain strategic guidance, gap detection,
/// risk assessment, or disambiguation. Unlike a reviewer, an advisor sees no
/// finished artifact and produces no findings; it produces advice.
///
/// IMPORTANT: This contract is an SDK abstraction. It is intentionally more
/// expressive than the raw Anthropic Advisor Tool API, which receives no
/// client-side question (the executor invokes the tool, the server forwards the
/// full transcript). For Anthropic-backed implementations, treat
/// <see cref="AdvisorQuery.Question"/> as an internal marker of the consultation
/// occasion. For other implementations (sub-agents, memory-backed advisors,
/// future provider APIs), the question is forwarded.
/// </summary>
public interface IAdvisor
{
    /// <summary>Unique name of the advisor (for events, tracing, provenance).</summary>
    string Name { get; }

    /// <summary>The kind of advice this advisor provides.</summary>
    AdvisorKind Kind { get; }

    /// <summary>Consults the advisor and returns its response.</summary>
    /// <param name="query">The consultation request.</param>
    /// <param name="context">The current run context, for advisors that use context information.</param>
    /// <param name="cancellationToken">Cancellation token propagated from the orchestrator.</param>
    /// <returns>The advisor's response.</returns>
    Task<AdvisorResponse> ConsultAsync(
        AdvisorQuery query,
        IRunContext context,
        CancellationToken cancellationToken = default);
}
