# ADR-004 — Finding Merger and Council Summary (first consolidation layer)

- **Status:** Accepted
- **Date:** 2026-07-06
- **Milestone:** [MILESTONE-003](../milestones/MILESTONE-003-finding-merger-and-council-summary.md)
- **Builds on:** [ADR-003](./ADR-003-specialized-analyzers.md)

## Context

Milestone 002 gave us specialized analyzers, but the pipeline output was a **raw
concatenation** of their findings. With seven disciplines that overlap at the
edges (e.g. architecture and code-quality both flag a god class; reliability and
code-quality both flag a swallowed exception), the raw set contains duplicates,
inconsistent severities, and no executive view.

We want the first **consolidation layer** — the first council-like behavior —
without yet building a real multi-LLM council.

## Decision

Introduce two abstractions between the analyzers and the reporters:

1. **`IFindingMerger`** → `RuleBasedFindingMerger` (deterministic, no LLM).
   It clusters related raw findings and emits a consolidated set:
   - **Merge** when same category AND (similar title, OR overlapping file
     references AND similar recommendation).
   - **Severity** → keep the highest.
   - **Confidence** → High if ≥2 distinct agents corroborate; Medium for one
     agent with substantive evidence; else Low.
   - **Preserve** all evidence and all source agents; **record** severity and
     confidence rationales and the raw ids merged from.
   - **Same title, different category** → kept separate, annotated (rule 3).

2. **`ICouncilSummaryGenerator`** → `RuleBasedCouncilSummaryGenerator`
   (deterministic, no LLM). It produces a `CouncilSummary`: executive summary,
   key risks, recommended next actions, category/severity/confidence
   breakdowns, and a merge summary.

The `Finding` record gains consolidation fields: `Status`
(`New|Merged|Duplicate|Discarded`), `DuplicateOf`, `MergedFromFindingIds`,
`SourceAgents` (future-facing; `SourceAgent` kept for backward compatibility),
`SeverityRationale`, `ConfidenceRationale`.

The pipeline becomes: scan → analyze → **store raw** → **merge** → **summary** →
persist. Raw findings are preserved verbatim in `raw-findings.json`;
`findings.json` now holds the **consolidated** set.

## Why rule-based first

- **Deterministic and testable.** Merge behavior is unit-tested exactly (exact
  duplicate, similar title, severity escalation, agent/evidence preservation,
  no-cross-category merge, raw preserved). An LLM reconciler could not be pinned
  down this precisely.
- **Zero cost / offline.** Consistent with the mock-first design; no tokens
  spent on consolidation.
- **A clean seam.** `IFindingMerger` / `ICouncilSummaryGenerator` are the exact
  insertion points where an LLM reconciler/author will later drop in — callers
  and outputs do not change.

## Consequences

- Output is no longer a raw concatenation; consumers get a deduplicated,
  severity-reconciled set plus an executive summary, while the raw audit trail
  is retained separately.
- Global raw ids (`RAW-NNN`) are assigned at aggregation time so consolidation
  can reference provenance (analyzers number findings locally and would
  otherwise collide).
- Similarity is heuristic (title/recommendation token Jaccard, file-path
  overlap); thresholds are tunable constants. Good enough for consolidation,
  explicitly not a semantic merge — that is the future council's job.
- **Reserved for later:** `Duplicate`/`Discarded` statuses and `DuplicateOf`
  exist in the model but are not emitted by the rule-based merger yet; the
  future LLM council will use them.

## Explicitly out of scope

Multi-provider execution, semantic/LLM merging, and calling this a full
Engineering Council. This is the consolidation layer that prepares for it.
