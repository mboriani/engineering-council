# MILESTONE-015.1 — Parallel Agent Execution

- **Status:** ✅ Complete
- **Date:** 2026-08-12
- **Builds on:** [MILESTONE-014.4](./MILESTONE-014.4-targeted-semantic-reconciliation.md)
- **ADR:** none — execution-concern change with a conservative default, no
  architecture or contract change.

## Goal

Let **independent** acquisition steps of a Council run overlap instead of
serializing. A 3-provider council (OpenCode + Codex + Claude Code) ran
sequentially in M14.1 at ~150 s; the floor for any Council run is the **slowest**
provider, so parallelism bounds the cap at that floor. Must be OPT-IN and
conservative: `MaxConcurrency` defaults to `1`, which preserves strictly
sequential execution, and the parallel executor must never make package/Markdown
artifacts nondeterministic.

```
steps ──► [bounded parallelism ≤ MaxConcurrency] ──► plan-order aggregation
           (SemaphoreSlim; per-step provider/request/timeout/cancel)
```

## 1. Where the change lives

`EvidenceAcquisitionExecutor.ExecuteAsync` (`Infrastructure.Acquisition`) still
selects context, builds the request, calls the provider, stamps provenance, and
owns telemetry per step — unchanged. What changed is the **concurrency of the
steps**:

- Steps run through a `SemaphoreSlim(maxConcurrency)` (never one unbounded `Task`
  per step). Each task keeps its OWN provider, request, timeout CTS, and per-step
  locals — one step never touches another's state.
- The wait uses `WaitAsync(cancellationToken)` OUTSIDE the try/finally so a
  cancelled waiter never releases a permit it did not acquire (a `Release` on the
  queue would throw).
- The final `Evidence`/`records` lists aggregate strictly **in plan order** AFTER
  every task completes — completion order never leaks into `Evidence`,
  `provider-execution.json`, observations, JSON, or Markdown.
- Failure isolation is unchanged: a step problem becomes a failed Evidence +
  record (`Continue` semantics); a run cancellation throws (never a Failed step),
  queued steps do not start, and provider timeout stays distinct from
  cancellation.
- `maxConcurrency = Math.Max(1, options.MaxConcurrency)`.

## 2. Configuration

New `Evidence:Execution:MaxConcurrency` (naming matches the existing
`Execution`-section placement) with default **`1`** in BOTH the CLI and API
`appsettings.json`. Bound to `EvidenceOptions.MaxConcurrency` and
`CouncilOptions.MaxConcurrency` in `Cli/Program.cs` and `Api/Program.cs`. The
domain default (`EvidenceOptions.MaxConcurrency = 1`) is sequential — a consumer
that never sets the key is unchanged.

## 3. Package / consumer contract

Parallelism is an **execution concern**, not a contract change:

- `engineering-review-package.json` stays `schemaVersion **1.1**`; **no
  `maxConcurrency` field is added** to the package.
- The consumer-DTO gate stays green (`Package_and_consumer_contract_are_unchanged_by_parallelism`
  deserializes the package through the existing `PackageContract`).
- Parallel acquisition does not duplicate steps, does not change reconciler
  inputs, and does not add provider SDK/runtime types (test-verified).

## 4. Tests (all green — 456/456, +15 new)

`ParallelAcquisitionTests.cs` (deterministic only — `TaskCompletionSource` gates
and instant/controlled-delay fakes, zero network, zero credentials):

- default `MaxConcurrency` stays conservatively sequential;
- `MaxConcurrency=1` preserves strictly sequential execution (start order =
  plan order);
- `MaxConcurrency=2` / `3` never run more than the configured number of steps
  concurrently, and all independent steps complete when gates are released;
- `Continue` `ProviderFailureMode` (config) is preserved with a parallel
  executor — one failed provider isolates, remaining steps still run and the
  run succeeds;
