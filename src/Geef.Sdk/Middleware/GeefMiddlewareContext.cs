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

    /// <summary>Additional properties (extensible).</summary>
    public IDictionary<string, object> Properties { get; } = new Dictionary<string, object>();
}
