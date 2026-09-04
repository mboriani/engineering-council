# MILESTONE-015.4A — Dedup False-Merge Hardening

- **Status:** Complete
- **Date:** 2026-08-19
- **Milestone type:** Corrective (single concrete defect from [MILESTONE-015.4](./MILESTONE-015.4-second-real-repository-evaluation.md))
- **Builds on:** [MILESTONE-015.3D](./MILESTONE-015.3D-deterministic-finding-deduplication.md), [ADR-026](../adr/ADR-026-finding-deduplication-identity.md)
- **Scope guard:** NO provider invocation, NO new Council run, NO prompt changes, NO timeout/retry
  work, NO unrelated reconciliation redesign, NO embeddings / LLM judge / fuzzy similarity, NO
  stop-word hacks for this case, NO aggressive dedup increase, next milestone NOT started. The
  Engineering Council working tree is **not a Git repository** — no git against it.

## Problem (reproduced before the fix)

The M15.4 run (`20260819-195410-1086f6`) produced **one false dedup merge**:

```
CQL-c55887984d (dedup-identity, 0.9)
  RAW-010  medium  codeQuality  OpenCode  "Duplicated magic string for named HttpClient construction"
           10 obs (bundle): OBS-011 primary (ConfigureServices/CreateClient),
                           OBS-014 SECONDARY "Magic string item keys duplicated across telemetry classes"
                           carrying ContextLogger.WriteLog @ ContextLogger.cs:42 and
                           RequestFilter.OnResultExecutionAsync @ RequestFilter.cs:96
  RAW-014  low     codeQuality  ClaudeCode "Inconsistent, uncentralized magic string keys for
                            context items (CustomerID/BrandID casing)"
           OBS-117 exact-title, WriteLog @ ContextLogger.cs:42, OnResultExecutionAsync @ RequestFilter.cs:110
```

Why it merged under M15.3D: shared method-parts `{writelog, onresultexecutionasync}`; `writelog`
close-lines (ContextLogger.cs:42 both — `onresultexecutionasync` at |96−110| = 14 > 3 was NOT close);
shared title tokens `{magic, string}` = 2 ≥ `MinFocusTitleTokens`; severity diff |2−1| = 1 < 2. The
`MinFocusTitleTokens=2` guard was not discriminating: the generic phrase {magic, string} is a shared
*category*, not a shared *defect*, and RAW-010's shared method-part came from a SECONDARY observation
of a multi-issue bundle. RAW-010 and RAW-014 are distinct specific defects (named-HttpClient
construction string vs context-item key casing).

## Discriminating characteristic (data-grounded)

Observation-title → finding-title similarity (Jaccard on normalized tokens, both persisted runs) is
bimodal with a clean gap:

| class | examples | similarity |
|---|---|---|
| exact primary obs | all primary observations | 1.00 |
| same-defect rephrased obs | OBS-081 0.37, OBS-057 0.39, OBS-070 0.38, OBS-021 0.55, OBS-032 0.58, OBS-139 0.38 | ≥ 0.37 |
| distinct sub-issue obs | RAW-010 vs OBS-014 = 0.25 (maximum) | ≤ 0.25 |

Corpus document-frequency "distinctive token" tests were tried and REJECTED: `magic`/`string` have
DF 2/55 = 0.04 in M15.4 — identical to genuinely distinctive tokens, so DF does not discriminate and
a hardcoded blacklist of {magic, string} was forbidden anyway.

## Fix (deterministic, conservative)

`RuleBasedFindingReconciler` — **primary-focus anchor** on the shared dedup method-part:

1. `Signature` gains `PrimaryMethods` and `PrimarySymbolEvidence`. In `Signature.Build`, an
   observation is PRIMARY-FOCUS when its normalized title similarity to the finding's own title
   ≥ `PrimaryFocusAnchorSimilarity = 0.30` (exact normalized-token Jaccard, incl. stopwords, matching
   `TitleNormalizer.Similarity`). Observations without a title are never primary-focus.
2. `TryFindDedupIdentityMatch` requires the shared method-part to be primary-focus in BOTH findings;
   `SymbolLinesClose` is evaluated over PRIMARY-focus evidence only.
3. Retained unchanged as redundant secondary barriers: `FocusConsistent` (≥ 2 significant title
   tokens), the severity contradiction guard, the dedup score 0.9, and the cross-discipline barrier.

Properties:
- **Rejects the exact false merge:** `writelog`/`onresultexecutionasync` are primary in RAW-014 but
  secondary (inherited from bundle member OBS-014 at 0.25 < 0.30) in RAW-010 → separate.
