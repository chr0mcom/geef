using Geef.Sdk.Advisors;
using Geef.Sdk.Context;
using Geef.Sdk.Runtime;

namespace Geef.Sdk;

/// <summary>
/// The complete result of a pipeline run.
/// </summary>
/// <typeparam name="TOutput">The application-specific output type.</typeparam>
public sealed record GeefPipelineResult<TOutput>
{
    /// <summary>The final output produced by the finalizer.</summary>
    public required TOutput Output { get; init; }

    /// <summary>Unique run ID.</summary>
    public required string RunId { get; init; }

    /// <summary>Total number of iterations executed.</summary>
    public required int TotalIterations { get; init; }

    /// <summary>Total wall-clock duration of the run.</summary>
    public required TimeSpan TotalDuration { get; init; }

    /// <summary>Whether the run completed successfully.</summary>
    public required bool Success { get; init; }

    /// <summary>The final context snapshot at the time of finalization.</summary>
    public required IRunContext FinalContext { get; init; }

    /// <summary>Complete iteration history.</summary>
    public required IterationHistory History { get; init; }

    /// <summary>All advisor consultations that happened during the run.</summary>
    public IReadOnlyList<AdvisorConsultationRecord> AdvisorConsultations { get; init; }
        = Array.Empty<AdvisorConsultationRecord>();

    /// <summary>All artifact attributions to advisor consultations.</summary>
    public IReadOnlyList<AdvisorArtifactAttribution> AdvisorAttributions { get; init; }
        = Array.Empty<AdvisorArtifactAttribution>();
}
