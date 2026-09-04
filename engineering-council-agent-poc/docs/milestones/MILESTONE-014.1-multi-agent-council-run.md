# MILESTONE-014.1 — First Multi-Agent Engineering Council Run

- **Status:** ✅ Complete
- **Date:** 2026-08-10
- **Builds on:** [MILESTONE-013.5](./MILESTONE-013.5-claude-code-agentic-adapter.md)

## Goal

Run the **first real multi-agent Council**: one AnalysisRun that executes the three
real agentic coding runtimes together — **OpenCode, Codex, and Claude Code** — over
the same discipline (Security) and the same repository, then consolidates their
evidence through the existing deterministic reconciler into a single Engineering
Review Package. No SARIF, no API (LLM) providers, one discipline, one run.

The milestone also fixes and locks in the **M14.1 hang**: an `opencode run` that
falls back to the shared default local server (instead of starting its own isolated
server) can queue behind an interactive OpenCode session until the provider timeout.
The root cause was config loading, not the process adapter.

## Root cause and fix (the hang)

- **Symptom:** `dotnet run --project src\EngineeringCouncil.Cli` from the **repo
  root** with `--provider OpenCode` hit the 120 s timeout and produced no evidence.
- **Root cause:** the host built its configuration with `AddJsonFile("appsettings.json")`
  resolved against the **process current directory**. Launched from the repo root,
  that cwd is the repo root, which has no `appsettings.json`, so
  `Evidence:OpenCode:Port = 0` (the M14.1 isolated-`--port` mitigation) was
  **silently inactive**. With `Port` null the provider invoked `opencode run`
  **without** `--port`, OpenCode competed for the shared default server held by the
  user's interactive session, and the run queued until the 120 s timeout.
- **Fix:** `Cli/Program.cs` now anchors config to the app base directory:
  `ConfigureAppConfiguration((_, config) => config.SetBasePath(AppContext.BaseDirectory))`.
  The CLI's own `appsettings.json` always loads, regardless of cwd, so
  `Evidence:OpenCode:Port = 0` is effective and the provider emits a concrete
  isolated `--port <n>` for every OpenCode run.
- **Deterministic regression guard** — `Cli_appsettings_keeps_the_m141_isolated_port_and_explicit_model`
  (`Tests/OpenCodeModelSelectionTests.cs`): resolves the shipped
  `src/EngineeringCouncil.Cli/appsettings.json` from the repo and asserts
  `Evidence:OpenCode:Port == 0`, a non-empty `Model`, and `Enabled == false`. The
  existing fake-runner tests already pin `Port=0 → --port <concrete>` argv.
- **No architectural change** — a config-loading fix plus a deterministic config
  contract test; no new ADR.

## Acceptance — config verification (from the repo root)

Effective OpenCode options, verified via the CLI's config probe on a mock run
launched from the repo root (`Port` is the critical field):

```
[CP] OpenCodeOptions Enabled=False Executable=opencode ModelSet=True Port=0 TimeoutSeconds=120
```

- `Enabled` — false by default (explicit `--provider OpenCode` opts in).
- `Model` — set to `opencode/deepseek-v4-flash-free` (M13.3 format, passed as `--model`).
- `Port` — **0** (⇒ provider generates a concrete isolated `--port <n>` per run).
- `TimeoutSeconds` — 120.
- `DEEPSEEK_API_KEY` — **presence checked only** (value never printed); absent from
  the User/Machine environment. OpenCode's own auth store is never read by the council.

## Acceptance — OpenCode-only smoke

One real OpenCode-only Security run, launched **from the repo root** (the scenario
that previously reproduced the hang):

- **Run:** `20260811-011607-662832` · repo `m14-1-council-smoke-repo` · Security.
- Scan → OpenCode provider started → **concrete isolated `--port` emitted** →
  completed normally → Evidence (1) → **8 observations → 7 findings** → package.
- Duration ~35 s (well under the 120 s timeout; no queueing against the busy shared
  server). `timeoutCount = 0`, `success = true`. **No orphan OpenCode process** left
  (the pre-existing interactive session was untouched).

## Acceptance — final M14.1 Council run

**One AnalysisRun**, providers `OpenCode,Codex,ClaudeCode`, discipline `Security`,
repository `m14-1-council-smoke-repo` (2 files / 1 project / deliberately vulnerable
fixture):

- **RunId:** `20260811-011804-d3eb0a` · status `completed`.
- **Concrete isolated port verified end-to-end** — process-command-line capture showed
  the OpenCode child as:
  `opencode.exe run --model opencode/deepseek-v4-flash-free --port 58041 <instruction>`.
- **All 3 providers succeeded** (executions 3, failures 0, timeouts 0):

  | Provider | Success | Duration | Observations | Raw findings |
  |----------|---------|----------|--------------|--------------|
  | OpenCode  | ✅ | 00:00:32.89 | 8  | 6 |
  | Codex     | ✅ | 00:00:59.73 | 6  | 5 |
  | ClaudeCode| ✅ | 00:00:57.73 | 10 | 4 |

- **Council totals:** acquisition 00:02:30.36 (run 00:02:30.47) · **24 observations** ·
  **15 raw findings** · **13 consolidated findings** · contradictions 0.
- **Agreement:** supported by **all 3** providers = **2** findings · supported by
  **exactly 2** = **4** findings · **exclusive** = **7** findings (OpenCode 3,
  ClaudeCode 3, Codex 1).
- **Disagreements (descriptive only, reconciler authoritative):** severity = 2 ·
  observation-type = 2 · location = 5.
- **Artifacts** — exactly one `engineering-review-package.json` (+ `.md`,
  `findings.json`, `raw-findings.json`, `observations.json`, `provider-execution.json`,
  `run.json`); `schemaVersion` = **1.1**; provider provenance preserved per
  observation/finding (`sourceProvider`/`evidenceProvider`); **no secrets** in any
  artifact (the only secret-looking content is the fixture's own hardcoded
  credentials, correctly surfaced as findings).
- **No orphan processes** — all three run processes exited; the only `opencode`
  process present was the user's pre-existing interactive session (untouched).

## Verification

| Criterion | Result |
|-----------|--------|
| Build (0 errors, 0 warnings) | ✅ |
| All tests pass | ✅ 398/398 (incl. the new M14.1 config-contract guard) |
| Config loaded from app base dir (repo-root launch) → `Port=0` effective | ✅ |
| OpenCode emits a concrete isolated `--port <n>` | ✅ (port 58041 observed) |
| OpenCode-only real smoke from repo root completes without timeout | ✅ (~35 s) |
| Council: 3/3 agentic providers succeed on one run | ✅ OpenCode + Codex + ClaudeCode |
| Deterministic reconciler consolidates (15 raw → 13 findings) | ✅ |
| Package `schemaVersion` = 1.1, one package per run | ✅ |
| Provider provenance preserved | ✅ |
| No secrets in artifacts; `DEEPSEEK_API_KEY` presence-only | ✅ |
| No orphan processes after the run | ✅ |

```
dotnet build EngineeringCouncil.slnx        # 0 errors, 0 warnings
dotnet test  EngineeringCouncil.slnx        # Passed: 398
```

## Deferred work (M14.2+)

Parallel Council execution, agent-to-agent deliberation, reconciliation
improvement/scoring, model-level calibration of agentic sources, and OS
sandboxing/containers.
