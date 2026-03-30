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
