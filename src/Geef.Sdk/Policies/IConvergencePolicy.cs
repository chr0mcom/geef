using Geef.Sdk.Results;
using Geef.Sdk.Runtime;

namespace Geef.Sdk.Policies;

/// <summary>
/// Decides after each evaluation round how the loop should proceed.
/// Replaces a simple MaxIterations counter with a configurable strategy.
/// </summary>
public interface IConvergencePolicy
{
    /// <summary>
    /// Evaluates the current situation and returns a decision.
    /// </summary>
    /// <param name="history">Complete history of all previous iterations.</param>
    /// <param name="currentAggregate">The evaluation result of the current round.</param>
    /// <param name="elapsed">Total elapsed time since pipeline start.</param>
    ConvergenceDecision Evaluate(
        IterationHistory history,
        EvaluationAggregate currentAggregate,
        TimeSpan elapsed);
}
