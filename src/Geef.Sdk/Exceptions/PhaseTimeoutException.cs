using Geef.Sdk.Middleware;

namespace Geef.Sdk.Exceptions;

/// <summary>
/// A phase timeout was exceeded.
/// </summary>
public sealed class PhaseTimeoutException : GeefException
{
    /// <summary>The phase that timed out.</summary>
    public required GeefPhase Phase { get; init; }

    /// <summary>The configured timeout that was exceeded.</summary>
    public required TimeSpan Timeout { get; init; }

    /// <inheritdoc />
    public PhaseTimeoutException(string message, string? runId = null) : base(message, runId) { }
}
