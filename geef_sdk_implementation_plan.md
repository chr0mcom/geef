# GEEF SDK Implementation Plan
 
## Context
 
The user provided a comprehensive architecture specification for the GEEF SDK — a .NET 8 C# library that orchestrates AI-powered workflows through four phases: Grounding, Execution, Evaluation, and Finalize. The core pattern is a controlled feedback loop between Execution and Evaluation with typed context, convergence policies, and structured observability. The project directory is empty; this is a greenfield implementation.
 
## Approach
 
Implement bottom-up from foundational types to the orchestrator, following the specification's recommended order. The spec is exhaustive with full C# code — the implementation will follow it exactly.
 
## Implementation Steps
 
### 1. Solution & Project Setup
- Create `Geef.Sdk.sln`
- Create `src/Geef.Sdk/Geef.Sdk.csproj` (net8.0, C# 12, nullable, implicit usings)
- Create `tests/Geef.Sdk.Tests/Geef.Sdk.Tests.csproj` (xUnit, NSubstitute, FluentAssertions)
- Dependencies as specified in A.13
 
### 2. Context Layer (`Context/`)
- `ContextKey<T>` — typed key record
- `IRunContext` — interface with Get/Set/Remove/Contains/Keys
- `RunContext` — immutable impl on `ImmutableDictionary<string, object>`
- `GeefKeys` — predefined keys (OriginalInput, PreviousFindings, CurrentIteration, RunStartedAt, RunId, IterationHistory)
 
### 3. Results Layer (`Results/`)
- `FindingSeverity` enum (Info, Warning, Error, Critical)
- `ReviewDecision` enum (6 values)
- `Finding` record (with Fingerprint, Severity, Category, ArtifactReference, Metadata)
- `GroundingResult`, `ExecutionResult`, `ReviewResult`, `EvaluationAggregate`, `FinalizeResult<T>`
 
### 4. Provider Interfaces (`Providers/`)
- `IGroundingStep`, `IExecutionStep`, `IReviewer` (with Name, Priority), `IFinalizer<TOutput>`
 
### 5. Runtime (`Runtime/`)
- `IterationRecord` — single iteration data
- `IterationHistory` — with `IsStagnant()` and `HasRegression()`
 
### 6. Policies (`Policies/`)
- `IConvergencePolicy` + `DefaultConvergencePolicy` (MaxIterations, MaxElapsedTime, Stagnation, Regression, CriticalBlocker)
- `ConvergenceDecision` enum (7 values)
- `IEvaluationStrategy` + 4 strategies: Sequential, Parallel, FailFast, PriorityOrdered
 
### 7. Events (`Events/`)
- `IGeefEvent` marker interface
- All event records (PipelineStarted, GroundingStarted/Completed, ExecutionStarted/Completed, ReviewerStarted/Completed, EvaluationRejected/Approved, FinalizeStarted/Completed, PipelineCompleted/Failed)
- `IGeefEventSink` interface
- Sinks: `NullEventSink`, `CompositeEventSink`, `DelegateEventSink`, `LoggingEventSink`
 
### 8. Exceptions (`Exceptions/`)
- `GeefException` (base), `ConvergenceFailedException`, `PhaseTimeoutException`, `ProviderException`, `PipelineConfigurationException`
 
### 9. Middleware (`Middleware/`)
- `GeefPhase` enum
- `GeefMiddlewareContext`
- `IGeefMiddleware` interface
- `TimeoutMiddleware`, `TracingMiddleware`, `ExceptionHandlingMiddleware`
 
### 10. Diagnostics (`Diagnostics/`)
- `GeefDiagnostics` with static `ActivitySource`
 
### 11. Pipeline Core
- `GeefPipelineBuilder<TOutput>` — fluent builder with validation at Build()
- `GeefPipelineRunner<TOutput>` — immutable orchestrator with the full Grounding → [Exec ↔ Eval] → Finalize loop
- `GeefPipelineResult<TOutput>` — final result record
- `Geef` — static entry point
 
### 12. Hosting (`Hosting/`)
- `GeefServiceCollectionExtensions` — `AddGeefPipeline<TOutput>` extension methods
 
### 13. Tests
- `RunContextTests` — immutability, typed access, KeyNotFoundException
- `DefaultConvergencePolicyTests` — all decision paths
- `IterationHistoryTests` — stagnation, regression detection
- `EvaluationStrategyTests` — all 4 strategies
- `GeefPipelineBuilderTests` — validation, defaults
- `FullPipelineIntegrationTests` — complete run with mock providers, including failed iterations
 
### 14. Verification
- `dotnet build` — zero errors, zero warnings
- `dotnet test` — all green
- Review against all 58 requirements (F-001–F-037, NF-001–NF-021)
- Commit and push
 
## Key Files
All files under `src/Geef.Sdk/` and `tests/Geef.Sdk.Tests/` as specified in A.12.
 
## Verification
- `dotnet build src/Geef.Sdk/` must succeed with 0 warnings
- `dotnet test tests/Geef.Sdk.Tests/` must pass all tests
- Invoke a review agent to verify spec compliance