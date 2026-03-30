# GEEF SDK für .NET — Zielarchitektur V1

## Vollständige Spezifikation, Anforderungskatalog und Implementierungsanleitung

**Version:** 1.0  
**Zielplattform:** .NET 8+ / C# 12  
**Paketname:** `Geef.Sdk`  
**Lizenz:** MIT  

---

# Teil A — Architekturspezifikation

---

## A.1 — Überblick und Designphilosophie

### A.1.1 Was ist GEEF?

GEEF ist ein Orchestrierungsmuster für KI-gestützte Workflows. Es steht für vier Phasen:

- **Grounding** — Kontextualisierung des Inputs (z.B. RAG, Dateien lesen, Kontext sammeln)
- **Execution** — Generierung/Modifikation von Artefakten (z.B. Code, Text, Konfiguration)
- **Evaluation** — Prüfung der Artefakte durch unabhängige Reviewer
- **Finalize** — Aufbereitung und Ausgabe der finalen Artefakte

Der Kern des Musters ist der **kontrollierte Feedback-Loop** zwischen Execution und Evaluation: Solange Reviewer Fehler finden, wird die Arbeit zurückgewiesen und der Execution-Agent muss korrigieren.

### A.1.2 Designziele des SDK

1. **KI-agnostisch:** Das SDK kennt kein konkretes KI-Modell. Es orchestriert den Ablauf; die Implementierung der Phasen obliegt dem Konsumenten.
2. **Typsicher:** Kein `IDictionary<string, object>`. Alle Artefakte und Findings werden über typisierte Schlüssel transportiert.
3. **Snapshot-basiert:** Phasen verändern den Kontext nicht direkt. Sie liefern strukturierte Ergebnisobjekte zurück. Der Orchestrator baut den nächsten Zustand.
4. **Policy-gesteuert:** Loop-Konvergenz, Reviewer-Scheduling und Fehlerbehandlung werden über austauschbare Policies konfiguriert, nicht hart kodiert.
5. **Observable:** Strukturierte Events, Middleware-Pipeline und nativer Support für `System.Diagnostics.ActivitySource` (OpenTelemetry-kompatibel).
6. **DI-first:** Nahtlose Integration in `Microsoft.Extensions.DependencyInjection` und ASP.NET Core.
7. **Testbar:** Durch Snapshot-basierte Zustandsverwaltung und klare Interfaces sind alle Komponenten isoliert testbar.

### A.1.3 Architektur-Schichten

Das SDK ist in fünf logische Schichten gegliedert:

```
┌─────────────────────────────────────────────────────┐
│                  Hosting Layer                      │
│   DI-Registration, ASP.NET Integration, Console     │
├─────────────────────────────────────────────────────┤
│               Observability Layer                   │
│   IGeefEventSink, Middleware, ActivitySource,        │
│   Structured Logging                                │
├─────────────────────────────────────────────────────┤
│                Definition Layer                     │
│   GeefPipelineBuilder, Fluent API, Konfiguration    │
├─────────────────────────────────────────────────────┤
│                 Runtime Layer                       │
│   GeefPipelineRunner (Orchestrator), State Machine, │
│   ConvergencePolicy, EvaluationStrategy, Scheduler  │
├─────────────────────────────────────────────────────┤
│                 Provider Layer                      │
│   IGroundingStep, IExecutionStep, IReviewer,        │
│   IFinalizer — vom Konsumenten implementiert        │
└─────────────────────────────────────────────────────┘
```

---

## A.2 — Typed Context Store

### A.2.1 Designentscheidung

Anstelle eines `IDictionary<string, object>` verwendet das SDK einen typisierten Context Store auf Basis von `ContextKey<T>`. Das bietet:

- Compile-Time-Typsicherheit beim Lesen und Schreiben
- Keine Casts, keine `InvalidCastException` zur Laufzeit
- Klare, selbstdokumentierende API
- Sichere parallele Lesezugriffe (Immutable Snapshots)
- Serialisierbarkeit für Persistenz und Replay

### A.2.2 ContextKey\<T\>

```csharp
namespace Geef.Sdk.Context;

/// <summary>
/// Ein typisierter Schlüssel für den Context Store.
/// Jeder Schlüssel ist über seinen Namen eindeutig und über seinen Typparameter typsicher.
/// </summary>
/// <typeparam name="T">Der Typ des gespeicherten Werts.</typeparam>
public sealed record ContextKey<T>(string Name)
{
    public override string ToString() => $"ContextKey<{typeof(T).Name}>(\"{Name}\")";
}
```

### A.2.3 IRunContext (Interface)

```csharp
namespace Geef.Sdk.Context;

/// <summary>
/// Der typisierte Context Store, der durch die gesamte Pipeline gereicht wird.
/// Implementierungen MÜSSEN thread-safe für Lesezugriffe sein (Reviewer laufen ggf. parallel).
/// </summary>
public interface IRunContext
{
    /// <summary>Liest einen Wert. Wirft KeyNotFoundException, wenn nicht vorhanden.</summary>
    T GetRequired<T>(ContextKey<T> key);

    /// <summary>Versucht einen Wert zu lesen. Gibt false zurück, wenn nicht vorhanden.</summary>
    bool TryGet<T>(ContextKey<T> key, out T? value);

    /// <summary>Prüft, ob ein Schlüssel vorhanden ist.</summary>
    bool Contains<T>(ContextKey<T> key);

    /// <summary>
    /// Erzeugt einen NEUEN RunContext mit dem zusätzlichen/aktualisierten Wert.
    /// Der ursprüngliche RunContext bleibt unverändert (Immutability).
    /// </summary>
    IRunContext Set<T>(ContextKey<T> key, T value);

    /// <summary>
    /// Erzeugt einen NEUEN RunContext ohne den angegebenen Schlüssel.
    /// </summary>
    IRunContext Remove<T>(ContextKey<T> key);

    /// <summary>Gibt alle vorhandenen Schlüsselnamen zurück.</summary>
    IReadOnlyCollection<string> Keys { get; }
}
```

### A.2.4 RunContext (Default-Implementierung)

```csharp
namespace Geef.Sdk.Context;

using System.Collections.Frozen;
using System.Collections.Immutable;

/// <summary>
/// Immutable-Implementierung des Context Store.
/// Jeder Set/Remove erzeugt eine neue Instanz (Persistent Data Structure).
/// Thread-safe für beliebig viele gleichzeitige Leser.
/// </summary>
public sealed class RunContext : IRunContext
{
    private readonly ImmutableDictionary<string, object> _store;

    public RunContext() 
        => _store = ImmutableDictionary<string, object>.Empty;

    private RunContext(ImmutableDictionary<string, object> store) 
        => _store = store;

    public T GetRequired<T>(ContextKey<T> key)
    {
        if (!_store.TryGetValue(key.Name, out var value))
            throw new KeyNotFoundException(
                $"Context key '{key.Name}' (type: {typeof(T).Name}) not found. " +
                $"Available keys: [{string.Join(", ", _store.Keys)}]");
        return (T)value;
    }

    public bool TryGet<T>(ContextKey<T> key, out T? value)
    {
        if (_store.TryGetValue(key.Name, out var raw))
        {
            value = (T)raw;
            return true;
        }
        value = default;
        return false;
    }

    public bool Contains<T>(ContextKey<T> key) 
        => _store.ContainsKey(key.Name);

    public IRunContext Set<T>(ContextKey<T> key, T value) 
        => new RunContext(_store.SetItem(key.Name, value!));

    public IRunContext Remove<T>(ContextKey<T> key) 
        => new RunContext(_store.Remove(key.Name));

    public IReadOnlyCollection<string> Keys => _store.Keys;
}
```

### A.2.5 Vordefinierte Standard-Schlüssel

```csharp
namespace Geef.Sdk.Context;

/// <summary>
/// Vordefinierte ContextKeys, die das SDK intern verwendet.
/// Konsumenten können eigene ContextKeys für ihre domänenspezifischen Artefakte definieren.
/// </summary>
public static class GeefKeys
{
    /// <summary>Der ursprüngliche Input-String (z.B. User-Prompt).</summary>
    public static readonly ContextKey<string> OriginalInput = new("geef:original-input");

    /// <summary>Findings aus der letzten Evaluation-Runde.</summary>
    public static readonly ContextKey<IReadOnlyList<Finding>> PreviousFindings = new("geef:previous-findings");

    /// <summary>Die aktuelle Iterationsnummer (1-basiert).</summary>
    public static readonly ContextKey<int> CurrentIteration = new("geef:current-iteration");

    /// <summary>Der Zeitpunkt, zu dem der Pipeline-Run gestartet wurde.</summary>
    public static readonly ContextKey<DateTimeOffset> RunStartedAt = new("geef:run-started-at");

    /// <summary>Eine eindeutige Run-ID für Tracing und Korrelation.</summary>
    public static readonly ContextKey<string> RunId = new("geef:run-id");

    /// <summary>Die vollständige Iterationshistorie für Konvergenzanalyse.</summary>
    public static readonly ContextKey<IterationHistory> IterationHistory = new("geef:iteration-history");
}
```

---

## A.3 — Ergebnisobjekte (Result Objects)

### A.3.1 Designentscheidung

Phasen verändern den Kontext nicht seiteneffektartig. Stattdessen liefert jede Phase ein strukturiertes Ergebnisobjekt zurück. Der Orchestrator entscheidet, wie das Ergebnis in den nächsten Kontext-Snapshot eingebaut wird.

### A.3.2 GroundingResult

```csharp
namespace Geef.Sdk.Results;

/// <summary>
/// Ergebnis der Grounding-Phase.
/// Enthält den initialisierten RunContext mit allen gesammelten Kontextinformationen.
/// </summary>
public sealed record GroundingResult
{
    /// <summary>Der initialisierte Context mit allen Grounding-Daten.</summary>
    public required IRunContext Context { get; init; }

    /// <summary>Optionale Notizen/Logs aus dem Grounding-Prozess.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}
```

### A.3.3 ExecutionResult

```csharp
namespace Geef.Sdk.Results;

/// <summary>
/// Ergebnis der Execution-Phase.
/// Enthält den aktualisierten RunContext mit den generierten/modifizierten Artefakten.
/// </summary>
public sealed record ExecutionResult
{
    /// <summary>
    /// Der aktualisierte Context. Der Executor erzeugt einen neuen Context-Snapshot
    /// via context.Set(), ohne den Input-Context zu mutieren.
    /// </summary>
    public required IRunContext UpdatedContext { get; init; }

    /// <summary>Optionale Notizen aus dem Execution-Prozess.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}
```

