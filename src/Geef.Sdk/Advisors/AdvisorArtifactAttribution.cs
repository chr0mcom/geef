namespace Geef.Sdk.Advisors;

/// <summary>
/// A record linking an artifact (identified by its context-key name) to an
/// advisor consultation that influenced it. Written by providers via
/// <see cref="IAdvisorOrchestrator.AttributeArtifactToConsultation"/>.
/// </summary>
public sealed record AdvisorArtifactAttribution
{
    /// <summary>The name of the context key under which the artifact was written.</summary>
    public required string ArtifactContextKey { get; init; }

    /// <summary>The ID of the consultation that influenced the artifact.</summary>
    public required string ConsultationId { get; init; }

    /// <summary>When the attribution was recorded.</summary>
    public required DateTimeOffset Timestamp { get; init; }
}
