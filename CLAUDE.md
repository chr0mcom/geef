# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build
dotnet build Geef.Sdk.sln
dotnet build Geef.Sdk.sln -c Release

# Test
dotnet test tests/Geef.Sdk.Tests/Geef.Sdk.Tests.csproj
dotnet test tests/Geef.Sdk.Tests/ --filter "FullName~IntegrationTests"
dotnet test tests/Geef.Sdk.Tests/ -v detailed
```

## Architecture

Geef.Sdk is a **policy-driven orchestration framework for AI agent workflows** (.NET 7, C# 11). It implements the GEEF pattern — a typed, observable, policy-controlled feedback loop for AI tasks.

### The GEEF Loop

```
Input → Grounding → Execution → Evaluation (Reviewers)
                        ↑               ↓ rejected (loop back)
                        └───────────────┘
                                        ↓ approved
                                   Finalize → TOutput
```

1. **Grounding** — Gathers context (RAG, files, etc.), returns `GroundingResult` with an initial `IRunContext`
2. **Execution** — Generates/modifies artifacts; reads `GeefKeys.PreviousFindings` to fix prior issues
3. **Evaluation** — One or more `IReviewer` instances independently review; loop continues until all approve
4. **Finalize** — Produces the typed `TOutput` from final context

### Core Interfaces to Implement

| Interface | Key Method | Purpose |
|---|---|---|
| `IGroundingStep` | `RunAsync(string input, ct)` | Build initial context |
| `IExecutionStep` | `RunAsync(IRunContext ctx, ct)` | Generate/revise artifacts |
| `IReviewer` | `ReviewAsync(IRunContext ctx, ct)` | Return `ReviewResult` with findings |
| `IFinalizer<T>` | `FinalizeAsync(IRunContext ctx, ct)` | Produce final typed output |

### Context System

`IRunContext` is **immutable** — `context.Set<T>(key, value)` returns a new snapshot, never mutates. Use `ContextKey<T>` for type-safe access. Pre-defined keys live in `GeefKeys` (e.g., `GeefKeys.PreviousFindings`, `GeefKeys.CurrentIteration`).

### Convergence & Policies (`src/Geef.Sdk/Policies/`)

`DefaultConvergencePolicy` controls loop termination:
- `MaxIterations` (default: 10)
- `MaxElapsedTime` (default: 30 min)
- Stagnation detection — same findings repeat N times
- Regression detection — previously-fixed findings reappear
- Critical abort — any `FindingSeverity.Critical` finding

### Evaluation Strategies (`src/Geef.Sdk/Policies/`)

- `SequentialEvaluationStrategy` — default, runs reviewers in order
- `ParallelEvaluationStrategy` — all reviewers via `Task.WhenAll`
- `FailFastEvaluationStrategy` — parallel, cancels on first rejection
- `PriorityOrderedEvaluationStrategy` — sequential by `IReviewer.Priority`, stops on error-severity

### Builder API

```csharp
var pipeline = Geef.CreatePipeline<TOutput>()
    .UseGrounding(new MyGrounding())
    .UseExecution(new MyExecution())
    .AddReviewer(new MyReviewer())
    .UseFinalizer(new MyFinalizer())
    .UseConvergencePolicy(new DefaultConvergencePolicy { MaxIterations = 15 })
    .UseEvaluationStrategy(new ParallelEvaluationStrategy())
    .UseMiddleware(new TimeoutMiddleware())
    .Build(); // Throws PipelineConfigurationException if Grounding/Execution/Finalizer/≥1 Reviewer missing

