# ADR-009 — Discipline-Aware Evidence Acquisition

- **Status:** Accepted
- **Date:** 2026-07-16
- **Milestone:** [MILESTONE-008](../milestones/MILESTONE-008-discipline-aware-evidence-acquisition.md)
- **Builds on:** [ADR-006](./ADR-006-multi-provider-execution.md), [ADR-008](./ADR-008-engineering-observation-layer.md)

## Context

Milestone 007 collected evidence with a single repository-wide, cross-discipline
prompt, then interpreted it into observations. That topology is right for
repository-wide static sources (SonarQube, SARIF, Roslyn, Git, coverage), which
naturally scan the whole tree once. But for LLM sources it flattens depth: every
discipline is served from one generic request, so the Security lens and the
Architecture lens receive the same shallow, undifferentiated evidence.

We want both: focused, per-discipline LLM requests **and** one-pass repository-wide
static sources — without moving provider knowledge back into analyzers.

## Decision

Introduce two acquisition scopes and an explicit plan.

- **`EvidenceAcquisitionScope { Repository, Discipline }`** — each source declares
  its identity and behavior via **`EvidenceProviderMetadata`** (`Name`,
  `ProviderType`, `DefaultAcquisitionScope`, `SupportedDisciplines`,
  `RequiresAnalyzerInstructions`, `SupportsRepositoryWideAnalysis`, `Version`).
  Configuration may override the scope. The planner branches only on metadata,
  never on concrete provider names.
- **`IEvidenceAcquisitionPlanner`** takes an `AnalysisRunConfiguration`, the
  snapshot, the resolved providers, and the selected disciplines, and produces an
  explicit **`EvidenceAcquisitionPlan`**: a repository-scoped source contributes one
  step; a discipline-scoped source contributes one step per supported, selected
  discipline (e.g. `Mock#Architecture`, `Mock#Security`, `Sonar#repository`). A
  discipline that a provider does not support creates **no step** and is recorded in
  the plan's `UnsupportedCombinations`. Ordering is stable (provider order →
  repository step before discipline steps → discipline order), so the same inputs
  always yield the same plan.
- **`IAnalysisContextSelector`** selects the files most relevant to each step
  (rule-based, respecting the scanner's ignore rules; no embeddings) and returns a
  deterministic **`AnalysisContextSelection`** (`Strategy`, `Files`,
  `TotalRepositoryFiles`, `SelectedFileCount`, `EstimatedContentSize`,
  `SelectionReasons`). It honors the configured `Evidence:Context` file/character
  limits by including whole files (never silently truncating) until a budget would
  be exceeded, and records excluded counts in `SelectionReasons`.
- **`EvidenceRequest`** becomes first-class (`RunId`, `RepositorySnapshot`, `Scope`,
  `Discipline`, `Instructions`, `ContextSelection`, `ProviderNames`,
  `CorrelationId`, `Metadata`). Providers implement `CollectAsync(request)`. LLM
  sources use the discipline instructions + selected context; repository-wide
  static sources may ignore the discipline fields. **Focused prompts are restored**
  via `DisciplinePrompts` (shared constraints + discipline objective + the
  observation output contract). The shared constraints treat all repository content
  as untrusted input. Providers still return observations, never findings.
- **`IEvidenceAcquisitionExecutor`** executes the plan step by step, stamps
  acquisition provenance onto the evidence, isolates failures (a failed step never
  aborts the rest), and **owns its telemetry**: it returns an
  `EvidenceAcquisitionResult { Evidence, Executions, Plan }` — no process-global
  state, so two concurrent runs never interleave.
- Provenance flows through: `EngineeringObservation` gains `AcquisitionScope`,
  `RequestedDiscipline`, `AcquisitionCorrelationId`, `AcquisitionStepId`,
  `ContextFileCount`, `ContextSelectionStrategy`, so a finding is traceable
  Finding → Observation → Interpreter → Evidence → Evidence request → selected
  context → provider execution step.
- **Discipline-mismatch rule.** When a discipline-scoped request returns an
  observation for a *different* discipline, the interpreter does not silently
  rewrite it: the observation keeps its own discipline, but its confidence is
  lowered and it is tagged `discipline-mismatch`; the count surfaces in the
  interpretation summary telemetry.

## Why one repository-wide LLM request weakens specialized analysis

An LLM has a finite attention/output budget. Asking it to survey *all* disciplines
at once forces breadth over depth, so each discipline receives generic signals. A
request scoped to one discipline — with instructions and context selected for that
discipline — lets the model reason deeply about exactly that concern.

## Why static sources naturally execute once

SonarQube, SARIF, Roslyn, Git, and coverage analyze the entire repository in a
single pass; per-discipline invocation would be redundant and often impossible.
Their evidence is inherently cross-discipline, so Repository scope is correct.

## Why analyzers remain independent of acquisition

Analyzers consume `EngineeringObservation`s only (ADR-008). The new topology lives
entirely in the planner/selector/executor; observations still arrive normalized and
discipline-tagged, so analyzers neither know nor care whether their observations
came from a discipline-scoped LLM step or a repository-wide static step.

## Why an explicit plan beats implicit loops

A first-class plan is inspectable (written to `provider-execution.json`, `run.json`,
the package), testable (deterministic, unit-tested), and the single place topology
decisions live — instead of nested loops scattered through the pipeline. The
pipeline just executes the plan.

## Consequences

- With a discipline-scoped provider and N disciplines, that provider runs N times
  (one focused request each); a repository-scoped provider runs once. The plan makes
  the cost explicit.
- Similar observations from different scopes are **preserved, not de-duplicated**;
  the merger continues to keep cross-provider results separate. A future council
  will reconcile them.
- No new top-level artifacts: the plan + context-selection summary ride inside
  `provider-execution.json`, `run.json`, and the package; the review document gains a
  concise **Evidence Acquisition Coverage** appendix.

## Explicitly out of scope

Provider comparison, consensus, voting, provider weights, semantic reconciliation,
discipline councils, council deliberation, ticket generation, and parallel execution.
