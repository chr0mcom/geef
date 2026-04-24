namespace Geef.Sdk.Policies;

/// <summary>
/// Runtime tracking of advisor budget consumption. Held by the AdvisorOrchestrator
/// (one instance per run); updated on every consultation. Thread-safe.
/// </summary>
public sealed class AdvisorBudgetState
{
    private readonly object _lock = new();
    private int _consultationsTotal;
    private int _consultationsThisIteration;
    private int _currentIteration;
    private int _tokensTotal;
    private TimeSpan _timeTotal;

    /// <summary>The immutable budget configuration this state tracks against.</summary>
    public AdvisorBudget Budget { get; }

    /// <summary>Creates a new budget state tracking the given budget.</summary>
    /// <param name="budget">The budget to track consumption against.</param>
    public AdvisorBudgetState(AdvisorBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        Budget = budget;
    }

    /// <summary>Total number of consultations recorded so far in this run.</summary>
    public int ConsultationsTotal { get { lock (_lock) return _consultationsTotal; } }

    /// <summary>Number of consultations recorded in the current iteration.</summary>
    public int ConsultationsThisIteration { get { lock (_lock) return _consultationsThisIteration; } }

    /// <summary>Total tokens consumed so far in this run.</summary>
    public int TokensTotal { get { lock (_lock) return _tokensTotal; } }

    /// <summary>Total wall-clock time consumed by advisor consultations so far in this run.</summary>
    public TimeSpan TimeTotal { get { lock (_lock) return _timeTotal; } }

    /// <summary>
    /// Called by the orchestrator at the start of each new iteration. The runner
    /// drives the iteration lifecycle by calling <c>orchestrator.StartIteration(iteration)</c>,
    /// which in turn invokes this method on the budget state.
    /// </summary>
    /// <param name="iteration">The iteration number that is about to start.</param>
    public void StartIteration(int iteration)
    {
        lock (_lock)
        {
            _currentIteration = iteration;
            _consultationsThisIteration = 0;
        }
    }

    /// <summary>
    /// Records a consultation that consumed budget. Called for any outcome
    /// where the advisor was actually invoked — i.e. Success and InfrastructureFailure.
    /// NOT called for BudgetExceeded or policy-rejected NoApplicableAdvice, where
    /// the advisor was never invoked.
    /// </summary>
    /// <param name="tokens">Approximate token count consumed by the consultation.</param>
    /// <param name="duration">Wall-clock duration of the consultation.</param>
    public void RecordConsumption(int tokens, TimeSpan duration)
    {
        lock (_lock)
        {
            _consultationsTotal++;
            _consultationsThisIteration++;
            _tokensTotal += tokens;
            _timeTotal += duration;
        }
    }

    /// <summary>
    /// True if the next consultation, with the given estimated token count, would
    /// exceed any of the configured budgets. Checks per-consultation token cap,
    /// per-iteration consultation cap, per-run consultation cap, total token cap,
    /// and total time cap.
    /// </summary>
    /// <param name="estimatedTokens">The estimated token cost of the next consultation.</param>
    public bool WouldExceedBudget(int estimatedTokens)
    {
        lock (_lock)
        {
            return estimatedTokens > Budget.MaxTokensPerConsultation
                || _consultationsTotal + 1 > Budget.MaxConsultationsPerRun
                || _consultationsThisIteration + 1 > Budget.MaxConsultationsPerIteration
                || _tokensTotal + estimatedTokens > Budget.MaxTotalAdvisorTokens
                || _timeTotal >= Budget.MaxTotalAdvisorTime;
        }
    }
}
