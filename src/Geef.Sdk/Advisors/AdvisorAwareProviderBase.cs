namespace Geef.Sdk.Advisors;

/// <summary>
/// Optional base class for providers that want a thread-safe <see cref="IAdvisorOrchestrator"/>
/// reference per run. Providers may opt in by deriving from this class and using
/// <see cref="Advisor"/> in their phase methods.
///
/// The <c>AsyncLocal</c> field is instance-level, not static. This means that if a single
/// provider instance is used in multiple concurrent runs, each run sees its own
/// orchestrator (the <c>AsyncLocal</c> isolates the value per async flow, and the runner
/// sets/clears it within each run's flow). The runner is expected to call
/// <c>SetAdvisorOrchestrator(null)</c> at the end of a run to release the reference;
/// this base class then clears the <c>AsyncLocal</c> slot for the current async flow.
/// </summary>
public abstract class AdvisorAwareProviderBase : IAdvisorAware
{
    private readonly AsyncLocal<IAdvisorOrchestrator?> _current = new();

    /// <summary>The orchestrator for the current run, or null if no run is active.</summary>
    protected IAdvisorOrchestrator? Advisor => _current.Value;

    void IAdvisorAware.SetAdvisorOrchestrator(IAdvisorOrchestrator? orchestrator)
        => _current.Value = orchestrator;
}
