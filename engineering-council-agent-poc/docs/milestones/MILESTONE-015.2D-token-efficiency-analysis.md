# MILESTONE-015.2D — Token Efficiency Analysis

- **Status:** ✅ Complete
- **Date:** 2026-08-18
- **Builds on:** [MILESTONE-015.2C](./MILESTONE-015.2C-claude-code-cache-token-telemetry.md)
  (authoritative cache telemetry), [MILESTONE-015.2A](./MILESTONE-015.2A-agentic-token-usage-telemetry.md)
- **ADR:** none — provider-neutral DERIVED metrics over the existing M12.1/M15.2C
  telemetry; no contract or architecture decision changed.
- **No provider invocation occurred** in this milestone: it reuses the persisted
  authoritative fixture from run `20260818-183933-db74b2` (M15.2C). No rerun of
  RedirectToService, no Security × 3, no 3×7 Council.

## Goal

Make already-captured authoritative token telemetry **interpretable** without
changing its meaning. Distinguish fresh input, cache creation, cache reads,
output, and the M12.1 total — then answer "where is the agent spending
context/token activity?" WITHOUT claiming monetary cost or billable usage.

## Preserved semantics (unchanged)

- `TotalTokens = SumKnown(InputTokens, OutputTokens)` — cache tokens are NEVER
  folded in.
- `KnownTokenExecutionCount` still means executions with M12.1 input/output-known
  usage.
- All M12.1/M15.2C raw fields remain authoritative operational telemetry.

## Derived metrics (Milestone 015.2D)

All provider-neutral, computed from authoritative raw fields via
`TokenEfficiencyMetrics` (Core.Domain), `SumKnown` semantics (unknown stays null,
never 0):

| Metric | Definition | Meaning |
|--------|-----------|---------|
| `ContextTokenActivity?` | `SumKnown(InputTokens, CacheCreationInputTokens, CacheReadInputTokens)` | Observed INPUT-side context **token activity** reported by the runtime. NOT cost, NOT billable, NOT unique context size, NOT prompt size, NOT model context-window occupancy, NOT `TotalTokens`, NOT unique tokens processed. |
| `FreshContextTokens?` | `SumKnown(InputTokens, CacheCreationInputTokens)` | Input-side activity that was not reported as cache reads. NOT cost; NOT necessarily unique repository tokens. |
| `CacheReuseRatio?` | `CacheReadInputTokens / ContextTokenActivity` (when denominator meaningful > 0) | Fraction of observed input-side token activity served as **cache reads**. NOT a cost-savings percentage; NOT "how much of the repository was cached." |

No derived metric is introduced for output (OutputTokens already exists) and no
"efficiency score" is invented (no arbitrary weighting).

## Where the metrics live

- **Per execution:** `ProviderExecutionRecord.ContextTokenActivity` /
  `FreshContextTokens` / `CacheReuseRatio` (stored at record construction from
  the authoritative raw fields — single computation path in the executor).
- **Run level:** `ProviderExecutionReport.TotalContextTokenActivity` /
  `TotalFreshContextTokens` (SumKnown aggregation) and report-level
  `CacheReuseRatio` = `TotalCacheReadInputTokens / TotalContextTokenActivity`
  (computed from **aggregate authoritative values, never an average of
  per-execution ratios**).
- **Serialized in:** `provider-execution.json` (and internal `run.json`).
- **NOT exposed in:** `engineering-review-package.json`. `PackageExecutionView`
  strips the derived fields (and the M15.2C cache fields) before the external
  package is built. `schemaVersion` stays **1.1**.

## Tests

New `TokenEfficiencyMetricsTests.cs` (13 tests, fully offline — pure helpers +
scripted fake runner; no network/providers/credentials/long tests):

