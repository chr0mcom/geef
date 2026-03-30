using System.Diagnostics;
using Geef.Sdk.Context;
using Geef.Sdk.Diagnostics;
using Geef.Sdk.Events;
using Geef.Sdk.Exceptions;
using Geef.Sdk.Middleware;
using Geef.Sdk.Policies;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;
using Geef.Sdk.Runtime;

namespace Geef.Sdk;

/// <summary>
/// The orchestrator. Executes the GEEF pipeline: Grounding → [Execution ↔ Evaluation loop] → Finalize.
/// Immutable after Build() and thread-safe — can be reused for multiple concurrent runs.
/// </summary>
/// <typeparam name="TOutput">The output type of the pipeline.</typeparam>
public sealed class GeefPipelineRunner<TOutput>
{
    private readonly IGroundingStep _grounding;
    private readonly IExecutionStep _execution;
    private readonly IReadOnlyList<IReviewer> _reviewers;
    private readonly IFinalizer<TOutput> _finalizer;
    private readonly IConvergencePolicy _convergencePolicy;
    private readonly IEvaluationStrategy _evaluationStrategy;
    private readonly IReadOnlyList<IGeefMiddleware> _middlewares;
    private readonly IGeefEventSink _eventSink;

    internal GeefPipelineRunner(GeefPipelineBuilder<TOutput> builder)
    {
        _grounding = builder.Grounding!;
        _execution = builder.Execution!;
        _reviewers = builder.Reviewers.ToList();
        _finalizer = builder.Finalizer!;
        _convergencePolicy = builder.ConvergencePolicy;
        _evaluationStrategy = builder.EvaluationStrategy;
        _middlewares = builder.Middlewares.ToList();
        _eventSink = builder.EventSinks.Count switch
        {
            0 => new NullEventSink(),
            1 => builder.EventSinks[0],
            _ => new CompositeEventSink(builder.EventSinks)
        };
    }

    /// <summary>
    /// Executes the full GEEF pipeline.
    /// </summary>
    /// <param name="input">The initial input (e.g. user prompt).</param>
    /// <param name="cancellationToken">Cancellation token for the entire run.</param>
    /// <returns>The result of the finalize phase.</returns>
    /// <exception cref="ConvergenceFailedException">If the loop does not converge per the ConvergencePolicy.</exception>
    /// <exception cref="PhaseTimeoutException">If a phase timeout is exceeded.</exception>
    /// <exception cref="ProviderException">If a provider raises an infrastructure error.</exception>
    public async Task<GeefPipelineResult<TOutput>> RunAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        var runId = Guid.NewGuid().ToString("N")[..12];
        var sw = Stopwatch.StartNew();

        using var rootActivity = GeefDiagnostics.ActivitySource.StartActivity("geef.pipeline.run");
        rootActivity?.SetTag("geef.run_id", runId);
        rootActivity?.SetTag("geef.input_length", input?.Length ?? 0);

        await _eventSink.PublishAsync(
            new PipelineStartedEvent(runId, input ?? string.Empty, DateTimeOffset.UtcNow), cancellationToken);

        // 1. GROUNDING
        await _eventSink.PublishAsync(
            new GroundingStartedEvent(runId, DateTimeOffset.UtcNow), cancellationToken);

