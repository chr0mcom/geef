using System.Diagnostics;
using Geef.Sdk.Context;
using Geef.Sdk.Diagnostics;
using Geef.Sdk.Events;
using Geef.Sdk.Middleware;
using Geef.Sdk.Policies;

namespace Geef.Sdk.Advisors;

/// <summary>
/// Default implementation of <see cref="IAdvisorOrchestrator"/>. Wraps the raw
/// <see cref="IAdvisor.ConsultAsync"/> call with budget enforcement, policy enforcement,
/// provenance logging, event publishing and tracing. One instance per pipeline run.
/// </summary>
public sealed class AdvisorOrchestrator : IAdvisorOrchestrator
{
    // Event / provenance / budget semantics per outcome:
    // | Outcome                    | Advisor invoked | Provenance | Started event | Completed event | Budget counted |
    // | No advisor registered      |       no        |     no     |      no       |       no        |       no       |
    // | BudgetExceeded             |       no        |    yes     |      no       |      yes        |       no       |
    // | NoApplicableAdvice (policy)|       no        |    yes     |      no       |      yes        |       no       |
    // | Success                    |      yes        |    yes     |     yes       |      yes        |      yes       |
    // | InfrastructureFailure      |      yes        |    yes     |     yes       |      yes        |      yes       |

    private const int DefaultEstimatedTokens = 1000;

    private readonly Dictionary<string, IAdvisor> _advisorsByName;
    private readonly IAdvisor? _defaultAdvisor;
    private readonly IAdvisorPolicy _policy;
    private readonly AdvisorBudgetState _budgetState;
    private readonly AdvisorProvenanceLog _provenance;
    private readonly IGeefEventSink _eventSink;
    private readonly string _runId;

    private GeefPhase _currentPhase;
    private int? _currentIteration;
    private readonly object _phaseLock = new();

