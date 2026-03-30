using Geef.Sdk.Middleware;

namespace Geef.Sdk.Exceptions;

/// <summary>
/// A provider (Grounding, Execution, Reviewer, Finalizer) raised an infrastructure error.
/// </summary>
public sealed class ProviderException : GeefException
{
    /// <summary>The phase in which the error occurred.</summary>
    public required GeefPhase Phase { get; init; }

    /// <summary>The name of the provider that failed, if available.</summary>
    public string? ProviderName { get; init; }

    /// <inheritdoc />
    public ProviderException(string message, Exception inner, string? runId = null) : base(message, inner, runId) { }
}
