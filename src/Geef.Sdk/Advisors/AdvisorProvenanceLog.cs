namespace Geef.Sdk.Advisors;

/// <summary>
/// Default thread-safe implementation of <see cref="IAdvisorProvenanceLog"/>.
/// One instance is held per run by the <see cref="AdvisorOrchestrator"/>.
/// </summary>
public sealed class AdvisorProvenanceLog : IAdvisorProvenanceLog
{
    private readonly object _lock = new();
    private readonly List<AdvisorConsultationRecord> _consultations = new();
    private readonly List<AdvisorArtifactAttribution> _attributions = new();

    /// <inheritdoc />
    public IReadOnlyList<AdvisorConsultationRecord> Consultations
    {
        get { lock (_lock) return _consultations.ToArray(); }
    }

    /// <inheritdoc />
    public IReadOnlyList<AdvisorArtifactAttribution> Attributions
    {
        get { lock (_lock) return _attributions.ToArray(); }
    }

    /// <inheritdoc />
    public void RecordConsultation(AdvisorConsultationRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        lock (_lock) _consultations.Add(record);
    }

    /// <inheritdoc />
    public void RecordAttribution(string artifactContextKey, string consultationId)
    {
        ArgumentException.ThrowIfNullOrEmpty(artifactContextKey);
        ArgumentException.ThrowIfNullOrEmpty(consultationId);
        lock (_lock) _attributions.Add(new AdvisorArtifactAttribution
        {
            ArtifactContextKey = artifactContextKey,
            ConsultationId = consultationId,
            Timestamp = DateTimeOffset.UtcNow
        });
    }
}
