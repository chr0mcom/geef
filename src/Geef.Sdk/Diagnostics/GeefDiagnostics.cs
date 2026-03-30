using System.Diagnostics;

namespace Geef.Sdk.Diagnostics;

/// <summary>
/// Provides an ActivitySource for OpenTelemetry-compatible distributed tracing.
/// Each pipeline run creates a root span; each iteration, execution and review create child spans.
/// </summary>
public static class GeefDiagnostics
{
    /// <summary>The shared ActivitySource for all GEEF tracing.</summary>
    public static readonly ActivitySource ActivitySource = new("Geef.Sdk", "1.0.0");
}
