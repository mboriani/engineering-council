# ADR-010 — Native SARIF Evidence Source

- **Status:** Accepted
- **Date:** 2026-07-21
- **Milestone:** [MILESTONE-009](../milestones/MILESTONE-009-native-sarif-provider.md)
- **Builds on:** [ADR-008](./ADR-008-engineering-observation-layer.md), [ADR-009](./ADR-009-discipline-aware-evidence-acquisition.md)

## Context

Through Milestone 008 the platform has an evidence → interpretation → observation →
analyzer → package pipeline, but every real run has used LLM-style providers (Claude,
Mock). The architecture *claims* heterogeneous sources can participate without touching
analyzers, interpreters, or the package. Milestone 009 validates that claim with the
first **real, deterministic** evidence source.

The objective is explicitly **not** to improve analysis quality — it is to prove the
seams hold when a genuinely different kind of source is plugged in.

## Decision

Implement a native **SARIF 2.1.0** evidence source as a provider + interpreter pair,
changing nothing in the analyzers, the observation model's consumers, or the package
builder's public shape.

- **`SarifEvidenceProvider`** (Static, Repository-scoped) does **not** run scanners; it
  imports existing SARIF files (one or many). It produces **one `Evidence` per SARIF
  run**, preserving the tool name/version, invocation metadata, artifact/result counts,
  and the original SARIF payload. It only imports and shards — it assigns no meaning.
- **`SarifEvidenceInterpreter`** is the ONLY component that understands the SARIF
  schema. It converts each `result` into one `EngineeringObservation` with a
  deterministic discipline/type/severity/confidence mapping and full rule metadata +
  provenance. No analyzer contains any SARIF knowledge.
- Supported subset: `runs[]`, `tool.driver`, `rules`, `results`, `locations`,
  `physicalLocation`, `artifactLocation`, `region`. Deferred: `codeFlows`,
  `threadFlows`, `graphs`, `suppressions`, `fixes`, `webRequests`, `webResponses`.

### One provider execution, many evidence

A repository-scoped provider runs once, but a SARIF import can contain multiple runs
across multiple files. To model this honestly, `IEvidenceProvider.CollectAsync` now
returns `IReadOnlyList<Evidence>` (was a single `Evidence`). The executor stamps
acquisition provenance onto each item and records `EvidenceCount = n` on the single
step's telemetry. LLM providers simply return a one-item list. This is the minimal
change that lets one step yield one-evidence-per-run without weakening the provider
abstraction or the acquisition planner/executor.

### Deterministic mappings (documented in `SarifMappings`)

- **Severity:** `error → Critical · warning → High · note → Medium · none → Low`; a
  missing/unknown level is treated as `warning` and flagged as a metadata deficiency.
- **Discipline:** a keyword scan over rule id + name + description + tags (tags carry
  taxonomy such as CWE), first match wins, in the order Security → Reliability →
  Testing → Observability → Documentation → Architecture → CodeQuality; **no match →
  `Unknown`**. The raw properties JSON is deliberately excluded from the signal to
  avoid false matches (e.g. the `security-severity` property key).
- **Observation type:** a keyword scan → a known `ObservationTypes` value, else
  `Unknown`.
- **Confidence:** static analyzers are deterministic → default **High (0.95)**;
  reduced only for concrete data deficiencies (malformed rule, missing file, incomplete
  location, incomplete metadata): 0 → High, 1 → Medium, 2+ → Low. No invented heuristics.

Unknown rules stay `Unknown` in both discipline and type — meaning is never invented.

## Why SARIF was selected as the first deterministic provider

SARIF is an OASIS standard emitted by a large ecosystem (CodeQL, ESLint, Roslyn
analyzers, Semgrep, and many more), so a single interpreter unlocks many tools at once.
It is deterministic (no model variance), file-based (no scanner execution required in
this POC), and rich enough (rules, locations, regions, taxonomy) to exercise every part
of the observation model — the ideal stress test for the "heterogeneous sources"
architecture.

## Why interpreters isolate external schemas

Keeping the SARIF schema entirely inside the interpreter means analyzers keep consuming
the normalized `EngineeringObservation` language (ADR-008) and never learn a second
vocabulary. Adding SARIF touched no analyzer. The provider handles transport/sharding;
the interpreter owns semantics; the boundary is the observation.

## Why analyzers remain provider-independent

Analyzers select observations by discipline, not by source. A SARIF-derived Security
observation and an LLM-derived Security observation are indistinguishable to the
`SecurityAnalyzer` — exactly the property this milestone set out to prove.

## Why repository-scoped evidence naturally executes once

SARIF describes results for a whole repository in one document; there is no per-
discipline SARIF. Declaring the source Repository-scoped (ADR-009) means the planner
emits exactly one step regardless of how many disciplines are selected, and the
executor runs it once.

## Consequences

- `EngineeringObservation` gains `ColumnReferences`; `FindingCategory` and
  `ObservationTypes` gain an `Unknown` member; `AnalysisRun` now carries the raw
  `Evidence`; the package gains `StaticAnalysisSources` (Tool, Version, Imported
  Results, Generated Observations), surfaced in `engineering-review-package.json` and a
  concise **Static Analysis Sources** appendix in `engineering-review.md` (never raw
  SARIF).
- The provider contract is now list-returning; all providers and the executor were
  updated. No analyzer, interpreter-resolver, merger, summary, or package-builder logic
  changed.

## Explicitly out of scope

Reconciliation, councils, provider weighting/voting/comparison, recommendation engine,
duplicate merging, ticket generation, parallel execution, and re-opening repository
files to enrich SARIF snippets (a later milestone).
