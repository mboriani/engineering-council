# ADR-026 — Finding Deduplication Identity

- **Status:** Accepted
- **Date:** 2026-08-19
- **Milestone:** [MILESTONE-015.3D](../milestones/MILESTONE-015.3D-deterministic-finding-deduplication.md)
- **Amended:** [MILESTONE-015.4A](../milestones/MILESTONE-015.4A-dedup-false-merge-hardening.md) — added the
  primary-focus anchor requirement (see "Amendments (M15.4A)" below).
- **Builds on:** [ADR-011](./ADR-011-deterministic-multi-source-reconciliation.md), [ADR-008](./ADR-008-engineering-observation-layer.md)

## Context

Multiple providers routinely report the SAME engineering defect, and the deterministic
reconciler (ADR-011) is deliberately conservative — false negatives preferred. The
persisted M15.2 run exposes two genuine cross-provider duplicates that every existing
stage keeps separate: the RequestFilter NullReferenceException defect (RAW-012/RAW-016)
and the outbound-wallet HttpClient-without-resilience defect (RAW-011/RAW-014).

We need a **FindingDeduplicationKey** — a deterministic, symbol-grounded identity that
answers "are these two findings the same defect?" without an LLM, embeddings, or fuzzy
similarity. It must merge exactly the genuine duplicates and never the near-misses
(ContextLogger, broad-catch, magic-string, health-endpoint, cross-discipline views).

## Decision

Add a **dedup-identity** stage to `RuleBasedFindingReconciler` (score 0.9, between
`exact-rule-location` 0.95 and `type-symbol` 0.85). Two same-discipline findings are the
same finding only when ALL of the following hold:

1. **Shared method-part symbol** — the last `.`-segment of a symbol (`MethodPart`), so
   fully-qualified and short spellings are the same identity even when the exact symbol
   sets of stage 3 do not intersect.
2. **Primary-focus anchor (M15.4A)** — the shared method-part must be a PRIMARY-focus
   method-part of BOTH findings: it is attached to an observation whose normalized title
   is similar enough to the finding's own title
   (`PrimaryFocusAnchorSimilarity = 0.30`, exact Jaccard on normalized tokens). An
   observation without a title is never primary-focus. A bundle's secondary observation
   therefore never lends dedup identity.
3. **Close lines, same file** for that symbol — at least one per-symbol evidence pair from
   each finding's PRIMARY-focus evidence in the same normalized file within
   `LineTolerance = 3`.
4. **Focus consistency** — the normalized titles share ≥ `MinFocusTitleTokens = 2`
   significant tokens (exact, stopword-filtered). Never a summary-text match. Retained as
   a redundant secondary barrier now that the primary-focus anchor decides identity.
5. **No material severity contradiction** — `|severityA − severityB| ≥ 2` refuses the pair.

The identity is **additive**: the exact-rule-location and type-symbol stages are
untouched, existing merged findings keep their strategy/reason, and the reconciliation
contract gains defaulted `PreDedupFindingCount`/`PostDedupFindingCount`/
`DeduplicatedFindingCount` (external package `schemaVersion` stays 1.1). Persisted
pre-M15.3D documents still deserialize (the new fields are not `required`).

## Consequences

- The genuine duplicates merge deterministically (29 → 27 on the persisted M15.2 run);
  every near-miss stays separate (regression-tested offline and via the replay test).
- The primary focus guard is the sole title signal: a verbose summary that happens to
  name shared symbols can never create a merge. This explicitly avoids the trap where
  RAW-016's summary naming `GetContextAsync`/`RedirectToAsync` would otherwise absorb the
  distinct broad-catch finding RAW-015.
- False negatives remain possible (e.g. < 2 shared significant title tokens, or no symbol
  evidence) — accepted, consistent with ADR-011's conservatism.
- Severity spread (High↔Medium) is recorded as a range on the merged finding, never
  erased; an explicit Low↔Critical contradiction is never deduplicated.
- The dedup stage never crosses disciplines (same barrier as every other stage), so
  observability/code-quality views of the same defect remain separate findings by design.

## Amendments (M15.4A)

The M15.4 real-repository run exposed one false merge that the M15.3D guard did not prevent:
bundle `RAW-010` ("Duplicated magic string for named HttpClient construction") absorbed the
distinct finding `RAW-014` ("Inconsistent, uncentralized magic string keys for context items
(CustomerID/BrandID casing)") because the titles share the generic 2-token phrase {magic,
string} and RAW-010 carried a secondary observation sharing `WriteLog`/`OnResultExecutionAsync`.

The anchor is now the PRIMARY-FOCUS method-part (requirement 2 above), and the close-line check
is scoped to primary-focus evidence (requirement 3). The threshold `0.30` is grounded in the
persisted M15.2/M15.4 runs: same-defect-rephrased observations score ≥ 0.37 (OBS-081 = 0.37,
OBS-057 = 0.39, OBS-070 = 0.38, OBS-021 = 0.55, OBS-032 = 0.58, OBS-139 = 0.38), distinct-sub-issue
observations score ≤ 0.25 (the false-merge RAW-010 vs OBS-014 = 0.25 is the maximum). Corpus
document-frequency "distinctive token" tests were tried and REJECTED (the false-merge tokens
{magic, string} have corpus-wide DF 2/55 = 0.04, identical to genuinely distinctive tokens — not
discriminating). The dedup SCORE stays 0.9; only the identity predicate changed.

Consequences of the amendment: the false merge no longer occurs (RAW-010 and RAW-014 stay
separate), all three genuine M15.4 merges are preserved (wallet 4-member `REL-42ba184796`,
brand-auth testing `TST-d4244b718f`, readiness probe `OBS-70131abcc2`), and the M15.2 replay is
unchanged (29 → 27, both genuine merges still fire — including the wallet pair whose shared
method-parts come from rephrased same-defect observations at 0.37/0.38, proving the anchor is not
an exact-title requirement). Cluster-order safety is intrinsic: the anchor is computed per finding
from its own observations + title, so equivalent input permutations yield equivalent partitions.
No hardcoded blacklist, no embeddings, no fuzzy similarity, no schema/contract change.