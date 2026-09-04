# ADR-013 — Provider Evaluation and Calibration

- **Status:** Accepted
- **Date:** 2026-07-24
- **Milestone:** [MILESTONE-012](../milestones/MILESTONE-012-provider-calibration.md)
- **Builds on:** [ADR-011](./ADR-011-deterministic-multi-source-reconciliation.md), [ADR-012](./ADR-012-real-llm-evidence-providers.md)

## Context

The architecture is now complete end-to-end: real Claude and OpenAI providers, Mock and
SARIF sources, deterministic acquisition, interpretation, observations, analyzers,
reconciliation, and a stable external package contract. What has NOT been done is
*measuring* the platform on real repositories. Milestone 012 adds an evaluation and
calibration framework — instrumentation and reporting only — so future decisions rest on
evidence rather than intuition.

## Decision

Add a measurement layer around the existing pipeline: additive run instrumentation, a
pure metrics collector, an `EvaluationRunner`, an `EvaluationReportExporter` that writes an
internal `evaluation-report.md`, a small deterministic fixture dataset, and an `evaluate`
CLI verb.

- **Additive instrumentation only.** `AnalysisRun` gains diagnostic `StageTimings`; the
  interpretation summary gains `InvalidFileReferencesDropped`. Neither is part of the
  Engineering Review Package, and neither changes analysis output.
- **The collector is pure measurement.** `EvaluationMetricsCollector` reads a completed
  `AnalysisResult` and produces context, prompt-calibration, reconciliation, quality,
  usage, operational, and package-validation metrics. It never mutates the run or the
  package — a test asserts the package is unchanged after collection.
- **Measurements, not verdicts.** The report presents provider-comparison tables over
  identical inputs but never declares a winner, ranks providers, or assigns weights.
- **Cost is optional configuration.** Pricing lives in `Evaluation:Pricing`, never in the
  domain. Absent pricing or absent usage means cost is reported as *unavailable*, never
  invented, and the external consumer never depends on cost fields.
- **The package is untouched.** `engineering-review-package.json` stays at
  `schemaVersion 1.1`; `evaluation-report.md` is a strictly internal artifact.

## Why calibration precedes councils

A council (LLM arbitration, weighting, voting) is a large, non-deterministic addition. Its
value can only be judged against a baseline: how much do real providers actually agree,
how often does reconciliation merge correctly, where is time and cost spent, how often is
structured output invalid? Building a council before measuring would optimize blind.
Calibration produces that baseline and a repeatable harness to compare any future change
against — so it must come first.

## How repositories were selected

Five small, deterministic, self-authored fixtures — no third-party code, no secrets, no
personal data, so the dataset is publicly shareable and reproducible. Each targets a
distinct behavior and documents its purpose in `evaluation.json`:

- `01-clean-service` — a **false-positive control** (findings here mean over-reporting);
- `02-vulnerable-payments` — intentional Security defects **plus a matching SARIF** so
  reconciliation has a real multi-source case;
- `03-tangled-architecture` — layering violations and circular coupling;
- `04-fragile-reliability` — missing timeout/retry/cancellation and a swallowed exception;
- `05-undocumented-untested` — absent docs/tests and a long branch-heavy method.

The control case matters as much as the defect cases: a platform that only ever sees
broken code cannot reveal its false-positive rate.

## How metrics should be interpreted

- **Comparative, not absolute.** Numbers are meaningful *relative to each other* on
  identical inputs, not as universal scores. Offline Mock runs measure the pipeline and
  cost nothing; only real-provider runs measure model behavior.
- **Signals, not gates.** Context "repeated sends", "hallucinated references", or
  "false-merge candidates" flag things to look at; none of them fail a run or change a
  package.
- **False-merge candidates are a review queue**, not defects: they are consolidated
  findings whose sources share no file, surfaced for a human to confirm.
- **Provider comparison is descriptive.** Two sources producing different counts on the
  same repository is information, not a ranking.

## Current limitations

- Offline runs (Mock/SARIF) exercise the harness and the pipeline but not model quality;
  provider comparison and prompt-calibration numbers are only meaningful once real
  providers are enabled with credentials.
- "Hallucinated file references" counts only references the guard could drop (paths not in
  the snapshot); subtler hallucinations (real path, wrong claim) are not auto-detected.
- False-merge detection is a heuristic (no shared file), deliberately conservative.
- The dataset is intentionally tiny and synthetic; it calibrates behavior and shape, not
  real-world prevalence.

## Consequences

- New diagnostic-only fields on `AnalysisRun` / `ObservationInterpretationSummary`; new
  `EngineeringCouncil.Infrastructure.Evaluation` namespace; new `evaluate` CLI verb; a
  committed dataset and a sample `evaluation-report.md`.
- The consumer-DTO contract test remains the authoritative compatibility gate and still
  passes unchanged.

## Explicitly out of scope

Discipline Councils, Engineering Council, recommendation engine, provider ranking,
provider weights, LLM reconciliation, ticket generation, automatic code fixes, PR
generation. This milestone only measures and validates.
