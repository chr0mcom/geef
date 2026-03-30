using Geef.Sdk.Exceptions;

namespace Geef.Sdk.Middleware;

/// <summary>
/// Middleware that enforces per-phase timeouts.
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
        using var cts = new CancellationTokenSource(timeout);

        try
        {
            var task = next();
            var completed = await Task.WhenAny(task, Task.Delay(timeout, cts.Token));
            if (completed != task)
            {
                throw new PhaseTimeoutException(
                    $"Phase {ctx.Phase} exceeded timeout of {timeout}.",
                    ctx.RunId)
                {
                    Phase = ctx.Phase,
                    Timeout = timeout
                };
            }
            await task;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            throw new PhaseTimeoutException(
                $"Phase {ctx.Phase} exceeded timeout of {timeout}.",
                ctx.RunId)
            {
                Phase = ctx.Phase,
                Timeout = timeout
            };
        }
    }
}
