# ADR-015 — Domain & Metrics Correctness

- **Status:** Accepted
- **Date:** 2026-08-07
- **Milestone:** [MILESTONE-011.2](../milestones/MILESTONE-011.2-domain-metrics-correctness.md)
- **Builds on:** [ADR-014](./ADR-014-reliability-security-hardening.md), [ADR-008](./ADR-008-engineering-observation-layer.md), [ADR-009](./ADR-009-discipline-aware-evidence-acquisition.md)

## Context

Three domain/metrics correctness issues were confirmed by reading the code:

- **A4 — provider metrics are not mutually exclusive.** `EngineeringMetrics.From`
  computed `SuccessfulProviders` as *"succeeded at least once"*
  (`Failures < Executions`) and `FailedProviders` as *"produced no evidence"*
  (`EvidenceCount == 0`). A provider with mixed successful+failed executions was
  therefore counted as **Successful**, and a provider whose executions all "succeeded"
  but produced zero evidence was counted as **Failed** — the two predicates measured
  different things and overlapped. There was no way to tell a provider that partially
  failed apart from one that fully succeeded.
- **A5 — an unrecognized discipline silently becomes CodeQuality.**
  `StructuredLlmEvidenceInterpreter` fell back to `FindingCategory.CodeQuality` when the
  LLM returned a discipline string it did not recognize. Unknown observations were
  therefore re-labelled CodeQuality, were consumed by the CodeQuality analyzer, and
  inflated CodeQuality findings/metrics. The SARIF interpreter already mapped
  unrecognized signals to `FindingCategory.Unknown`; the LLM path did not.
- **A6 — a requested discipline with no registered analyzer does not fail fast.** A
  valid-enum discipline with no registered `IAnalyzerAgent` (Performance,
  Maintainability, Dependencies, DeveloperExperience, Unknown) was silently dropped by
  `AnalysisPipeline.SelectedDisciplines` (the requested set is intersected with the
  registered set). The CLI rejected only unknown *enum names*; the API validated only
  enum names too. A run that requested only such disciplines produced **zero findings**
  and reported **Excellent** health — a misleading success.

## Decision

Apply the smallest corrective fixes that keep the architecture and the external
`engineering-review-package.json` consumer contract unchanged
(`schemaVersion` stays **1.1**; all new fields are additive).

### A4 — disjoint provider states

`EngineeringMetrics` gains `PartialProviders` and classifies every provider with at
least one execution into exactly one of three mutually exclusive states based on
**execution outcomes** (not evidence counts):

| State | Condition |
|-------|-----------|
| Successful | all executions succeeded (`Executions > 0 && Failures == 0`) |
| Partial | at least one success AND one failure (`0 < Failures < Executions`) |
| Failed | all executions failed (`Executions > 0 && Failures == Executions`) |

- A provider with **zero executions is never Successful/Failed/Partial**.
- `SuccessfulProviders`/`FailedProviders` are retained for compatibility but now carry
  the disjoint semantics; `PartialProviders` is additive.
- Totals stay deterministic integer counts; the markdown exporter shows the three-part
  breakdown.

### A5 — Unknown discipline, never a guess

- `StructuredLlmEvidenceInterpreter` maps an unrecognized discipline string to
  `FindingCategory.Unknown` instead of `CodeQuality`.
- `EngineeringObservation.Discipline` and `Finding.Category` domain defaults change from
  `CodeQuality` to `Unknown`: a discipline-less observation/finding is **never**
  implicitly claimed by CodeQuality.
- Unknown observations remain persisted with full provenance, are visible in
  `observations.json`, `ObservationsByDiscipline` (a `"Unknown"` entry), and the
  `CoverageByDiscipline`-style diagnostics. No analyzer registers `Unknown`, so the
  observation-based analyzers never consume them and CodeQuality metrics are never
  inflated.
- **No Unknown analyzer is created** and Unknown observations are **not** forced into
  another discipline.
- New additive metric `UnclaimedObservationCount` (interpretation summary, flows to
  `EvidenceSummary.UnclaimedObservations`) and `EngineeringMetrics.UnclaimedObservations`
  both count observations whose discipline is `Unknown` — the unclaimed signal is
  surfaced instead of hidden. A recognized-but-unanalyzed enum (e.g. `Performance`)
  stays exactly as produced: it is an A6 configuration concern at request time, never
  silently reclassified.

### A6 — shared analyzer-registration validation, fail fast

- New `EngineeringCouncil.Core.Application.AnalyzerDisciplineValidator` is the **single
  shared validation component**. `Unsupported(requested, registered)` returns every
  requested discipline with no registered analyzer; `EnsureSupported(...)` throws a new
  `UnsupportedDisciplineException` that carries and reports **all** unsupported
  disciplines (e.g. `Requested discipline 'Performance' has no registered analyzer.`).
- `AnalysisPipeline.RunAsync` calls `EnsureSupported` on the **effective** discipline set
  (`request.Disciplines ?? EvidenceOptions.Disciplines`) **before the repository scan**,
  so the guard holds for every entry point (CLI, API, evaluate) and for config-driven
  selections. An exception propagates out — no run, no provider execution, no package.
- A degenerate configuration with **zero registered analyzers** is refused with an
  `InvalidOperationException` before scan, so an empty effective discipline set can
  never produce an Excellent "no findings" run.
- The CLI resolves the orchestrator and reports the formatted unsupported list with exit
  code **1** before any analysis. The API returns **400** with the same message for
  unsupported request disciplines and converts the config-driven pipeline exception to
  **400** as well.

## Trade-offs and decisions

- **Execution outcomes, not evidence counts, define provider health.** Evidence counts
  conflate "no evidence because nothing was found" with "the provider failed"; execution
  success/failure is the unambiguous signal and makes the three states disjoint.
- **Keep the legacy names.** `SuccessfulProviders`/`FailedProviders` appear in the
  package metrics and markdown; renaming would be a breaking consumer change for no gain.
- **`Unknown` is not forced elsewhere.** Forcing Unknown observations into an existing
  discipline would recreate the A5 problem; creating an Unknown analyzer would add an
  unregistered-capability for a non-discipline. Surfacing them unclaimed is honest.
- **One validator, not three.** A single `AnalyzerDisciplineValidator` called by CLI,
  API, and the pipeline entry means the registered-set check can never drift between
  entry points.
- **Fail before the run exists.** Validating at the top of `RunAsync` (before
  `AnalysisRunId.New()`/scan) means nothing is persisted and no misleading package is
  produced — structurally matching the "fail fast before scan" requirement.

## Consequences

- Provider metrics are deterministic and mutually exclusive; partial failures are visible.
- Unknown LLM evidence stays Unknown: persisted, traceable, unclaimed, and never counted
  as CodeQuality. `UnclaimedObservations` surfaces it in the package metrics.
- Any entry point requesting an unanalyzed discipline aborts with a clear, complete error
  before touching the repository; an empty effective discipline set is impossible.
- `engineering-review-package.json` gains only additive metric fields
  (`partialProviders`, `unclaimedObservations`); `schemaVersion` stays **1.1** and the
  consumer-DTO contract test passes unchanged.

## Explicitly out of scope (deferred)

C4 context budgeting, C6 context metrics, parallel execution, provider calibration,
councils, LLM reconciliation, run delta/trends, background jobs, caching, new exporters,
stable evidence IDs, and unrelated refactoring. See MILESTONE-011.2.
