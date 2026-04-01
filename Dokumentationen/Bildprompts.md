# Bild-KI-Prompts für Dokumentations-Grafiken

Prompts zur Generierung von Schaubildern und Diagrammen für die Geef.Sdk-Dokumentation.
Optimiert für Midjourney, DALL-E 3, Stable Diffusion oder vergleichbare Bild-KIs.

---

## 1. GEEF-Loop — Kerndiagramm

**Verwendung in:**
- `02-Konzepte-und-Architektur.md` (Zeile 9-18)
- `Fachartikel-GEEF-Pattern.md` (Zeile 51-60)

**Prompt:**

> A clean, modern technical diagram on a white background showing the GEEF feedback loop architecture. Four rounded rectangular boxes are arranged horizontally, connected by directional arrows from left to right: "Grounding" (light blue), "Execution" (green), "Evaluation" (orange), and "Finalize" (purple). A bold curved arrow labeled "Rejected — PreviousFindings" loops back from "Evaluation" to "Execution", forming a visible feedback loop. Between "Evaluation" and "Finalize", a diamond-shaped decision node is labeled "Approved?". An input arrow labeled "Input" enters from the left into "Grounding". An output arrow exits "Finalize" to the right, labeled "TOutput". A subtle label "Convergence Policy" sits near the decision diamond. The style is flat design, minimal, no shadows, suitable for a technical whitepaper. Use a monospace-friendly sans-serif font like Inter or Source Sans Pro. 16:9 aspect ratio. --ar 16:9 --style raw --v 6

---

## 2. Middleware-Pipeline-Fluss

**Verwendung in:**
- `02-Konzepte-und-Architektur.md` (Zeile 198-202)

**Prompt:**

> A clean horizontal flow diagram on white background illustrating an ASP.NET-Core-style middleware pipeline. Three concentric rounded rectangles represent middleware layers, nested from outside to inside: "Outer Middleware" (light gray border), "Middle Middleware" (medium gray border), "Inner Middleware" (dark gray border). At the center is a solid box labeled "Phase Operation" (teal/blue). A forward arrow flows from left to right through all layers with the label "Request", and a return arrow flows from right to left below it with the label "Response". Each middleware layer has small labels: "before" on the left side and "after" on the right side. The design is flat, minimal, technical, suitable for developer documentation. Use a clean sans-serif font. 16:9 aspect ratio. --ar 16:9 --style raw --v 6

---

## 3. Event-Lifecycle-Baum

**Verwendung in:**
- `06-Observability.md` (Zeile 17-34)

**Prompt:**

> A vertical tree diagram on a white background showing the lifecycle events of a pipeline run. The root node at the top is "PipelineStartedEvent" (dark blue). Below it, connected by vertical lines, are sequential nodes: "GroundingStartedEvent" and "GroundingCompletedEvent" (light blue pair). Then a dashed box labeled "Loop — per Iteration" contains: "ExecutionStartedEvent" and "ExecutionCompletedEvent" (green pair), "ReviewerStartedEvent" and "ReviewerCompletedEvent" (yellow pair, with a note "per Reviewer"), and a branching node splitting into "EvaluationApprovedEvent" (green checkmark) or "EvaluationRejectedEvent" (red X). A curved dotted arrow labeled "retry" loops from "EvaluationRejectedEvent" back up to "ExecutionStartedEvent". After the loop box: "FinalizeStartedEvent" and "FinalizeCompletedEvent" (purple pair). At the bottom, a branching node splits into "PipelineCompletedEvent" (green, success) or "PipelineFailedEvent" (red, failure). Clean flat design, developer documentation style, no decorative elements. Tall vertical layout. --ar 9:16 --style raw --v 6

---

## 4. OpenTelemetry-Span-Hierarchie

**Verwendung in:**
- `06-Observability.md` (Zeile 142-152)

**Prompt:**

> A horizontal waterfall/Gantt chart showing distributed tracing spans, styled like a Jaeger or Zipkin trace viewer. White background with subtle grid lines. The root span at the top is labeled "geef.pipeline.run" as a wide teal bar spanning the full width. Below it, nested spans are shown as shorter bars indented to the right: "geef.grounding" (blue, short), then "geef.iteration" (gray, medium width, repeating twice to show iteration 1 and iteration 2). Within each iteration: "geef.execution" (green bar), "geef.evaluation" (orange bar), and within evaluation: two small "geef.review" bars (yellow, side by side for parallel reviewers). After the last iteration: "geef.finalize" (purple, short). Each bar has its span name as a label. Tags "run_id=a1b2c3" and "iteration=1" are shown as small gray annotations. Clean, technical, minimal style resembling actual APM tooling. 16:9 aspect ratio. --ar 16:9 --style raw --v 6

