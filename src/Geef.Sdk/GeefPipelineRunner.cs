using System.Diagnostics;
using Geef.Sdk.Advisors;
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
    private readonly IReadOnlyList<IAdvisor> _advisors;
    private readonly IReadOnlyList<(IAdvisor Advisor, AdvisorTrigger Trigger)> _triggeredAdvisors;
    private readonly IAdvisorPolicy _advisorPolicy;
    private readonly AdvisorBudget _advisorBudgetTemplate;
    private readonly bool _bestEffortOnNonConvergence;

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
        _advisors = builder.Advisors.ToList();
        _triggeredAdvisors = builder.TriggeredAdvisors.ToList();
        _advisorPolicy = builder.AdvisorPolicy;
        _advisorBudgetTemplate = builder.AdvisorBudget;
        _bestEffortOnNonConvergence = builder.BestEffortOnNonConvergence;
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

        // All advisors (provider-driven + runner-triggered) share the same orchestrator
        // so budget, policy, provenance, and events are tracked uniformly.
        var allAdvisors = _advisors.Concat(_triggeredAdvisors.Select(t => t.Advisor)).ToList();
        var orchestrator = new AdvisorOrchestrator(
            allAdvisors, _advisorPolicy, _advisorBudgetTemplate, _eventSink, runId);
        orchestrator.SetCurrentPhase(GeefPhase.Grounding, null);

        var advisorAware = new List<IAdvisorAware>();
        if (_grounding is IAdvisorAware ga) advisorAware.Add(ga);
        if (_execution is IAdvisorAware ea) advisorAware.Add(ea);
        foreach (var r in _reviewers) if (r is IAdvisorAware ra) advisorAware.Add(ra);
        if (_finalizer is IAdvisorAware fa) advisorAware.Add(fa);
        foreach (var aware in advisorAware) aware.SetAdvisorOrchestrator(orchestrator);

        try
        {

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
                var ctx = MakeContext(GeefPhase.Grounding, new RunContext(), runId, null, cancellationToken);
                groundingResult = await RunWithMiddlewareAsync(ctx,
                    () => _grounding.RunAsync(input ?? string.Empty, ctx.CancellationToken));
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or GeefException))
            {
                await PublishPipelineFailedAsync(runId, ConvergenceDecision.Continue, 0, new IterationHistory(), cancellationToken);
                throw new ProviderException(
                    $"Grounding step failed: {ex.Message}", ex, runId)
                {
                    Phase = GeefPhase.Grounding,
                    ProviderName = _grounding.GetType().Name
                };
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
        var recoveryAttempted = false;

        while (convergenceDecision == ConvergenceDecision.Continue)
        {
            var iteration = context.GetRequired<int>(GeefKeys.CurrentIteration) + 1;
            context = context.Set(GeefKeys.CurrentIteration, iteration);

            orchestrator.StartIteration(iteration);
            orchestrator.SetCurrentPhase(GeefPhase.Execution, iteration);

            // Consult runner-triggered pre-execution advisors and inject GeefKeys.AdvisorContext.
            context = await ConsultPreExecutionAdvisorsAsync(context, iteration, orchestrator, cancellationToken);

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
                    var ctx = MakeContext(GeefPhase.Execution, context, runId, iteration, cancellationToken);
                    var capturedContext = context;
                    executionResult = await RunWithMiddlewareAsync(ctx,
                        () => _execution.RunAsync(capturedContext, ctx.CancellationToken));
                }
                catch (Exception ex) when (ex is not (OperationCanceledException or GeefException))
                {
                    await PublishPipelineFailedAsync(runId, ConvergenceDecision.Continue, iteration, iterationHistory, cancellationToken);
                    throw new ProviderException(
                        $"Execution step failed in iteration {iteration}: {ex.Message}", ex, runId)
                    {
                        Phase = GeefPhase.Execution,
                        ProviderName = _execution.GetType().Name
                    };
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
            orchestrator.SetCurrentPhase(GeefPhase.Evaluation, iteration);

            EvaluationAggregate aggregate;
            try
            {
                aggregate = await RunEvaluationAsync(runId, iteration, context, cancellationToken);
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or GeefException))
            {
                await PublishPipelineFailedAsync(runId, ConvergenceDecision.Continue, iteration, iterationHistory, cancellationToken);
                throw new ProviderException(
                    $"Evaluation strategy failed in iteration {iteration}: {ex.Message}", ex, runId)
                {
                    Phase = GeefPhase.Evaluation,
                    ProviderName = _evaluationStrategy.GetType().Name
                };
            }

            lastAggregate = aggregate;

            var record = new IterationRecord
            {
                Iteration = iteration,
                StartedAt = iterationStart,
                ExecutionDuration = execSw.Elapsed,
                EvaluationResult = aggregate,
                Context = _bestEffortOnNonConvergence ? context : null
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
                // If OnConvergenceFailure advisors are registered and recovery has not been tried yet,
                // consult them, inject their guidance, and allow exactly ONE recovery pass.
                var onFailureAdvisors = _triggeredAdvisors
                    .Where(t => t.Trigger == AdvisorTrigger.OnConvergenceFailure)
                    .ToList();

                if (!recoveryAttempted && onFailureAdvisors.Count > 0)
                {
                    recoveryAttempted = true;
                    orchestrator.SetCurrentPhase(GeefPhase.Execution, iteration);
                    context = await ConsultOnConvergenceFailureAdvisorsAsync(
                        context, iteration, onFailureAdvisors, orchestrator, cancellationToken);
                    // Re-enter the loop for one recovery pass.
                    convergenceDecision = ConvergenceDecision.Continue;
                }
                else if (_bestEffortOnNonConvergence)
                {
                    // Best-effort mode: finalize the best available iteration instead of throwing.
                    await PublishPipelineFailedAsync(runId, convergenceDecision, iteration, iterationHistory, cancellationToken);

                    var bestRecord = SelectBestIteration(iterationHistory);
                    var bestContext = bestRecord.Context ?? context;

                    orchestrator.SetCurrentPhase(GeefPhase.Finalize, null);
                    var bestEffortFinalize = await RunBestEffortFinalizeAsync(runId, bestContext, cancellationToken);

                    sw.Stop();
                    await _eventSink.PublishAsync(
                        new PipelineCompletedEvent(runId, false, iterationHistory.Count, sw.Elapsed, DateTimeOffset.UtcNow), cancellationToken);

                    return new GeefPipelineResult<TOutput>
                    {
                        Output           = bestEffortFinalize.Output,
                        RunId            = runId,
                        TotalIterations  = iterationHistory.Count,
                        TotalDuration    = sw.Elapsed,
                        Success          = false,
                        StopReason       = convergenceDecision,
                        DegradedIterations = CountDegradedIterations(iterationHistory),
                        FinalContext     = bestEffortFinalize.FinalContext,
                        History          = iterationHistory,
                        AdvisorConsultations = orchestrator.Provenance.Consultations,
                        AdvisorAttributions  = orchestrator.Provenance.Attributions,
                    };
                }
                else
                {
                    await PublishPipelineFailedAsync(runId, convergenceDecision, iteration, iterationHistory, cancellationToken);

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
        }

        // 4. FINALIZE
        orchestrator.SetCurrentPhase(GeefPhase.Finalize, null);

        await _eventSink.PublishAsync(
            new FinalizeStartedEvent(runId, DateTimeOffset.UtcNow), cancellationToken);

        var finalizeSw = Stopwatch.StartNew();
        FinalizeResult<TOutput> finalizeResult;

        using (var finalizeActivity = GeefDiagnostics.ActivitySource.StartActivity("geef.finalize"))
        {
            finalizeActivity?.SetTag("geef.run_id", runId);
            try
            {
                var ctx = MakeContext(GeefPhase.Finalize, context, runId, null, cancellationToken);
                var capturedContext = context;
                finalizeResult = await RunWithMiddlewareAsync(ctx,
                    () => _finalizer.FinalizeAsync(capturedContext, ctx.CancellationToken));
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or GeefException))
            {
                await PublishPipelineFailedAsync(runId, ConvergenceDecision.Continue, iterationHistory.Count, iterationHistory, cancellationToken);
                throw new ProviderException(
                    $"Finalizer failed: {ex.Message}", ex, runId)
                {
                    Phase = GeefPhase.Finalize,
                    ProviderName = _finalizer.GetType().Name
                };
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
            Output             = finalizeResult.Output,
            RunId              = runId,
            TotalIterations    = iterationHistory.Count,
            TotalDuration      = sw.Elapsed,
            FinalContext       = finalizeResult.FinalContext,
            History            = iterationHistory,
            Success            = true,
            DegradedIterations = CountDegradedIterations(iterationHistory),
            AdvisorConsultations = orchestrator.Provenance.Consultations,
            AdvisorAttributions  = orchestrator.Provenance.Attributions,
        };

        }
        finally
        {
            foreach (var aware in advisorAware) aware.SetAdvisorOrchestrator(null);
        }
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

        var ctx = MakeContext(GeefPhase.Evaluation, context, runId, iteration, cancellationToken);
        return await RunWithMiddlewareAsync(ctx,
            () => _evaluationStrategy.ExecuteAsync(instrumentedReviewers, context, ctx.CancellationToken));
    }

    private async Task<TResult> RunWithMiddlewareAsync<TResult>(
        GeefMiddlewareContext ctx,
        Func<Task<TResult>> operation)
    {
        if (_middlewares.Count == 0)
            return await operation();

        TResult result = default!;

        Func<Task> core = async () => { result = await operation(); };
        Func<Task> chain = core;

        for (var i = _middlewares.Count - 1; i >= 0; i--)
        {
            var mw = _middlewares[i];
            var next = chain;
            chain = () => mw.InvokeAsync(ctx, next);
        }

        await chain();
        return result;
    }

    private static GeefMiddlewareContext MakeContext(
        GeefPhase phase,
        IRunContext runContext,
        string runId,
        int? iteration,
        CancellationToken cancellationToken)
        => new GeefMiddlewareContext
        {
            Phase = phase,
            RunContext = runContext,
            RunId = runId,
            Iteration = iteration,
            CancellationToken = cancellationToken
        };

    private async Task<IRunContext> ConsultPreExecutionAdvisorsAsync(
        IRunContext context,
        int iteration,
        AdvisorOrchestrator orchestrator,
        CancellationToken cancellationToken)
    {
        var active = _triggeredAdvisors
            .Where(t => t.Trigger == AdvisorTrigger.BeforeEveryExecution
                     || (t.Trigger == AdvisorTrigger.BeforeFirstExecution && iteration == 1))
            .ToList();

        if (active.Count == 0) return context;

        return await BuildAdvisorContextAsync(
            context, active, orchestrator,
            new AdvisorQuery { Question = "Pre-execution guidance requested.", Character = AdvisorQueryCharacter.DecisionSupport },
            cancellationToken);
    }

    private async Task<IRunContext> ConsultOnConvergenceFailureAdvisorsAsync(
        IRunContext context,
        int iteration,
        IReadOnlyList<(IAdvisor Advisor, AdvisorTrigger Trigger)> advisors,
        AdvisorOrchestrator orchestrator,
        CancellationToken cancellationToken)
    {
        return await BuildAdvisorContextAsync(
            context, advisors, orchestrator,
            new AdvisorQuery { Question = "Convergence failure: recovery guidance requested.", Character = AdvisorQueryCharacter.Diagnostic },
            cancellationToken);
    }

    private static async Task<IRunContext> BuildAdvisorContextAsync(
        IRunContext context,
        IReadOnlyList<(IAdvisor Advisor, AdvisorTrigger Trigger)> advisors,
        AdvisorOrchestrator orchestrator,
        AdvisorQuery query,
        CancellationToken cancellationToken)
    {
        var outputs = new List<string>(advisors.Count);

        foreach (var (advisor, _) in advisors)
        {
            AdvisorResponse response;
            try
            {
                response = await orchestrator.ConsultAsync(advisor.Name, query, context, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                continue;
            }

            if (response.Outcome == AdvisorOutcome.Success && !string.IsNullOrWhiteSpace(response.AdviceText))
                outputs.Add($"## {advisor.Name}\n{response.AdviceText}");
        }

        if (outputs.Count == 0) return context;

        var block = $"[Advisor consultations]\n\n{string.Join("\n\n", outputs)}\n\n[End of advisor consultations]";
        return context.Set(GeefKeys.AdvisorContext, block);
    }

    private async Task<FinalizeResult<TOutput>> RunBestEffortFinalizeAsync(
        string runId,
        IRunContext bestContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var ctx = MakeContext(GeefPhase.Finalize, bestContext, runId, null, cancellationToken);
            return await RunWithMiddlewareAsync(ctx,
                () => _finalizer.FinalizeAsync(bestContext, ctx.CancellationToken));
        }
        catch
        {
            // If the best-effort finalize itself fails, run without middleware so we always
            // return something rather than throwing in the non-throw path.
            return await _finalizer.FinalizeAsync(bestContext, CancellationToken.None);
        }
    }

    /// <summary>
    /// Selects the best iteration from history for best-effort finalization:
    /// fewest <see cref="ReviewDecision.Rejected"/> reviews (most progress toward convergence),
    /// then most recent iteration as a tiebreaker.
    /// </summary>
    private static IterationRecord SelectBestIteration(IterationHistory history)
        => history.Records
            .OrderBy(r => r.EvaluationResult.Reviews.Count(rv => rv.Decision == ReviewDecision.Rejected))
            .ThenByDescending(r => r.Iteration)
            .First();

    private static int CountDegradedIterations(IterationHistory history)
        => history.Records.Count(r => r.EvaluationResult.HasFailedReviewers);

    private async Task PublishPipelineFailedAsync(
        string runId,
        ConvergenceDecision reason,
        int totalIterations,
        IterationHistory history,
        CancellationToken cancellationToken)
    {
        try
        {
            await _eventSink.PublishAsync(
                new PipelineFailedEvent(runId, reason, totalIterations, history, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch
        {
            // Swallow event-sink errors during failure path
        }
    }
}
