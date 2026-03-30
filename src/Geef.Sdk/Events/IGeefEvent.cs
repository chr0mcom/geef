namespace Geef.Sdk.Events;

/// <summary>
/// Marker interface for all GEEF events.
/// </summary>
public interface IGeefEvent
{
    /// <summary>Timestamp of the event.</summary>
    DateTimeOffset Timestamp { get; }

    /// <summary>Run ID for correlation.</summary>
    string RunId { get; }
}