### A.3.4 Finding und FindingSeverity

```csharp
namespace Geef.Sdk.Results;

/// <summary>
/// Ein einzelner Befund eines Reviewers.
/// </summary>
public sealed record Finding
{
    /// <summary>Name des Reviewers, der diesen Befund erstellt hat.</summary>
    public required string ReviewerName { get; init; }

    /// <summary>Eindeutiger Fingerprint dieses Findings für Konvergenzanalyse.
    /// Ermöglicht Erkennung von Stagnation (gleicher Fingerprint über Iterationen).
    /// Empfehlung: Hash aus ReviewerName + Kategorie + normalisierter Message.</summary>
    public required string Fingerprint { get; init; }

    /// <summary>Menschenlesbare Beschreibung des Befunds.</summary>
    public required string Message { get; init; }

    /// <summary>Schweregrad des Befunds.</summary>
    public FindingSeverity Severity { get; init; } = FindingSeverity.Error;

    /// <summary>Optionale Kategorie (z.B. "Security", "Style", "Logic").</summary>
    public string? Category { get; init; }

    /// <summary>Optionaler Verweis auf das betroffene Artefakt (z.B. Dateiname, Zeile).</summary>
    public string? ArtifactReference { get; init; }

    /// <summary>Optionale Metadaten (z.B. Regel-ID, Confidence-Score).</summary>
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }
}

public enum FindingSeverity
{
    /// <summary>Rein informativ. Blockiert den Loop nicht.</summary>
    Info,

    /// <summary>Warnung. Blockiert den Loop standardmäßig nicht, 
    /// aber konfigurierbar über ConvergencePolicy.</summary>
    Warning,

    /// <summary>Fehler. Blockiert den Loop standardmäßig.</summary>
    Error,

    /// <summary>Kritischer Blocker. Kann je nach Policy zum sofortigen Abbruch führen.</summary>
    Critical
}
```

### A.3.5 ReviewResult und ReviewDecision

```csharp
namespace Geef.Sdk.Results;

/// <summary>
/// Ergebnis eines einzelnen Reviewers.
/// Trennt klar zwischen "fachliche Bewertung" und "technisches Scheitern des Reviewers".
/// </summary>
public sealed record ReviewResult
{
    /// <summary>Name des Reviewers.</summary>
    public required string ReviewerName { get; init; }

    /// <summary>Entscheidung des Reviewers.</summary>
    public required ReviewDecision Decision { get; init; }

    /// <summary>Liste der Findings (kann auch bei Approved Warnings enthalten).</summary>
    public IReadOnlyList<Finding> Findings { get; init; } = [];

    /// <summary>Dauer des Reviews.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Optionaler Confidence-Score (0.0 bis 1.0).</summary>
    public double? Confidence { get; init; }

    /// <summary>Optionale empfohlene Retry-Strategie bei Rejection.</summary>
    public string? SuggestedRetryHint { get; init; }
}

/// <summary>
/// Mögliche Entscheidungen eines Reviewers.
/// </summary>
public enum ReviewDecision
{
    /// <summary>Keine Fehler gefunden. Artefakte sind akzeptabel.</summary>
    Approved,

    /// <summary>Fehler gefunden. Execution muss wiederholt werden.</summary>
    Rejected,

    /// <summary>Genehmigt, aber mit Warnungen die dokumentiert werden sollten.</summary>
    ApprovedWithWarnings,

    /// <summary>Reviewer empfiehlt Wiederholung, kann aber keinen klaren Fehler benennen.</summary>
    RetrySuggested,

    /// <summary>Reviewer ist für diese Art von Artefakt nicht zuständig.</summary>
    NotApplicable,

    /// <summary>Reviewer konnte technisch nicht ausgeführt werden 
    /// (z.B. API-Timeout, Infrastrukturfehler). 
    /// NICHT zu verwechseln mit inhaltlicher Ablehnung.</summary>
    Failed
}
```

### A.3.6 EvaluationAggregate

```csharp
namespace Geef.Sdk.Results;

/// <summary>
/// Aggregiertes Ergebnis aller Reviewer einer Evaluation-Runde.
/// Wird von der EvaluationStrategy erzeugt.
/// </summary>
public sealed record EvaluationAggregate
{
    /// <summary>Alle einzelnen Review-Ergebnisse.</summary>
    public required IReadOnlyList<ReviewResult> Reviews { get; init; }

    /// <summary>Alle Findings aller Reviewer, zusammengefasst.</summary>
    public IReadOnlyList<Finding> AllFindings =>
        Reviews.SelectMany(r => r.Findings).ToList();

    /// <summary>True, wenn mindestens ein Reviewer Rejected oder Failed ist.</summary>
    public bool HasBlockingIssues =>
        Reviews.Any(r => r.Decision is ReviewDecision.Rejected or ReviewDecision.Failed);

    /// <summary>True, wenn alle Reviewer Approved, ApprovedWithWarnings oder NotApplicable sind.</summary>
    public bool IsFullyApproved =>
        Reviews.All(r => r.Decision is ReviewDecision.Approved
            or ReviewDecision.ApprovedWithWarnings
            or ReviewDecision.NotApplicable);

    /// <summary>Gesamtdauer aller Reviews.</summary>
    public TimeSpan TotalDuration => 
        Reviews.Aggregate(TimeSpan.Zero, (sum, r) => sum + r.Duration);
}
```

### A.3.7 FinalizeResult\<TOutput\>

```csharp
namespace Geef.Sdk.Results;

/// <summary>
/// Ergebnis der Finalize-Phase.
/// </summary>
/// <typeparam name="TOutput">Der anwendungsspezifische Output-Typ.</typeparam>
public sealed record FinalizeResult<TOutput>
{
    /// <summary>Das finale Ergebnis der Pipeline.</summary>
    public required TOutput Output { get; init; }

    /// <summary>Der finale Kontext-Snapshot zum Zeitpunkt der Finalisierung.</summary>
    public required IRunContext FinalContext { get; init; }

    /// <summary>Optionale Zusammenfassung/Notizen.</summary>
    public string? Summary { get; init; }
}
```

---

## A.4 — Iteration History und Konvergenzanalyse

### A.4.1 IterationRecord

```csharp
namespace Geef.Sdk.Runtime;

/// <summary>
/// Aufzeichnung einer einzelnen Loop-Iteration für Konvergenzanalyse.
/// </summary>
public sealed record IterationRecord
{
    /// <summary>Iterationsnummer (1-basiert).</summary>
    public required int Iteration { get; init; }

    /// <summary>Zeitpunkt des Starts dieser Iteration.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Dauer der Execution-Phase in dieser Iteration.</summary>
    public required TimeSpan ExecutionDuration { get; init; }

    /// <summary>Das aggregierte Evaluation-Ergebnis dieser Iteration.</summary>
    public required EvaluationAggregate EvaluationResult { get; init; }

    /// <summary>Set der Finding-Fingerprints in dieser Iteration 
    /// (für Stagnations-/Regressionserkennung).</summary>
    public IReadOnlySet<string> FindingFingerprints =>
        EvaluationResult.AllFindings.Select(f => f.Fingerprint).ToHashSet();
}
```

### A.4.2 IterationHistory

```csharp
namespace Geef.Sdk.Runtime;

/// <summary>
/// Vollständige Historie aller Loop-Iterationen.
/// Wird der ConvergencePolicy zur Entscheidungsfindung übergeben.
/// </summary>
public sealed class IterationHistory
{
    private readonly List<IterationRecord> _records = [];

    public IReadOnlyList<IterationRecord> Records => _records.AsReadOnly();
    public int Count => _records.Count;

    public void Add(IterationRecord record) => _records.Add(record);

    /// <summary>
    /// Prüft, ob sich die Finding-Fingerprints zwischen den letzten N Iterationen 
    /// nicht verändert haben (Stagnation).
    /// </summary>
    public bool IsStagnant(int lookbackIterations = 3)
    {
        if (_records.Count < lookbackIterations) return false;

        var recent = _records.TakeLast(lookbackIterations).ToList();
        var firstSet = recent[0].FindingFingerprints;
        return recent.Skip(1).All(r => r.FindingFingerprints.SetEquals(firstSet));
    }

    /// <summary>
    /// Prüft, ob ein Finding-Fingerprint, der in einer früheren Iteration verschwunden war,
    /// wieder aufgetaucht ist (Regression).
    /// </summary>
    public bool HasRegression()
    {
        if (_records.Count < 3) return false;
        var current = _records[^1].FindingFingerprints;
        var previous = _records[^2].FindingFingerprints;
        var beforeThat = _records[^3].FindingFingerprints;

        // Ein Fingerprint, der in ^2 nicht war aber in ^3 und ^1 schon → Regression
        return current.Any(fp => !previous.Contains(fp) && beforeThat.Contains(fp));
    }

    /// <summary>Gesamtlaufzeit über alle Iterationen.</summary>
    public TimeSpan TotalElapsed =>
        _records.Count == 0
            ? TimeSpan.Zero
            : DateTimeOffset.UtcNow - _records[0].StartedAt;
}
```

---

## A.5 — Provider Interfaces (Vom Konsumenten implementiert)

### A.5.1 IGroundingStep

```csharp
namespace Geef.Sdk.Providers;

/// <summary>
/// Kontextualisierung des Inputs. Sammelt alle notwendigen Informationen,
/// um die Aufgabe zu verstehen (z.B. RAG-Abfragen, Dateien lesen, APIs aufrufen).
/// 
/// Verantwortung:
/// - Den initialen RunContext aufbauen
/// - Den OriginalInput-Key setzen
/// - Alle für die Execution notwendigen Kontextdaten laden
/// </summary>
public interface IGroundingStep
{
    Task<GroundingResult> RunAsync(
        string input,
        CancellationToken cancellationToken = default);
}
```

### A.5.2 IExecutionStep

```csharp
namespace Geef.Sdk.Providers;

/// <summary>
/// Der "Macher". Nimmt den aktuellen Kontext (inklusive ggf. vorhandenem Feedback 
/// aus PreviousFindings) und generiert oder modifiziert Artefakte.
///
/// WICHTIG: Die Implementierung DARF den übergebenen RunContext NICHT mutieren.
/// Sie MUSS einen neuen Context-Snapshot via context.Set() erzeugen und
/// im ExecutionResult.UpdatedContext zurückgeben.
/// </summary>
public interface IExecutionStep
{
    Task<ExecutionResult> RunAsync(
        IRunContext context,
        CancellationToken cancellationToken = default);
}
```

