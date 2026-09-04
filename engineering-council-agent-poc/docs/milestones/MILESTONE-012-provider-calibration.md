# MILESTONE-012 — Real Provider Evaluation and Calibration

- **Status:** ✅ Complete
- **Date:** 2026-07-24
- **Version:** v12 (evaluation + calibration harness)
- **ADR:** [ADR-013](../adr/ADR-013-provider-calibration.md)

## Goal

Evaluate, measure, and calibrate the EXISTING platform against real repositories and real
provider executions. No new analysis capability, no councils, no ranking. The output the
external Engineering Review application consumes stays exactly
`engineering-review-package.json`.

## What changed

- **Additive instrumentation (diagnostic only):** `AnalysisRun.StageTimings`
  (`RunStageTimings`) captures per-stage wall-clock; `ObservationInterpretationSummary`
  gains `InvalidFileReferencesDropped` (references the "do not invent files" guard dropped).
  Neither is in the package; neither changes analysis.
- **Evaluation framework** (`EngineeringCouncil.Infrastructure.Evaluation`):
  - `EvaluationMetricsCollector` — pure, read-only measurement of a completed run:
    operational timings, context selection, prompt calibration, reconciliation, quality
    distributions, provider usage (+ optional cost), and package validation.
  - `EvaluationRunner` — runs the existing pipeline over a dataset (per-case DI factory),
    records failures instead of throwing, and supports per-provider comparison on identical
    inputs.
  - `EvaluationReportExporter` — writes the internal `evaluation-report.md`. Presents
    measurements; never declares a winner, ranks, or weights.
  - `ModelPricing` — optional, configuration-only cost estimation; absent pricing/usage ⇒
    cost reported as unavailable, never invented.
- **Evaluation dataset** (`evaluation/dataset/`): five small, deterministic, self-authored
  fixtures with documented `evaluation.json` manifests — clean control, vulnerable (+SARIF),
  tangled architecture, fragile reliability, undocumented/untested.
- **CLI `evaluate` verb:** `--dataset`, `--outputs`, `--providers`, repeatable `--compare`
  (per-provider on identical inputs), `--disciplines` / `--discipline`.

## Metrics captured

Repository metadata · providers/disciplines executed · execution duration · token usage ·
estimated cost (optional) · observations · raw & consolidated findings · reconciliation
metrics (duplicate reduction %, agreement distribution, contradictions, false-merge
candidates) · context metrics (selected/omitted files, characters, repeated sends,
truncations) · prompt calibration (schema failures, invalid %, repair success, hallucinated
refs, missing locations, unsupported types) · quality distributions · operational stage
timings (slowest stage, provider latency) · package validation (size, serialization time,
determinism, required root properties, no type leaks).

## Outputs

Unchanged per-run artifacts, plus the internal `evaluation-report.md`. The external
application still consumes only `engineering-review-package.json`. A representative report
is committed at `evaluation/evaluation-report.md`.

## Tests (all green — 157/157, +7 new)

`EvaluationTests`: metrics reflect the run and collection does NOT change the package ·
operational timings captured · cost absent without pricing · cost stays unavailable when a
provider reports no usage even with pricing · report presents measurements with no
winner/ranking/weighting language · an evaluation error is recorded not thrown · the
committed dataset manifests load with documented purposes and a SARIF-bearing repo.

## Exit criteria

| Criterion | Result |
|-----------|--------|
| Build (0 errors, 0 warnings) | ✅ |
| All normal tests pass (no keys/network) | ✅ 157/157 |
| Real repositories evaluated | ✅ 5-repo dataset, run offline (Mock/SARIF) |
| Claude vs OpenAI comparable on identical inputs | ✅ `--compare` (opt-in with keys) |
| Context selection metrics collected | ✅ |
| Reconciliation metrics collected | ✅ |
| Package compatibility intact | ✅ consumer-DTO gate unchanged, `schemaVersion 1.1` |
| Evaluation report generated | ✅ `evaluation-report.md` |
| External app still consumes the same package unmodified | ✅ |

## Verification

```
dotnet build EngineeringCouncil.slnx        # 0 errors, 0 warnings
dotnet test  EngineeringCouncil.slnx        # Passed: 157 (no credentials required)

# Offline evaluation over the dataset
dotnet run --project src/EngineeringCouncil.Cli -- evaluate \
    --dataset ./evaluation/dataset --providers Mock,Sarif \
    --disciplines Security,Reliability,Architecture,Documentation,Testing,CodeQuality \
    --outputs ./outputs/evaluations
#   → 5/5 repositories evaluated; evaluation-report.md written

# Provider comparison on identical inputs (needs credentials)
dotnet run --project src/EngineeringCouncil.Cli -- evaluate \
    --dataset ./evaluation/dataset --compare Claude --compare OpenAI --discipline Security
```

## Explicitly out of scope (future milestones)

Discipline Councils · Engineering Council · recommendation engine · provider ranking ·
provider weights · LLM reconciliation · ticket generation · automatic code fixes · PR
generation. This milestone strictly measures and validates.
