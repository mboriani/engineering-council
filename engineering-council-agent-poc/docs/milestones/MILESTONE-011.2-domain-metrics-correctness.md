# MILESTONE-011.2 — Domain & Metrics Correctness

- **Status:** ✅ Complete
- **Date:** 2026-08-07
- **Version:** v12.2 (domain & metrics correctness)
- **ADR:** [ADR-015](../adr/ADR-015-domain-metrics-correctness.md)

## Goal

A corrective milestone addressing exactly three verified issues — A4 (provider metrics
not mutually exclusive), A5 (unknown-discipline fallback to CodeQuality), A6 (requested
discipline with no registered analyzer doesn't fail fast). No new capability, no
architecture change. The external application still consumes only
`engineering-review-package.json` (`schemaVersion 1.1`).

## Scope

- **A4** — disjoint provider states: Successful / Partial / Failed
- **A5** — unrecognized discipline maps to `FindingCategory.Unknown` (never CodeQuality);
  unclaimed observations surfaced, not hidden
- **A6** — shared analyzer-registration validation; fail fast before scan for every entry
  point

Explicitly **out of scope** (documented, deferred): C4 context budgeting · C6 context
metrics · parallel execution · provider calibration · councils · LLM reconciliation · run
delta/trends · background jobs · caching · new exporters · stable evidence IDs ·
unrelated refactoring.

## Reproduced findings

| # | Reproduction | Before |
|---|--------------|--------|
| A4 | `EngineeringMetrics.From` classified providers by two overlapping predicates — `SuccessfulProviders = Failures < Executions` ("succeeded at least once") and `FailedProviders = EvidenceCount == 0` ("produced no evidence"). | A provider with mixed success+failure was counted **Successful**; a provider whose executions all succeeded but produced no evidence was counted **Failed**. No way to distinguish a partially-failed provider. |
| A5 | `StructuredLlmEvidenceInterpreter` used `ParseEnum(dto.Discipline, FindingCategory.CodeQuality)`; the LLM path also relied on `EngineeringObservation.Discipline`/`Finding.Category` defaulting to `CodeQuality`. | An unrecognized discipline string was **rewritten to CodeQuality**, consumed by the CodeQuality analyzer, and inflated CodeQuality findings/metrics. |
| A6 | `AnalysisPipeline.SelectedDisciplines` silently intersects requested with registered analyzers; CLI/API validated only unknown **enum names** (`InvalidDisciplines`), never registration. | `review --disciplines Performance` produced a run with **zero findings** and **Excellent** health instead of failing fast. |

All three reproduced by inspection; each is now guarded by regression tests.

## Fixes

- **A4 (`EngineeringMetrics`)** — added `PartialProviders`; provider states are now
  mutually exclusive by execution outcome: Successful = all executions succeeded,
  Partial = ≥1 success AND ≥1 failure, Failed = all executions failed, zero executions ⇒
  never counted. `SuccessfulProviders`/`FailedProviders` retained for compatibility with
  the disjoint semantics. Markdown shows `Successful / partial / failed providers`.
- **A5 (`StructuredLlmEvidenceInterpreter` + domain defaults + metrics)** — unrecognized
  discipline → `FindingCategory.Unknown`; `EngineeringObservation.Discipline` and
  `Finding.Category` defaults → `Unknown`. No Unknown analyzer; unknown observations stay
  persisted, provenance-preserved, and unconsumed by any analyzer. New additive
  `UnclaimedObservationCount` (interpretation summary) → `EvidenceSummary.
  UnclaimedObservations` and `EngineeringMetrics.UnclaimedObservations`, both counting
  `Unknown`-discipline observations.
- **A6 (`AnalyzerDisciplineValidator` + pipeline + CLI + API)** — new shared validator in
  `Core.Application` with `Unsupported(...)`/`EnsureSupported(...)` and
  `UnsupportedDisciplineException` (reports ALL unsupported disciplines).
  `AnalysisPipeline.RunAsync` validates the **effective** discipline set
  (`request ?? config`) before the scan and refuses an empty registered set; CLI exits
  **1** with the formatted message before analysis; API returns **400** for unsupported
  request disciplines and for the config-driven pipeline exception.

## Regression tests

- **A4 (`DomainMetricsCorrectnessTests`)** — all-successful ⇒ Successful only;
  all-failed ⇒ Failed only; mixed ⇒ Partial only (never Successful); zero executions ⇒
  never counted; three providers ⇒ exactly one each (disjoint, sum == provider count);
  `EvidenceSources` still counts evidence-producing providers.
- **A5 (`DomainMetricsCorrectnessTests`)** — unrecognized discipline string ⇒ `Unknown`
  (not CodeQuality); recognized-but-unanalyzed enum (Performance) never rewritten;
  CodeQuality analyzer consumes only its own observations (Unknown excluded, provenance
  intact); `Metrics.UnclaimedObservations` counts Unknown observations;
  interpretation summary reports `UnclaimedObservationCount` + a `"Unknown"` entry in
  `ObservationsByDiscipline`; `EvidenceSummary.UnclaimedObservations` passthrough.
- **A6 (`DomainMetricsCorrectnessTests`)** — validator reports every unregistered
  discipline; accepts supported/empty/null requests; `EnsureSupported` throws listing all
  unsupported; pipeline fails fast (scanner never invoked, nothing persisted) for both
  request-driven and config-driven unregistered disciplines; pipeline refuses a run with
  zero registered analyzers; a supported discipline is NOT fail-fast (proceeds to scan).

## Verification

| Criterion | Result |
|-----------|--------|
| Build (0 errors, 0 warnings) | ✅ |
| All tests pass | ✅ 199/199 (17 new) |
| Consumer-DTO contract test green | ✅ `schemaVersion` unchanged at **1.1** |
| Offline Mock smoke | ✅ package produced; `successfulProviders=1 partialProviders=0 failedProviders=0 unclaimedObservations=0` |
| A6 CLI fail-fast smoke | ✅ `review --disciplines Performance` exits **1**, reports `Requested discipline 'Performance' has no registered analyzer.`, writes **no** output |
| `engineering-review-package.json` compatible | ✅ only additive metric fields (`partialProviders`, `unclaimedObservations`); sample regenerated |

```
dotnet build EngineeringCouncil.slnx        # 0 errors, 0 warnings
dotnet test  EngineeringCouncil.slnx        # Passed: 199 (no credentials required)
```

## Deferred diagnostic findings

C4 (context budget vs 8000-char truncation mismatch) · C6 (`ContextFilesConsidered`
semantics) · stable evidence IDs. These remain on the diagnostic backlog and were
deliberately **not** changed by this milestone. A4/A5/A6 were the verified scope and are
now closed.