### A.5.3 IReviewer

```csharp
namespace Geef.Sdk.Providers;

/// <summary>
/// Ein unabhängiger Reviewer, der die Artefakte im Kontext prüft.
/// Es können beliebig viele Reviewer registriert werden.
///
/// WICHTIG: Reviewer DÜRFEN den RunContext nur LESEN, nicht verändern.
/// Infrastrukturfehler (z.B. API-Timeouts) sollen als ReviewDecision.Failed
/// zurückgegeben werden, NICHT als Exception geworfen werden (außer bei fatalen Fehlern).
/// </summary>
public interface IReviewer
{
    /// <summary>Eindeutiger Name des Reviewers (für Logging, Events, Finding-Zuordnung).</summary>
    string Name { get; }

    /// <summary>
    /// Priorität für das Scheduling. Niedrigere Werte = höhere Priorität.
    /// Reviewer mit höherer Priorität laufen zuerst (bei PriorityOrdered-Strategie).
    /// Default: 100.
    /// </summary>
    int Priority => 100;

    Task<ReviewResult> ReviewAsync(
        IRunContext context,
        CancellationToken cancellationToken = default);
}
```

### A.5.4 IFinalizer\<TOutput\>

```csharp
namespace Geef.Sdk.Providers;

/// <summary>
/// Der Abschluss-Schritt. Wird NUR aufgerufen, wenn die Evaluation erfolgreich war.
/// Bereitet die finalen Artefakte aus dem Kontext für die Ausgabe auf 
/// (z.B. Git Commit, Datei speichern, API Response).
/// </summary>
/// <typeparam name="TOutput">Der anwendungsspezifische Output-Typ.</typeparam>
public interface IFinalizer<TOutput>
{
    Task<FinalizeResult<TOutput>> FinalizeAsync(
        IRunContext context,
        CancellationToken cancellationToken = default);
}
```

---

## A.6 — Policies

### A.6.1 IConvergencePolicy

```csharp
namespace Geef.Sdk.Policies;

/// <summary>
/// Entscheidet nach jeder Evaluation-Runde, wie der Loop fortfahren soll.
/// Ersetzt den simplen MaxIterations-Zähler durch eine konfigurierbare Strategie.
/// </summary>
public interface IConvergencePolicy
{
    /// <summary>
    /// Bewertet die aktuelle Situation und gibt eine Entscheidung zurück.
    /// </summary>
    /// <param name="history">Vollständige Historie aller bisherigen Iterationen.</param>
    /// <param name="currentAggregate">Das Evaluation-Ergebnis der aktuellen Runde.</param>
    /// <param name="elapsed">Gesamtlaufzeit seit Pipeline-Start.</param>
    ConvergenceDecision Evaluate(
        IterationHistory history,
        EvaluationAggregate currentAggregate,
        TimeSpan elapsed);
}

/// <summary>
/// Mögliche Entscheidungen der Konvergenz-Policy.
/// </summary>
public enum ConvergenceDecision
{
    /// <summary>Evaluation bestanden. Loop verlassen, weiter zu Finalize.</summary>
    Approved,

    /// <summary>Evaluation fehlgeschlagen, aber Fortschritt erkennbar. Weiter iterieren.</summary>
    Continue,

    /// <summary>Maximale Iterationen oder Zeitbudget erreicht. Pipeline abbrechen.</summary>
    StopMaxAttemptsReached,

    /// <summary>Stagnation erkannt (gleiche Findings über mehrere Runden). Abbruch.</summary>
    StopStagnant,

    /// <summary>Regression erkannt (behobene Fehler tauchen wieder auf). Abbruch.</summary>
    StopRegression,

    /// <summary>Kritischer Security-/Safety-Blocker. Sofortiger Abbruch.</summary>
    AbortCriticalBlocker,

    /// <summary>Automatische Lösung scheint nicht möglich. An Menschen eskalieren.</summary>
    EscalateToHuman
}
```

### A.6.2 DefaultConvergencePolicy

```csharp
namespace Geef.Sdk.Policies;

/// <summary>
/// Standard-Konvergenz-Policy mit konfigurierbaren Schwellwerten.
/// </summary>
public sealed class DefaultConvergencePolicy : IConvergencePolicy
{
    /// <summary>Maximale Anzahl von Iterationen. Default: 10.</summary>
    public int MaxIterations { get; init; } = 10;

    /// <summary>Maximales Zeitbudget für den gesamten Loop. Default: 30 Minuten.</summary>
    public TimeSpan MaxElapsedTime { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>Anzahl Iterationen ohne Veränderung der Findings, ab der Stagnation erkannt wird. Default: 3.</summary>
    public int StagnationThreshold { get; init; } = 3;

    /// <summary>Ob bei Critical-Findings sofort abgebrochen werden soll. Default: true.</summary>
    public bool AbortOnCritical { get; init; } = true;

    /// <summary>Ob Regression erkannt und gemeldet werden soll. Default: true.</summary>
    public bool DetectRegression { get; init; } = true;

    public ConvergenceDecision Evaluate(
        IterationHistory history,
        EvaluationAggregate currentAggregate,
        TimeSpan elapsed)
    {
        // 1. Bestanden?
        if (currentAggregate.IsFullyApproved)
            return ConvergenceDecision.Approved;

        // 2. Kritischer Blocker?
        if (AbortOnCritical && currentAggregate.AllFindings.Any(f => f.Severity == FindingSeverity.Critical))
            return ConvergenceDecision.AbortCriticalBlocker;

        // 3. Zeitbudget?
        if (elapsed > MaxElapsedTime)
            return ConvergenceDecision.StopMaxAttemptsReached;

        // 4. Iterationslimit?
        if (history.Count >= MaxIterations)
            return ConvergenceDecision.StopMaxAttemptsReached;

        // 5. Stagnation?
        if (history.IsStagnant(StagnationThreshold))
            return ConvergenceDecision.StopStagnant;

        // 6. Regression?
        if (DetectRegression && history.HasRegression())
            return ConvergenceDecision.StopRegression;

        // 7. Sonst: weitermachen
        return ConvergenceDecision.Continue;
    }
}
```

### A.6.3 IEvaluationStrategy

```csharp
namespace Geef.Sdk.Policies;

/// <summary>
/// Steuert, WIE die Reviewer ausgeführt werden (parallel, sequenziell, fail-fast, etc.).
/// </summary>
public interface IEvaluationStrategy
{
    /// <summary>
    /// Führt die Reviewer gemäß der Strategie aus und gibt das aggregierte Ergebnis zurück.
    /// </summary>
    Task<EvaluationAggregate> ExecuteAsync(
        IReadOnlyList<IReviewer> reviewers,
        IRunContext context,
        CancellationToken cancellationToken = default);
}
```

### A.6.4 Mitgelieferte Evaluation-Strategien

```csharp
namespace Geef.Sdk.Policies;

/// <summary>
/// Führt Reviewer sequenziell aus. Einfachste Strategie.
/// </summary>
public sealed class SequentialEvaluationStrategy : IEvaluationStrategy { /* ... */ }

/// <summary>
/// Führt alle Reviewer parallel aus via Task.WhenAll.
/// </summary>
public sealed class ParallelEvaluationStrategy : IEvaluationStrategy { /* ... */ }

/// <summary>
/// Führt Reviewer parallel aus, bricht aber sofort ab, 
/// sobald der erste Reviewer mit Decision == Rejected oder Failed zurückkommt.
/// Spart Kosten bei teuren Reviewern.
/// </summary>
public sealed class FailFastEvaluationStrategy : IEvaluationStrategy { /* ... */ }

/// <summary>
/// Führt Reviewer nach Priorität sortiert sequenziell aus.
/// Bricht ab, sobald ein Reviewer mit Severity >= Error ablehnt.
/// Günstige/schnelle Checks (Syntax, Formatierung) laufen vor teuren (KI-Review).
/// </summary>
public sealed class PriorityOrderedEvaluationStrategy : IEvaluationStrategy { /* ... */ }
```

---

## A.7 — Observability

### A.7.1 Structured Events

```csharp
namespace Geef.Sdk.Events;

/// <summary>Marker-Interface für alle GEEF-Events.</summary>
public interface IGeefEvent
{
    /// <summary>Zeitpunkt des Events.</summary>
    DateTimeOffset Timestamp { get; }

    /// <summary>Run-ID für Korrelation.</summary>
    string RunId { get; }
}

// Konkrete Events:
public sealed record PipelineStartedEvent(string RunId, string Input, DateTimeOffset Timestamp) : IGeefEvent;
public sealed record GroundingStartedEvent(string RunId, DateTimeOffset Timestamp) : IGeefEvent;
public sealed record GroundingCompletedEvent(string RunId, GroundingResult Result, TimeSpan Duration, DateTimeOffset Timestamp) : IGeefEvent;
public sealed record ExecutionStartedEvent(string RunId, int Iteration, DateTimeOffset Timestamp) : IGeefEvent;
public sealed record ExecutionCompletedEvent(string RunId, int Iteration, ExecutionResult Result, TimeSpan Duration, DateTimeOffset Timestamp) : IGeefEvent;
public sealed record ReviewerStartedEvent(string RunId, int Iteration, string ReviewerName, DateTimeOffset Timestamp) : IGeefEvent;
public sealed record ReviewerCompletedEvent(string RunId, int Iteration, ReviewResult Result, DateTimeOffset Timestamp) : IGeefEvent;
public sealed record EvaluationRejectedEvent(string RunId, int Iteration, EvaluationAggregate Aggregate, ConvergenceDecision Decision, DateTimeOffset Timestamp) : IGeefEvent;
public sealed record EvaluationApprovedEvent(string RunId, int Iteration, EvaluationAggregate Aggregate, DateTimeOffset Timestamp) : IGeefEvent;
public sealed record FinalizeStartedEvent(string RunId, DateTimeOffset Timestamp) : IGeefEvent;
public sealed record FinalizeCompletedEvent(string RunId, TimeSpan Duration, DateTimeOffset Timestamp) : IGeefEvent;
public sealed record PipelineCompletedEvent(string RunId, bool Success, int TotalIterations, TimeSpan TotalDuration, DateTimeOffset Timestamp) : IGeefEvent;
public sealed record PipelineFailedEvent(string RunId, ConvergenceDecision Reason, int TotalIterations, IterationHistory History, DateTimeOffset Timestamp) : IGeefEvent;
```

