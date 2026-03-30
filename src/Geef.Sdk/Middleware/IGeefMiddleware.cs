namespace Geef.Sdk.Middleware;

/// <summary>
/// Middleware for cross-cutting concerns in the pipeline.
/// Inspired by ASP.NET Core middleware, but for the GEEF context.
/// </summary>
public interface IGeefMiddleware
{
    /// <summary>
    /// Processes the pipeline call. MUST call next() to continue the chain,
    /// unless the middleware wants to abort the call.
    /// </summary>
    Task InvokeAsync(GeefMiddlewareContext middlewareContext, Func<Task> next);
}
