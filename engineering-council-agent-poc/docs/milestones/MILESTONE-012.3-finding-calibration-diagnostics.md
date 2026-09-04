# MILESTONE-012.3 — Finding Calibration Diagnostics

- **Status:** ✅ Complete
- **Date:** 2026-08-07
- **Version:** v15 (finding calibration diagnostics)
- **ADR:** [ADR-019](../adr/ADR-019-finding-calibration-diagnostics.md)

## Goal

Produce a deterministic, provider-neutral, **descriptive** finding-calibration
artifact from a multi-LLM run's **already-reconciled** data — no provider calls,
no rescan, no semantic matching, no re-interpretation, no
ranking/scoring/weighting/calibration. The external
`engineering-review-package.json` consumer contract is unchanged
(`schemaVersion` stays **1.1**).

## Scope

Exactly three diagnostic types, only for `Comparable` disciplines:

- **`ExclusiveFinding`** — consolidated finding supported by **exactly one**
  compared provider (reuses M12.2 `FindingMetrics.ExclusiveFindingIds`; resolved
  through the consolidated `Findings`, never a raw id)
- **`SeverityDisagreement`** — **shared** consolidated finding whose reconciler
  `SeverityRange` contains an en dash (`–`); per-provider severities attributed
  via `SupportingFindingIds` → raw findings → a **single** compared provider
- **`LowAgreement`** — per discipline when `ConsolidatedFindingCount ≥
  MinimumSampleFindings (3)` **and** `AgreementRate < LowAgreementThreshold
  (0.5)`; counts copied verbatim from M12.2 `AgreementMetrics`

Rules: one diagnostic per finding at most; `NonComparable` (differing context
fingerprints) and `Incomplete` (a failed execution) disciplines become
`Limitations` notes only; thresholds are explicit
`CalibrationDiagnosticCriteria` constants; everything sorted deterministically.

Explicitly **out of scope** (documented, deferred): ranking/scoring/weighting/
calibration · semantic-similarity matching · changing reconciliation rules ·
exposing any calibration field on the package · using diagnostics to change
findings/confidence.

## Changes

- **`Core/Domain/CalibrationDiagnostics.cs` (new)** —
  `CalibrationDiagnosticType` (`ExclusiveFinding`/`SeverityDisagreement`/
  `LowAgreement`), `CalibrationDiagnosticCriteria` (`MinimumSampleFindings=3`,
  `LowAgreementThreshold=0.5`), `ProviderSeverity`,
  `FindingDiagnostic` (`Type`, `Discipline`, `Provider`, `FindingId`, `Title`,
  `Severity`, `Confidence`, `Files`, `SeverityRange`, `SeverityRationale`,
  `ProviderSeverities`, `ConsolidatedFindingCount`, `SharedFindingCount`,
  `AgreementRate`, `ExclusiveFindingCountByProvider`),
  `CalibrationDiagnosticsReport` (`RunId`, Repository/Branch/Commit,
  `Diagnostics`, `Limitations`, `Criteria`, `GeneratedAt`).
- **Model (additive, no renames):** `AnalysisRun.CalibrationDiagnostics`
  (nullable, after `ProviderComparison`).
- **`Core/Application/CalibrationDiagnosticsBuilder.cs` (new, static)** —
  `Build(AnalysisRun)` → null when `run.ProviderComparison` is null; per
  `Comparable` discipline: exclusive findings from
  `FindingMetrics.ExclusiveFindingIds` resolved in `run.Findings` (deduped,
  unresolved skipped), severity disagreements from shared findings with a `–`
  range, one low-agreement diagnostic when the sample and rate thresholds are
  met; `BuildLimitation` for `NonComparable`/`Incomplete`; a single
  "comparable-only, non-ranking" note; deterministic ordering
  (`Type`→`Discipline`→`Provider`→`FindingId`→`Title`); `GeneratedAt=UtcNow`.
- **Pipeline (`Core/Application/AnalysisPipeline.cs`)** — step 7c: on the
  success path after the comparison, `run = run with {
  CalibrationDiagnostics = CalibrationDiagnosticsBuilder.Build(run) }` (null for
  single-provider runs).
- **Reporting** — `IJsonReportGenerator.RenderCalibrationDiagnostics` +
  `JsonReportGenerator` impl (null-safe).
- **Persistence** — `FileSystemAnalysisRunRepository.SaveAsync` writes
  `calibration-diagnostics.json` only when non-null (after
  `provider-comparison.*`, before `run.json`).

## Regression tests

New `src/EngineeringCouncil.Tests/CalibrationDiagnosticsTests.cs` — **14 focused
tests** on scripted fakes + a real-pipeline dual-provider run (no network, no
credentials). Coverage:

- **Exclusive findings:** Claude-only finding ⇒ diagnostic; OpenAI-only finding
  ⇒ diagnostic; a **shared** finding is never exclusive
- **Severity:** shared finding with reconciler `SeverityRange` `High–Medium`
  ⇒ `SeverityDisagreement` with per-provider severities attributed to exactly
  one compared provider each; **same severity** across providers ⇒ no signal
- **Low agreement:** sufficient sample (4 consolidated, rate 0.25) ⇒ signal
  with counts copied from `AgreementMetrics`; **small sample** (2) ⇒ no signal
  (exclusives still surface)
- **Limitations only:** `NonComparable` discipline ⇒ no diagnostics + a
  limitation note; `Incomplete` discipline ⇒ no diagnostics + a limitation note
- **Determinism:** identical serialized result regardless of enumeration order
  (modulo `GeneratedAt`)
- **Safety / contract:** single-provider run ⇒ no report and no artifact;
  dual-provider pipeline writes `calibration-diagnostics.json` with **no extra
  provider calls** (call counts unchanged before/after reading the artifact);
  package never carries `calibration`/`exclusiveFinding`/`severityDisagreement`
  and stays `schemaVersion 1.1`

## Verification

| Criterion | Result |
|-----------|--------|
| Build (0 errors, 0 warnings) | ✅ |
| All tests pass | ✅ 261/261 (14 new; 247 prior) |
| Consumer-DTO contract test green | ✅ `schemaVersion` unchanged at **1.1** |
| Offline Mock smoke (single provider) | ✅ no `calibration-diagnostics.json` written; no `providerComparison` in package; `schemaVersion 1.1` |
| `engineering-review-package.json` compatible | ✅ unchanged (`schemaVersion 1.1`); calibration stays strictly internal |
| Determinism | ✅ same logical result regardless of provider/observation/finding enumeration order |

```
dotnet build EngineeringCouncil.slnx        # 0 errors, 0 warnings
dotnet test  EngineeringCouncil.slnx        # Passed: 261 (no credentials required)
dotnet run --project src/EngineeringCouncil.Cli -- review --path . --provider Claude,OpenAI
```

## Deferred diagnostic findings

Ranking/scoring/weighting/calibration, semantic-similarity matching,
reconciliation rule changes, exposing any calibration field on the external
package, and using diagnostics to change findings or confidence remain on the
diagnostic backlog and were deliberately **not** changed by this milestone. The
exclusive/severity-disagreement/low-agreement dataset this milestone produces is
the finding-level calibration signal for the evaluation framework — still
measurement only, never a verdict.
