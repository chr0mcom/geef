using Geef.Sdk.Advisors;
using Geef.Sdk.Middleware;
using Geef.Sdk.Policies;
using Geef.Sdk.Results;
using Geef.Sdk.Runtime;

namespace Geef.Sdk.Events;

/// <summary>Fired when a pipeline run starts.</summary>
public sealed record PipelineStartedEvent(string RunId, string Input, DateTimeOffset Timestamp) : IGeefEvent;

/// <summary>Fired when the grounding phase starts.</summary>
public sealed record GroundingStartedEvent(string RunId, DateTimeOffset Timestamp) : IGeefEvent;

/// <summary>Fired when the grounding phase completes.</summary>
public sealed record GroundingCompletedEvent(string RunId, GroundingResult Result, TimeSpan Duration, DateTimeOffset Timestamp) : IGeefEvent;

/// <summary>Fired when an execution phase starts.</summary>
public sealed record ExecutionStartedEvent(string RunId, int Iteration, DateTimeOffset Timestamp) : IGeefEvent;

/// <summary>Fired when an execution phase completes.</summary>
public sealed record ExecutionCompletedEvent(string RunId, int Iteration, ExecutionResult Result, TimeSpan Duration, DateTimeOffset Timestamp) : IGeefEvent;

/// <summary>Fired when a reviewer starts.</summary>
public sealed record ReviewerStartedEvent(string RunId, int Iteration, string ReviewerName, DateTimeOffset Timestamp) : IGeefEvent;

/// <summary>Fired when a reviewer completes.</summary>
public sealed record ReviewerCompletedEvent(string RunId, int Iteration, ReviewResult Result, DateTimeOffset Timestamp) : IGeefEvent;

/// <summary>Fired when an evaluation round results in rejection.</summary>
public sealed record EvaluationRejectedEvent(string RunId, int Iteration, EvaluationAggregate Aggregate, ConvergenceDecision Decision, DateTimeOffset Timestamp) : IGeefEvent;

/// <summary>Fired when an evaluation round results in approval.</summary>
public sealed record EvaluationApprovedEvent(string RunId, int Iteration, EvaluationAggregate Aggregate, DateTimeOffset Timestamp) : IGeefEvent;

/// <summary>Fired when the finalize phase starts.</summary>
public sealed record FinalizeStartedEvent(string RunId, DateTimeOffset Timestamp) : IGeefEvent;

/// <summary>Fired when the finalize phase completes.</summary>
public sealed record FinalizeCompletedEvent(string RunId, TimeSpan Duration, DateTimeOffset Timestamp) : IGeefEvent;

/// <summary>Fired when a pipeline run completes successfully.</summary>
public sealed record PipelineCompletedEvent(string RunId, bool Success, int TotalIterations, TimeSpan TotalDuration, DateTimeOffset Timestamp) : IGeefEvent;

/// <summary>Fired when a pipeline run fails due to convergence issues.</summary>
public sealed record PipelineFailedEvent(string RunId, ConvergenceDecision Reason, int TotalIterations, IterationHistory History, DateTimeOffset Timestamp) : IGeefEvent;

/// <summary>
/// Fired when a reviewer throws an infrastructure exception that is caught and converted to
/// <see cref="ReviewDecision.Failed"/> by <see cref="Runtime.InstrumentedReviewer"/>. The pipeline
/// continues; the calling policy decides whether to treat <c>Failed</c> as blocking or non-blocking.
/// </summary>
public sealed record ReviewerFaultIsolatedEvent(string RunId, int Iteration, string ReviewerName, string FaultMessage, DateTimeOffset Timestamp) : IGeefEvent;

/// <summary>
/// Fired when an advisor consultation starts. Only fired when the advisor is actually
/// invoked — NOT fired on <see cref="AdvisorOutcome.BudgetExceeded"/> or policy-rejected
/// <see cref="AdvisorOutcome.NoApplicableAdvice"/> paths.
/// </summary>
public sealed record AdvisorConsultationStartedEvent(
    string RunId,
    int? Iteration,
    GeefPhase Phase,
    string AdvisorName,
    string ConsultationId,
    AdvisorQuery Query,
    DateTimeOffset Timestamp) : IGeefEvent;

/// <summary>
/// Fired when an advisor consultation completes (including degraded outcomes
/// <see cref="AdvisorOutcome.BudgetExceeded"/>, <see cref="AdvisorOutcome.InfrastructureFailure"/>,
/// and <see cref="AdvisorOutcome.NoApplicableAdvice"/> from policy rejection).
/// Not fired when no advisor is registered at all.
/// </summary>
public sealed record AdvisorConsultationCompletedEvent(
    string RunId,
    int? Iteration,
    GeefPhase Phase,
    string AdvisorName,
    string ConsultationId,
    AdvisorResponse Response,
    DateTimeOffset Timestamp) : IGeefEvent;
