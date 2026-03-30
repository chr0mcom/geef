using Geef.Sdk.Exceptions;

namespace Geef.Sdk.Middleware;

/// <summary>
/// Middleware that catches exceptions and converts them to structured errors.
/// </summary>
public sealed class ExceptionHandlingMiddleware : IGeefMiddleware
{
    /// <inheritdoc />
    public async Task InvokeAsync(GeefMiddlewareContext ctx, Func<Task> next)
    {
        try
        {
            await next();
        }
        catch (GeefException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ProviderException(
                $"Unhandled exception in phase {ctx.Phase}: {ex.Message}",
                ex,
                ctx.RunId)
            {
                Phase = ctx.Phase,
                ProviderName = ctx.ComponentName
            };
        }
    }
}
