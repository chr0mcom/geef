namespace Geef.Sdk.Events;

/// <summary>
/// No-op event sink. Used as default when no sinks are configured.
/// </summary>
public sealed class NullEventSink : IGeefEventSink
{
    /// <inheritdoc />
    public ValueTask PublishAsync(IGeefEvent geefEvent, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
}
