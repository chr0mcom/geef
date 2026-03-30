namespace Geef.Sdk.Middleware;

/// <summary>
/// The phases of a GEEF pipeline.
/// </summary>
public enum GeefPhase
{
    /// <summary>Context gathering phase.</summary>
    Grounding,

    /// <summary>Artifact generation/modification phase.</summary>
    Execution,

    /// <summary>Review/evaluation phase.</summary>
    Evaluation,

    /// <summary>Final output preparation phase.</summary>
    Finalize
}
