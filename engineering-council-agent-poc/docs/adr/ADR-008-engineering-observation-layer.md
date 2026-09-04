# ADR-008 — Engineering Observation & Evidence Interpreter Layer

- **Status:** Accepted
- **Date:** 2026-07-16
- **Milestone:** [MILESTONE-007](../milestones/MILESTONE-007-engineering-observation-layer.md)
- **Builds on:** [ADR-005](./ADR-005-evidence-provider-layer.md), [ADR-006](./ADR-006-multi-provider-execution.md), [ADR-007](./ADR-007-engineering-review-package.md)

## Context

Analyzers interpreted raw provider `Evidence` (LLM JSON) directly into `Finding`s.
That works for one evidence shape, but the platform must ingest heterogeneous
sources — SonarQube, Roslyn, SARIF, Semgrep, NDepend, git history, test coverage —
whose formats have nothing in common with an LLM response or each other. If every
analyzer had to understand every format, adding a source would touch every
analyzer, and the analyzers would drown in parsing instead of reasoning.

## Decision

Introduce a normalized intermediate domain, **`EngineeringObservation`**, and an
**Evidence Interpreter** layer that converts provider-specific `Evidence` into it.
The layers and their responsibilities become sharply separated:

```
Evidence (provider-specific, raw)
   → IEvidenceInterpreter → EngineeringObservation (normalized, source-neutral)
      → Analyzer → Finding (actionable conclusion)
         → EngineeringReviewPackage (official deliverable)
```

- **`IEvidenceInterpreter`** understands one evidence format and emits zero+
  observations. It never creates findings, writes files, renders Markdown, or
  knows the package.
- **`IEvidenceInterpreterResolver`** picks an interpreter by discovery
  (`CanInterpret`), not a provider-name switch; returns null → the caller records
  *unsupported* evidence rather than inventing observations.
- **`IEvidenceInterpretationPipeline`** runs interpretation over the collected
  evidence, preserves failed acquisitions for telemetry, isolates interpreter
  failures (one failure is not fatal), and returns a unified observation set plus
  an `ObservationInterpretationSummary`.
- Evidence is now acquired **once, centrally** by the pipeline; analyzers no
  longer acquire evidence. Each analyzer selects the observations for its
  discipline, correlates them, and produces findings that carry full provenance:
  `ObservationIds`, `SourceRules`, `SupportingProviders`, `SupportingObservationCount`.
- v7 ships `StructuredLlmEvidenceInterpreter` (Mock + Claude output). Future
  interpreters (SARIF, Sonar, Roslyn, Semgrep, NDepend, Git, coverage) exist as
  **unregistered, non-throwing skeletons** with availability metadata.

## Why raw Evidence is not a stable analyzer input

Raw evidence is provider-shaped and unstable: an LLM returns prose+JSON, SARIF
returns a schema, Sonar returns issues, git returns commits. Coupling analyzers to
these shapes makes analyzers fragile and format-specific.

## Why heterogeneous providers require interpreters

Only a component that *understands one format* can faithfully normalize it.
Interpreters localize all format knowledge, so a new source is a new interpreter
(one class + one registration) with **zero analyzer changes** — symmetric with how
a new provider is one `IEvidenceProvider`.

## Why `EngineeringObservation` is the common language

An observation is a single normalized signal — type, discipline, severity,
confidence, file/symbol/line refs, a short evidence excerpt, and provenance back to
the evidence and provider. Every source, LLM or tool, maps onto this one shape, so
analyzers reason over a stable vocabulary instead of N provider formats.

## Why Findings remain higher-level

A finding is an actionable engineering *conclusion* an analyzer reaches by
selecting and correlating observations and applying discipline judgment. Keeping
finding distinct from observation preserves the boundary between "a normalized
signal" and "a conclusion a human should act on."

## How traceability is preserved

Each finding records the observation ids, rules, and providers behind it; each
observation records its `SourceEvidenceId`, provider and rule; provider execution
is captured per call. So every finding is traceable:
`Finding → EngineeringObservation → Evidence → provider execution`, surfaced in
`observations.json`, the package, and the review document's traceability appendix.

## Consequences

- Evidence is acquired once centrally (one provider call set per run) rather than
  per-analyzer; the LLM collection prompt asks for observations across disciplines.
- Analyzers became thin discipline reasoners over observations; `FindingsNormalizer`
  was replaced by the interpreter (JSON parsing + the "do not invent files" guard).
- New per-run artifact: `observations.json`. Package/metrics gained observation
  counts; the review document gained an Observation & Evidence Traceability section.

## Explicitly out of scope

No multi-provider consensus, voting, provider weighting, LLM finding
reconciliation, discipline councils, council deliberation, ticket generation, or
parallel execution. This milestone only establishes the normalized language.