### A.7.2 IGeefEventSink

```csharp
namespace Geef.Sdk.Events;

/// <summary>
/// Event-Senke für strukturierte Pipeline-Events.
/// Kann per DI registriert werden. Mehrere Sinks sind erlaubt (Composite Pattern).
/// </summary>
public interface IGeefEventSink
{
    ValueTask PublishAsync(IGeefEvent geefEvent, CancellationToken cancellationToken = default);
}

/// <summary>
/// Composite-Sink, die mehrere Sinks gleichzeitig bedient.
/// </summary>
public sealed class CompositeEventSink : IGeefEventSink
{
    private readonly IReadOnlyList<IGeefEventSink> _sinks;

    public CompositeEventSink(IEnumerable<IGeefEventSink> sinks) 
        => _sinks = sinks.ToList();

    public async ValueTask PublishAsync(IGeefEvent geefEvent, CancellationToken ct)
    {
        foreach (var sink in _sinks)
            await sink.PublishAsync(geefEvent, ct);
    }
}
```

### A.7.3 Mitgelieferte Event-Sinks

```csharp
namespace Geef.Sdk.Events;

/// <summary>
/// Loggt Events via ILogger (Microsoft.Extensions.Logging).
/// </summary>
public sealed class LoggingEventSink : IGeefEventSink { /* ... */ }

/// <summary>
/// Ruft konfigurierbare Delegates auf (für einfache Szenarien ohne DI).
/// Rückwärtskompatibel mit dem ursprünglichen Hook-Konzept.
/// </summary>
public sealed class DelegateEventSink : IGeefEventSink
{
    public Func<PipelineStartedEvent, Task>? OnPipelineStarted { get; set; }
    public Func<EvaluationRejectedEvent, Task>? OnEvaluationRejected { get; set; }
    public Func<EvaluationApprovedEvent, Task>? OnEvaluationApproved { get; set; }
    public Func<PipelineCompletedEvent, Task>? OnPipelineCompleted { get; set; }
    public Func<PipelineFailedEvent, Task>? OnPipelineFailed { get; set; }
    // ... weitere nach Bedarf
}
```

### A.7.4 ActivitySource-basiertes Tracing

```csharp
namespace Geef.Sdk.Diagnostics;

/// <summary>
/// Stellt eine ActivitySource für OpenTelemetry-kompatibles Distributed Tracing bereit.
/// Jeder Pipeline-Run erzeugt einen Root-Span; jede Iteration, Execution und Review 
/// erzeugen Child-Spans.
/// </summary>
public static class GeefDiagnostics
{
    public static readonly ActivitySource ActivitySource = new("Geef.Sdk", "1.0.0");
}
```

Tracing-Integration im Runner (konzeptionell):

```
Pipeline.RunAsync            → Root Activity "geef.pipeline.run"
├── Grounding                → Activity "geef.grounding"
├── Iteration 1              → Activity "geef.iteration" (tags: iteration=1)
│   ├── Execution            → Activity "geef.execution"
│   ├── Review: SecurityBot  → Activity "geef.review" (tags: reviewer=SecurityBot)
│   └── Review: StyleChecker → Activity "geef.review" (tags: reviewer=StyleChecker)
├── Iteration 2              → Activity "geef.iteration" (tags: iteration=2)
│   ├── Execution            → Activity "geef.execution"
│   └── Review: SecurityBot  → Activity "geef.review"
└── Finalize                 → Activity "geef.finalize"
```

---

## A.8 — Middleware

### A.8.1 IGeefMiddleware

```csharp
namespace Geef.Sdk.Middleware;

/// <summary>
/// Middleware für Cross-Cutting Concerns in der Pipeline.
/// Inspiriert von ASP.NET Core Middleware, aber für den GEEF-Kontext.
/// </summary>
public interface IGeefMiddleware
{
    /// <summary>
    /// Verarbeitet den Pipeline-Aufruf. MUSS next() aufrufen, um die Kette fortzusetzen,
    /// es sei denn, die Middleware will den Aufruf abbrechen.
    /// </summary>
    Task InvokeAsync(GeefMiddlewareContext middlewareContext, Func<Task> next);
}

/// <summary>
/// Kontext, der einer Middleware übergeben wird.
/// </summary>
public sealed class GeefMiddlewareContext
{
    /// <summary>Aktuelle Phase (Grounding, Execution, Evaluation, Finalize).</summary>
    public required GeefPhase Phase { get; init; }

    /// <summary>Aktueller RunContext (readonly Snapshot).</summary>
    public required IRunContext RunContext { get; init; }

    /// <summary>Name der aktuellen Komponente (z.B. Reviewer-Name).</summary>
    public string? ComponentName { get; init; }

    /// <summary>Aktuelle Iteration (für Execution/Evaluation).</summary>
    public int? Iteration { get; init; }

    /// <summary>Run-ID.</summary>
    public required string RunId { get; init; }

    /// <summary>Zusätzliche Properties (extensible).</summary>
    public IDictionary<string, object> Properties { get; } = new Dictionary<string, object>();
}

public enum GeefPhase
{
    Grounding,
    Execution,
    Evaluation,
    Finalize
}
```

### A.8.2 Mitgelieferte Middleware

```csharp
namespace Geef.Sdk.Middleware;

/// <summary>Middleware, die per-Phase-Timeouts erzwingt.</summary>
public sealed class TimeoutMiddleware : IGeefMiddleware
{
    /// <summary>Timeout pro Phase. Default: 5 Minuten.</summary>
    public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Phasenspezifische Timeouts (überschreiben DefaultTimeout).</summary>
    public Dictionary<GeefPhase, TimeSpan> PhaseTimeouts { get; init; } = [];

    public async Task InvokeAsync(GeefMiddlewareContext ctx, Func<Task> next)
    {
        var timeout = PhaseTimeouts.GetValueOrDefault(ctx.Phase, DefaultTimeout);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(/* ... */);
        cts.CancelAfter(timeout);
        await next(); // Vereinfacht — reale Implementierung braucht Token-Propagation
    }
}

/// <summary>Middleware, die jede Phase-Ausführung über ActivitySource als Span tracked.</summary>
public sealed class TracingMiddleware : IGeefMiddleware { /* ... */ }

/// <summary>Middleware, die Exceptions abfängt und in strukturierte Fehler umwandelt.</summary>
public sealed class ExceptionHandlingMiddleware : IGeefMiddleware { /* ... */ }
```

---

## A.9 — Exceptions

```csharp
namespace Geef.Sdk.Exceptions;

/// <summary>Basis-Exception für alle GEEF-spezifischen Fehler.</summary>
public class GeefException : Exception
{
    public string? RunId { get; init; }
    public GeefException(string message, string? runId = null) : base(message) => RunId = runId;
    public GeefException(string message, Exception inner, string? runId = null) : base(message, inner) => RunId = runId;
}

/// <summary>Pipeline hat das Konvergenzlimit erreicht, ohne alle Reviews zu bestehen.</summary>
public sealed class ConvergenceFailedException : GeefException
{
    public required ConvergenceDecision Reason { get; init; }
    public required IterationHistory History { get; init; }
    public required EvaluationAggregate LastEvaluation { get; init; }
    public ConvergenceFailedException(string message, string? runId = null) : base(message, runId) { }
}

/// <summary>Ein Phase-Timeout wurde überschritten.</summary>
public sealed class PhaseTimeoutException : GeefException
{
    public required GeefPhase Phase { get; init; }
    public required TimeSpan Timeout { get; init; }
    public PhaseTimeoutException(string message, string? runId = null) : base(message, runId) { }
}

/// <summary>Ein Provider (Grounding, Execution, Reviewer, Finalizer) hat einen Infrastrukturfehler ausgelöst.</summary>
public sealed class ProviderException : GeefException
{
    public required GeefPhase Phase { get; init; }
    public string? ProviderName { get; init; }
    public ProviderException(string message, Exception inner, string? runId = null) : base(message, inner, runId) { }
}

/// <summary>Die Pipeline-Konfiguration ist ungültig (z.B. fehlender Provider).</summary>
public sealed class PipelineConfigurationException : GeefException
{
    public PipelineConfigurationException(string message) : base(message) { }
}
```

---

## A.10 — Pipeline Builder und Runner

### A.10.1 GeefPipelineBuilder (Definition Layer)