- **Cluster-order safe:** the anchor is intrinsic per finding (own obs + own title); cluster
  membership never changes it, so equivalent input permutations yield equivalent partitions (tested).
- **Bundle protection without a global ban:** bundles still merge on their primary issue; only a
  bundle's secondary observations lose identity-lending power.
- **Not an exact-title requirement:** the M15.2 wallet pair (RAW-011/RAW-014) merges through rephrased
  same-defect observations at 0.39/0.38 — proving the threshold sits above the distinct-sub-issue
  ceiling (0.25) and below the same-defect floor (0.37). False negatives are preferred (conservative).

## Regression fixtures

- Exact persisted M15.4 shape RAW-010/RAW-014 (primary OBS-011 `CodeHotspot`, secondary OBS-014
  `CodeHotspot`, OBS-117 `MagicStringInconsistency`) — expected same cluster = **false**; also under
  reversed and permuted input order.
- General negatives: generic 2-token overlap insufficient by itself; weak secondary-symbol
  inheritance insufficient; observation without title contributes no identity; near-match pairs stay
  separate.
- Positives: all three genuine M15.4 merges preserved (wallet 4-member, brand-auth testing, readiness
  probe); rephrased-primary-observation still anchors; bundle merges on its primary issue.
- Evidence/provenance/severity semantics preserved; deterministic IDs; diagnostics reflect the
  corrected partition; `schemaVersion` stays 1.1 with no new package fields.

## Verification

- Build `EngineeringCouncil.slnx`: **0 warnings, 0 errors**.
- Focused M15.4A + dedup + replay tests: **49/49 green**.
- Full suite run ONCE: **618/618 green** (0 skipped, 0 failed), including the persisted M15.2 replay
  (29 → 27, both genuine merges preserved) and the persisted M15.4 replay (below).

## Real-data replay (persisted M15.4 artifacts, NO provider invocation)

| metric | original M15.4 | M15.4A corrected replay |
|---|---|---|
| raw findings | 55 | 55 |
| pre-dedup | 55 | 55 |
| deduplicated joins | 6 | 5 |
| post-dedup / consolidated | 49 | **50** |
| merged clusters | 4 (incl. 1 false) | 3 (all genuine) |
| RAW-010 separate from RAW-014 | NO (CQL-c55887984d) | **YES** |

Per genuine M15.4A merge (source IDs / same defect / evidence preserved / provenance preserved /
severity semantics preserved):

| cluster | source IDs | same defect | evidence | provenance | severity |
|---|---|---|---|---|---|
| `REL-42ba184796` | RAW-017, RAW-018, RAW-020, RAW-025 | YES (wallet HttpClient without timeout/retry/circuit-breaker) | YES (all 14 obs) | YES (OpenCode+Codex+ClaudeCode) | YES (`Medium–High`, blended High) |
| `TST-d4244b718f` | RAW-035, RAW-036 | YES (brand JWT auth pipeline never exercised) | YES (all 9 obs) | YES (OpenCode+Codex+ClaudeCode) | YES (Medium) |
| `OBS-70131abcc2` | RAW-045, RAW-049 | YES (readiness probe sequential realm iteration) | YES (all 4 obs) | YES (OpenCode+Codex) | YES (`Low–Medium`, blended Medium) |

Near-match re-check: all 5 named D-109 pairs (RAW-046/051, RAW-050/055, RAW-026/043, RAW-023/034,
RAW-002/044) remain separate; RAW-010 and RAW-014, inspected independently after separation, keep
their own discipline/severity/provenance/observations (RAW-010: OpenCode+Codex, 10 obs; RAW-014:
ClaudeCode, 1 obs).

## Score / contract / docs

- Dedup score stays **0.9** — the identity predicate was hardened, not the score (no cosmetic change).
- Diagnostics (`PreDedup=55`, `PostDedup=50`, `Deduplicated=5`) reflect the ACTUAL new partition; the
  old 49 was not forced.
- `schemaVersion` stays **1.1**; no new package/domain fields; consumer DTO unchanged.
- CouncilAssessment / SemanticReview / severity / confidence / health scoring NOT modified.
- Docs: this milestone, PROJECT_MEMORY v37, DECISION_LOG **D-110**, ADR-026 **amended** (preferred
  over a new ADR), RUNBOOK unchanged (no operator-visible behavior change).
- Tests: +16 (`DedupFalseMergeHardeningTests`), existing dedup/replay fixtures made faithful (primary
  observations now carry their finding's title, matching the real pipeline).