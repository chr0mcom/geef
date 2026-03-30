using Geef.Sdk.Context;

namespace Geef.Sdk.Results;

/// <summary>
/// Result of the finalize phase.
/// </summary>
/// <typeparam name="TOutput">The application-specific output type.</typeparam>
public sealed record FinalizeResult<TOutput>
{
    /// <summary>The final result of the pipeline.</summary>
    public required TOutput Output { get; init; }

    /// <summary>The final context snapshot at the time of finalization.</summary>
    public required IRunContext FinalContext { get; init; }

    /// <summary>Optional summary/notes.</summary>
    public string? Summary { get; init; }
}
