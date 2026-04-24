namespace Geef.Sdk.Advisors;

/// <summary>
/// Opt-in interface that providers (grounding, execution, reviewers, finalizers)
/// can implement to receive the per-run <see cref="IAdvisorOrchestrator"/>. Implementing
/// this interface is opt-in and does not break existing providers.
///
/// Implementations MUST store the orchestrator in a field that is safe for
/// concurrent runs (e.g. <c>AsyncLocal</c>). The provided <see cref="AdvisorAwareProviderBase"/>
/// handles this correctly.
/// </summary>
public interface IAdvisorAware
{
    /// <summary>
    /// Called by the runner at the start of each run, before any phase methods are invoked.
    /// Called again with <c>null</c> at the end of the run to release the reference.
    /// </summary>
    /// <param name="orchestrator">The per-run orchestrator, or null when releasing at end of run.</param>
    void SetAdvisorOrchestrator(IAdvisorOrchestrator? orchestrator);
}