var result = await pipeline.RunAsync("input", cancellationToken);
// result.Output, result.TotalIterations, result.History, result.Success
```

### DI Integration (`src/Geef.Sdk/Hosting/`)

```csharp
builder.Services.AddGeefPipeline<TOutput>((sp, pipeline) =>
{
    pipeline.UseGrounding(sp.GetRequiredService<MyGrounding>())
            .UseExecution(sp.GetRequiredService<MyExecution>())
            .AddReviewer(sp.GetRequiredService<MyReviewer>());
});
// Injects GeefPipelineRunner<TOutput> into controllers/services
```

### Observability

- **Events** — `IGeefEventSink` with 13+ typed events; configure via `.ConfigureEvents(e => { e.OnEvaluationRejected = ...; })`
- **Middleware** — `TimeoutMiddleware`, `TracingMiddleware`, `ExceptionHandlingMiddleware` in `src/Geef.Sdk/Middleware/`
- **OpenTelemetry** — `ActivitySource("Geef.Sdk")` in `src/Geef.Sdk/Diagnostics/`

### Exceptions (`src/Geef.Sdk/Exceptions/`)

| Exception | When |
|---|---|
| `ConvergenceFailedException` | Loop hit max iterations/time without approval |
| `PhaseTimeoutException` | A single phase exceeded its timeout |
| `ProviderException` | Infrastructure error from a provider |
| `PipelineConfigurationException` | Missing required pipeline components |

### Testing Patterns

Test doubles live in the test project: `DelegateGrounding`, `DelegateExecution`, `DelegateReviewer`, `DelegateFinalizer` — use these to build pipelines without real implementations. The SDK has no AI SDK dependency; bring your own LLM client.

# Important instructions:

## Definition of Done - Review Process

Um eine Aufgabe abzuschließen, MUSST du den folgenden strikten Review-Prozess durchlaufen. Du darfst die Aufgabe erst als "Task complete" markieren, wenn alle Schritte erfolgreich waren.

1. **Code Implementierung:** Schreibe den Code zur Lösung der im Prompt gestellten Aufgabe.
2. **OpenAI Codex Code Review:** Rufe die Codex CLI über Bash auf, um die Code-Qualität zu prüfen.
3. **Claude Issue Review:** Rufe eine weitere Claude-Instanz über Bash auf, um zu prüfen, ob die Aufgabe inhaltlich gelöst wurde.
4. **Lokale Tests:** Führe die lokale Test-Suite aus (z.B. `npm run test`).

## Review Rules (CRITICAL)

* **Keine Timeouts:** Reviewer bekommen kein Zeitlimit. Sie laufen, solange sie brauchen.
* **Kein Shell-Piping:** Du darfst den Output der Reviewer NIEMALS über Shell-Pipes (`>`) umleiten. Du MUSST den Reviewer in seinem Prompt explizit anweisen, seine Findings in eine Datei zu schreiben (z.B. `claude -p "Prüfe den Code und schreibe das Ergebnis in review_log.txt"`).
* **Null-Toleranz:** Ein Review gilt nur als bestanden, wenn die Log-Datei exakt "0 findings" oder "APPROVED" meldet.
* **On-Error Loop:** Wenn ein Reviewer Fehler findet, MUSST du den Code korrigieren und den Review-Prozess für diesen Agenten komplett neu starten.
* **Rollen-Trennung:** Review-Agenten dürfen niemals selbst Code-Dateien ändern.

## Additional personal instructions

- Do not make comments within the code. XML documentation for public methods is acceptable. However, it should always be in the interface if possible; the implementation (class) should then only have a /// <inheritdoc />.
- If possible, static helper methods should always be moved to a separate class as extensions.
- If possible and appropriate, add a readme.md file to the end of each task, or update it if one already exists.
- Also, if possible and appropriate, write one or more unit tests for the logic you've created.
- Whenever possible, use a primary constructor for class parameters. This also applies to models. For example: `public record CategoryCreate([property: JsonPropertyName("name"), Required] string Name, [property: JsonPropertyName("description")] string? Description = null, [property: JsonPropertyName("parent_id")] int? ParentId = null, [property: JsonPropertyName("position")] int Position = 0);`
- Always use the "frontend designer" plugin when there is a task related to an interface where you want the design and user interface to be as good as possible.
- At the end of a task, commit and push the code to GitHub, if possible.
- When you do something with GIT, e.g., commit, PR, etc., use the corresponding plugin if possible.