```csharp
namespace Geef.Sdk;

/// <summary>
/// Fluent Builder für die Pipeline-Konfiguration.
/// Trennt Definition (Builder) von Ausführung (Runner).
/// </summary>
/// <typeparam name="TOutput">Der Output-Typ der Pipeline.</typeparam>
public sealed class GeefPipelineBuilder<TOutput>
{
    internal IGroundingStep? Grounding { get; private set; }
    internal IExecutionStep? Execution { get; private set; }
    internal List<IReviewer> Reviewers { get; } = [];
    internal IFinalizer<TOutput>? Finalizer { get; private set; }
    internal IConvergencePolicy ConvergencePolicy { get; private set; } = new DefaultConvergencePolicy();
    internal IEvaluationStrategy EvaluationStrategy { get; private set; } = new SequentialEvaluationStrategy();
    internal List<IGeefMiddleware> Middlewares { get; } = [];
    internal List<IGeefEventSink> EventSinks { get; } = [];

    // --- Fluent Methods ---

    public GeefPipelineBuilder<TOutput> UseGrounding(IGroundingStep grounding)
    { Grounding = grounding ?? throw new ArgumentNullException(nameof(grounding)); return this; }

    public GeefPipelineBuilder<TOutput> UseExecution(IExecutionStep execution)
    { Execution = execution ?? throw new ArgumentNullException(nameof(execution)); return this; }

    public GeefPipelineBuilder<TOutput> AddReviewer(IReviewer reviewer)
    { Reviewers.Add(reviewer ?? throw new ArgumentNullException(nameof(reviewer))); return this; }

    public GeefPipelineBuilder<TOutput> UseFinalizer(IFinalizer<TOutput> finalizer)
    { Finalizer = finalizer ?? throw new ArgumentNullException(nameof(finalizer)); return this; }

    public GeefPipelineBuilder<TOutput> UseConvergencePolicy(IConvergencePolicy policy)
    { ConvergencePolicy = policy ?? throw new ArgumentNullException(nameof(policy)); return this; }

    public GeefPipelineBuilder<TOutput> UseEvaluationStrategy(IEvaluationStrategy strategy)
    { EvaluationStrategy = strategy ?? throw new ArgumentNullException(nameof(strategy)); return this; }

    public GeefPipelineBuilder<TOutput> UseMiddleware(IGeefMiddleware middleware)
    { Middlewares.Add(middleware ?? throw new ArgumentNullException(nameof(middleware))); return this; }

    public GeefPipelineBuilder<TOutput> UseMiddleware<TMiddleware>() where TMiddleware : IGeefMiddleware, new()
    { Middlewares.Add(new TMiddleware()); return this; }

    public GeefPipelineBuilder<TOutput> AddEventSink(IGeefEventSink sink)
    { EventSinks.Add(sink ?? throw new ArgumentNullException(nameof(sink))); return this; }

    public GeefPipelineBuilder<TOutput> ConfigureEvents(Action<DelegateEventSink> configure)
    {
        var sink = new DelegateEventSink();
        configure(sink);
        EventSinks.Add(sink);
        return this;
    }

    // --- Build ---

    /// <summary>
    /// Validiert die Konfiguration und erzeugt einen ausführbaren Pipeline-Runner.
    /// Wirft PipelineConfigurationException bei fehlender Konfiguration.
    /// </summary>
    public GeefPipelineRunner<TOutput> Build()
    {
        if (Grounding is null) throw new PipelineConfigurationException("Grounding step is required. Call UseGrounding().");
        if (Execution is null) throw new PipelineConfigurationException("Execution step is required. Call UseExecution().");
        if (Finalizer is null) throw new PipelineConfigurationException("Finalizer is required. Call UseFinalizer().");
        if (Reviewers.Count == 0) throw new PipelineConfigurationException("At least one reviewer is required. Call AddReviewer().");

        return new GeefPipelineRunner<TOutput>(this);
    }
}

/// <summary>Statischer Einstiegspunkt.</summary>
public static class Geef
{
    public static GeefPipelineBuilder<TOutput> CreatePipeline<TOutput>() => new();
}
```

### A.10.2 GeefPipelineRunner (Runtime Layer)

```csharp
namespace Geef.Sdk;

/// <summary>
/// Der Orchestrator. Führt die GEEF-Pipeline aus: Grounding → [Execution ↔ Evaluation Loop] → Finalize.
/// Ist nach dem Build() immutable und thread-safe (kann für mehrere Runs wiederverwendet werden).
/// </summary>
/// <typeparam name="TOutput">Der Output-Typ der Pipeline.</typeparam>
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
    }

    /// <summary>
    /// Führt die vollständige GEEF-Pipeline aus.
    /// </summary>
    /// <param name="input">Der initiale Input (z.B. User-Prompt).</param>
    /// <param name="cancellationToken">Cancellation Token für den gesamten Run.</param>
    /// <returns>Das Ergebnis der Finalize-Phase.</returns>
    /// <exception cref="ConvergenceFailedException">
    /// Wenn der Loop nicht konvergiert (je nach ConvergencePolicy).
    /// </exception>
    /// <exception cref="PhaseTimeoutException">Wenn ein Phase-Timeout überschritten wird.</exception>
    /// <exception cref="ProviderException">Wenn ein Provider einen Infrastrukturfehler hat.</exception>
    public async Task<GeefPipelineResult<TOutput>> RunAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        var runId = Guid.NewGuid().ToString("N")[..12];
        var sw = System.Diagnostics.Stopwatch.StartNew();

        using var activity = GeefDiagnostics.ActivitySource.StartActivity("geef.pipeline.run");
        activity?.SetTag("geef.run_id", runId);

        await _eventSink.PublishAsync(
            new PipelineStartedEvent(runId, input, DateTimeOffset.UtcNow), cancellationToken);

        // --- 1. GROUNDING ---
        await _eventSink.PublishAsync(
            new GroundingStartedEvent(runId, DateTimeOffset.UtcNow), cancellationToken);

        var groundingSw = System.Diagnostics.Stopwatch.StartNew();
        GroundingResult groundingResult;
        try
        {
            groundingResult = await _grounding.RunAsync(input, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ProviderException(
                $"Grounding step failed: {ex.Message}", ex, runId) { Phase = GeefPhase.Grounding };
        }
        groundingSw.Stop();

        var context = groundingResult.Context
            .Set(GeefKeys.OriginalInput, input)
            .Set(GeefKeys.RunId, runId)
            .Set(GeefKeys.RunStartedAt, DateTimeOffset.UtcNow)
            .Set(GeefKeys.CurrentIteration, 0);

        await _eventSink.PublishAsync(
            new GroundingCompletedEvent(runId, groundingResult, groundingSw.Elapsed, DateTimeOffset.UtcNow), cancellationToken);

        // --- 2 & 3. EXECUTION ↔ EVALUATION LOOP ---
        var iterationHistory = new IterationHistory();
        context = context.Set(GeefKeys.IterationHistory, iterationHistory);
        ConvergenceDecision convergenceDecision = ConvergenceDecision.Continue;
        EvaluationAggregate? lastAggregate = null;

        while (convergenceDecision == ConvergenceDecision.Continue)
        {
            var iteration = context.GetRequired<int>(GeefKeys.CurrentIteration) + 1;
            context = context.Set(GeefKeys.CurrentIteration, iteration);

            var iterationStart = DateTimeOffset.UtcNow;
            using var iterActivity = GeefDiagnostics.ActivitySource.StartActivity("geef.iteration");
            iterActivity?.SetTag("geef.iteration", iteration);

            // --- Execution ---
            await _eventSink.PublishAsync(
                new ExecutionStartedEvent(runId, iteration, DateTimeOffset.UtcNow), cancellationToken);

            var execSw = System.Diagnostics.Stopwatch.StartNew();
            ExecutionResult executionResult;
            try
            {
                executionResult = await _execution.RunAsync(context, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new ProviderException(
                    $"Execution step failed in iteration {iteration}: {ex.Message}", ex, runId) 
                    { Phase = GeefPhase.Execution, ProviderName = "Execution" };
            }
            execSw.Stop();

            context = executionResult.UpdatedContext
                .Set(GeefKeys.CurrentIteration, iteration)
                .Set(GeefKeys.RunId, runId)
                .Set(GeefKeys.IterationHistory, iterationHistory);

            await _eventSink.PublishAsync(
                new ExecutionCompletedEvent(runId, iteration, executionResult, execSw.Elapsed, DateTimeOffset.UtcNow), cancellationToken);

            // --- Evaluation ---
            EvaluationAggregate aggregate;
            try
            {
                aggregate = await _evaluationStrategy.ExecuteAsync(_reviewers, context, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new ProviderException(
                    $"Evaluation strategy failed in iteration {iteration}: {ex.Message}", ex, runId) 
                    { Phase = GeefPhase.Evaluation };
            }
            lastAggregate = aggregate;

            // Iteration aufzeichnen
            var record = new IterationRecord
            {
                Iteration = iteration,
                StartedAt = iterationStart,
                ExecutionDuration = execSw.Elapsed,
                EvaluationResult = aggregate
            };
            iterationHistory.Add(record);

            // Konvergenz prüfen
            convergenceDecision = _convergencePolicy.Evaluate(
                iterationHistory, aggregate, sw.Elapsed);

            if (convergenceDecision == ConvergenceDecision.Approved)
            {
                await _eventSink.PublishAsync(
                    new EvaluationApprovedEvent(runId, iteration, aggregate, DateTimeOffset.UtcNow), cancellationToken);
            }
            else if (convergenceDecision == ConvergenceDecision.Continue)
            {
                // Findings in den Kontext schreiben für die nächste Execution
                context = context.Set(GeefKeys.PreviousFindings, aggregate.AllFindings);
                await _eventSink.PublishAsync(
                    new EvaluationRejectedEvent(runId, iteration, aggregate, convergenceDecision, DateTimeOffset.UtcNow), cancellationToken);
            }
            else
            {
                // Abbruch (Stagnation, Regression, MaxAttempts, Critical, Escalation)
                await _eventSink.PublishAsync(
                    new PipelineFailedEvent(runId, convergenceDecision, iteration, iterationHistory, DateTimeOffset.UtcNow), cancellationToken);

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

        // --- 4. FINALIZE ---
        await _eventSink.PublishAsync(
            new FinalizeStartedEvent(runId, DateTimeOffset.UtcNow), cancellationToken);

        var finalizeSw = System.Diagnostics.Stopwatch.StartNew();
        FinalizeResult<TOutput> finalizeResult;
        try
        {
            finalizeResult = await _finalizer.FinalizeAsync(context, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ProviderException(
                $"Finalizer failed: {ex.Message}", ex, runId) { Phase = GeefPhase.Finalize };
        }
        finalizeSw.Stop();

        await _eventSink.PublishAsync(
            new FinalizeCompletedEvent(runId, finalizeSw.Elapsed, DateTimeOffset.UtcNow), cancellationToken);

        sw.Stop();
        await _eventSink.PublishAsync(
            new PipelineCompletedEvent(runId, true, iterationHistory.Count, sw.Elapsed, DateTimeOffset.UtcNow), cancellationToken);

        return new GeefPipelineResult<TOutput>
        {
            Output = finalizeResult.Output,
            RunId = runId,
            TotalIterations = iterationHistory.Count,
            TotalDuration = sw.Elapsed,
            FinalContext = finalizeResult.FinalContext,
            History = iterationHistory,
            Success = true
        };
    }
}
```

### A.10.3 GeefPipelineResult\<TOutput\>

```csharp
namespace Geef.Sdk;

/// <summary>
/// Das vollständige Ergebnis eines Pipeline-Runs.
/// </summary>
public sealed record GeefPipelineResult<TOutput>
{
    /// <summary>Das finale Ergebnis.</summary>
    public required TOutput Output { get; init; }

    /// <summary>Eindeutige Run-ID.</summary>
    public required string RunId { get; init; }

    /// <summary>Gesamtanzahl der Iterationen.</summary>
    public required int TotalIterations { get; init; }

    /// <summary>Gesamtlaufzeit.</summary>
    public required TimeSpan TotalDuration { get; init; }

    /// <summary>Erfolg oder Misserfolg.</summary>
    public required bool Success { get; init; }

    /// <summary>Der finale Kontext-Snapshot.</summary>
    public required IRunContext FinalContext { get; init; }

    /// <summary>Vollständige Iterationshistorie.</summary>
    public required IterationHistory History { get; init; }
}
```

