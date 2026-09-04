# ADR-019 — Finding Calibration Diagnostics

- **Status:** Accepted
- **Date:** 2026-08-07
- **Milestone:** [MILESTONE-012.3](../milestones/MILESTONE-012.3-finding-calibration-diagnostics.md)
- **Builds on:** [ADR-011](./ADR-011-deterministic-multi-source-reconciliation.md),
  [ADR-013](./ADR-013-provider-calibration.md),
  [ADR-018](./ADR-018-provider-comparison-semantics.md)

## Context

The calibration track produced a descriptive, provider-neutral
provider-comparison artifact (ADR-018 / Milestone 012.2). The next diagnostic
question is **finding-level**: of the findings a multi-provider run actually
produced, which were exclusive to one provider, which exposed a severity
disagreement between providers, and which disciplines had so little
cross-provider agreement that single-provider output should not be over-trusted?
Like the comparison milestone, this must be a **pure projection of the run's own
already-reconciled data** — no provider calls, no rescan, no semantic matching,
no re-interpretation, no ranking or scoring of providers, and no change to the
external `engineering-review-package.json` consumer contract.

## Decision

Add an **internal, deterministic, provider-neutral finding-calibration artifact**
(`calibration-diagnostics.json`), generated only when a run has at least two LLM
provider executions of the same discipline. It is a pure projection of the
already-completed `AnalysisRun` (raw findings, reconciled `Findings`,
`ProviderComparison`) built by `CalibrationDiagnosticsBuilder.Build(run)` and
carried on `AnalysisRun.CalibrationDiagnostics` (null for single-provider runs).
Never part of the external contract.

### Exactly three diagnostic types, and nothing else

- **`ExclusiveFinding`** — a consolidated finding supported by **exactly one**
  compared provider (`FindingMetrics.ExclusiveFindingIds` per provider from the
  M12.2 comparison — the same exclusivity definition, so the two artifacts can
  never disagree). Ids are resolved through the consolidated `Findings`;
  unresolved or deduplicated ids are skipped.
- **`SeverityDisagreement`** — a **shared** consolidated finding (supported by
  ≥2 compared providers) whose reconciler `SeverityRange` contains an en dash
  (`–`), i.e. the sources disagreed on severity. Per-provider severities are
  attributed by mapping each `SupportingFindingId` to the **single** compared
  provider that produced it (from `EvidenceProvider`, else exactly-one
  `SupportingProvider`) — so attribution can never disagree with
  reconciliation. Unattributable ids are reported as "unknown".
- **`LowAgreement`** — one per discipline when the discipline's
  `AgreementMetrics.ConsolidatedFindingCount` ≥ `MinimumSampleFindings` (3)
  **and** `AgreementRate < LowAgreementThreshold` (0.5). Counts are copied
  verbatim from M12.2's agreement metrics (shared/exclusive-by-provider).

A finding never gets two diagnostics: exclusive findings are mutually exclusive
with shared findings; a severity disagreement implies a shared finding (never
"exclusive"); LowAgreement is a discipline-level signal, not a finding-level one.
No ranking, scoring, weighting, or calibration of providers is ever produced.

### Comparable-only, limitations elsewhere

Diagnostics are produced **only** for `ProviderComparisonStatus.Comparable`
disciplines. `NonComparable` (same executions, different context fingerprints)
and `Incomplete` (an execution failed) disciplines contribute a `Limitations`
note explaining why no diagnostics were produced — they are never dropped and
never fail the report, consistent with ADR-018.

### Determinism and artifact boundary

- Every collection in the report is sorted; the logical result is identical
  regardless of provider, observation, or finding enumeration order (only
  `GeneratedAt` is a wall-clock timestamp, consistent with existing artifacts).
  Finding ids are resolved through M12.2's `ExclusiveFindingIds` (never a raw
  id, whose `StableId` may have been renamed by the reconciler).
- `calibration-diagnostics.json` is written **only** when
  `AnalysisRun.CalibrationDiagnostics is not null` (≥2 LLM providers on a
  discipline) — between `provider-comparison.*` and `run.json`. Single-provider
  runs are byte-identical to before.
- The consumer DTO gate (`PackageContract`, `schemaVersion` **1.1**) is
  unchanged and a test asserts the package never carries `calibration`,
  `exclusiveFinding`, or `severityDisagreement`.

## Trade-offs and decisions

- **Projection over analysis.** Building from the run's own reconciled findings
  and the M12.2 comparison guarantees no provider calls, no rescan, no semantic
  matching, and no new merge/similarity rules that could disagree with
  reconciliation.
- **Single source of truth per fact.** Exclusivity reuses M12.2's exact
  `ExclusiveFindingCountByProvider`; severity disagreement reuses the
  reconciler's `SeverityRange` and `SupportingFindingIds`; agreement reuses
  M12.2's `AgreementMetrics`. The calibration artifact can therefore never
  contradict either the comparison or the reconciliation it describes.
- **Thresholds as explicit constants, not magic.** `MinimumSampleFindings = 3`
  and `LowAgreementThreshold = 0.5` live on `CalibrationDiagnosticCriteria` so
  the sample-size gate and the agreement cut-off are visible, documented, and
  testable.
- **Comparable-only.** Diagnostic signals would be meaningless or misleading for
  disciplines where providers saw different context or one failed; those are
  represented honestly as limitations instead.
- **Internal only.** Like the comparison, calibration diagnostics stay strictly
  diagnostic — the external app depends on `engineering-review-package.json`
  alone, so internal diagnostics can evolve freely.

## Consequences

- Dual-provider runs now produce a deterministic, descriptive calibration
  artifact: provider-exclusive findings, provider severity disagreements on
  shared findings, and per-discipline low-agreement signals with explicit
  sample-size and rate thresholds.
- Single-provider runs are byte-identical to before (no calibration artifact);
  the package is unchanged (`schemaVersion 1.1`).
- `run.json` round-trips `CalibrationDiagnostics` additively.
- The evaluation framework can consume the dataset without new provider calls.

## Explicitly out of scope (deferred)

Ranking/scoring/weighting/calibration of providers, semantic-similarity
matching, changing reconciliation rules, exposing any calibration field on the
external package, using diagnostics to change findings or confidence, and
re-interpreting raw responses for attribution. See MILESTONE-012.3.
