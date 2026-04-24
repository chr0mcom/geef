namespace Geef.Sdk.Advisors;

/// <summary>
/// A request to an advisor. For native Anthropic backends the question is an
/// internal marker; for other backends it is forwarded as the actual prompt.
/// </summary>
public sealed record AdvisorQuery
{
    /// <summary>The question or topic of the consultation.</summary>
    public required string Question { get; init; }

    /// <summary>The character of the consultation (diagnostic, heuristic, ...).</summary>
    public required AdvisorQueryCharacter Character { get; init; }

    /// <summary>
    /// Optional set of context-key names the advisor should focus on.
    /// Implementations may use this for context filtering or prompt construction;
    /// they MAY also ignore it (e.g. native Anthropic backends always see the full transcript).
    /// </summary>
    public IReadOnlyList<string> RelevantContextKeys { get; init; } = Array.Empty<string>();

    /// <summary>Optional cap on the advice length, in tokens.</summary>
    public int? MaxTokens { get; init; }
}