---

## A.11 — DI Integration (Hosting Layer)

```csharp
namespace Geef.Sdk.Hosting;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension Methods für die Integration in Microsoft.Extensions.DependencyInjection.
/// </summary>
public static class GeefServiceCollectionExtensions
{
    /// <summary>
    /// Registriert eine GEEF-Pipeline im DI Container.
    /// </summary>
    public static IServiceCollection AddGeefPipeline<TOutput>(
        this IServiceCollection services,
        Action<GeefPipelineBuilder<TOutput>> configure)
    {
        var builder = new GeefPipelineBuilder<TOutput>();
        configure(builder);
        var runner = builder.Build();
        services.AddSingleton(runner);
        return services;
    }

    /// <summary>
    /// Registriert eine GEEF-Pipeline mit Zugriff auf den ServiceProvider 
    /// (für DI-aufgelöste Provider).
    /// </summary>
    public static IServiceCollection AddGeefPipeline<TOutput>(
        this IServiceCollection services,
        Action<IServiceProvider, GeefPipelineBuilder<TOutput>> configure)
    {
        services.AddSingleton(sp =>
        {
            var builder = new GeefPipelineBuilder<TOutput>();
            configure(sp, builder);
            return builder.Build();
        });
        return services;
    }
}
```

---

## A.12 — Projekt- und Paketstruktur

```
Geef.Sdk/
├── Geef.Sdk.sln
├── src/
│   └── Geef.Sdk/
│       ├── Geef.Sdk.csproj                    # NuGet-Paket
│       ├── Geef.cs                            # Statischer Einstiegspunkt
│       ├── GeefPipelineBuilder.cs
│       ├── GeefPipelineRunner.cs
│       ├── GeefPipelineResult.cs
│       ├── Context/
│       │   ├── ContextKey.cs
│       │   ├── IRunContext.cs
│       │   ├── RunContext.cs
│       │   └── GeefKeys.cs
│       ├── Providers/
│       │   ├── IGroundingStep.cs
│       │   ├── IExecutionStep.cs
│       │   ├── IReviewer.cs
│       │   └── IFinalizer.cs
│       ├── Results/
│       │   ├── GroundingResult.cs
│       │   ├── ExecutionResult.cs
│       │   ├── Finding.cs
│       │   ├── FindingSeverity.cs
│       │   ├── ReviewResult.cs
│       │   ├── ReviewDecision.cs
│       │   ├── EvaluationAggregate.cs
│       │   └── FinalizeResult.cs
│       ├── Policies/
│       │   ├── IConvergencePolicy.cs
│       │   ├── DefaultConvergencePolicy.cs
│       │   ├── IEvaluationStrategy.cs
│       │   ├── SequentialEvaluationStrategy.cs
│       │   ├── ParallelEvaluationStrategy.cs
│       │   ├── FailFastEvaluationStrategy.cs
│       │   └── PriorityOrderedEvaluationStrategy.cs
│       ├── Events/
│       │   ├── IGeefEvent.cs
│       │   ├── IGeefEventSink.cs
│       │   ├── CompositeEventSink.cs
│       │   ├── NullEventSink.cs
│       │   ├── LoggingEventSink.cs
│       │   ├── DelegateEventSink.cs
│       │   └── Events.cs                     # Alle konkreten Event-Records
│       ├── Middleware/
│       │   ├── IGeefMiddleware.cs
│       │   ├── GeefMiddlewareContext.cs
│       │   ├── GeefPhase.cs
│       │   ├── TimeoutMiddleware.cs
│       │   ├── TracingMiddleware.cs
│       │   └── ExceptionHandlingMiddleware.cs
│       ├── Runtime/
│       │   ├── IterationRecord.cs
│       │   └── IterationHistory.cs
│       ├── Exceptions/
│       │   ├── GeefException.cs
│       │   ├── ConvergenceFailedException.cs
│       │   ├── PhaseTimeoutException.cs
│       │   ├── ProviderException.cs
│       │   └── PipelineConfigurationException.cs
│       ├── Diagnostics/
│       │   └── GeefDiagnostics.cs
│       └── Hosting/
│           └── GeefServiceCollectionExtensions.cs
└── tests/
    └── Geef.Sdk.Tests/
        ├── Geef.Sdk.Tests.csproj
        ├── Context/
        │   └── RunContextTests.cs
        ├── Policies/
        │   ├── DefaultConvergencePolicyTests.cs
        │   └── EvaluationStrategyTests.cs
        ├── Runtime/
        │   ├── IterationHistoryTests.cs
        │   └── GeefPipelineRunnerTests.cs
        ├── Builder/
        │   └── GeefPipelineBuilderTests.cs
        └── Integration/
            └── FullPipelineIntegrationTests.cs
```

---

## A.13 — Abhängigkeiten

```xml
<!-- Geef.Sdk.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>12</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Geef.Sdk</RootNamespace>
    <PackageId>Geef.Sdk</PackageId>
    <Version>1.0.0</Version>
    <Description>GEEF Pipeline SDK — A policy-driven orchestration framework for AI agent workflows with typed context, convergence policies, and structured observability.</Description>
    <PackageTags>ai;agent;pipeline;orchestration;geef;review-loop</PackageTags>
  </PropertyGroup>

  <ItemGroup>
    <!-- Einzige externe Abhängigkeiten — bewusst minimal -->
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="8.0.*" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.*" />
    <!-- System.Collections.Immutable ist ab .NET 8 bereits enthalten -->
  </ItemGroup>
</Project>

<!-- Geef.Sdk.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
    <PackageReference Include="NSubstitute" Version="5.*" />
    <PackageReference Include="FluentAssertions" Version="7.*" />
    <ProjectReference Include="../../src/Geef.Sdk/Geef.Sdk.csproj" />
  </ItemGroup>
</Project>
```

---

# Teil B — Anforderungskatalog

---

## B.1 — Funktionale Anforderungen

| ID | Kategorie | Anforderung | Priorität |
|---|---|---|---|
| F-001 | Pipeline | Die Pipeline MUSS die vier Phasen Grounding, Execution, Evaluation, Finalize in dieser Reihenfolge durchlaufen. | MUSS |
| F-002 | Pipeline | Die Pipeline MUSS einen Feedback-Loop zwischen Execution und Evaluation implementieren: Bei nicht bestandener Evaluation MUSS die Execution mit dem Feedback (PreviousFindings) wiederholt werden. | MUSS |
| F-003 | Pipeline | Die Pipeline MUSS nach erfolgreicher Evaluation (alle Reviewer bestanden) automatisch zur Finalize-Phase übergehen. | MUSS |
| F-004 | Context | Der Context Store MUSS typisierte Schlüssel (`ContextKey<T>`) verwenden. Kein `IDictionary<string, object>`. | MUSS |
| F-005 | Context | Der RunContext MUSS immutable sein. Jeder `Set()`/`Remove()`-Aufruf MUSS eine neue Instanz zurückgeben. | MUSS |
| F-006 | Context | Der RunContext MUSS thread-safe für gleichzeitige Lesezugriffe sein. | MUSS |
| F-007 | Context | `GetRequired<T>()` MUSS eine `KeyNotFoundException` mit aussagekräftiger Fehlermeldung werfen, wenn der Schlüssel nicht existiert. Die Meldung MUSS den fehlenden Key-Namen und die verfügbaren Keys enthalten. | MUSS |
| F-008 | Results | Alle Phasen MÜSSEN strukturierte Ergebnisobjekte zurückgeben (GroundingResult, ExecutionResult, ReviewResult, FinalizeResult). Keine Seiteneffekte auf den Kontext. | MUSS |
| F-009 | Results | Jedes Finding MUSS einen `Fingerprint` enthalten, der Stagnations- und Regressionserkennung ermöglicht. | MUSS |
| F-010 | Results | ReviewResult MUSS eine `ReviewDecision` enthalten mit den Werten: Approved, Rejected, ApprovedWithWarnings, RetrySuggested, NotApplicable, Failed. | MUSS |
| F-011 | Results | ReviewResult MUSS die Dauer des Reviews als `TimeSpan` enthalten. | MUSS |
| F-012 | Results | EvaluationAggregate MUSS Properties `HasBlockingIssues` und `IsFullyApproved` bereitstellen. | MUSS |
| F-013 | Convergence | Die Pipeline MUSS eine konfigurierbare `IConvergencePolicy` verwenden statt eines einfachen MaxIterations-Zählers. | MUSS |
| F-014 | Convergence | Die `DefaultConvergencePolicy` MUSS folgende Abbruchgründe unterstützen: MaxIterations, MaxElapsedTime, Stagnation, Regression, CriticalBlocker. | MUSS |
| F-015 | Convergence | Stagnationserkennung MUSS auf Finding-Fingerprint-Vergleich basieren, nicht auf String-Vergleich der Messages. | MUSS |
| F-016 | Convergence | Regressionserkennung MUSS erkennen, wenn ein zuvor behobener Finding-Fingerprint wieder auftaucht. | MUSS |
| F-017 | Convergence | Die ConvergenceDecision MUSS mindestens folgende Werte unterstützen: Approved, Continue, StopMaxAttemptsReached, StopStagnant, StopRegression, AbortCriticalBlocker, EscalateToHuman. | MUSS |
| F-018 | Evaluation | Die Pipeline MUSS eine konfigurierbare `IEvaluationStrategy` verwenden, die bestimmt, wie Reviewer ausgeführt werden. | MUSS |
| F-019 | Evaluation | Es MÜSSEN mindestens vier Strategien mitgeliefert werden: Sequential, Parallel, FailFast, PriorityOrdered. | MUSS |
| F-020 | Evaluation | Bei der PriorityOrdered-Strategie MÜSSEN Reviewer nach ihrem `Priority`-Wert sortiert ausgeführt werden (niedrigerer Wert = höhere Priorität). | MUSS |
| F-021 | Evaluation | Bei der FailFast-Strategie MUSS die Ausführung abgebrochen werden, sobald der erste Reviewer Rejected oder Failed zurückgibt. | MUSS |
| F-022 | Builder | Der Builder MUSS eine Fluent API bereitstellen mit den Methoden: UseGrounding, UseExecution, AddReviewer, UseFinalizer, UseConvergencePolicy, UseEvaluationStrategy, UseMiddleware, AddEventSink, ConfigureEvents. | MUSS |
| F-023 | Builder | Der Builder MUSS bei `Build()` validieren, dass Grounding, Execution, mindestens ein Reviewer und Finalizer konfiguriert sind. Bei fehlender Konfiguration MUSS eine `PipelineConfigurationException` geworfen werden. | MUSS |
| F-024 | Builder | Wenn keine ConvergencePolicy explizit gesetzt wird, MUSS die DefaultConvergencePolicy verwendet werden. | MUSS |
| F-025 | Builder | Wenn keine EvaluationStrategy explizit gesetzt wird, MUSS die SequentialEvaluationStrategy verwendet werden. | MUSS |
| F-026 | Runner | Der Runner MUSS nach `Build()` immutable und thread-safe sein. Er MUSS für mehrere gleichzeitige Runs wiederverwendbar sein. | MUSS |
| F-027 | Runner | Der Runner MUSS eine eindeutige Run-ID pro Aufruf generieren und im Context unter `GeefKeys.RunId` ablegen. | MUSS |
| F-028 | Runner | Der Runner MUSS `CancellationToken` an alle Provider-Aufrufe durchreichen. | MUSS |
| F-029 | History | Die vollständige `IterationHistory` MUSS während des Runs aufgebaut und über `GeefKeys.IterationHistory` im Context verfügbar sein. | MUSS |
| F-030 | History | Jeder `IterationRecord` MUSS Iterationsnummer, Startzeitpunkt, Execution-Dauer und EvaluationAggregate enthalten. | MUSS |
| F-031 | Findings | Findings MÜSSEN die Felder ReviewerName, Fingerprint, Message, Severity enthalten. | MUSS |
| F-032 | Findings | FindingSeverity MUSS die Werte Info, Warning, Error, Critical unterstützen. | MUSS |
| F-033 | Result | `GeefPipelineResult<TOutput>` MUSS Output, RunId, TotalIterations, TotalDuration, Success, FinalContext und History enthalten. | MUSS |
| F-034 | Providers | Alle Provider-Interfaces MÜSSEN `CancellationToken` als Parameter akzeptieren (mit Default `default`). | MUSS |
| F-035 | Providers | IReviewer MUSS ein `Name`-Property und ein `Priority`-Property (Default: 100) bereitstellen. | MUSS |
| F-036 | DI | Es MUSS eine `AddGeefPipeline<TOutput>` Extension Method für `IServiceCollection` geben. | MUSS |
| F-037 | Context | Der RunContext MUSS `GeefKeys.PreviousFindings` nach einer fehlgeschlagenen Evaluation setzen, bevor die nächste Execution startet. | MUSS |

