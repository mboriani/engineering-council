# MILESTONE-012.2 — Real Provider Comparison

- **Status:** ✅ Complete
- **Date:** 2026-08-07
- **Version:** v14 (real provider comparison)
- **ADR:** [ADR-018](../adr/ADR-018-provider-comparison-semantics.md)

## Goal

Produce a deterministic, provider-neutral, **descriptive** comparison of the real LLM
providers (Claude, OpenAI) executed in one run, from data the run **already produced**
— no provider calls, no rescan, no context rebuild, no re-interpretation, no
ranking/scoring/weighting/calibration. The external `engineering-review-package.json`
consumer contract is unchanged (`schemaVersion` stays **1.1**).

## Scope

- **Comparability** — same discipline + same effective context, proven by a new
  `ContextFingerprint` (SHA-256 of effective content per selected file; excludes
  absolute paths, provider, model, keys, timestamps, RunId)
- **Execution metrics** — M12.1 telemetry reused verbatim per provider per discipline
  (durations, retries, repairs, tokens, model, error category)
- **Observation metrics** — per-provider counts + sorted type/severity/confidence
  distributions, files referenced, with/without location, `ReferencedContextFileRate`
- **Finding/agreement metrics** — derived ONLY from the existing reconciler output:
  raw/consolidated/exclusive/multi-provider counts per provider, plus agreement
  (shared ÷ consolidated; `null` when no consolidated findings; never called
  "accuracy")
- **Honest partials** — `Incomplete` (a provider failed) and `NonComparable`
  (fingerprints differ) disciplines are represented with `Limitations` notes, never
  dropped and never failing the report
- **Artifact boundary** — `provider-comparison.json` / `.md` written only when ≥2
  LLM providers executed the same discipline; single-provider runs unchanged

Explicitly **out of scope** (documented, deferred): ranking/scoring/weighting/
calibration · semantic-similarity merging · changing reconciliation rules ·
comparison-trigger side-by-side reruns · exposing comparison on the package.

## Changes

- **`Core/Analysis/ContextFingerprint.cs` (new)** — `Compute(selection, policy?)`:
  SHA-256 over files ordered by `RelativePath`, effective content truncated at
  `ContextContentPolicy.MaxCharactersPerFile`, `\0`-joined; `"CTX-"` + 12 hex.
- **Model (additive, no renames):**
  - `Core/Domain/ProviderExecution.cs` — `ProviderExecutionRecord.ContextFingerprint`
  - `Core/Domain/Evidence.cs` — `Evidence.ContextFingerprint`
  - `Core/Domain/ProviderComparison.cs` (new) — `NamedCount`,
    `ProviderComparisonStatus` (`Comparable`/`NonComparable`/`Incomplete`),
    `ProviderComparisonReport`, `DisciplineComparison`,
    `ProviderExecutionComparison`, `ProviderObservationComparison`,
    `ProviderFindingComparison`, `AgreementMetrics`
  - `Core/Domain/AnalysisRun.cs` — additive nullable `ProviderComparison`
- **`Core/Application/ProviderComparisonBuilder.cs` (new, static)** —
  `Build(AnalysisRun)` → null unless ≥2 distinct LLM providers executed a discipline;
  groups LLM-only records by `RequestedDiscipline`; fingerprints decide
  comparable/status; output comparisons only when all succeeded; agreement only when
  `Comparable`; everything sorted deterministically.
- **`Infrastructure/Acquisition/EvidenceAcquisitionExecutor.cs`** — optional
  `ContextContentPolicy` constructor param; computes the fingerprint once per step and
  stamps it on success evidence, success record, and every failure path (unregistered
  provider, unavailable, timeout, `LlmProviderException`, generic exception);
  `Failure`/`Record` static helpers accept `contextFingerprint`.
- **Pipeline (`Core/Application/AnalysisPipeline.cs`)** — step 7b: after the council
  summary, `run = run with { ProviderComparison = ProviderComparisonBuilder.Build(run) }`
  (success path only; null for single-provider runs).
- **Reporting** — `IJsonReportGenerator.RenderProviderComparison` +
  `JsonReportGenerator` impl (serializes `run.ProviderComparison`);
  `Infrastructure/Reporting/ProviderComparisonMarkdownExporter.cs` (new) — concise
  markdown (scope / comparable inputs / execution metrics / observation comparison /
  finding agreement / provider-exclusive findings / limitations).
- **Persistence** — `FileSystemAnalysisRunRepository.SaveAsync` writes
  `provider-comparison.json` + `.md` only when `run.ProviderComparison is not null`
  (after `provider-execution.json`, before `run.json`).

## Regression tests

New `src/EngineeringCouncil.Tests/ProviderComparisonTests.cs` — **24 focused tests**
on scripted fakes + a real-pipeline dual-provider run (no network, no credentials).
Coverage:

- **Fingerprint:** same effective context ⇒ same value across providers; content
  change ⇒ different; order-independent + excludes absolute paths; honors per-file
  policy truncation
- **Comparability:** single provider ⇒ no report; two providers same discipline/
  context ⇒ `Comparable`; differing fingerprints ⇒ `NonComparable` (agreement
  withheld, execution metrics kept, limitation note); disciplines compared
  separately; static-analysis records excluded
- **Execution metrics:** M12.1 fields reused verbatim (tokens, retries, repairs,
  model); unknown tokens stay unknown + limitation note; failed execution ⇒
  `Incomplete`, empty output comparison, never fails the report
- **Observation metrics:** per-provider counts + type/severity distributions;
  `ReferencedContextFileRate` null when no context files
- **Agreement (real reconciler):** shared findings only from existing
  reconciliation; single-provider findings exclusive; similar-but-unreconciled
  findings stay exclusive; agreement rate null when no findings; agreement not
  computed when contexts differ
- **Determinism & safety:** identical serialized result regardless of enumeration
  order (modulo `GeneratedAt`); clean JSON with no provider SDK types, no secrets,
  no `errorMessage`; package never carries `providerComparison`; single-provider
  pipeline writes no comparison artifacts and the package stays 1.1
- **End-to-end:** dual-provider pipeline writes `provider-comparison.json`
  (2 disciplines, both `Comparable`, shared finding = 1 on Security), package stays
  1.1 with no comparison leak, `run.json` round-trips the comparison

## Verification

| Criterion | Result |
|-----------|--------|
| Build (0 errors, 0 warnings) | ✅ |
| All tests pass | ✅ 247/247 (24 new) |
| Consumer-DTO contract test green | ✅ `schemaVersion` unchanged at **1.1** |
| Offline Mock smoke (single provider) | ✅ no `provider-comparison.json`/`.md` written; records carry `contextFingerprint` (`CTX-…`); package has no `providerComparison`; `schemaVersion 1.1` |
| `engineering-review-package.json` compatible | ✅ unchanged (`schemaVersion 1.1`); comparison stays strictly internal |
| Determinism | ✅ same logical result regardless of provider/observation/finding enumeration order |

```
dotnet build EngineeringCouncil.slnx        # 0 errors, 0 warnings
dotnet test  EngineeringCouncil.slnx        # Passed: 247 (no credentials required)
dotnet run --project src/EngineeringCouncil.Cli -- review --path . --provider Claude,OpenAI
```

## Deferred diagnostic findings

Ranking/scoring/weighting/calibration, semantic-similarity merging, reconciliation
rule changes, side-by-side comparison reruns, and exposing any comparison field on
the external package remain on the diagnostic backlog and were deliberately **not**
changed by this milestone. The agreement/shared/exclusive dataset this milestone
produces is the basis for the M12.3 evaluation dataset.
