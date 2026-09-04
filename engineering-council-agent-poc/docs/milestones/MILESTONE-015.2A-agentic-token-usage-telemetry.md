# MILESTONE-015.2A — Agentic Token Usage Telemetry

- **Status:** ✅ Complete
- **Date:** 2026-08-18
- **Builds on:** [MILESTONE-012.1](./MILESTONE-012.1-real-provider-metrics.md),
  [MILESTONE-013.5](./MILESTONE-013.5-claude-code-agentic-adapter.md),
  [MILESTONE-015.2](./MILESTONE-015.2-post-run-output-quality.md)
- **ADR:** none — this milestone extends an existing adapter's telemetry mapping
  inside the existing M12.1 provider-neutral surface; no contract or architecture
  decision changed.

## Goal

Capture **per-execution token usage for the three agentic runtimes** (OpenCode,
Codex, Claude Code) whenever the runtime reports **authoritative** usage in the
machine-readable output the adapters **already consume** — reusing the M12.1
telemetry surface (`Evidence.InputTokens`/`OutputTokens`/`TokensUsed`,
`ProviderExecutionRecord`, and the run report's `TotalInputTokens`/
`TotalOutputTokens`/`TotalTokens`/`KnownTokenExecutionCount`).

Measurement **only**. Explicit non-goals (unchanged from M12.1): token/prompt
optimization, estimation/tokenizers, cost/pricing, provider selection/ranking,
new providers, new package fields, reconciliation, M15.3 follow-ups, and a full
3×7 rerun.

## What the runtimes actually expose (verified)

| Runtime | Output the adapter captures | Authoritative usage in it? | Action |
|---------|------------------------------|----------------------------|--------|
| **Claude Code** (`claude -p --output-format json`) | The CLI's own JSON result envelope (`{"type":"result","result":"…","usage":{…}}`) | **Yes** — `usage.input_tokens` / `usage.output_tokens` (plus cache categories and `total_cost_usd`). Cumulative for the whole call — the top-level agent loop including its internal Read/Glob/Grep tool interactions; subagents are not used by this adapter. | **Mapped.** `ClaudeCodeOutputExtractor` lifts the two provider-neutral counts; the provider stamps them on `Evidence` + metadata. |
| **OpenCode** (`opencode run`) | Final assistant text only (the structured observations envelope) | **No** — the captured stdout carries no machine-readable usage. | **Null** (documented gap). Nothing is fabricated; the adapter does not switch to `--format json`. |
| **Codex** (`codex exec`, formatted stdout) | Final message only (the structured observations envelope) | **No** — the captured stdout carries no machine-readable usage. | **Null** (documented gap). Nothing is fabricated; the adapter does not switch to `--json`. |

Unknown usage stays **null, never 0** (M12.1 `SumKnown` semantics), so run
aggregates never treat an agentic call as a free call.

## Changes

### `ClaudeCodeOutputExtractor.cs` (Infrastructure)

- `TryExtractResult` gains `out ClaudeCodeUsage usage`.
- New `ParseUsage(JsonElement root)`: reads `usage.input_tokens` /
  `usage.output_tokens` from the result envelope. Missing/non-numeric values
  stay null — usage is never fabricated.
- New internal record `ClaudeCodeUsage(int? InputTokens, int? OutputTokens)`
  with `TotalTokens` = Input + Output when at least one side is known (M12.1
  semantics), null when neither is known.

### `ClaudeCodeEvidenceProvider.cs` (Infrastructure)

- Maps `usage.InputTokens` / `usage.OutputTokens` / `usage.TotalTokens` onto
  `Evidence.InputTokens` / `OutputTokens` / `TokensUsed`.
- Adds metadata keys `inputTokens` / `outputTokens` / `totalTokens` (only when
  present — same convention as `model`).
- No provider-specific type leaks past the adapter boundary; downstream is
  untouched.

### No changes to OpenCode / Codex

Their captured output genuinely carries no usage, so their adapters are
unchanged and honestly report null. No new package fields, no contract change
(`schemaVersion` stays **1.1**), no new telemetry model, no new artifact.

## Tests

New `AgenticTokenTelemetryTests.cs` (12 tests, fully offline — scripted fake
runners, no network, no credentials, no paid calls):

1. ClaudeCode reported usage maps to evidence **and** record (input/output/total).
2. ClaudeCode envelope **without** `usage` keeps tokens null (never fabricated).
3. ClaudeCode total computed only from valid usage; unknown total stays null.
4. OpenCode captured output exposes no usage → tokens stay null.
5. Codex captured output exposes no usage → tokens stay null.
6. Three-agent run with no usage → run report keeps every aggregate null,
   `KnownTokenExecutionCount = 0` (unknown never becomes 0).
7. Agentic telemetry survives the evidence → execution → run report flow
   through the full `AnalysisPipeline`.
