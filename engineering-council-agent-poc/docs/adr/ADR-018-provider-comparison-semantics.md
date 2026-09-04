# ADR-018 — Real Provider Comparison

- **Status:** Accepted
- **Date:** 2026-08-07
- **Milestone:** [MILESTONE-012.2](../milestones/MILESTONE-012.2-real-provider-comparison.md)
- **Builds on:** [ADR-005](./ADR-005-evidence-provider-layer.md),
  [ADR-006](./ADR-006-multi-provider-execution.md),
  [ADR-011](./ADR-011-deterministic-multi-source-reconciliation.md),
  [ADR-012](./ADR-012-real-llm-evidence-providers.md),
  [ADR-016](./ADR-016-context-budget-correctness.md),
  [ADR-017](./ADR-017-real-provider-metrics.md)

## Context

Milestone 012.1 proved that real providers (Claude, OpenAI) can run against one
shared contract and record reliable, provider-neutral per-execution metrics
(`provider-execution.json`). The evaluation harness then asks: did the two
providers actually see the same thing, and did they agree? Answering this from the
**run's own data** — without new provider calls, a rescan, a context rebuild, or a
re-interpretation of raw responses — is the natural next step of the calibration
track. It must stay descriptive: the council still never ranks, scores, weights, or
calibrates providers, and the external `engineering-review-package.json` consumer
contract is untouched.

## Decision

Add an **internal, deterministic, provider-neutral provider-comparison artifact**
(`provider-comparison.json` + a concise `provider-comparison.md`), generated only
when a run has at least two LLM-provider executions of the same discipline. The
comparison is a **pure projection of the already-produced `AnalysisRun`** built by
`ProviderComparisonBuilder.Build(run)` and carried on `AnalysisRun.ProviderComparison`
(null for single-provider runs). Never part of the external contract.

### Comparability — context equivalence, not trust

Two LLM executions are comparable only when they targeted the **same discipline**
and received the **same effective context**, proven by a new deterministic
`ContextFingerprint`:

- `ContextFingerprint.Compute(AnalysisContextSelection, ContextContentPolicy?)` =
  SHA-256 over the selected files ordered by relative path, each carrying its
  **effective** content after `ContextContentPolicy.MaxCharactersPerFile` (exactly
  what the renderer produces), `\0`-joined; output `"CTX-"` + first 12 hex chars.
- The fingerprint deliberately excludes absolute paths, provider name, model, API
  keys, timestamps, and RunId — two providers that received identical effective
  context produce the identical value.
- The executor stamps it on every execution record and every `Evidence`
  (`ContextFingerprint`, default `""`) on **both** the success and failure paths.
- A discipline is `Comparable` when all executions succeeded and the fingerprints
  agree; `NonComparable` when executions succeeded but fingerprints differ
  (execution metrics still reported, agreement withheld); `Incomplete` when any
  execution failed (output comparison unavailable, never fails the report).

### Descriptive metrics only

- **Execution metrics** reuse the M12.1 telemetry verbatim (no re-collection):
  durations, retries, repairs, tokens, model via `ProviderVersion`, error category.
  Unknown token usage stays unknown — no difference is ever inferred from `null`s.
- **Observation metrics** are per-provider counts and sorted distributions (type,
  severity, confidence), files referenced, with/without location, and a descriptive
  `ReferencedContextFileRate` (referenced ÷ context files; `null` when no context).
- **Finding/agreement metrics** are derived **only** from the existing
  `RuleBasedFindingReconciler` output (`Finding.SupportingProviders` +
  `AgreementCount`). No semantic similarity, no new merge rules. A shared finding
  is one supported by ≥2 compared providers; exclusive findings are supported by
  exactly one. `AgreementRate = SharedFindingCount ÷ ConsolidatedFindingCount`
  (`null` when there are no consolidated findings) — agreement is **not** accuracy.
- SARIF/static-analysis executions are excluded from the comparison (LLM executions
  only), though a deterministic source's corroboration still counts via the
  reconciler's `SupportingProviders`; `MultiProviderFindingCount` reflects the
  reconciler's `AgreementCount ≥ 2` semantics (may include non-compared sources).

### Determinism and artifact boundary

- Every collection in the report is sorted; the logical result is identical
  regardless of provider, observation, or finding enumeration order (only
  `GeneratedAt` is a wall-clock timestamp, consistent with existing artifacts).
- `provider-comparison.json` / `provider-comparison.md` are written **only** when
  `AnalysisRun.ProviderComparison is not null` (≥2 LLM providers on a discipline).
- The consumer DTO gate (`PackageContract`, `schemaVersion` **1.1**) is unchanged
  and a test asserts the package never carries `providerComparison`.

## Trade-offs and decisions

- **Projection over re-collection.** Building from the run's own records,
  observations, and reconciled findings guarantees no provider calls, no rescan,
  no context rebuild, no re-interpretation, and no cost. A side-by-side rerun for
  comparison would be expensive and non-deterministic.
- **Fingerprint over "same selector, trust me".** Two providers could in principle
  receive different context (different policy/config). The fingerprint proves
  equivalence from the artifacts themselves.
- **Reconciler output over similarity.** Agreement must come from the one place the
  system already proves two sources describe the same issue — never from a new,
  ad-hoc similarity heuristic that could silently disagree with reconciliation.
- **`NonComparable`/`Incomplete` over skipping.** The report represents what is
  comparable honestly (partial/failed runs included with `Limitations` notes)
  instead of failing or hiding the fact that comparison was unavailable.
- **Internal only.** Comparison artifacts stay strictly diagnostic — the external
  app depends on `engineering-review-package.json` alone, so internal metrics can
  evolve freely.

## Consequences

- Real dual-provider runs now produce a deterministic, descriptive comparison of
  what Claude and OpenAI each produced under equivalent context, with explicit
  agreement/shared/exclusive counts and honest limitations.
- Single-provider runs are byte-identical to before (no comparison artifact).
- `run.json` round-trips `ProviderComparison` additively; the package is unchanged.
- The evaluation framework can build the agreement/long-tail dataset (M12.3) on top
  of this artifact without new provider calls.

## Explicitly out of scope (deferred)

Ranking/scoring/weighting/calibration of providers, semantic-similarity merging,
changing reconciliation rules, comparison triggers (side-by-side reruns), exposing
any comparison field on the external package, and using agreement to change
findings or confidence. See MILESTONE-012.2.