## B.2 — Nicht-funktionale Anforderungen

| ID | Kategorie | Anforderung | Priorität |
|---|---|---|---|
| NF-001 | Events | Für jede Phasen-Transition MUSS ein strukturiertes Event über `IGeefEventSink` publiziert werden (Started/Completed für jede Phase, Rejected, Approved, PipelineFailed). | MUSS |
| NF-002 | Events | Alle Events MÜSSEN `Timestamp` und `RunId` enthalten. | MUSS |
| NF-003 | Events | Es MUSS eine `CompositeEventSink` geben, die mehrere Sinks gleichzeitig bedient. | MUSS |
| NF-004 | Events | Es MUSS eine `NullEventSink` geben (No-Op), die als Default verwendet wird, wenn keine Sinks konfiguriert sind. | MUSS |
| NF-005 | Events | Es MUSS eine `DelegateEventSink` geben für einfache Szenarien ohne DI. | MUSS |
| NF-006 | Events | Es MUSS eine `LoggingEventSink` geben, die Events via `ILogger` loggt. | MUSS |
| NF-007 | Tracing | Es MUSS eine `ActivitySource` namens "Geef.Sdk" geben für OpenTelemetry-kompatibles Tracing. | MUSS |
| NF-008 | Tracing | Der Runner MUSS Activities für Pipeline-Run, Grounding, jede Iteration, jede Execution und jedes Review erzeugen. | MUSS |
| NF-009 | Tracing | Jede Activity MUSS relevante Tags setzen (run_id, iteration, reviewer name, etc.). | MUSS |
| NF-010 | Exceptions | Es MUSS eine Exception-Hierarchie geben: GeefException (Basis), ConvergenceFailedException, PhaseTimeoutException, ProviderException, PipelineConfigurationException. | MUSS |
| NF-011 | Exceptions | ConvergenceFailedException MUSS Reason (ConvergenceDecision), History und LastEvaluation enthalten. | MUSS |
| NF-012 | Exceptions | ProviderException MUSS Phase und ProviderName enthalten und die Inner Exception einschließen. | MUSS |
| NF-013 | Exceptions | Infrastrukturfehler von Providern MÜSSEN als `ProviderException` gewrapped werden (kein nacktes `throw`). | MUSS |
| NF-014 | Middleware | Es MUSS ein `IGeefMiddleware`-Interface mit `InvokeAsync(GeefMiddlewareContext, Func<Task> next)` geben. | MUSS |
| NF-015 | Middleware | Es MUSS eine `TimeoutMiddleware` geben mit konfigurierbaren per-Phase-Timeouts. | MUSS |
| NF-016 | Middleware | Es MUSS eine `TracingMiddleware` geben, die Activities über `GeefDiagnostics.ActivitySource` erzeugt. | MUSS |
| NF-017 | Abhängigkeiten | Das SDK DARF nur von `Microsoft.Extensions.DependencyInjection.Abstractions` und `Microsoft.Extensions.Logging.Abstractions` abhängen. Keine weiteren externen Pakete. | MUSS |
| NF-018 | Zielplattform | Das SDK MUSS .NET 8+ targeten mit C# 12, Nullable Reference Types enabled. | MUSS |
| NF-019 | Tests | Es MÜSSEN Unit-Tests für RunContext, DefaultConvergencePolicy, IterationHistory, alle EvaluationStrategies und den GeefPipelineBuilder geben. | MUSS |
| NF-020 | Tests | Es MUSS mindestens einen Integrationstest geben, der einen vollständigen Pipeline-Durchlauf mit Mock-Providern simuliert (inkl. mindestens einer fehlgeschlagenen Iteration). | MUSS |
| NF-021 | Tests | Tests MÜSSEN xUnit, NSubstitute und FluentAssertions verwenden. | MUSS |

---

# Teil C — Nutzungsbeispiele (Referenz für Implementierung)

## C.1 — Minimales Beispiel

```csharp
using Geef.Sdk;
using Geef.Sdk.Context;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;

// Eigene ContextKeys definieren
public static class MyKeys
{
    public static readonly ContextKey<string> GeneratedCode = new("my:generated-code");
    public static readonly ContextKey<string> FilePath = new("my:file-path");
}

// Grounding: Kontext aufbauen
public class SimpleGrounding : IGroundingStep
{
    public Task<GroundingResult> RunAsync(string input, CancellationToken ct)
    {
        var context = new RunContext()
            .Set(MyKeys.FilePath, "/src/Button.tsx");

        return Task.FromResult(new GroundingResult { Context = context });
    }
}

// Execution: Code generieren
public class SimpleExecution : IExecutionStep
{
    public Task<ExecutionResult> RunAsync(IRunContext context, CancellationToken ct)
    {
        var filePath = context.GetRequired<string>(MyKeys.FilePath);
        
        // Prüfen, ob es Feedback aus der vorherigen Runde gibt
        if (context.TryGet(GeefKeys.PreviousFindings, out var findings) && findings?.Count > 0)
        {
            // Fehler korrigieren basierend auf Feedback
        }

        var code = $"// Generated code for {filePath}\nexport const Button = () => <button>Click</button>;";
        var updatedContext = context.Set(MyKeys.GeneratedCode, code);

        return Task.FromResult(new ExecutionResult { UpdatedContext = updatedContext });
    }
}

// Reviewer: Code prüfen
public class SimpleReviewer : IReviewer
{
    public string Name => "SyntaxChecker";
    public int Priority => 10;

    public Task<ReviewResult> ReviewAsync(IRunContext context, CancellationToken ct)
    {
        var code = context.GetRequired<string>(MyKeys.GeneratedCode);
        var findings = new List<Finding>();

        if (!code.Contains("export"))
        {
            findings.Add(new Finding
            {
                ReviewerName = Name,
                Fingerprint = $"{Name}:missing-export",
                Message = "Missing export statement.",
                Severity = FindingSeverity.Error,
                Category = "Syntax"
            });
        }

        return Task.FromResult(new ReviewResult
        {
            ReviewerName = Name,
            Decision = findings.Any() ? ReviewDecision.Rejected : ReviewDecision.Approved,
            Findings = findings,
            Duration = TimeSpan.FromMilliseconds(50)
        });
    }
}

// Finalizer: Ergebnis aufbereiten
public class SimpleFinalizer : IFinalizer<string>
{
    public Task<FinalizeResult<string>> FinalizeAsync(IRunContext context, CancellationToken ct)
    {
        var code = context.GetRequired<string>(MyKeys.GeneratedCode);
        return Task.FromResult(new FinalizeResult<string>
        {
            Output = code,
            FinalContext = context,
            Summary = "Code successfully generated and reviewed."
        });
    }
}

// Pipeline zusammenbauen und ausführen
var pipeline = Geef.CreatePipeline<string>()
    .UseGrounding(new SimpleGrounding())
    .UseExecution(new SimpleExecution())
    .AddReviewer(new SimpleReviewer())
    .UseFinalizer(new SimpleFinalizer())
    .ConfigureEvents(events =>
    {
        events.OnEvaluationRejected = async (evt) =>
            Console.WriteLine($"Iteration {evt.Iteration} failed: {evt.Aggregate.AllFindings.Count} findings.");
        events.OnEvaluationApproved = async (evt) =>
            Console.WriteLine($"Approved after {evt.Iteration} iterations!");
    })
    .Build();

var result = await pipeline.RunAsync("Add a contact button to the homepage");
Console.WriteLine($"Done! Output: {result.Output}");
Console.WriteLine($"Iterations: {result.TotalIterations}, Duration: {result.TotalDuration}");
```

## C.2 — Fortgeschrittenes Beispiel mit mehreren Reviewern und Policies

