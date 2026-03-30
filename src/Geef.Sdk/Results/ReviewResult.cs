namespace Geef.Sdk.Results;

/// <summary>
/// Result of a single reviewer.
/// Clearly separates "domain evaluation" from "technical failure of the reviewer".
/// </summary>
public sealed record ReviewResult
{
    /// <summary>Name of the reviewer.</summary>
    public required string ReviewerName { get; init; }

    /// <summary>Decision of the reviewer.</summary>
    public required ReviewDecision Decision { get; init; }

    /// <summary>List of findings (may contain warnings even when Approved).</summary>
    public IReadOnlyList<Finding> Findings { get; init; } = Array.Empty<Finding>();

    /// <summary>Duration of the review.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Optional confidence score (0.0 to 1.0).</summary>
    public double? Confidence { get; init; }

    /// <summary>Optional suggested retry strategy on rejection.</summary>
    public string? SuggestedRetryHint { get; init; }
}
