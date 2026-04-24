using Geef.Sdk.Middleware;

namespace Geef.Sdk.Advisors;

/// <summary>
/// A record of a single advisor consultation. Used for budget tracking,
/// provenance, and audit-trail reconstruction.
/// </summary>
public sealed record AdvisorConsultationRecord
{
    /// <summary>Unique consultation ID (e.g. GUID-based, 12 chars).</summary>
    public required string ConsultationId { get; init; }

    /// <summary>Name of the advisor that was consulted.</summary>
    public required string AdvisorName { get; init; }

    /// <summary>The phase in which the consultation happened.</summary>
    public required GeefPhase Phase { get; init; }

    /// <summary>The iteration in which the consultation happened (null for Grounding/Finalize).</summary>
    public int? Iteration { get; init; }

    /// <summary>The query that was sent.</summary>
    public required AdvisorQuery Query { get; init; }

    /// <summary>The response that was received.</summary>
    public required AdvisorResponse Response { get; init; }

    /// <summary>When the consultation happened.</summary>
    public required DateTimeOffset Timestamp { get; init; }
}
