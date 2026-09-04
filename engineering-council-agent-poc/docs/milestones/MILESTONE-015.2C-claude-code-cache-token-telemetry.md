# MILESTONE-015.2C — Claude Code Complete Token Telemetry

- **Status:** ✅ Complete
- **Date:** 2026-08-18
- **Builds on:** [MILESTONE-015.2A](./MILESTONE-015.2A-agentic-token-usage-telemetry.md)
  (ClaudeCode `input`/`output` token mapping), [MILESTONE-015.2B](./MILESTONE-015.2B-agentic-provider-timeout-fix.md)
- **ADR:** none — additive provider-neutral telemetry fields inside the existing
  M12.1 surface; no contract or architecture decision changed.

## Goal

Preserve Claude Code's authoritative **cache-token** telemetry so future Council
runs can measure actual token behavior. The `--output-format json` result
envelope exposes `usage.cache_read_input_tokens` and
`usage.cache_creation_input_tokens`, which M15.2A deliberately discarded. This
milestone captures them as **separate** provider-neutral operational telemetry
without redefining the historical M12.1 `TotalTokens` semantics and without any
cost/pricing interpretation.

## What changed

| Layer | Change |
|-------|--------|
| `ClaudeCodeOutputExtractor` | `ParseUsage` now also reads `cache_read_input_tokens` / `cache_creation_input_tokens` (numeric-only; missing/non-numeric → null). `ClaudeCodeUsage` gains `CacheReadInputTokens?` / `CacheCreationInputTokens?`; `TotalTokens` is unchanged (input + output only). |
| `ClaudeCodeEvidenceProvider` | Stamps `Evidence.CacheReadInputTokens` / `Evidence.CacheCreationInputTokens` + metadata keys `cacheReadInputTokens` / `cacheCreationInputTokens` (only when present). |
| `Evidence` (Core.Domain) | Additive `int? CacheReadInputTokens`, `int? CacheCreationInputTokens` — generic operational telemetry, not Claude-specific types. |
| `ProviderExecutionRecord` (Core.Domain) | Same two additive nullable fields. |
| `EvidenceAcquisitionExecutor` | `Record(...)` lifts the cache fields from the evidence onto the execution record (same single telemetry path as input/output). |
| `ProviderExecutionReport` | Additive run-level `TotalCacheReadInputTokens?` / `TotalCacheCreationInputTokens?` via the existing `SumKnown` helper. **`KnownTokenExecutionCount` is unchanged** — it still means executions with M12.1 input/output-known usage; cache-only usage never counts. |
| `EngineeringReviewPackageBuilder` | `PackageExecutionView` projects the report with cache fields nulled before embedding in the external package. |

**Run-level aggregation decision: added.** It was a clean, minimal extension of
the existing `SumKnown` aggregation (two nullable totals, independent of
`TotalTokens`/`KnownTokenExecutionCount`), so cache totals are available at run
level rather than per-execution only.

**Package boundary:** the external `engineering-review-package.json` does NOT
gain cache-token fields. Cache telemetry lives in `provider-execution.json`
(and the internal `run.json`) operational artifacts only.

### TotalTokens semantics (unchanged — M12.1)

```
TotalTokens = SumKnown(InputTokens, OutputTokens)
```

Cache tokens are **never** added to `TotalTokens` and never to any derived
"total" in this milestone. With `InputTokens = 179, OutputTokens = 17605,
CacheReadInputTokens = 47377, CacheCreationInputTokens = 16300`:
`TotalTokens = 17784` (not `81461`). The four raw categories stay separately
observable; no derived metric (`EffectiveTokens`, `ContextTokens`,
`BillableTokens`, cost, …) is introduced — we don't yet know which semantic is
useful, so we persist the authoritative raw categories first.

### OpenCode / Codex — unchanged

Neither runtime exposes authoritative machine-readable token usage in the output
our adapters consume, so their state remains honestly unknown:
`InputTokens/OutputTokens/TotalTokens/CacheReadInputTokens/CacheCreationInputTokens = null`.
Nothing is estimated, tokenized, parsed from duration, or scraped.

## Tests

New `ClaudeCodeCacheTokenTelemetryTests.cs` (12 tests, fully offline — scripted
fake runners, no network/Claude runtime/credentials):

1. `cache_read_input_tokens` maps to evidence, metadata, and record.
2. `cache_creation_input_tokens` maps to evidence, metadata, and record.
3. Both cache values propagate to `Evidence`.
4. Both cache values propagate to `ProviderExecutionRecord`.
5. Missing cache values stay null (envelope without cache keys).
6. Missing/explicit-null cache values never become 0 (metadata keys absent too).
7. `TotalTokens` still excludes cache values (179+17605 = 17784, never the summed 81461).
8. Existing input/output behavior unchanged (evidence, metadata, record).
9. `provider-execution.json` serializes per-execution + run-level cache telemetry.
10. External `engineering-review-package.json` gains NO cache fields (run level
    and records; schemaVersion stays 1.1) while historical token fields remain.