- `FailRun` mode with a failing provider fails the whole run;
- run **cancellation** cancels in-flight provider work and propagates (queued
  steps do not start) — distinct from a timeout, never a Failed step;
- parallel execution does not duplicate or reorder steps (aggregation is plan
  order);
- the package / consumer-DTO contract is unchanged by parallelism (`maxConcurrency`
  absent, `PackageContract` deserialization green).

## Verification

```
dotnet build EngineeringCouncil.slnx        # 0 errors, 0 warnings
dotnet test  EngineeringCouncil.slnx        # Passed: 456 (no network/credentials)
dotnet test --filter ParallelAcquisitionTests   # 15/15
```

Real council smoke (OpenCode + Codex + Claude Code / Security / fixture
`m14-1-council-smoke-repo`, published CLI with `Evidence:Execution:MaxConcurrency=3`),
run `20260812-222936-2229b2`:

| Provider | StartedAt (UTC) | Duration | Observations | executedAt (UTC) |
|----------|------------------|----------|--------------|------------------|
| OpenCode | 22:29:36.583     | 30.35 s  | 7            | 22:30:06.928     |
| Codex    | 22:29:36.642     | 52.58 s  | 6            | 22:30:29.217     |
| ClaudeCode | 22:29:36.670   | 57.12 s  | 8            | 22:30:33.794     |

(plan `createdAt` 22:29:36.577)

- **Concurrency proven:** all three providers started within **87 ms** of each
  other (22:29:36.58 → 22:29:36.67) → true overlap, `max concurrency = 3`
  observed. Cross-checked at the process level: `opencode` (pid 40884) and
  `codex` (node 32632 / codex.exe 24764) were alive simultaneously
  ~22:29:37 → 22:30:06 (~29 s overlap).
- **Acquisition wall-clock:** first start 22:29:36.58 → last finish 22:30:33.79 =
  **~57.2 s** (bounded by the slowest provider, ClaudeCode 57.12 s). Sequential
  M14.1 baseline ~150.36 s ⇒ **~62% reduction**.
  Note: `provider-execution.json` `totalDuration` (`00:02:20.04`) is the M12.1
  **SUM** aggregator over per-step durations, NOT wall-clock — the ~59 s council
  wall-clock was measured externally (49% faster than the 140 s sum).
- **Correctness:** 3/3 providers succeeded, 0 failures, 0 timeouts, 0 retries;
  21 observations → 13 raw findings → **12 consolidated findings** (3
  multi-provider, 0 contradictions — reconciler log line); findings/summary 12/12;
  exactly one package, `schemaVersion 1.1`, no `maxConcurrency` in the package.
- **No orphans:** all three run PIDs exited; no lingering `exec -s read-only` /
  `opencode run` children after the run.
- The `[CP]` stderr checkpoint lines (`TEMPORARY diagnostic checkpoint`, added
  earlier to observe overlap) were **removed** after the run — the executor ships
  clean (build/tests re-verified after removal).

## Exit criteria

| Criterion | Result |
|-----------|--------|
| Build (0 errors, 0 warnings) | ✅ |
| All tests pass | ✅ 456/456 (+15) |
| Default `MaxConcurrency` is sequential (1) | ✅ |
| Independent steps actually overlap when configured | ✅ (87 ms start spread; process-level overlap) |
| Aggregation stays in plan order (deterministic artifacts) | ✅ |
| Failure isolation preserved (`Continue` / `FailRun`) | ✅ |
| Cancellation propagates; timeout stays distinct | ✅ |
| No step duplication | ✅ |
| Package unchanged (`schemaVersion` 1.1, no `maxConcurrency`) | ✅ |
| Consumer DTO gate green | ✅ |
| No orphan processes on Windows | ✅ |

## What remains for M15.2

User-defined scope/updated orchestration after real parallel evidence; **not**
started here. This milestone changed only execution concurrency — no reconciler
rules, no package contract, no new provider, no semantic layer.