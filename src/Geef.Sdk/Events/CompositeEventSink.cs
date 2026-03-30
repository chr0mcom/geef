namespace Geef.Sdk.Events;

/// <summary>
/// Composite sink that serves multiple sinks simultaneously.
/// </summary>
public sealed class CompositeEventSink : IGeefEventSink
{
    private readonly IReadOnlyList<IGeefEventSink> _sinks;

    /// <summary>Initializes a new <see cref="CompositeEventSink"/> with the given sinks.</summary>
    public CompositeEventSink(IEnumerable<IGeefEventSink> sinks)
        => _sinks = sinks.ToList();

    /// <inheritdoc />
    public async ValueTask PublishAsync(IGeefEvent geefEvent, CancellationToken cancellationToken = default)
    {
        foreach (var sink in _sinks)
            await sink.PublishAsync(geefEvent, cancellationToken);
    }
}
