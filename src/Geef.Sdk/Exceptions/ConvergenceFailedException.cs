using Geef.Sdk.Policies;
using Geef.Sdk.Results;
using Geef.Sdk.Runtime;

namespace Geef.Sdk.Exceptions;

/// <summary>
/// Pipeline reached the convergence limit without passing all reviews.
/// </summary>
public sealed class ConvergenceFailedException : GeefException
{
    /// <summary>The reason for the convergence failure.</summary>
    public required ConvergenceDecision Reason { get; init; }

    /// <summary>The complete iteration history.</summary>
    public required IterationHistory History { get; init; }

    /// <summary>The last evaluation aggregate before failure.</summary>
    public required EvaluationAggregate LastEvaluation { get; init; }

    /// <inheritdoc />
    public ConvergenceFailedException(string message, string? runId = null) : base(message, runId) { }
}
