# MILESTONE-007 — Engineering Observation & Evidence Interpreter Layer

- **Status:** ✅ Complete
- **Date:** 2026-07-16
- **Version:** v7 (normalized observation layer)
- **ADR:** [ADR-008](../adr/ADR-008-engineering-observation-layer.md)

## Goal

Introduce a normalized intermediate domain — **`EngineeringObservation`** — and an
Evidence Interpreter layer that converts provider-specific raw `Evidence` into it,
so analyzers reason over a stable, source-neutral language rather than raw provider
responses. This lets future SonarQube / Roslyn / SARIF / Semgrep / Git / coverage /
LLM evidence participate in the same review process.

## New flow

```
Repository → Evidence Sources → Raw Evidence → Evidence Interpreters
   → Engineering Observations → Specialized Analyzers → Findings
   → Merger & Summary → Engineering Review Package
```

## What changed

- **Domain:** `EngineeringObservation` (+ open-string `ObservationTypes`),
  `ObservationInterpretationSummary`; `Evidence.Id`; `Finding` provenance
  (`ObservationIds`, `SourceRules`, `SupportingProviders`,
  `SupportingObservationCount`); `AnalysisRun.Observations` + summary.
- **Abstractions:** `IEvidenceInterpreter`, `IEvidenceInterpreterResolver`,
  `IEvidenceInterpretationPipeline`, `AnalyzerContext`; `IAnalyzerAgent` now
  consumes `IReadOnlyList<EngineeringObservation>`.
- **Interpreters:** `StructuredLlmEvidenceInterpreter` (parses the observations
  JSON, drops invented file refs, lowers confidence when incomplete);
  `EvidenceInterpreterResolver` (discovery, not a name switch);
  `EvidenceInterpretationPipeline` (failure/unsupported isolation + telemetry).
  Future interpreters (SARIF, Sonar, Roslyn, Semgrep, NDepend, Git, coverage) are
  **unregistered, non-throwing skeletons**.
- **Providers:** `MockEvidenceProvider` now emits a multi-discipline `observations`
  envelope (not findings).
- **Analyzers:** `ObservationBasedAnalyzer` base + 7 thin discipline subclasses;
  they select their discipline's observations, correlate by type, and build
  findings with provenance. Evidence acquisition moved from analyzers to the
  pipeline (central, once).
- **Pipeline:** scan → acquire evidence → interpret → observations → analyze over
  observations → merge → summary → package → persist.
- **Reporting:** new `observations.json`; package metrics + evidence summary gained
  observation counts; the review document gained an **Observation and Evidence
  Traceability** appendix (+ per-finding supporting-observation count).

## Output files (per run)

`engineering-review-package.json`, `engineering-review.md`, **`observations.json`**,
`findings.json`, `raw-findings.json`, `provider-execution.json`, `run.json`.

## Architectural boundaries (enforced)

Evidence = provider-specific & raw · Observation = normalized & source-neutral ·
Finding = actionable conclusion · Package = official deliverable. No component
converts provider output straight to Markdown.

## Tests (all green — 48/48)

`EvidenceInterpreterTests` (one→one, one→many, malformed, invented-file drop +
confidence lowering, `CanInterpret`, resolver discovery/unsupported);
`ObservationPipelineTests` (aggregation + unique ids, failed evidence counted,
unsupported reported, interpreter-failure isolation); provenance +
observation-artifact + observation-count assertions in the updated pipeline and
package tests; analyzer-independence test now proves analyzers consume observations
only. `FindingsNormalizerTests` removed (normalizer replaced by the interpreter).

## Exit criteria

| Criterion | Result |
|-----------|--------|
| Build (0 errors, 0 warnings) | ✅ |
| Tests pass | ✅ 48/48 |
| Analyzers consume observations, not raw evidence | ✅ `IAnalyzerAgent` |
| Interpreter discovery (no provider-name switch) | ✅ `CanInterpret` resolver |
| Interpreter failure isolated; unsupported reported | ✅ pipeline telemetry |
| Finding → observation → evidence → provider traceable | ✅ provenance fields + `observations.json` |
| `observations.json` emitted; package counts observations | ✅ |
| Future interpreters don't throw during discovery | ✅ unregistered skeletons |

## Verification

```
dotnet build EngineeringCouncil.slnx        # 0 errors, 0 warnings
dotnet test  EngineeringCouncil.slnx        # Passed: 48
dotnet run --project src/EngineeringCouncil.Cli -- review --path src/EngineeringCouncil.Core --provider Mock
#   7 observations → 7 findings; each finding traces to OBS-xxx / rule / provider; observations.json written
```

## Explicitly out of scope (future milestones)

Multi-provider consensus · voting · provider weighting · LLM finding reconciliation ·
discipline councils · council deliberation · ticket generation · parallel execution.
