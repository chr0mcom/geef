using Geef.Sdk.Results;

namespace Geef.Sdk.Providers;

/// <summary>
/// Contextualizes the input. Gathers all necessary information
/// to understand the task (e.g. RAG queries, reading files, calling APIs).
/// </summary>
public interface IGroundingStep
{
    /// <summary>
    /// Runs the grounding phase to build the initial context.
    /// </summary>
    Task<GroundingResult> RunAsync(
        string input,
        CancellationToken cancellationToken = default);
}
