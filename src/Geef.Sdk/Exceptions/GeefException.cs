namespace Geef.Sdk.Exceptions;

/// <summary>
/// Base exception for all GEEF-specific errors.
/// </summary>
public class GeefException : Exception
{
    /// <summary>The run ID associated with this error, if available.</summary>
    public string? RunId { get; init; }

    /// <inheritdoc />
    public GeefException(string message, string? runId = null) : base(message) => RunId = runId;

    /// <inheritdoc />
    public GeefException(string message, Exception inner, string? runId = null) : base(message, inner) => RunId = runId;
}