---

## 5. Exception-Hierarchie

**Verwendung in:**
- `07-Fehlerbehandlung.md` (Zeile 5-12)

**Prompt:**

> A UML-style class hierarchy diagram on a white background showing exception inheritance. At the top is a box labeled "Exception" (light gray). Below it, connected by a vertical inheritance arrow, is "GeefException" (blue header bar) with a property label "RunId?". From "GeefException", four inheritance arrows branch downward to four boxes side by side: "ConvergenceFailedException" (red header, properties: "Reason, History, LastEvaluation"), "PhaseTimeoutException" (orange header, properties: "Phase, Timeout"), "ProviderException" (yellow header, properties: "Phase, ProviderName"), and "PipelineConfigurationException" (gray header, no extra properties). Each box has a clean border with the class name in bold and properties in regular weight below. Flat design, no shadows, no 3D effects, suitable for a technical reference document. 16:9 aspect ratio. --ar 16:9 --style raw --v 6

---

## 6. Konvergenz-Entscheidungsbaum

**Verwendung in:**
- `Fachartikel-GEEF-Pattern.md` (Zeile 143-165)

**Prompt:**

> A vertical flowchart decision tree on a white background. Starting from a rounded box at the top labeled "Evaluation complete", a series of diamond-shaped decision nodes flow downward, each with "Yes" and "No" branches. The decisions in order from top to bottom: (1) "All Approved?" — Yes leads to a green terminal box "APPROVED → Finalize", No continues down. (2) "Critical Finding?" — Yes leads to a red terminal box "ABORT CRITICAL". (3) "Max Time exceeded?" — Yes leads to an orange terminal box "STOP MAX ATTEMPTS". (4) "Max Iterations?" — Yes leads to the same orange box. (5) "Stagnation?" — Yes leads to a yellow terminal box "STOP STAGNANT". (6) "Regression?" — Yes leads to a yellow terminal box "STOP REGRESSION". At the bottom, the final "No" path leads to a blue box "CONTINUE → next iteration" with a curved arrow looping back to the top. Clean flowchart style with consistent colors: green for success, red for critical abort, orange for limits, yellow for detection, blue for continue. Sans-serif font, flat design. --ar 9:16 --style raw --v 6

---

## 7. Vier-Phasen-Übersicht (Grounding, Execution, Evaluation, Finalize)

**Verwendung in:**
- `01-Schnellstart.md` (Textbeschreibung, Zeile 14-17)
- `Fachartikel-GEEF-Pattern.md` (Phasentabelle, Zeile 62-69)

**Prompt:**

> A modern infographic showing four sequential phases as large numbered cards arranged horizontally on a white background. Card 1: "Grounding" with a magnifying glass icon, subtitle "Context sammeln", color: light blue. Card 2: "Execution" with a gear/cog icon, subtitle "Artefakte erzeugen", color: green. Card 3: "Evaluation" with a checklist/clipboard icon, subtitle "Qualität prüfen", color: orange. Card 4: "Finalize" with a checkmark/flag icon, subtitle "Ergebnis produzieren", color: purple. Cards are connected by forward arrows. Between card 3 and card 4, a small conditional diamond shows "Approved?". A subtle return arrow from card 3 back to card 2 is labeled "Feedback". Below each card, a one-line analogy in smaller text: "Recherche", "Schreiben", "Code Review", "Veröffentlichung". Clean flat design, professional, suitable for presentations and articles. 16:9 aspect ratio. --ar 16:9 --style raw --v 6

---

## 8. Evaluation-Strategien-Vergleich

**Verwendung in:**
- `02-Konzepte-und-Architektur.md` (Strategietabelle, Zeile 174-183)
- `Fachartikel-GEEF-Pattern.md` (Abschnitt 4, Zeile 170-210)

**Prompt:**

> A comparison infographic showing four evaluation strategies side by side on a white background. Each strategy is shown as a timeline diagram with three reviewer bars (R1, R2, R3). Strategy 1 "Sequential": Three bars stacked vertically one after another, no overlap. An arrow shows total time = sum of all durations. Strategy 2 "Parallel": Three bars aligned horizontally at the same start time, all ending at different points. Arrow shows total time = max duration. Strategy 3 "FailFast": Three bars start at the same time (parallel), but after R1 finishes with a red "Rejected" mark, a cancel symbol (X) appears on R2 and R3 which are cut short. Arrow shows total time = first rejection. Strategy 4 "PriorityOrdered": Three bars stacked vertically but ordered by priority labels (P=10, P=50, P=100), with the first bar completing with a red X and subsequent bars grayed out with "skipped" labels. Use consistent colors: green for approved, red for rejected, gray for cancelled/skipped. Clean, minimal, technical illustration style. 16:9 aspect ratio. --ar 16:9 --style raw --v 6