```csharp
var pipeline = Geef.CreatePipeline<GitCommitResult>()
    .UseGrounding(new ClaudeCodeGroundingProvider(claudeClient))
    .UseExecution(new ClaudeCodeExecutionProvider(claudeClient))
    .AddReviewer(new SyntaxReviewer { Priority = 10 })        // Schnell & günstig → zuerst
    .AddReviewer(new SecurityReviewer { Priority = 20 })       // Mittel
    .AddReviewer(new ClaudeCodeReviewer { Priority = 50 })     // Teuer → zuletzt
    .UseFinalizer(new GitCommitFinalizer(gitClient))
    .UseConvergencePolicy(new DefaultConvergencePolicy
    {
        MaxIterations = 15,
        MaxElapsedTime = TimeSpan.FromMinutes(20),
        StagnationThreshold = 3,
        AbortOnCritical = true,
        DetectRegression = true
    })
    .UseEvaluationStrategy(new PriorityOrderedEvaluationStrategy())
    .UseMiddleware(new TimeoutMiddleware
    {
        DefaultTimeout = TimeSpan.FromMinutes(5),
        PhaseTimeouts = new()
        {
            [GeefPhase.Grounding] = TimeSpan.FromMinutes(2),
            [GeefPhase.Execution] = TimeSpan.FromMinutes(10),
        }
    })
    .AddEventSink(new LoggingEventSink(logger))
    .Build();

try
{
    var result = await pipeline.RunAsync(
        "Implement user authentication with JWT tokens",
        cancellationToken);

    Console.WriteLine($"Commit: {result.Output.CommitHash}");
}
catch (ConvergenceFailedException ex)
{
    Console.WriteLine($"Failed: {ex.Reason}");
    Console.WriteLine($"Last findings: {string.Join(", ", ex.LastEvaluation.AllFindings.Select(f => f.Message))}");
}
```

## C.3 — ASP.NET Core Integration

```csharp
// In Program.cs oder Startup.cs
builder.Services.AddGeefPipeline<CodeGenerationResult>((sp, pipeline) =>
{
    var claudeClient = sp.GetRequiredService<IClaudeClient>();
    var logger = sp.GetRequiredService<ILogger<GeefPipelineRunner<CodeGenerationResult>>>();

    pipeline
        .UseGrounding(new ClaudeGrounding(claudeClient))
        .UseExecution(new ClaudeExecution(claudeClient))
        .AddReviewer(new SyntaxReviewer())
        .AddReviewer(new SecurityReviewer())
        .UseFinalizer(new CodeOutputFinalizer())
        .AddEventSink(new LoggingEventSink(logger));
});

// In einem Controller oder Service
public class CodeController(GeefPipelineRunner<CodeGenerationResult> pipeline)
{
    [HttpPost("generate")]
    public async Task<IActionResult> Generate([FromBody] string prompt, CancellationToken ct)
    {
        var result = await pipeline.RunAsync(prompt, ct);
        return Ok(new { result.Output, result.TotalIterations, result.TotalDuration });
    }
}
```

---

# Teil D — Claude Code Prompt

Der folgende Prompt ist so formuliert, dass er direkt an Claude Code übergeben werden kann. Er verweist auf dieses Architekturdokument (Teile A–C) als autoritative Spezifikation. Die Datei `GEEF_SDK_V1_Architecture.md` muss im Projektverzeichnis liegen oder Claude Code als Kontext übergeben werden.

---

```
Du bist ein erfahrener .NET/C#-Architekt. Deine Aufgabe ist es, das GEEF SDK als vollständige, kompilierfähige C#-Bibliothek zu implementieren.

## Spezifikation

Die vollständige Architekturspezifikation findest du in der Datei `GEEF_SDK_V1_Architecture.md`. Lies diese Datei ZUERST vollständig durch, bevor du mit der Implementierung beginnst. Das Dokument enthält:

- **Teil A — Architekturspezifikation:** Alle Interfaces, Records, Enums, Default-Implementierungen, die Projektstruktur und Abhängigkeiten. Jede Klasse ist mit vollständigem C#-Code und XML-Dokumentation ausformuliert. Implementiere die Interfaces, Signaturen und Typen EXAKT wie dort definiert.
- **Teil B — Anforderungskatalog:** 37 funktionale und 21 nicht-funktionale Anforderungen mit IDs (F-001 bis F-037, NF-001 bis NF-021). Jede einzelne MUSS-Anforderung muss erfüllt sein.
- **Teil C — Nutzungsbeispiele:** Drei Referenzbeispiele (Minimal, Fortgeschritten, ASP.NET Core), die zeigen, wie die fertige API konsumiert wird. Diese Beispiele dienen als Akzeptanzkriterium: Die fertige API MUSS diese Nutzungsmuster ermöglichen.

## Deine Aufgabe

Erstelle ein vollständiges .NET 8 Solution mit zwei Projekten:
1. `src/Geef.Sdk/` — Die Bibliothek (Class Library)
2. `tests/Geef.Sdk.Tests/` — Die Tests (xUnit)

## Technische Rahmenbedingungen

- .NET 8, C# 12, Nullable Reference Types enabled, Implicit Usings enabled
- Keine externen Abhängigkeiten außer:
  - Microsoft.Extensions.DependencyInjection.Abstractions (8.x)
  - Microsoft.Extensions.Logging.Abstractions (8.x)
- Tests mit xUnit, NSubstitute, FluentAssertions
- Jede öffentliche Klasse, Interface, Methode und Property MUSS einen vollständigen XML-Dokumentationskommentar haben (/// <summary>)
- Alle Code-Kommentare und Dokumentation auf Englisch

## Architektur-Kernentscheidungen (NICHT verhandelbar)

Diese Punkte sind im Architekturdokument ausführlich beschrieben. Hier die Kurzfassung als Checkliste — bei Unklarheiten gilt die Spezifikation in Teil A:

1. **Typed Context Store** — `ContextKey<T>` + immutable `RunContext` auf `ImmutableDictionary`. KEIN `IDictionary<string, object>`. Siehe A.2.
2. **Result Objects** — Jede Phase gibt ein strukturiertes Ergebnis zurück (GroundingResult, ExecutionResult, ReviewResult, FinalizeResult). Keine Seiteneffekte auf den Kontext. Siehe A.3.
3. **IConvergencePolicy** — Ersetzt den simplen MaxIterations-Zähler. DefaultConvergencePolicy mit Stagnation, Regression, Zeitbudget, Critical-Abort. Siehe A.6.1 + A.6.2.
4. **IEvaluationStrategy** — Vier Strategien: Sequential, Parallel, FailFast, PriorityOrdered. Siehe A.6.3 + A.6.4.
5. **ReviewDecision** — Differenziertes Enum mit 6 Werten (Approved, Rejected, ApprovedWithWarnings, RetrySuggested, NotApplicable, Failed). Siehe A.3.5.
6. **Finding mit Fingerprint** — Jedes Finding hat einen Fingerprint für Konvergenzanalyse. Siehe A.3.4.
7. **IterationHistory** — Mit `IsStagnant()` und `HasRegression()`. Wird im Context gespeichert. Siehe A.4.
8. **Strukturierte Events** — `IGeefEventSink` + Event-Records für jede Phasen-Transition. Vier Sinks: Composite, Null, Delegate, Logging. Siehe A.7.
9. **Middleware** — `IGeefMiddleware` mit drei Implementierungen: Timeout, Tracing, ExceptionHandling. Siehe A.8.
10. **Exception-Hierarchie** — 5 domänenspezifische Exception-Klassen. Siehe A.9.
11. **ActivitySource** — "Geef.Sdk" für OpenTelemetry-kompatibles Tracing mit Spans für jeden Phasenschritt. Siehe A.7.4.
12. **Builder + Runner Trennung** — `GeefPipelineBuilder<TOutput>` (Konfiguration) + `GeefPipelineRunner<TOutput>` (immutable, ausführbar). Siehe A.10.
13. **DI Integration** — `AddGeefPipeline<TOutput>` Extension Methods. Siehe A.11.

## Projektstruktur

Exakt wie in Teil A, Abschnitt A.12 definiert. Halte dich an die dort beschriebene Ordner- und Dateistruktur.

## Qualitätsanforderungen

1. ALLES MUSS kompilieren. Führe `dotnet build` aus und behebe alle Fehler.
2. ALLE Tests MÜSSEN grün sein. Führe `dotnet test` aus und behebe alle Fehler.
3. Jeder public Type MUSS XML-Dokumentationskommentare haben.
4. Keine Warnings bei `dotnet build`.
5. Prüfe am Ende nochmal die Vollständigkeit: Jedes Interface, jede Klasse, jedes Enum aus der Spezifikation (Teil A) MUSS vorhanden sein.
6. Die NullEventSink MUSS als Default verwendet werden, wenn keine Sinks konfiguriert sind.
7. Der Runner MUSS Infrastrukturfehler von Providern als ProviderException wrappen.
8. Der Runner MUSS OperationCanceledException NICHT wrappen (durchlassen).
9. Activities (Tracing) MÜSSEN im Runner erzeugt werden, nicht nur in der TracingMiddleware.
10. Prüfe jeden Anforderungspunkt aus Teil B (F-001 bis F-037, NF-001 bis NF-021) und stelle sicher, dass er erfüllt ist.

## Implementierungsreihenfolge (empfohlen)

Arbeite Bottom-Up von den Grundtypen zum Orchestrator:

1. Context/ (ContextKey, IRunContext, RunContext, GeefKeys) + Tests
2. Results/ (alle Records und Enums)
3. Providers/ (alle Interfaces)
4. Runtime/ (IterationRecord, IterationHistory) + Tests
5. Policies/ (IConvergencePolicy, DefaultConvergencePolicy) + Tests
6. Policies/ (IEvaluationStrategy, alle 4 Strategien) + Tests
7. Events/ (alle Interfaces, Records, Sinks)
8. Exceptions/ (alle 5 Klassen)
9. Middleware/ (Interface, Context, Phase, 3 Implementierungen)
10. Diagnostics/ (GeefDiagnostics)
11. GeefPipelineBuilder + Tests
12. GeefPipelineRunner + Integrationstests
13. GeefPipelineResult
14. Geef.cs (statischer Einstiegspunkt)
15. Hosting/ (DI Extensions)
16. Finaler Build + Test + Cleanup

Führe nach jedem größeren Block `dotnet build` aus, um Fehler frühzeitig zu erkennen. Am Ende muss `dotnet test` ohne Fehler durchlaufen.

Starte jetzt: Lies die Datei `GEEF_SDK_V1_Architecture.md`, dann beginne mit Schritt 1.
```

---

*Ende des Dokuments*