8. Mixed known/unknown run aggregation sums only known values.
9. `KnownTokenExecutionCount` counts only executions with known usage.
10. `provider-execution.json` serializes the agentic token fields + run totals.
11. Package contract stays **1.1**; token fields appear only inside the existing
    `providerExecution` section, never in `findings`.
12. No credential/secret text appears in serialized telemetry.

Build 0/0 warnings/errors; **468/468 tests pass** (12 new + 456 existing green).

## Live validation (2026-08-18)

### Council run `20260818-172513-2cb592` (Security, OpenCode+Codex+ClaudeCode, MaxConcurrency 3)

Real run against `RedirectToService` (122 files, 6 projects, `master @ 4a7d279aaa99`):
**all three agentic executions timed out at 00:02:00**. The telemetry behaved
exactly as designed under failure:

| Provider | Result | InputTokens | OutputTokens | TokensUsed | ErrorCategory |
|----------|--------|:-----------:|:------------:|:----------:|---------------|
| OpenCode | timeout | null | null | null | `Timeout` |
| Codex    | timeout | null | null | null | `Timeout` |
| ClaudeCode | timeout | null | null | null | `Timeout` |
| **Run**  | 0/3 success | `TotalInputTokens: null` | `TotalOutputTokens: null` | `TotalTokens: null` | `knownTokenExecutionCount: 0`, `timeoutCount: 3` |

**Root cause of the timeouts (pre-existing gap, NOT introduced here):**
`EvidenceOptions.ProviderTimeout` (default 120 s) is the executor's hard per-step
cap and is **not bound from configuration** in the CLI/API
(`CouncilServiceCollectionExtensions` constructs `EvidenceOptions` without it), so
every real step is cut at 120 s regardless of the per-provider `TimeoutSeconds`
(ClaudeCode is configured 300 s). A real Security analysis of 122 files takes
longer than 120 s in this environment → it can never succeed through the CLI
today. Recommended follow-up (M15.3): wire `Evidence:Execution:ProviderTimeout`
from config or derive the step timeout from each provider's own options.

### Direct `claude -p --output-format json` smoke

Confirmed the runtime and the exact envelope shape the extractor lifts:

```json
{ "is_error": false, "subtype": "success",
  "usage": { "input_tokens": 6, "output_tokens": 379,
             "cache_read_input_tokens": 47377, "cache_creation_input_tokens": 16300,
             "total_cost_usd": 0.1177161, "num_turns": 3, "duration_api_ms": 7297 },
  "result": "…" }
```

**Cache nuance (documented, out of scope):** `usage.input_tokens` counts only the
**non-cached** new input; the real agentic context is dominated by
`cache_*_input_tokens` (47 377 read + 16 300 created vs 6 new here). The milestone
faithfully maps the provider-reported `input_tokens`/`output_tokens` into M12.1
`InputTokens`/`OutputTokens` — but a follow-up should map the cache categories too
if the intent is to size total context consumed.

No orphan provider processes remained after the run (timeout path terminated the
process trees correctly).

## Exit criteria

| Criterion | Result |
|-----------|--------|
| ClaudeCode authoritative usage captured (envelope `usage`) | ✅ mapped |
| OpenCode / Codex honest null when no machine-readable usage | ✅ + documented |
| Unknown is never 0; run aggregates reuse M12.1 `SumKnown` | ✅ |
| No contract change (`schemaVersion` 1.1, no new package field/artifact) | ✅ |
| No provider-specific type leaks to Core/downstream | ✅ |
| Offline tests green (build 0/0, 468/468) | ✅ |
| No ADR (adapter-internal mapping, no architecture decision) | ✅ |

## Known gaps (documented, not fixed)

- **OpenCode**: the default `opencode run` stdout carries no usage. A future
  milestone could switch to OpenCode's JSON output / session usage if/when the
  adapter adopts a machine-readable mode.
- **Codex**: the default `codex exec` formatted stdout carries no usage. Same
  caveat for `--json`.
- **ClaudeCode cache tokens & cost**: `usage.cache_*` and `total_cost_usd` exist
  in the envelope but are deliberately not mapped (out of scope).
- **M15.3 follow-ups** (from M15.2) are still open: acquisition robustness,
  snapshot pinning, dedup + coverage gating.

## Recommended follow-up

1. **M15.3 robustness**: wire `Evidence:Execution:ProviderTimeout` from
   configuration (or derive the step timeout from each provider's own
   `TimeoutSeconds`) so slow-but-successful agentic runs are not cut at the
   implicit 120 s cap — the direct blocker observed in the live validation.
2. **Cache-token mapping**: capture ClaudeCode `cache_read_input_tokens` /
   `cache_creation_input_tokens` if total-context sizing is wanted (`input_tokens`
   alone understates a real agentic call by ~50k tokens here).
3. Re-run the full 3×7 Council run after M15.3 robustness and compare
   `KnownTokenExecutionCount`, `TotalInputTokens`, `TotalOutputTokens`,
   `TotalTokens` per runtime to size agentic cost per discipline.
