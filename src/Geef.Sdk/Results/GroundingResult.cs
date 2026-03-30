using Geef.Sdk.Context;

namespace Geef.Sdk.Results;

/// <summary>
/// Result of the grounding phase.
/// Contains the initialized RunContext with all collected context information.
/// </summary>
public sealed record GroundingResult
{
    /// <summary>The initialized context with all grounding data.</summary>
    public required IRunContext Context { get; init; }

    /// <summary>Optional notes/logs from the grounding process.</summary>
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}
