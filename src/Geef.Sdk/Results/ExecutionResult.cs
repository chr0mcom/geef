using Geef.Sdk.Context;

namespace Geef.Sdk.Results;

/// <summary>
/// Result of the execution phase.
/// Contains the updated RunContext with the generated/modified artifacts.
/// </summary>
public sealed record ExecutionResult
{
    /// <summary>
    /// The updated context. The executor creates a new context snapshot
    /// via context.Set(), without mutating the input context.
    /// </summary>
    public required IRunContext UpdatedContext { get; init; }

    /// <summary>Optional notes from the execution process.</summary>
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}
