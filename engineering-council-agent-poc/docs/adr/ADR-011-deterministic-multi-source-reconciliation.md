# ADR-011 — Deterministic Multi-Source Reconciliation

- **Status:** Accepted
- **Date:** 2026-07-22
- **Milestone:** [MILESTONE-010](../milestones/MILESTONE-010-deterministic-multi-source-reconciliation.md)
- **Builds on:** [ADR-004](./ADR-004-finding-merger-and-council-summary.md), [ADR-007](./ADR-007-engineering-review-package.md), [ADR-008](./ADR-008-engineering-observation-layer.md), [ADR-010](./ADR-010-native-sarif-provider.md)

## Context

The platform now acquires and normalizes evidence from heterogeneous sources — LLM
providers (Claude, Codex, Mock) and the native SARIF import (ADR-010). Each analyzer
turns observations into raw findings, but multiple sources routinely describe the *same*
engineering issue. Until now the pipeline's only consolidation was the rule-based
`IFindingMerger`, which deliberately refused to merge across providers.

The external **Engineering Review application** must consume a single consolidated
result — `engineering-review-package.json` — without knowing about raw findings,
observations, providers, acquisition plans, reconciliation internals, SARIF, or
LLM-specific formats.

## Decision

Introduce a deterministic **`IFindingReconciler`** that runs BEFORE the Engineering
Review Package builder, turning raw findings + observations into one consolidated
finding collection plus traceability, and make the package the stable integration
contract.

- **`ReconciliationResult { ConsolidatedFindings, Groups, Summary }`** — the reconciler's
  output. The pipeline stores the consolidated findings on `AnalysisRun.Findings`, the
  summary on `AnalysisRun.ReconciliationSummary`, and the groups on
  `AnalysisRun.ReconciliationGroups`; `RawFindings` are preserved untouched.
- **Staged, explicit grouping** (all require the same discipline; first match wins):
  1. `exact-rule-location` — shared normalized rule id + shared file + close lines.
  2. `type-symbol` — shared observation type + shared file + shared symbol.
  3. `title-location` — similar normalized title (Jaccard ≥ 0.6) + shared file or symbol.
  4. `keep-separate` — insufficient deterministic evidence ⇒ standalone.
- **Provider-independent agreement.** `AgreementCount` counts distinct provider
  identities, not the number of raw findings; `SupportingProviders` and
  `SupportingFindingIds` are both preserved.
- **Severity** keeps the highest and records a `SeverityRange`; agreement never inflates
  severity. **Confidence** starts from the highest source confidence, boosts moderately
  for ≥2 independent providers (+1) and more when a deterministic source corroborates an
  LLM (+2), caps at High, and is limited by location disagreement / discipline-mismatch
  tags. Never an average; never provider weights.
- **Contradictions** (material severity spread, disjoint files) are flagged
  (`HasContradiction`, `ContradictionReasons`) — never silently merged away.
- **Stable ids** derive from discipline + primary rule + primary file + primary symbol +
  normalized title (SHA-256 → `SEC-…`), so the same issue in the same repository state
  yields the same id regardless of finding order. Collisions get a numeric suffix.
- **Package integration is additive:** `schemaVersion: "1.1"`, a provider-neutral
  `reconciliation` summary, and consolidated findings carrying reconciliation fields.
  The legacy `version: "1.0"` and all prior fields remain.

## Why reconciliation occurs before package construction

The package is the product. If reconciliation ran after (or inside) package building,
every consumer and projection would have to re-derive consolidation. Placing the
reconciler between raw findings and the builder means the builder — and therefore
`engineering-review-package.json`, the markdown, and the metrics — all see one already
consolidated truth.

## Why deterministic reconciliation before LLM councils

Determinism is testable, reproducible, offline, and cheap. It gives the external
application a stable contract now, and establishes the seam (`IFindingReconciler`) an
LLM council could later sit behind without changing callers. Shipping an LLM arbiter
first would make the integration contract non-reproducible and unversioned.

## Why false negatives are preferred to incorrect merges

An incorrect merge hides a real, distinct issue and corrupts provider attribution and
severity — actively misleading. A missed merge merely leaves two findings a human can
still read. So every stage demands concrete shared evidence (rule, location, symbol, or
strong title overlap); "sounds related" is never enough.

## Why the Engineering Review Package remains the official integration contract

There must be exactly one machine-to-machine artifact. `engineering-review-package.json`
already carries the executive view, findings, metrics, coverage, and provenance; adding
reconciliation additively keeps a single product. `findings.json`, `raw-findings.json`,
`observations.json`, `provider-execution.json`, and `run.json` stay strictly diagnostic.

## Why raw artifacts remain internal

Raw findings, observations, and provider execution are debugging and audit aids whose
shape may change. Binding the external application to them would couple it to internal
stages. It consumes only the package.

## How schema versioning protects the external application

`schemaVersion` is explicit and documented: additive fields bump the minor (1.0 → 1.1),
breaking removals/renames bump the major. Optional new properties never prevent an older
consumer from reading the package, and no implementation-specific .NET type names or
polymorphic metadata are ever serialized — only stable, camelCase JSON property names.

## Consequences

- `Finding` gains additive reconciliation fields (`SupportingFindingIds`,
  `AgreementCount`, `SeverityRange`, `ConfidenceRange`, `ReconciliationReason`,
  `ReconciliationStrategy`, `IsConsolidated`, `HasContradiction`, `ContradictionReasons`,
  plus `SymbolReferences`, `LineReferences`, `Metadata`).
- The pipeline consolidates via the reconciler; the `IFindingMerger` is retained
  (registered, unit-tested) for backward compatibility but is no longer on the pipeline.
- A consumer-facing DTO + contract test gate the integration, and a representative
  `artifacts/samples/engineering-review-package.sample.json` documents it.

## Explicitly out of scope

LLM reconciliation, voting, provider weights, discipline councils, council deliberation,
recommendation engine, ticket generation, parallel execution, embeddings, vector DBs.
