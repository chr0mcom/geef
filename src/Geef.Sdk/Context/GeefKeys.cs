using Geef.Sdk.Results;
using Geef.Sdk.Runtime;

namespace Geef.Sdk.Context;

/// <summary>
/// Predefined context keys used internally by the SDK.
/// Consumers can define their own ContextKeys for domain-specific artifacts.
/// </summary>
public static class GeefKeys
{
    /// <summary>The original input string (e.g. user prompt).</summary>
    public static readonly ContextKey<string> OriginalInput = new("geef:original-input");

    /// <summary>Findings from the last evaluation round.</summary>
    public static readonly ContextKey<IReadOnlyList<Finding>> PreviousFindings = new("geef:previous-findings");

    /// <summary>The current iteration number (1-based).</summary>
    public static readonly ContextKey<int> CurrentIteration = new("geef:current-iteration");

    /// <summary>The timestamp when the pipeline run started.</summary>
    public static readonly ContextKey<DateTimeOffset> RunStartedAt = new("geef:run-started-at");

    /// <summary>A unique run ID for tracing and correlation.</summary>
    public static readonly ContextKey<string> RunId = new("geef:run-id");

    /// <summary>The complete iteration history for convergence analysis.</summary>
    public static readonly ContextKey<IterationHistory> IterationHistory = new("geef:iteration-history");
}