        var groundingSw = Stopwatch.StartNew();
        GroundingResult groundingResult;
        using (var groundingActivity = GeefDiagnostics.ActivitySource.StartActivity("geef.grounding"))
        {
            groundingActivity?.SetTag("geef.run_id", runId);
            try
            {
                groundingResult = await _grounding.RunAsync(input ?? string.Empty, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new ProviderException(
                    $"Grounding step failed: {ex.Message}", ex, runId)
                { Phase = GeefPhase.Grounding };
            }
        }
        groundingSw.Stop();

        var context = groundingResult.Context
            .Set(GeefKeys.OriginalInput, input ?? string.Empty)
            .Set(GeefKeys.RunId, runId)
            .Set(GeefKeys.RunStartedAt, DateTimeOffset.UtcNow)
            .Set(GeefKeys.CurrentIteration, 0);

        await _eventSink.PublishAsync(
            new GroundingCompletedEvent(runId, groundingResult, groundingSw.Elapsed, DateTimeOffset.UtcNow), cancellationToken);

        // 2 & 3. EXECUTION ↔ EVALUATION LOOP
        var iterationHistory = new IterationHistory();
        context = context.Set(GeefKeys.IterationHistory, iterationHistory);

        var convergenceDecision = ConvergenceDecision.Continue;
        EvaluationAggregate? lastAggregate = null;

        while (convergenceDecision == ConvergenceDecision.Continue)
        {
            var iteration = context.GetRequired<int>(GeefKeys.CurrentIteration) + 1;
            context = context.Set(GeefKeys.CurrentIteration, iteration);

            var iterationStart = DateTimeOffset.UtcNow;

            using var iterActivity = GeefDiagnostics.ActivitySource.StartActivity("geef.iteration");
            iterActivity?.SetTag("geef.run_id", runId);
            iterActivity?.SetTag("geef.iteration", iteration);

            // --- Execution ---
            await _eventSink.PublishAsync(
                new ExecutionStartedEvent(runId, iteration, DateTimeOffset.UtcNow), cancellationToken);

            var execSw = Stopwatch.StartNew();
            ExecutionResult executionResult;

            using (var execActivity = GeefDiagnostics.ActivitySource.StartActivity("geef.execution"))
            {
                execActivity?.SetTag("geef.run_id", runId);
                execActivity?.SetTag("geef.iteration", iteration);
                try
                {
                    executionResult = await _execution.RunAsync(context, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    throw new ProviderException(
                        $"Execution step failed in iteration {iteration}: {ex.Message}", ex, runId)
                    { Phase = GeefPhase.Execution, ProviderName = "Execution" };
                }
            }
            execSw.Stop();

            context = executionResult.UpdatedContext
                .Set(GeefKeys.CurrentIteration, iteration)
                .Set(GeefKeys.RunId, runId)
                .Set(GeefKeys.IterationHistory, iterationHistory);

            await _eventSink.PublishAsync(
                new ExecutionCompletedEvent(runId, iteration, executionResult, execSw.Elapsed, DateTimeOffset.UtcNow), cancellationToken);

            // --- Evaluation ---
            EvaluationAggregate aggregate;
            try
            {
                aggregate = await RunEvaluationAsync(runId, iteration, context, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new ProviderException(
                    $"Evaluation strategy failed in iteration {iteration}: {ex.Message}", ex, runId)
                { Phase = GeefPhase.Evaluation };
            }

            lastAggregate = aggregate;

            var record = new IterationRecord
            {
                Iteration = iteration,
                StartedAt = iterationStart,
                ExecutionDuration = execSw.Elapsed,
                EvaluationResult = aggregate
            };
            iterationHistory.Add(record);

            convergenceDecision = _convergencePolicy.Evaluate(iterationHistory, aggregate, sw.Elapsed);

            if (convergenceDecision == ConvergenceDecision.Approved)
            {
                await _eventSink.PublishAsync(
                    new EvaluationApprovedEvent(runId, iteration, aggregate, DateTimeOffset.UtcNow), cancellationToken);
            }
            else if (convergenceDecision == ConvergenceDecision.Continue)
            {
                context = context.Set(GeefKeys.PreviousFindings, aggregate.AllFindings);
                await _eventSink.PublishAsync(
                    new EvaluationRejectedEvent(runId, iteration, aggregate, convergenceDecision, DateTimeOffset.UtcNow), cancellationToken);
            }
            else
            {
                await _eventSink.PublishAsync(
                    new PipelineFailedEvent(runId, convergenceDecision, iteration, iterationHistory, DateTimeOffset.UtcNow), cancellationToken);

                throw new ConvergenceFailedException(
                    $"Pipeline did not converge after {iteration} iterations. Reason: {convergenceDecision}.",
                    runId)
                {
                    Reason = convergenceDecision,
                    History = iterationHistory,
                    LastEvaluation = aggregate
                };
            }
        }

        // 4. FINALIZE
        await _eventSink.PublishAsync(
            new FinalizeStartedEvent(runId, DateTimeOffset.UtcNow), cancellationToken);

        var finalizeSw = Stopwatch.StartNew();
        FinalizeResult<TOutput> finalizeResult;

        using (var finalizeActivity = GeefDiagnostics.ActivitySource.StartActivity("geef.finalize"))
        {
            finalizeActivity?.SetTag("geef.run_id", runId);
            try
            {
                finalizeResult = await _finalizer.FinalizeAsync(context, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new ProviderException(
                    $"Finalizer failed: {ex.Message}", ex, runId)
                { Phase = GeefPhase.Finalize };
            }
        }
        finalizeSw.Stop();

        await _eventSink.PublishAsync(
            new FinalizeCompletedEvent(runId, finalizeSw.Elapsed, DateTimeOffset.UtcNow), cancellationToken);

        sw.Stop();

        await _eventSink.PublishAsync(
            new PipelineCompletedEvent(runId, true, iterationHistory.Count, sw.Elapsed, DateTimeOffset.UtcNow), cancellationToken);

        return new GeefPipelineResult<TOutput>
        {
            Output = finalizeResult.Output,
            RunId = runId,
            TotalIterations = iterationHistory.Count,
            TotalDuration = sw.Elapsed,
            FinalContext = finalizeResult.FinalContext,
            History = iterationHistory,
            Success = true
        };
    }

    private async Task<EvaluationAggregate> RunEvaluationAsync(
        string runId,
        int iteration,
        IRunContext context,
        CancellationToken cancellationToken)
    {
        using var evalActivity = GeefDiagnostics.ActivitySource.StartActivity("geef.evaluation");
        evalActivity?.SetTag("geef.run_id", runId);
        evalActivity?.SetTag("geef.iteration", iteration);

        var instrumentedReviewers = _reviewers
            .Select(r => new InstrumentedReviewer(r, runId, iteration, _eventSink))
            .ToList<IReviewer>();

        return await _evaluationStrategy.ExecuteAsync(instrumentedReviewers, context, cancellationToken);
    }
}
