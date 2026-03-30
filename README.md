# Geef.Sdk

A policy-driven orchestration framework for AI agent workflows with typed context, convergence policies, and structured observability.

## What is GEEF?

GEEF is an orchestration pattern for AI-powered workflows, standing for four phases:

- **Grounding** — Contextualize the input (RAG, read files, gather context)
- **Execution** — Generate or modify artifacts (code, text, configuration)
- **Evaluation** — Independently review artifacts
- **Finalize** — Prepare and output the final artifacts

The core of the pattern is a **controlled feedback loop** between Execution and Evaluation: while reviewers find issues, work is rejected and the Execution agent must correct it.

```
Input → Grounding → ┌─ Execution ──┐
                    │              ↓
                    └──────── Evaluation
                                   ↓ (approved)
                              Finalize → Output
```

## Quick Start

```csharp
var pipeline = Geef.CreatePipeline<string>()
    .UseGrounding(new MyGrounding())
    .UseExecution(new MyExecution())
    .AddReviewer(new MySyntaxReviewer())
    .AddReviewer(new MySecurityReviewer())
    .UseFinalizer(new MyFinalizer())
    .ConfigureEvents(e =>
    {
        e.OnEvaluationRejected = async evt =>
            Console.WriteLine($"Iteration {evt.Iteration} failed: {evt.Aggregate.AllFindings.Count} findings.");
        e.OnEvaluationApproved = async evt =>
            Console.WriteLine($"Approved after {evt.Iteration} iterations!");
    })
    .Build();

var result = await pipeline.RunAsync("Generate a login component");
Console.WriteLine($"Output: {result.Output}");
Console.WriteLine($"Iterations: {result.TotalIterations}, Duration: {result.TotalDuration}");
```

## Architecture

### Typed Context Store

All pipeline state is carried in an immutable, typed context store. No `IDictionary<string, object>` — every value is accessed through a typed key:

```csharp
// Define keys
public static class MyKeys
{
    public static readonly ContextKey<string> GeneratedCode = new("my:code");
    public static readonly ContextKey<string> FilePath      = new("my:file-path");
}

// Write (returns new snapshot)
var updated = context.Set(MyKeys.GeneratedCode, "export const Button = () => ...");

// Read
var code = context.GetRequired(MyKeys.GeneratedCode);

// Conditional read
if (context.TryGet(GeefKeys.PreviousFindings, out var findings))
    // incorporate feedback
```

### Provider Interfaces

Implement four interfaces to define the pipeline's behavior:

| Interface | Responsibility |
|---|---|
| `IGroundingStep` | Build the initial context (RAG, file reads, API calls) |
| `IExecutionStep` | Generate/modify artifacts; read `GeefKeys.PreviousFindings` for feedback |
| `IReviewer` | Independently review artifacts; return `ReviewResult` with `Finding`s |
| `IFinalizer<TOutput>` | Produce the final typed output from the approved context |

### Convergence Policies

The loop terminates based on a pluggable `IConvergencePolicy`. The built-in `DefaultConvergencePolicy` supports:

- **MaxIterations** — hard iteration cap (default: 10)
- **MaxElapsedTime** — wall-clock budget (default: 30 min)
- **Stagnation detection** — same `Finding` fingerprints across N rounds
- **Regression detection** — previously-fixed findings reappear
- **Critical abort** — any `FindingSeverity.Critical` finding

```csharp
.UseConvergencePolicy(new DefaultConvergencePolicy
{
    MaxIterations     = 15,
    MaxElapsedTime    = TimeSpan.FromMinutes(20),
    StagnationThreshold = 3,
    AbortOnCritical   = true,
    DetectRegression  = true
})
```

### Evaluation Strategies

Control how reviewers execute:

| Strategy | Behavior |
|---|---|
| `SequentialEvaluationStrategy` | Run one-by-one in registration order |
| `ParallelEvaluationStrategy` | Run all concurrently via `Task.WhenAll` |
| `FailFastEvaluationStrategy` | Parallel, but cancel once the first reviewer rejects |
| `PriorityOrderedEvaluationStrategy` | Sequential by `IReviewer.Priority` (low = high priority), stops on first error-severity rejection |

### Observability

**Structured Events** via `IGeefEventSink`:

```csharp
// Delegate sink (no DI needed)
.ConfigureEvents(e =>
{
    e.OnEvaluationRejected = async evt => { /* ... */ };
    e.OnPipelineFailed      = async evt => { /* ... */ };
})

// Logging sink (via ILogger)
.AddEventSink(new LoggingEventSink(logger))
```

**Distributed Tracing** via `System.Diagnostics.ActivitySource` ("Geef.Sdk") — compatible with OpenTelemetry. Each run produces spans for the pipeline root, grounding, every iteration, every execution, every review, and finalize.

**Middleware** for cross-cutting concerns:

```csharp
.UseMiddleware(new TimeoutMiddleware
{
    DefaultTimeout = TimeSpan.FromMinutes(5),
    PhaseTimeouts  = new() { [GeefPhase.Execution] = TimeSpan.FromMinutes(10) }
})
.UseMiddleware<TracingMiddleware>()
.UseMiddleware<ExceptionHandlingMiddleware>()
```

### ASP.NET Core / DI Integration

```csharp
builder.Services.AddGeefPipeline<CodeResult>((sp, pipeline) =>
{
    pipeline
        .UseGrounding(sp.GetRequiredService<MyGrounding>())
        .UseExecution(sp.GetRequiredService<MyExecution>())
        .AddReviewer(sp.GetRequiredService<SyntaxReviewer>())
        .UseFinalizer(sp.GetRequiredService<MyFinalizer>());
});

// In a controller:
public class CodeController(GeefPipelineRunner<CodeResult> pipeline) { ... }
```

## Project Structure

```
src/Geef.Sdk/
├── Context/          ContextKey<T>, IRunContext, RunContext, GeefKeys
├── Providers/        IGroundingStep, IExecutionStep, IReviewer, IFinalizer<T>
├── Results/          Finding, ReviewResult, EvaluationAggregate, FinalizeResult<T>
├── Policies/         IConvergencePolicy, DefaultConvergencePolicy, 4× EvaluationStrategy
├── Events/           IGeefEvent, IGeefEventSink, 4× sinks, 13× event records
├── Middleware/       IGeefMiddleware, TimeoutMiddleware, TracingMiddleware, ExceptionHandlingMiddleware
├── Runtime/          IterationRecord, IterationHistory, InstrumentedReviewer
├── Exceptions/       GeefException hierarchy (5 types)
├── Diagnostics/      GeefDiagnostics (ActivitySource)
├── Hosting/          GeefServiceCollectionExtensions
├── GeefPipelineBuilder.cs
├── GeefPipelineRunner.cs
├── GeefPipelineResult.cs
└── Geef.cs           (static entry point)
```

## Dependencies

The SDK has exactly two external dependencies, both `Microsoft.Extensions.*`:

- `Microsoft.Extensions.DependencyInjection.Abstractions`
- `Microsoft.Extensions.Logging.Abstractions`

## License

MIT
