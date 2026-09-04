# MILESTONE-015.3D — Deterministic Finding Deduplication Improvements

Date: 2026-08-19 · Decision: `D-108` · ADR: `ADR-026` (FindingDeduplicationKey identity)

## Context

M15.2's persisted run (`20260813-230240-b3e36e`, 29 raw findings) contains genuine
**cross-provider duplicates** that the reconciliation stages do not merge:

- **RAW-012** (OpenCode, High) + **RAW-016** (ClaudeCode, High): the same RequestFilter
  NullReferenceException defect. RAW-012 carries the defect as its title focus; RAW-016
  carries the identical defect. Their full symbol sets intersect, but the pipeline's
  existing stages never joined them because they were produced as separate bundles whose
  best explicit match never fired before the keep-separate stage.
- **RAW-011** (OpenCode, High) + **RAW-014** (Codex, Medium): the same outbound-wallet
  HttpClient-without-timeout/resilience defect. Same discipline (reliability), same
  location, focus-consistent titles; one-step severity spread (High↔Medium) — a range,
  not a contradiction.

The existing reconciliation is deliberately conservative (false negatives preferred), so
these duplicates survive as separate findings. This milestone adds a **deterministic
dedup identity** stage that merges exactly these genuine duplicates and nothing else —
29 raw findings → **27** consolidated — while keeping every near-miss separate:

- Health-endpoint pair (RAW-028 / RAW-029) — same discipline, same file, but no shared
  symbol ⇒ separate.
- Magic-string trio (RAW-001 / RAW-003 / RAW-005) — same discipline, different literals,
  files and symbols ⇒ separate.
- ContextLogger finding (RAW-017) — shares `WriteLog` with the RAW-012 bundle's secondary
  observation, but the bundle's title focus is the RequestFilter NRE ⇒ separate.
- Broad-catch finding (RAW-015) — shares `GetContextAsync`/`RedirectToAsync` with the NRE
  pair, but its title focus is broad exception handling ⇒ separate.
- Cross-discipline RequestFilter views (RAW-026 observability / RAW-004 code-quality) —
  cross-discipline barrier ⇒ separate.

Scope (user-confirmed): **conservative — the 2 genuine merges only.** Not a recall
optimization; the dedup identity is intentionally narrow.

## What changed

### A new deterministic reconciliation stage: `dedup-identity`

`RuleBasedFindingReconciler.Match` gained a **stage 2** `dedup-identity` (score **0.9**)
placed between the existing stage 1 `exact-rule-location` (0.95) and stage 3
`type-symbol` (0.85). All stages still require the **same discipline**; first/best match
wins.

The dedup predicate merges two findings ONLY when ALL of these hold:

1. **Shared suffix-compatible method-symbol.** The findings share at least one
   `MethodPart` (the last `.`-segment of a symbol) — so `OnActionExecutionAsync` and
   `RequestFilter.OnActionExecutionAsync` are the same identity, while the exact-symbol
   sets of stage 3 may not intersect at all. Evidence is collected per observation AND per
   finding (from `SymbolReferences` × `FileReferences`).
2. **Close lines in the SAME file** for that shared symbol: at least one evidence
   (file, line) pair from each finding, for the SAME method-part, must share the same
   normalized file with `|lineA − lineB| ≤ LineTolerance` (3). Far-apart lines or
   different files never satisfy the identity.
3. **Focus consistency.** The two normalized titles must share at least
   `MinFocusTitleTokens = 2` significant tokens (exact, stopword-filtered, length ≥ 3).
   This is the guard that keeps a multi-issue bundle whose title focuses on issue X from
   absorbing a distinct finding about issue Y just because the bundle also carries a
   secondary Y observation.
4. **No material severity contradiction.** If `|severityA − severityB| ≥ 2`
   (e.g. Low vs Critical) the pair is never deduplicated — an explicit contradiction must
   not be silently erased.

