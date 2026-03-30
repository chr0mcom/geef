using Microsoft.Extensions.DependencyInjection;

namespace Geef.Sdk.Hosting;

/// <summary>
/// Extension methods for integrating GEEF pipelines into Microsoft.Extensions.DependencyInjection.
/// </summary>
public static class GeefServiceCollectionExtensions
{
    /// <summary>
    /// Registers a GEEF pipeline in the DI container.
    /// </summary>
    public static IServiceCollection AddGeefPipeline<TOutput>(
        this IServiceCollection services,
        Action<GeefPipelineBuilder<TOutput>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new GeefPipelineBuilder<TOutput>();
        configure(builder);
        var runner = builder.Build();
        services.AddSingleton(runner);
        return services;
    }

    /// <summary>
    /// Registers a GEEF pipeline with access to the service provider (for DI-resolved providers).
    /// </summary>
    public static IServiceCollection AddGeefPipeline<TOutput>(
        this IServiceCollection services,
        Action<IServiceProvider, GeefPipelineBuilder<TOutput>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        services.AddSingleton(sp =>
        {
            var builder = new GeefPipelineBuilder<TOutput>();
            configure(sp, builder);
            return builder.Build();
        });
        return services;
    }
}