---

## 9. Testprojekt-Struktur

**Verwendung in:**
- `08-Testanleitung.md` (Zeile 355-369)

**Prompt:**

> A clean file tree diagram on a white background resembling an IDE project explorer. The root folder "tests/Geef.Sdk.Tests/" is shown with a folder icon at the top. Below it, five subfolders branch out with file icons beneath each: "Context/" contains "RunContextTests.cs", "Runtime/" contains "IterationHistoryTests.cs" and "GeefPipelineRunnerTests.cs", "Policies/" contains "DefaultConvergencePolicyTests.cs" and "EvaluationStrategyTests.cs", "Builder/" contains "GeefPipelineBuilderTests.cs", and "Integration/" contains "FullPipelineIntegrationTests.cs". Each file has a small C# icon (purple/blue). Folders use standard folder icons in yellow/gold. The style resembles VS Code or Rider file explorer — flat, clean, with subtle indentation lines. Monospace font for file names. 4:3 aspect ratio. --ar 4:3 --style raw --v 6

---

## 10. Immutable-Context-Snapshots

**Verwendung in:**
- `02-Konzepte-und-Architektur.md` (Zeile 88-98, Codebeispiel zeigt Snapshot-Konzept)

**Prompt:**

> A technical diagram on white background illustrating immutable context snapshots. Three stacked horizontal bars represent context states, arranged vertically from top to bottom: "ctx1" (empty, light gray, label "empty"), "ctx2" (contains one key-value pair "myKey = Wert A", light blue), "ctx3" (contains one key-value pair "myKey = Wert B", medium blue). Arrows labeled ".Set()" point from ctx1 down to ctx2 and from ctx2 down to ctx3. Crucially, each bar is visually independent — no mutation arrows. A bold annotation on the side reads "ctx1 remains unchanged!" with a green checkmark. A crossed-out red mutation arrow is shown to emphasize that the original is never modified. Clean, minimal, developer-documentation style. 16:9 aspect ratio. --ar 16:9 --style raw --v 6

---

## 11. Stagnation und Regression — Konvergenzanalyse

**Verwendung in:**
- `Fachartikel-GEEF-Pattern.md` (Abschnitt 3, Zeile 98-140)

**Prompt:**

> A technical diagram on white background showing three iteration timelines side by side to illustrate convergence concepts. Left panel labeled "Progress": Three rows for Iteration 1 (fingerprints: A, B, C), Iteration 2 (fingerprints: A, B), Iteration 3 (fingerprint: A). Colored dots decrease, with a green downward trend arrow and label "Findings decrease — progress". Middle panel labeled "Stagnation": Three rows all showing identical fingerprints (A, B, C) with red equals signs between them and label "Same findings repeat — no improvement". Right panel labeled "Regression": Three rows — Iteration 1 (A, B), Iteration 2 (A — B is gone, marked with green checkmark), Iteration 3 (A, B — B returns, marked with red warning triangle) and label "Fixed issue reappears". Each fingerprint is shown as a small colored circle with a letter inside. Clean, minimal, academic style suitable for a technical article. 16:9 aspect ratio. --ar 16:9 --style raw --v 6

---

## Hinweise zur Verwendung

### Stilkonsistenz
Alle Prompts verwenden folgende Parameter für ein einheitliches Erscheinungsbild:
- Weißer Hintergrund
- Flat Design ohne Schatten oder 3D-Effekte
- Sans-Serif-Schriftarten (Inter, Source Sans Pro)
- Konsistente Farbpalette: Blau (Grounding/Context), Grün (Execution/Approved), Orange (Evaluation), Lila (Finalize), Rot (Fehler/Abbruch)

### Modell-spezifische Anpassungen

**DALL-E 3:** Prompts können direkt verwendet werden. Entferne die `--ar` und `--style` Parameter am Ende.

**Midjourney:** Prompts sind bereits mit Midjourney-Parametern (`--ar`, `--style raw`, `--v 6`) versehen.

**Stable Diffusion / SDXL:** Ergänze am Anfang: `technical diagram, vector illustration, flat design,` und verwende negative prompts: `photorealistic, 3d render, shadows, gradients, decorative elements, people, hands`.

### Dateinamen-Konvention
```
geef-diag-01-loop.png
geef-diag-02-middleware.png
geef-diag-03-events.png
geef-diag-04-tracing.png
geef-diag-05-exceptions.png
geef-diag-06-convergence.png
geef-diag-07-phases.png
geef-diag-08-strategies.png
geef-diag-09-teststructure.png
geef-diag-10-immutability.png
geef-diag-11-convergence-analysis.png
```
