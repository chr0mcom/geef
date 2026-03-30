using Geef.Sdk.Context;

namespace Geef.Sdk.Middleware;

/// <summary>
/// Context passed to a middleware.
/// </summary>
public sealed class GeefMiddlewareContext
{
    /// <summary>Current phase (Grounding, Execution, Evaluation, Finalize).</summary>
    public required GeefPhase Phase { get; init; }

    /// <summary>Current RunContext (readonly snapshot).</summary>
    public required IRunContext RunContext { get; init; }

    /// <summary>Name of the current component (e.g. reviewer name).</summary>
    public string? ComponentName { get; init; }

    /// <summary>Current iteration (for Execution/Evaluation).</summary>
    public int? Iteration { get; init; }

    /// <summary>Run ID.</summary>
    public required string RunId { get; init; }

    /// <summary>
    /// Cancellation token for the current phase. Middleware may create a linked source
    /// (e.g. <see cref="TimeoutMiddleware"/>) and replace this token so downstream
    /// middleware and the final operation observe the derived token.
    /// </summary>
    public CancellationToken CancellationToken { get; set; }

    /// <summary>Additional properties (extensible).</summary>
    public IDictionary<string, object> Properties { get; } = new Dictionary<string, object>();
}