11. Run-level cache totals use `SumKnown` (known summed, null when none known).
12. Cache-only usage never changes `KnownTokenExecutionCount` or `TotalTokens`.

Build 0/0 warnings/errors; full suite **491/491 tests pass** (12 new + 479
existing green).

## Live validation (2026-08-18)

### Council run `20260818-183933-db74b2` (Security, OpenCode+Codex+ClaudeCode, MaxConcurrency 3)

Real run against `RedirectToService` (122 files, 6 projects, `master @
4a7d279aaa99`). **Runtime-only** overrides (env vars, shipped defaults
unchanged): `Evidence__OpenCode__TimeoutSeconds=300`,
`Evidence__ClaudeCode__TimeoutSeconds=300`. Result: **3/3 success**, 0 failures,
0 timeouts, 0 retries; 3 evidence items → 22 observations → 10 security findings
(C1/H0/M5/L4), 2 multi-provider, 0 contradictions.

| Provider | Duration | Result | InputTokens | OutputTokens | TotalTokens | CacheReadInputTokens | CacheCreationInputTokens |
|----------|----------|--------|:-----------:|:------------:|:-----------:|:---------------------:|:------------------------:|
| OpenCode | 00:03:18.58 | success (10 obs) | null | null | null | null | null |
| Codex | 00:01:43.78 | success (6 obs) | null | null | null | null | null |
| ClaudeCode | 00:02:40.05 | success (6 obs) | 44 | 14325 | 14369 | **1125952** | **52193** |

ClaudeCode values **all came from the authoritative `--output-format json`
usage envelope** (`usage.input_tokens`, `usage.output_tokens`,
`usage.cache_read_input_tokens`, `usage.cache_creation_input_tokens`). OpenCode
and Codex are honestly null (documented gap, not estimated).

Run aggregates: `KnownTokenExecutionCount 1`, `TotalInputTokens 44`,
`TotalOutputTokens 14325`, `TotalTokens 14369`,
`TotalCacheReadInputTokens 1125952`, `TotalCacheCreationInputTokens 52193`.

**Two views for ClaudeCode (interpretation preserved):**

```
Existing M12.1 telemetry:        InputTokens 44 / OutputTokens 14325 / TotalTokens 14369
Raw cache telemetry:             CacheReadInputTokens 1125952 / CacheCreationInputTokens 52193
```

The sum of these categories is NOT called "cost", "billable tokens", or "total
tokens" — categories are preserved intentionally before deciding how to
interpret them. Notable (consistent with M15.2A's smoke): the real agentic
context is dominated by cache reads (~1.13 M cached vs 44 new input tokens);
`input_tokens` alone understates the context an order of magnitude, which is
exactly why this telemetry is captured.

Artifacts: one package `schemaVersion` **1.1** with **no cache fields**
(run-level or records, verified); no credential/secret text (zero strong
credential-pattern hits); **no orphan provider processes** after the run.

## Exit criteria

| Criterion | Result |
|-----------|--------|
| Claude cache-read usage preserved | ✅ (per execution + run total) |
| Claude cache-creation usage preserved | ✅ (per execution + run total) |
| Values provider-neutral after infrastructure parsing | ✅ (`int?` on generic Evidence/record fields) |
| Missing values remain null (never 0) | ✅ + regression-tested |
| TotalTokens semantics unchanged (cache excluded) | ✅ + regression-tested |
| `provider-execution.json` exposes cache telemetry | ✅ + regression-tested |
| External package unchanged (`schemaVersion` 1.1, no cache fields) | ✅ + regression-tested |
| OpenCode/Codex remain honestly unknown | ✅ + documented |
| Focused tests pass; build 0/0; full suite green (491/491) | ✅ |
| One Security × 3 validation attempted; no full 3×7 run | ✅ |
| No ADR (additive telemetry, no architecture decision) | ✅ |

## Remaining telemetry gaps (documented, not fixed)

- **OpenCode / Codex**: still no authoritative machine-readable token usage in
  the captured output → unknown (null). Out of scope (and explicit non-goal).
- **Cost / pricing / billable-token math**: deliberately not calculated — cache
  read vs creation may have provider/model-specific pricing. Belongs to a later
  token-efficiency/cost milestone.
- **Derived/optimization metrics** (`EffectiveTokens`, `ContextTokens`, …):
  deliberately deferred until we know which semantic is useful.
- **M15.3 follow-ups** (from M15.2/M15.2A) are still open: acquisition
  robustness beyond timeouts, snapshot pinning, dedup + coverage gating.

## Recommended follow-up

1. **M15.3 robustness**: bounded retries/repair for slow agentic steps (OpenCode
   now needs ~3 m 19 s for a real Security analysis; the 300 s runtime override
   was proven sufficient — consider raising the shipped default).
2. **Token-efficiency/cost milestone**: define a cache-aware interpretation
   (e.g. total context consumed = input + cache-read + cache-creation) and, if
   wanted, a pricing table — only after we decide which semantic is useful.
3. Re-run the full 3×7 Council run and compare per-runtime
   `KnownTokenExecutionCount`, `TotalTokens`, and cache aggregates.