1. `ContextTokenActivity` sums known input-side categories (full, partial, cache-only, all-null).
2. `FreshContextTokens` sums input + cache creation.
3. `CacheReuseRatio` = cache read / activity; null when cache-read or activity unknown or activity zero.
4. **M15.2C exact fixture** (helper): 44+52,193+1,125,952 → activity **1,178,189**; fresh **52,237**; ratio ≈ **0.95566**; input+output = **14,369**.
5. M15.2C fixture flows through the executor unchanged (record-level derived metrics + `TokensUsed` 14,369).
6. Cache-absent records → ratio null; input-only → activity/fresh = input; no data → all null.
7. Unknown never becomes 0 in run-level aggregation.
8. `TotalTokens` semantics unchanged (44+14,325 = 14,369; cache never folded in).
9. OpenCode/Codex unknown data produce no fabricated derived metrics.
10. Run-level `CacheReuseRatio` uses aggregate totals (1700/3000 ≈ 0.5667), NOT the average of per-execution ratios (0.6).
11. Run-level aggregation is independent of record order.
12. `provider-execution.json` serializes derived metrics + run totals.
13. `engineering-review-package.json` gains NO derived or cache fields (schemaVersion 1.1; historical token fields remain).

Build 0/0 warnings/errors; full suite **504/504 tests pass** (13 new + 491
existing green).

## Analysis presentation

The authoritative M15.2C fixture (`20260818-183933-db74b2`, ClaudeCode /
Security):

```
ClaudeCode / Security
Fresh context activity:      52,237
Cache read activity:      1,125,952
Context token activity:   1,178,189
Cache reuse ratio:             95.6%
Output tokens:               14,325
M12.1 TotalTokens:           14,369
```

**Interpretation:** "Most observed input-side token activity was served from
provider cache." ~95.6% of the input-side context-token activity was cache reads
(1,125,952 of 1,178,189). This is **not** "95.6% cheaper" and **not** "95.6% of
the repository was cached". It is NOT correct to say "Claude consumed 1.17M
tokens" unless explicitly qualified as **context-token activity** — `TotalTokens`
(14,369) remains the M12.1 total.

## Exit criteria

| Criterion | Result |
|-----------|--------|
| Raw authoritative token categories unchanged | ✅ |
| `ContextTokenActivity` available where calculable | ✅ (record + run) |
| `FreshContextTokens` available where calculable | ✅ (record + run) |
| `CacheReuseRatio` available where calculable | ✅ (record + run, aggregate not average) |
| Metrics provider-neutral | ✅ (`TokenEfficiencyMetrics`, generic fields) |
| Unknown remains null | ✅ + regression-tested |
| `TotalTokens` semantics unchanged | ✅ + regression-tested |
| Run-level aggregation mathematically correct | ✅ (fixture + aggregate-not-average test) |
| External package unchanged (`schemaVersion` 1.1, no derived/cache fields) | ✅ + regression-tested |
| No real provider invocation occurs | ✅ (reuses M15.2C fixture) |
| Focused tests pass; build 0/0; full suite green once (504/504) | ✅ |
| No ADR (derived metrics, no architecture decision) | ✅ |

## Remaining telemetry gaps (documented, not fixed)

- **OpenCode / Codex**: no authoritative machine-readable token usage → all raw
  and derived metrics null (explicit non-goal).
- **Cost / pricing / billable tokens**: deliberately out of scope — the derived
  metrics are activity, not value; pricing belongs to a later cost milestone.
- **Prompt / context optimization**: explicitly NOT attempted here — this
  milestone only makes telemetry interpretable.
- **M15.3 follow-ups** (from M15.2/M15.2A): acquisition robustness beyond
  timeouts, snapshot pinning, dedup + coverage gating — still open.

## Recommended next step

1. **Token-efficiency/cost milestone**: decide a cache-aware interpretation
   (e.g. total context consumed = input + cache-creation + cache-read) and, if
   wanted, provider/model pricing — the derived metrics make this decision
   data-informed.
2. **M15.3 robustness**: bounded retries/repair for slow agentic steps + re-run
   the full 3×7 Council run comparing per-runtime token telemetry and derived
   activity.
3. **Potential prompt optimization study** (using ContextTokenActivity /
   CacheReuseRatio as the before/after measurement) — only after the semantics
   are agreed.