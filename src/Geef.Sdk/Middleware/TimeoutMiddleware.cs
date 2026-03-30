using Geef.Sdk.Exceptions;

namespace Geef.Sdk.Middleware;

/// <summary>
/// Middleware that enforces per-phase timeouts.
/// Propagates a linked <see cref="CancellationToken"/> to downstream operations
/// via <see cref="GeefMiddlewareContext.CancellationToken"/>.
/// </summary>
public sealed class TimeoutMiddleware : IGeefMiddleware
{
    /// <summary>Default timeout per phase. Default: 5 minutes.</summary>
    public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Phase-specific timeouts (override DefaultTimeout).</summary>
    public Dictionary<GeefPhase, TimeSpan> PhaseTimeouts { get; init; } = new();

    /// <inheritdoc />
    public async Task InvokeAsync(GeefMiddlewareContext ctx, Func<Task> next)
    {
        var timeout = PhaseTimeouts.GetValueOrDefault(ctx.Phase, DefaultTimeout);
        var outerToken = ctx.CancellationToken;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(outerToken);
        cts.CancelAfter(timeout);

        ctx.CancellationToken = cts.Token;

        try
        {
            await next();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !outerToken.IsCancellationRequested)
        {
            throw new PhaseTimeoutException(
                $"Phase {ctx.Phase} exceeded timeout of {timeout}.",
                ctx.RunId)
            {
                Phase = ctx.Phase,
                Timeout = timeout
            };
        }
        finally
        {
            ctx.CancellationToken = outerToken;
        }
    }
}
