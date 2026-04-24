using FluentAssertions;
using Geef.Sdk.Advisors;
using Geef.Sdk.Middleware;
using Xunit;

namespace Geef.Sdk.Tests.Advisors;

public sealed class AdvisorProvenanceLogTests
{
    private static AdvisorConsultationRecord MakeRecord(string id) => new()
    {
        ConsultationId = id,
        AdvisorName = "A",
        Phase = GeefPhase.Execution,
        Query = new AdvisorQuery { Question = "q", Character = AdvisorQueryCharacter.Diagnostic },
        Response = new AdvisorResponse { AdviceText = "a", Confidence = AdvisorConfidence.Medium },
        Timestamp = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void Consultations_returns_added_records_in_order()
    {
        var log = new AdvisorProvenanceLog();
        log.RecordConsultation(MakeRecord("id1"));
        log.RecordConsultation(MakeRecord("id2"));
        log.RecordConsultation(MakeRecord("id3"));

        log.Consultations.Select(c => c.ConsultationId).Should().ContainInOrder("id1", "id2", "id3");
    }

    [Fact]
    public void Attributions_returns_added_attributions_in_order()
    {
        var log = new AdvisorProvenanceLog();
        log.RecordAttribution("artifact-a", "id1");
        log.RecordAttribution("artifact-b", "id2");

        log.Attributions.Should().HaveCount(2);
        log.Attributions[0].ArtifactContextKey.Should().Be("artifact-a");
        log.Attributions[1].ConsultationId.Should().Be("id2");
    }

    [Fact]
    public void Empty_log_returns_empty_collections()
    {
        var log = new AdvisorProvenanceLog();

        log.Consultations.Should().BeEmpty();
        log.Attributions.Should().BeEmpty();
    }
}
