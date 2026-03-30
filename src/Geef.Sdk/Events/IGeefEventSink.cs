namespace Geef.Sdk.Events;

/// <summary>
/// Event sink for structured pipeline events.
/// Can be registered via DI. Multiple sinks are supported (composite pattern).
/// </summary>
public interface IGeefEventSink
{
    /// <summary>
    /// Publishes a GEEF event to this sink.
    /// </summary>
    ValueTask PublishAsync(IGeefEvent geefEvent, CancellationToken cancellationToken = default);
}
