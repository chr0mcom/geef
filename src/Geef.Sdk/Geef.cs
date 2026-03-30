namespace Geef.Sdk;

/// <summary>
/// Static entry point for creating GEEF pipelines.
/// </summary>
public static class Geef
{
    /// <summary>
    /// Creates a new pipeline builder for the specified output type.
    /// </summary>
    /// <typeparam name="TOutput">The output type produced by the finalizer.</typeparam>
    public static GeefPipelineBuilder<TOutput> CreatePipeline<TOutput>() => new();
}
