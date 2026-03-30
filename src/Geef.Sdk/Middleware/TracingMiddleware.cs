using Geef.Sdk.Diagnostics;

namespace Geef.Sdk.Middleware;

/// <summary>
/// Middleware that tracks each phase execution as a span via ActivitySource.
/// </summary>
public sealed class TracingMiddleware : IGeefMiddleware
{
    /// <inheritdoc />
    public async Task InvokeAsync(GeefMiddlewareContext ctx, Func<Task> next)
    {
        using var activity = GeefDiagnostics.ActivitySource.StartActivity($"geef.middleware.{ctx.Phase.ToString().ToLowerInvariant()}");
        activity?.SetTag("geef.run_id", ctx.RunId);
        activity?.SetTag("geef.phase", ctx.Phase.ToString());

        if (ctx.Iteration.HasValue)
            activity?.SetTag("geef.iteration", ctx.Iteration.Value);

        if (ctx.ComponentName is not null)
            activity?.SetTag("geef.component", ctx.ComponentName);

        await next();
    }
}
