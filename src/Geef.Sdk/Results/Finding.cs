namespace Geef.Sdk.Results;

/// <summary>
/// A single finding from a reviewer.
/// </summary>
public sealed record Finding
{
    /// <summary>Name of the reviewer that produced this finding.</summary>
    public required string ReviewerName { get; init; }

    /// <summary>
    /// Unique fingerprint of this finding for convergence analysis.
    /// Enables detection of stagnation (same fingerprint across iterations).
    /// Recommendation: Hash of ReviewerName + Category + normalized Message.
    /// </summary>
    public required string Fingerprint { get; init; }

    /// <summary>Human-readable description of the finding.</summary>
    public required string Message { get; init; }

    /// <summary>Severity of the finding.</summary>
    public FindingSeverity Severity { get; init; } = FindingSeverity.Error;

    /// <summary>Optional category (e.g. "Security", "Style", "Logic").</summary>
    public string? Category { get; init; }

    /// <summary>Optional reference to the affected artifact (e.g. filename, line).</summary>
    public string? ArtifactReference { get; init; }

    /// <summary>Optional metadata (e.g. rule ID, confidence score).</summary>
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }
}
