# MILESTONE-013.3 — OpenCode Model Selection + Real Smoke Verification

- **Status:** ✅ Complete
- **Date:** 2026-08-09
- **Builds on:** [MILESTONE-013.2](./MILESTONE-013.2-open-code-process-adapter.md)

## Goal

Two-part milestone over the M13.2 OpenCode adapter:

1. **Explicit model selection** — the adapter currently launches `opencode run`
   with no model flag and M13.2 deliberately left model identity unknown
   ("never guessed, never hardcoded — M13.3 configures/validates a specific model
   explicitly"). This milestone adds optional `Evidence:OpenCode:Model`
   (`provider/model` format) passed to the process as `--model <id>` via the
   existing safe `ArgumentList` invocation, plus telemetry metadata.
2. **Real smoke verification** — a first real end-to-end run of OpenCode +
   DeepSeek against a tiny fixture repository, proving the process adapter,
   model flag, and the whole pipeline actually work against a live agentic
   runtime — and resolving any demonstrated process-runner issue that surfaces.

## Scope

1. **`OpenCodeOptions.Model`** — optional string in OpenCode's `provider/model`
   format (e.g. `deepseek/deepseek-v4-flash`). Empty/null ⇒ OpenCode uses its own
   runtime default (unchanged M13.2 behavior). Never mandatory, never guessed,
   never hardcoded, never a credential.
2. **`--model` passthrough** — when set, `OpenCodeEvidenceProvider` adds
   `--model` + the id to `Arguments` (`OpenCodeProcessRequest.Arguments` →
   `ProcessStartInfo.ArgumentList`, `UseShellExecute = false`, **no shell string**).
   The instruction argument stays the last element, so the runner/telemetry
   contract is unchanged.
3. **Telemetry metadata** — when set, the evidence carries
   `Metadata["model"] = <id>` (built before the init-only `Evidence.Metadata`
   initializer); the success log line includes `Model=<id>`. When unset, model
   identity stays absent (never `"unknown"`). `ProviderVersion` remains
   `opencode-adapter-v1` (the adapter's version) — model identity is separate.
4. **Configuration** — `Evidence:OpenCode:Model: ""` documented in
   `appsettings.json` with a comment that credentials live in OpenCode's own
   auth (e.g. `DEEPSEEK_API_KEY`), never in this file.
5. **Process-runner regression hardening** — proven real-world issue: a
   **stale CLI binary** (built before this milestone) never passed `--model`, so
   OpenCode fell back to its billing-blocked default model and the run hung until
   the provider timeout. The runner's process-tree termination was separately
   proven correct (see Investigation below). Two new Windows-safe regression
   tests lock in that a timeout/cancellation terminates the **complete** process
   tree (`cmd /c ping -n 30`), guarding against orphan children.

## Changes

- **`Core/Abstractions/OpenCodeOptions.cs`** — added `Model` (nullable string,
  `provider/model`); XML-doc updated for M13.3.
- **`Infrastructure/Evidence/OpenCodeEvidenceProvider.cs`** — `ModelArgument =
  "--model"`; when configured, appends `--model <id>` to the process arguments
  (before the instruction); stamps `Metadata["model"]` on evidence; success log
  includes the model. No change when unset.
- **`Cli/appsettings.json`** — documented `Evidence:OpenCode:Model` (default
  `""`).
- **`Tests/OpenCodeModelSelectionTests.cs`** (new, 12 tests) — offline, fake
  runner, no OpenCode/network/keys:
  1. Model is optional and defaults to null
  2. A configured model reaches the runner as a `--model <id>` argument
  3. The argument list is exactly `[run, --model, <id>, <instruction>]` (the
     instruction stays last — runner/telemetry contract unchanged)
  4. The model id is never present when unset
  5. The model id is passed via `ArgumentList`, never concatenated into a shell
     string
  6. Evidence carries `Metadata["model"]` when configured
  7. Evidence carries no model metadata when unset
  8. `ProviderVersion` stays the adapter version, never the model
  9. The success telemetry logs the model
  10. Model never leaks into any credential field
  11. Model never fabricates a `ContextFingerprint`
  12. Downstream (observations → findings → consumer-compatible package,
      `schemaVersion 1.1`) is unchanged with a model configured
- **`Tests/OpenCodeProcessRunnerTests.cs`** (+2 tests, now 5) — Windows-safe
  real-process tests proving timeout and cancellation terminate the **complete**
  process tree (`cmd /c ping 127.0.0.1 -n 30`; snapshot pre-existing ping PIDs,
  assert no new survivor remains). No OpenCode install/network needed.

## Investigation — real smoke + process lifecycle

- **Environment verified:** OpenCode **1.18.15** (npm) at
  `C:\Users\User\AppData\Roaming\npm\node_modules\opencode-ai\bin\opencode.exe`;
  model identifiers are `opencode/<model>` (`opencode models`). The paid
  `opencode/deepseek-v4-flash` is blocked by "No payment method"; the free
  **`opencode/deepseek-v4-flash-free`** answers (PONG ~3.9s). Auth available via
  OpenCode Zen `api` credential; secret values never inspected.
- **Smoke repo:** `m13-3a\smoke-repo` (temp) — a tiny `net8.0` console app with
  deliberate Security issues (hardcoded live API key, embedded `P@ssw0rd!2026`
  connection string, unsalted SHA1 hashing).
- **First symptom:** runs hung until the executor's hardcoded
  `EvidenceOptions.ProviderTimeout` (120 s, DI-hardcoded — not configurable).
- **Root cause (NOT the runner):** the deployed CLI binary was **stale** (built
  before M13.3), so it never passed `--model`; OpenCode fell back to its
  billing-blocked default model and blocked. **Rebuilding the CLI fixed it** —
  no production code change was required for the hang. This is a documented
  operational rule: after source changes, rebuild the CLI before a real run.
- **Process-tree termination proven sound** with a TreeProbe (`cmd /c ping`
  tree fully killed on timeout, no orphans). The runner was correct; the two new
  tests lock the behavior in.
- **Successful real run** (`20260809-035158-8e62e7`): evidence acquisition
  ~27.2 s, total ~27.6 s; `--model opencode/deepseek-v4-flash-free` confirmed in
  the child argv; Evidence → **5 observations** → **4 Security findings** →
  reconciled → package; **no orphan processes** left.
  - Package: `schemaVersion **1.1**`, `repository: smoke-repo`,
    `overallHealth/risk: critical`, `totalFindings: 4` — critical "Passwords
    hashed with unsalted SHA1 (non-key-derivation)", high "Hardcoded live API key
    embedded in source code", medium "Insecure fallback defaults when environment
    secret is absent", low "Partial hash of secret value written to standard
    output".
  - Telemetry: `provider OpenCode/opencode/agentic`, `contextFingerprint ""`
    (correct — never fabricated for an agentic source), `strategy agentic`,
    `evidenceCount 1`, `observations 5`, `success true`, `duration 00:00:27.19`.

## Verification

| Criterion | Result |
|-----------|--------|
| Build (0 errors, 0 warnings) | ✅ |
| All tests pass | ✅ 319/319 (12 new model-selection, 2 new process-tree; 305 prior) |
| Model optional; empty ⇒ OpenCode default (no behavior change) | ✅ |
| `--model <id>` via `ArgumentList`, never a shell string | ✅ |
| Instruction stays the last argument (runner/telemetry contract intact) | ✅ |
| `Metadata["model"]` on evidence when set; absent when unset | ✅ |
| `ProviderVersion` stays the adapter version | ✅ |
| Timeout AND cancellation terminate the complete process tree | ✅ (2 new real-process tests) |
| Real smoke: OpenCode + `deepseek-v4-flash-free` → observations → findings → package | ✅ (no orphans) |
| `ContextFingerprint` never fabricated for agentic | ✅ `""` in telemetry |
| External package unchanged (`schemaVersion` **1.1**) | ✅ |

```
dotnet build EngineeringCouncil.slnx        # 0 errors, 0 warnings
dotnet test  EngineeringCouncil.slnx        # Passed: 319 (no OpenCode, no network, no keys)
```

## Deferred work (M13.4+)

Codex and Claude Code adapters, multiple OpenCode models, agent
comparison/calibration, tool-call and files-explored telemetry, command history,
OS sandboxing/containers/VM isolation, provider ranking/scoring, prompt
calibration, parallel execution, and councils.
