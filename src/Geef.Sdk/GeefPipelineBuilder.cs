using Geef.Sdk.Events;
using Geef.Sdk.Exceptions;
using Geef.Sdk.Middleware;
using Geef.Sdk.Policies;
using Geef.Sdk.Providers;

namespace Geef.Sdk;

/// <summary>
/// Fluent builder for pipeline configuration.
/// Separates definition (builder) from execution (runner).
/// </summary>
/// <typeparam name="TOutput">The output type of the pipeline.</typeparam>
public sealed class GeefPipelineBuilder<TOutput>
{
    internal IGroundingStep? Grounding { get; private set; }
    internal IExecutionStep? Execution { get; private set; }
    internal List<IReviewer> Reviewers { get; } = new();
    internal IFinalizer<TOutput>? Finalizer { get; private set; }
    internal IConvergencePolicy ConvergencePolicy { get; private set; } = new DefaultConvergencePolicy();
    internal IEvaluationStrategy EvaluationStrategy { get; private set; } = new SequentialEvaluationStrategy();
    internal List<IGeefMiddleware> Middlewares { get; } = new();
    internal List<IGeefEventSink> EventSinks { get; } = new();

    /// <summary>Sets the grounding step.</summary>
    public GeefPipelineBuilder<TOutput> UseGrounding(IGroundingStep grounding)
    { Grounding = grounding ?? throw new ArgumentNullException(nameof(grounding)); return this; }

    /// <summary>Sets the execution step.</summary>
    public GeefPipelineBuilder<TOutput> UseExecution(IExecutionStep execution)
    { Execution = execution ?? throw new ArgumentNullException(nameof(execution)); return this; }

    /// <summary>Adds a reviewer to the pipeline.</summary>
    public GeefPipelineBuilder<TOutput> AddReviewer(IReviewer reviewer)
    { Reviewers.Add(reviewer ?? throw new ArgumentNullException(nameof(reviewer))); return this; }

    /// <summary>Sets the finalizer.</summary>
    public GeefPipelineBuilder<TOutput> UseFinalizer(IFinalizer<TOutput> finalizer)
    { Finalizer = finalizer ?? throw new ArgumentNullException(nameof(finalizer)); return this; }

    /// <summary>Sets the convergence policy (default: <see cref="DefaultConvergencePolicy"/>).</summary>
    public GeefPipelineBuilder<TOutput> UseConvergencePolicy(IConvergencePolicy policy)
    { ConvergencePolicy = policy ?? throw new ArgumentNullException(nameof(policy)); return this; }

    /// <summary>Sets the evaluation strategy (default: <see cref="SequentialEvaluationStrategy"/>).</summary>
    public GeefPipelineBuilder<TOutput> UseEvaluationStrategy(IEvaluationStrategy strategy)
    { EvaluationStrategy = strategy ?? throw new ArgumentNullException(nameof(strategy)); return this; }

    /// <summary>Adds a middleware to the pipeline.</summary>
    public GeefPipelineBuilder<TOutput> UseMiddleware(IGeefMiddleware middleware)
    { Middlewares.Add(middleware ?? throw new ArgumentNullException(nameof(middleware))); return this; }

    /// <summary>Adds a middleware by type (must have a parameterless constructor).</summary>
    public GeefPipelineBuilder<TOutput> UseMiddleware<TMiddleware>() where TMiddleware : IGeefMiddleware, new()
    { Middlewares.Add(new TMiddleware()); return this; }

    /// <summary>Adds an event sink.</summary>
    public GeefPipelineBuilder<TOutput> AddEventSink(IGeefEventSink sink)
    { EventSinks.Add(sink ?? throw new ArgumentNullException(nameof(sink))); return this; }

    /// <summary>Configures a <see cref="DelegateEventSink"/> with delegate hooks and adds it.</summary>
    public GeefPipelineBuilder<TOutput> ConfigureEvents(Action<DelegateEventSink> configure)
    {
        var sink = new DelegateEventSink();
        configure(sink);
        EventSinks.Add(sink);
        return this;
    }

    /// <summary>
    /// Validates the configuration and creates an executable pipeline runner.
    /// Throws <see cref="PipelineConfigurationException"/> if required components are missing.
    /// </summary>
    public GeefPipelineRunner<TOutput> Build()
    {
        if (Grounding is null)
            throw new PipelineConfigurationException("Grounding step is required. Call UseGrounding().");
        if (Execution is null)
            throw new PipelineConfigurationException("Execution step is required. Call UseExecution().");
        if (Finalizer is null)
            throw new PipelineConfigurationException("Finalizer is required. Call UseFinalizer().");
        if (Reviewers.Count == 0)
            throw new PipelineConfigurationException("At least one reviewer is required. Call AddReviewer().");

        return new GeefPipelineRunner<TOutput>(this);
    }
}