    /// <summary>Creates a new orchestrator for a single pipeline run.</summary>
    /// <param name="advisors">The registered advisors (may be empty).</param>
    /// <param name="policy">The consultation policy.</param>
    /// <param name="budget">The advisor budget (will be cloned internally).</param>
    /// <param name="eventSink">The event sink used for advisor events.</param>
    /// <param name="runId">The run id for event / tracing correlation.</param>
    /// <exception cref="ArgumentException">If two advisors share the same <see cref="IAdvisor.Name"/>.</exception>
    public AdvisorOrchestrator(
        IReadOnlyList<IAdvisor> advisors,
        IAdvisorPolicy policy,
        AdvisorBudget budget,
        IGeefEventSink eventSink,
        string runId)
    {
        ArgumentNullException.ThrowIfNull(advisors);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(eventSink);
        ArgumentException.ThrowIfNullOrEmpty(runId);

        // Defensive: the builder validates name uniqueness, but the orchestrator
        // is also constructed in tests and may receive lists from other paths.
        // Fail with a clear message instead of a generic ToDictionary exception.
        var duplicate = advisors
            .GroupBy(a => a.Name, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException(
                $"Duplicate advisor name '{duplicate.Key}'. Advisor names must be unique.",
                nameof(advisors));

        _advisorsByName = advisors.ToDictionary(a => a.Name, StringComparer.Ordinal);
        _defaultAdvisor = advisors.Count > 0 ? advisors[0] : null;
        _policy = policy;
        _budgetState = new AdvisorBudgetState(budget.Clone());
        _provenance = new AdvisorProvenanceLog();
        _eventSink = eventSink;
        _runId = runId;
        _currentPhase = GeefPhase.Grounding;
        _currentIteration = null;
    }

    /// <inheritdoc />
    public bool HasAdvisor => _defaultAdvisor is not null;

    /// <inheritdoc />
    public IAdvisorProvenanceLog Provenance => _provenance;

    /// <summary>The runtime budget state (exposed for the runner/tests).</summary>
    public AdvisorBudgetState BudgetState => _budgetState;

    /// <summary>
    /// Called by the runner before each phase to keep phase/iteration tracking accurate.
    /// Not part of <see cref="IAdvisorOrchestrator"/> — providers should not call this.
    /// </summary>
    /// <param name="phase">The phase that is about to run.</param>
    /// <param name="iteration">The iteration number, or null for Grounding/Finalize.</param>
    public void SetCurrentPhase(GeefPhase phase, int? iteration)
    {
        lock (_phaseLock) { _currentPhase = phase; _currentIteration = iteration; }
    }

    /// <summary>
    /// Called by the runner at the start of each new iteration (also resets the per-iteration counter).
    /// Not part of <see cref="IAdvisorOrchestrator"/> — providers should not call this.
    /// </summary>
    /// <param name="iteration">The iteration number that is about to start.</param>
    public void StartIteration(int iteration)
    {
        lock (_phaseLock) _currentIteration = iteration;
        _budgetState.StartIteration(iteration);
    }

    /// <inheritdoc />
    public Task<AdvisorResponse> ConsultAsync(
        AdvisorQuery query,
        IRunContext context,
        CancellationToken cancellationToken = default)
        => ConsultInternalAsync(_defaultAdvisor, query, context, cancellationToken);

    /// <inheritdoc />
    public Task<AdvisorResponse> ConsultAsync(
        string advisorName,
        AdvisorQuery query,
        IRunContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(advisorName);
        _advisorsByName.TryGetValue(advisorName, out var advisor);
        return ConsultInternalAsync(advisor, query, context, cancellationToken);
    }

    private async Task<AdvisorResponse> ConsultInternalAsync(
        IAdvisor? advisor,
        AdvisorQuery query,
        IRunContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);

        // Step 1 — advisor resolution. If none, synthetic NoApplicableAdvice,
        // NO provenance and NO event (nothing ever happened).
        if (advisor is null)
        {
            return new AdvisorResponse
            {
                AdviceText = "No advisor was available for this consultation.",
                Confidence = AdvisorConfidence.Uncertain,
                Outcome = AdvisorOutcome.NoApplicableAdvice,
            };
        }

        // Step 2 — snapshot phase/iteration under the lock.
        GeefPhase phase;
        int? iteration;
        lock (_phaseLock)
        {
            phase = _currentPhase;
            iteration = _currentIteration;
        }

        // Step 3 — consultation id + token estimate.
        var consultationId = Guid.NewGuid().ToString("N")[..12];
        var estimatedTokens = query.MaxTokens ?? DefaultEstimatedTokens;
        var now = DateTimeOffset.UtcNow;

        // Step 4 — budget check.
        if (_budgetState.WouldExceedBudget(estimatedTokens))
        {
            var budgetResponse = new AdvisorResponse
            {
                AdviceText = "Advisor consultation was not invoked because the budget cap was reached.",
                Confidence = AdvisorConfidence.Uncertain,
                Outcome = AdvisorOutcome.BudgetExceeded,
                AdvisorName = advisor.Name,
                ConsultationId = consultationId,
            };

            _provenance.RecordConsultation(new AdvisorConsultationRecord
            {
                ConsultationId = consultationId,
                AdvisorName = advisor.Name,
                Phase = phase,
                Iteration = iteration,
                Query = query,
                Response = budgetResponse,
                Timestamp = now,
            });

            await _eventSink.PublishAsync(
                new AdvisorConsultationCompletedEvent(
                    _runId, iteration, phase, advisor.Name, consultationId, budgetResponse, DateTimeOffset.UtcNow),
                cancellationToken);

            return budgetResponse;
        }

        // Step 5 — policy check.
        var policyContext = new AdvisorConsultationContext
        {
            Phase = phase,
            Iteration = iteration,
            Advisor = advisor,
            Query = query,
            ConsultationsSoFar = _budgetState.ConsultationsTotal,
            ConsultationsInThisIteration = _budgetState.ConsultationsThisIteration,
        };

        if (!_policy.IsConsultationAllowed(policyContext))
        {
            var policyResponse = new AdvisorResponse
            {
                AdviceText = "Advisor consultation was blocked by the configured policy.",
                Confidence = AdvisorConfidence.Uncertain,
                Outcome = AdvisorOutcome.NoApplicableAdvice,
                AdvisorName = advisor.Name,
                ConsultationId = consultationId,
            };

            _provenance.RecordConsultation(new AdvisorConsultationRecord
            {
                ConsultationId = consultationId,
                AdvisorName = advisor.Name,
                Phase = phase,
                Iteration = iteration,
                Query = query,
                Response = policyResponse,
                Timestamp = now,
            });

            await _eventSink.PublishAsync(
                new AdvisorConsultationCompletedEvent(
                    _runId, iteration, phase, advisor.Name, consultationId, policyResponse, DateTimeOffset.UtcNow),
                cancellationToken);

            return policyResponse;
        }

        // Step 6 — tracing span.
        using var activity = GeefDiagnostics.ActivitySource.StartActivity("geef.advisor.consult");
        activity?.SetTag("geef.advisor.name", advisor.Name);
        activity?.SetTag("geef.advisor.kind", advisor.Kind.ToString());
        activity?.SetTag("geef.phase", phase.ToString());
        activity?.SetTag("geef.run_id", _runId);
        activity?.SetTag("geef.consultation_id", consultationId);

        // Step 7 — Started event.
        await _eventSink.PublishAsync(
            new AdvisorConsultationStartedEvent(
                _runId, iteration, phase, advisor.Name, consultationId, query, DateTimeOffset.UtcNow),
            cancellationToken);

        // Step 8 — actual advisor invocation.
        var sw = Stopwatch.StartNew();
        AdvisorResponse response;
        try
        {
            response = await advisor.ConsultAsync(query, context, cancellationToken);
            ArgumentNullException.ThrowIfNull(response);
        }
        catch (OperationCanceledException)
        {
            // Step 9 — OperationCanceledException is propagated untouched.
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            response = new AdvisorResponse
            {
                AdviceText = $"Advisor '{advisor.Name}' failed: {ex.Message}",
                Confidence = AdvisorConfidence.Uncertain,
                Outcome = AdvisorOutcome.InfrastructureFailure,
            };
        }
        finally
        {
            if (sw.IsRunning) sw.Stop();
        }

        // Step 10 — enrich response with advisor name, consultation id, measured duration.
        var enriched = response with
        {
            AdvisorName = advisor.Name,
            ConsultationId = consultationId,
            Duration = sw.Elapsed,
        };

        // Step 11 — count budget for Success and InfrastructureFailure (advisor was invoked).
        if (enriched.Outcome == AdvisorOutcome.Success || enriched.Outcome == AdvisorOutcome.InfrastructureFailure)
        {
            var tokensUsed = enriched.ApproximateTokenCount ?? estimatedTokens;
            _budgetState.RecordConsumption(tokensUsed, sw.Elapsed);
        }

        // Step 12 — provenance.
        _provenance.RecordConsultation(new AdvisorConsultationRecord
        {
            ConsultationId = consultationId,
            AdvisorName = advisor.Name,
            Phase = phase,
            Iteration = iteration,
            Query = query,
            Response = enriched,
            Timestamp = now,
        });

        // Step 13 — Completed event.
        await _eventSink.PublishAsync(
            new AdvisorConsultationCompletedEvent(
                _runId, iteration, phase, advisor.Name, consultationId, enriched, DateTimeOffset.UtcNow),
            cancellationToken);

        // Step 14 — return.
        return enriched;
    }

    /// <inheritdoc />
    public void AttributeArtifactToConsultation(string artifactContextKey, string consultationId)
    {
        ArgumentException.ThrowIfNullOrEmpty(artifactContextKey);
        ArgumentException.ThrowIfNullOrEmpty(consultationId);
        _provenance.RecordAttribution(artifactContextKey, consultationId);
    }
}