**Design note — the secondary focus guard was tried and removed.** An earlier design added
a secondary guard: "both findings name the shared symbol or its file base-name in
title∪summary". Analysis of the persisted data showed this WOULD false-merge RAW-015
(broad-catch) into the NRE cluster, because RAW-016's verbose summary literally names
`BalanceContextProvider.GetContextAsync` and `RedirectAppService.RedirectToAsync`; it
only survived by cluster-ordering luck. Since a verbose summary must never become a merge
signal, the secondary guard was **removed** and the focus guard is **primary-only**:
exact significant-title-token overlap (≥ 2). Re-verified against every real pair: it
accepts exactly the 2 intended merges and rejects every near-miss.

### Identity & signature support

`Signature` (the reconciler's per-finding grouping record) gained `MethodSymbols`,
`SymbolEvidence` (method-part → ordered list of `(normalizedFile, line)`), and
`TitleTokens`. All ordering is Ordinal; nothing depends on arrival order. `Cluster` gained
a `DedupJoins` counter (incremented per dedup join) so the summary can report how many
findings the stage removed.

### Additive diagnostics on the reconciliation contract

`ReconciliationSummary` gained three **additive, defaulted** fields:

- `PreDedupFindingCount` — consolidated findings that would exist WITHOUT the dedup stage.
- `PostDedupFindingCount` — after the stage (= `ConsolidatedFindingCount`).
- `DeduplicatedFindingCount` — findings removed by the stage (PreDedup − PostDedup; each
  join removes exactly one finding).

They are **not `required`**, so persisted pre-M15.3D documents (e.g. M15.2
`findings.json`) still deserialize with these fields reading zero. The external package
(`engineering-review-package.json`) gains the same three additive fields on the
`reconciliation` block; **`schemaVersion` stays 1.1**. The consumer DTO gate
(`PackageContract` + `EngineeringReviewPackageContract`) was updated with
`preDedupFindingCount`/`postDedupFindingCount`/`deduplicatedFindingCount`.

### Markdown

`EngineeringReviewMarkdownExporter` renders a compact line in the Multi-Source
Reconciliation block — `Deduplicated findings: N (pre-dedup X → post-dedup Y)` — only when
`DeduplicatedFindingCount > 0`.

## Files changed

| File | Change |
|------|--------|
| `src/EngineeringCouncil.Infrastructure/Reconciliation/RuleBasedFindingReconciler.cs` | New `dedup-identity` stage 2, `MethodSymbols`/`SymbolEvidence`/`TitleTokens` on `Signature`, `Cluster.DedupJoins`, severity+focus+close-line guards, `BuildSummary` counts, class doc (5 stages) |
| `src/EngineeringCouncil.Core/Domain/ReconciliationResult.cs` | `ReconciliationSummary`: `PreDedupFindingCount`/`PostDedupFindingCount`/`DeduplicatedFindingCount` (additive, defaulted) |
| `src/EngineeringCouncil.Infrastructure/Reporting/EngineeringReviewMarkdownExporter.cs` | Compact dedup line in Multi-Source Reconciliation block |
| `src/EngineeringCouncil.Tests/Contracts/EngineeringReviewPackageContract.cs` | Consumer DTO dedup fields (additive) |
| `src/EngineeringCouncil.Tests/DedupReconciliationTests.cs` | **24 new offline tests** (positive stage, negative fixtures A–H, bundle/focus guards, M15.2 regression pairs) |
| `src/EngineeringCouncil.Tests/PersistedRunDedupReplayTests.cs` | **7 new tests** replaying the real persisted M15.2 run (29→27, exactly 2 dedup merges, near-misses standalone, determinism) |
| `src/EngineeringCouncil.Tests/PackageContractTests.cs` | `BuildDedupRun()` + 3 package/markdown tests |

## Tests

Build 0/0 warnings/errors; full suite **600/600 tests** (566 existing + 34 new). Coverage
maps 1:1 to the milestone's required list:

Positive: shared method-symbol + close lines + focus merge via `dedup-identity`;
suffix-compatible symbols merge where exact symbol match would not; merged id is
deterministic and not first-arrival; idempotence + order independence; diagnostics counts
(PreDedup = PostDedup + Deduplicated); evidence preserved from both members.
Negative fixtures A–H: same file+symbol, different defect (focus guard); different
symbols; same severity/wording, different locations; same endpoint concept, different
endpoints; same magic-string category, different literals; explicit severity
contradiction (Low↔Critical) never deduplicated; shared symbol far apart / different file;
cross-discipline. Bundle guards: a bundle does not absorb a distinct single-issue finding;
a bundle merges with a finding sharing its primary issue; shared symbol with unrelated
titles does not merge; same file alone never merges. M15.2 regressions: RequestFilter NRE
pair merges; wallet pair merges with `Medium–High` range and no contradiction; health
endpoint pair separate; magic-string trio separate; ContextLogger survives the merge.
Replay over `outputs/20260813-230240-b3e36e`: raw 29 → consolidated 27, pre 29 → post 27,
dedup 2, exactly two `dedup-identity` findings (RAW-012+RAW-016, RAW-011+RAW-014),
RAW-028/029/001/003/005/017/026/004/015 all standalone, deterministic across reversed
input. Package: dedup diagnostics additive + schema-keeping; Markdown renders/omits the
compact dedup line.

## Live validation (2026-08-19)

Replay of the persisted M15.2 run through the UPDATED reconciler (no new provider
invocation; deterministic rule-based stage only):

| Metric | Value |
|--------|-------|
| Raw findings | 29 |
| Pre-dedup consolidated | 29 (dedup stage absent) |
| Post-dedup consolidated | **27** |
| Deduplicated findings | **2** |
| Merges | `RAW-012`+`RAW-016` (RequestFilter NRE, `dedup-identity`), `RAW-011`+`RAW-014` (wallet HttpClient, `dedup-identity`) |
| Health pair / magic trio / ContextLogger / broad-catch / cross-discipline views | separate (2 / 3 / 1 / 1 / 2 standalone findings) |

## Exit criteria

| Criterion | Result |
|-----------|--------|
| Genuine cross-provider duplicates merge deterministically | ✅ exactly RAW-012+RAW-016 and RAW-011+RAW-014 |
| False-merge traps (ContextLogger, broad-catch, magic strings, health pair, cross-discipline) stay separate | ✅ + regression-tested |
| Dedup is deterministic (input-order independent, stable id) | ✅ + regression-tested |
| Severity spread is recorded, never erased; explicit contradiction never deduped | ✅ `Medium–High` range; Low↔Critical refused |
| Diagnostics additive + backward-compatible (old persisted docs deserialize) | ✅ defaulted fields, `schemaVersion` 1.1, consumer DTO green |
| Existing stages and tests unchanged | ✅ 566 existing tests green |
| Focus guard is primary-only (no summary-text merge signal) | ✅ secondary guard removed, documented |
| Full suite once, build 0/0 | ✅ 34 new; 0/0; **600/600** |
| Docs updated | ✅ milestone, ADR-026, PROJECT_MEMORY (v35), DECISION_LOG (D-108) |

## Known limitations (documented, not fixed)

- The dedup identity merges only **within a discipline** (same barrier as every other
  stage). RAW-026 (observability) and RAW-004 (code-quality) describing the same
  RequestFilter defect remain separate by design.
- A genuine duplicate whose two titles share fewer than 2 significant tokens is a
  false negative (accepted: false negatives preferred). Reducing `MinFocusTitleTokens`
  to 1 risks the ContextLogger/broad-catch trap on real data.
- Findings whose evidence carries no symbol (test-less/generic observations) can never
  satisfy the identity; dedup is a symbol-grounded stage.
- The replay validates the persisted M15.2 run only; future runs are covered by the same
  deterministic stage + the offline regression fixtures.

## Next step

M15.3E (not started per instruction).