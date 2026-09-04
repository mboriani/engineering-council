# ADR-006 — Multi-Provider Evidence Execution

- **Status:** Accepted
- **Date:** 2026-07-07
- **Milestone:** [MILESTONE-005](../milestones/MILESTONE-005-multi-provider-execution.md)
- **Builds on:** [ADR-005](./ADR-005-evidence-provider-layer.md)

## Context

The Evidence Provider layer (ADR-005) let an analyzer obtain evidence from a
single provider chosen by configuration. But a real Engineering Council draws on
several sources at once — e.g. Claude *and* a static analyzer — and must keep
running when one of them is down. We want a single analyzer to collect evidence
from **multiple** providers per analysis, while explicitly **not** yet doing any
voting, comparison, or consensus.

## Decision

Introduce an **`IEvidenceExecutor`** between the analyzer and the providers:

- The analyzer builds one `EvidenceRequest` and hands it to the executor.
- The executor runs **every configured provider** (`Evidence:Providers`) for that
  request, isolating each: an unknown/unavailable provider, a thrown exception,
  or a timeout becomes a **failed `Evidence`** (with `Success=false`,
  `ErrorMessage`, `Duration`) and the remaining providers still run.
- The executor returns `IReadOnlyList<Evidence>`. The analyzer interprets **each**
  piece of evidence into findings, stamping every finding with the
  `EvidenceProvider` it came from (and its own analyzer name).
- Execution is **sequential**; the per-provider work is isolated in one method so
  enabling `Task.WhenAll` later is a one-method change.

Supporting changes:

- `Evidence` gains `ProviderId`, `ProviderVersion`, `Success`, `ErrorMessage`,
  `Duration`, `CostEstimate`.
- An `IProviderExecutionLog` collects a `ProviderExecutionRecord` per call; the
  pipeline snapshots it into a `ProviderExecutionReport` written to
  **`provider-execution.json`** (providers executed, executions, failures,
  evidence count, tokens, per-provider and per-record detail).
- The rule-based merger will **not merge findings across different providers**
  (guard on `EvidenceProvider`), preserving per-provider attribution. Cross-
  provider reconciliation/voting is a future milestone.
- Configuration moves from a single `Evidence:DefaultProvider` to a list
  `Evidence:Providers`; the CLI adds `--providers a,b` (and keeps `--provider`).

## Why analyzers consume evidence instead of binding to providers

1. **Multiplicity without coupling.** An analyzer that consumed a provider
   directly could use exactly one. Consuming a *collection of evidence* lets the
   same analyzer transparently use one provider or ten — the number and identity
   of providers is a configuration/executor concern, not an analyzer concern.
2. **Failure isolation is natural.** Because evidence is just data the executor
   returns, a missing or failing provider is simply an absent/failed `Evidence`
   entry — the analyzer degrades gracefully instead of crashing.
3. **Attribution and future consensus.** Each finding records which provider's
   evidence produced it. That per-provider provenance is the raw material a
   future council needs to compare, weight, and vote — none of which is possible
   if the analyzer had already collapsed everything into one provider's answer.
4. **Uniform telemetry.** Modeling every call as `Evidence` + a
   `ProviderExecutionRecord` gives one honest place to record duration, tokens,
   cost, and failures across heterogeneous backends.

## Consequences

- With N providers configured, each analyzer makes N provider calls; raw findings
  multiply by (successful providers). The consolidation layer keeps per-provider
  findings separate for now.
- The execution log is a run-scoped singleton reset by the pipeline; concurrent
  runs in one process would interleave — acceptable because parallel execution is
  explicitly deferred.

## Explicitly out of scope

No provider voting, no confidence comparison, no consensus, no council, and no
parallel execution. This milestone only collects evidence from multiple providers.
