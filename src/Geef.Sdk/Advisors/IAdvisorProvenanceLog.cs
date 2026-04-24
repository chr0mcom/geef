namespace Geef.Sdk.Advisors;

/// <summary>
/// Append-only log of advisor consultations and the artifacts they influenced.
/// Held per run by the <see cref="AdvisorOrchestrator"/>. Thread-safe for concurrent appends.
/// </summary>
public interface IAdvisorProvenanceLog
{
    /// <summary>All consultations recorded so far. Returns a snapshot.</summary>
    IReadOnlyList<AdvisorConsultationRecord> Consultations { get; }

    /// <summary>All artifact attributions recorded so far. Returns a snapshot.</summary>
    IReadOnlyList<AdvisorArtifactAttribution> Attributions { get; }

    /// <summary>Records a consultation.</summary>
    /// <param name="record">The consultation record to append.</param>
    void RecordConsultation(AdvisorConsultationRecord record);

    /// <summary>Records that an artifact (identified by a context-key name) was influenced by a consultation.</summary>
    /// <param name="artifactContextKey">The name of the context key under which the artifact was written.</param>
    /// <param name="consultationId">The consultation ID returned by the orchestrator.</param>
    void RecordAttribution(string artifactContextKey, string consultationId);
}
