using Geef.Sdk.Context;

namespace Geef.Sdk.Advisors;

/// <summary>
/// Central entry point for providers (grounding, execution, reviewers, finalizers)
/// that want to consult an advisor. Wraps budget enforcement, policy enforcement,
/// provenance logging, event publishing and tracing around the raw <see cref="IAdvisor"/> call.
///
/// Precedence rule: Budget &gt; Policy &gt; Provider. Budget violations and policy
/// rejections return a synthetic <see cref="AdvisorResponse"/> with the appropriate
/// <see cref="AdvisorOutcome"/>. They do NOT throw.
///
/// Each pipeline run gets its own orchestrator instance, holding its own budget
/// state and provenance log. The orchestrator is thread-safe for concurrent
/// consultations within a single run.
/// </summary>
public interface IAdvisorOrchestrator
{
    /// <summary>True if at least one advisor is registered.</summary>
    bool HasAdvisor { get; }

    /// <summary>
    /// Consults the default advisor (the first registered advisor). If none is
    /// registered, returns a synthetic <see cref="AdvisorOutcome.NoApplicableAdvice"/> response.
    /// </summary>
    /// <param name="query">The consultation request.</param>
    /// <param name="context">The current run context.</param>
    /// <param name="cancellationToken">Cancellation token for the consultation.</param>
    Task<AdvisorResponse> ConsultAsync(
        AdvisorQuery query,
        IRunContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Consults a specific advisor by name. If the named advisor is not registered,
    /// returns a synthetic <see cref="AdvisorOutcome.NoApplicableAdvice"/> response.
    /// </summary>
    /// <param name="advisorName">The <see cref="IAdvisor.Name"/> of the advisor to consult.</param>
    /// <param name="query">The consultation request.</param>
    /// <param name="context">The current run context.</param>
    /// <param name="cancellationToken">Cancellation token for the consultation.</param>
    Task<AdvisorResponse> ConsultAsync(
        string advisorName,
        AdvisorQuery query,
        IRunContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that an artifact (identified by its context-key name) was influenced
    /// by a previous consultation. Call this immediately after writing an artifact
    /// to the context that incorporated advisor advice. The consultation ID comes
    /// from <see cref="AdvisorResponse.ConsultationId"/>.
    /// </summary>
    /// <param name="artifactContextKey">The context-key name of the artifact.</param>
    /// <param name="consultationId">The consultation id returned in the advisor response.</param>
    void AttributeArtifactToConsultation(string artifactContextKey, string consultationId);

    /// <summary>
    /// Read-only access to the run's provenance log. The runner uses this to
    /// populate the advisor-related properties on the pipeline result at the end of the run.
    /// </summary>
    IAdvisorProvenanceLog Provenance { get; }
}
