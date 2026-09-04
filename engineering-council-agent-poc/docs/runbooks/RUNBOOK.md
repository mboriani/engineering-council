# Runbook — Running the Engineering Council Agent

How to build, test, and run the POC. It is **read-only** over any target
repository — it only ever writes under this POC's `outputs/` folder.

- Working directory for every command below:
  `c:\Repositories\Performances\EngineeringFinder\engineering-council-agent-poc`

---

## 1. Prerequisites

- **.NET SDK 10.0+** — check with `dotnet --version`.
- **Optional:** `OPENAI_API_KEY` to use the LLM (`Claude`) evidence provider.
  Without a key the offline **`Mock`** provider runs, so the full pipeline works
  with zero setup.
- **Optional:** the **`opencode`** executable on PATH to run the OpenCode agentic
  provider (`--provider OpenCode`; disabled by default, no API key).
- **Optional:** the **`codex`** executable (OpenAI Codex CLI) to run the Codex
  agentic provider (`--provider Codex`; disabled by default, no API key — Codex
  authenticates via its own login, `codex login`).
- **Optional:** the **`claude`** executable (Claude Code CLI) to run the Claude Code
  agentic provider (`--provider ClaudeCode`; disabled by default, no API key —
  Claude Code authenticates via its own login, `claude auth`).

---

## 2. Build & test

```powershell
cd c:\Repositories\Performances\EngineeringFinder\engineering-council-agent-poc

dotnet build EngineeringCouncil.slnx
dotnet test  EngineeringCouncil.slnx
```

Expected: build `0 Warning(s) 0 Error(s)`, tests `Passed! - Failed: 0`.

---

## 3. Run an analysis (the main use case)

Two verbs, same pipeline:
- **`review`** → prints the Engineering Review Package (health, risk, summary).
- **`analyze`** → same run, findings-oriented console output.

```powershell
# Produce an Engineering Review Package with the offline Mock provider
dotnet run --project src\EngineeringCouncil.Cli -- review --path "C:\path\to\your\solution" --provider Mock

# Quick self-demo: analyze this POC's Core project
dotnet run --project src\EngineeringCouncil.Cli -- review --path "src\EngineeringCouncil.Core" --provider Mock

# Only some disciplines (default is all seven)
dotnet run --project src\EngineeringCouncil.Cli -- review --path "src\EngineeringCouncil.Core" --provider Mock --disciplines Architecture,Security

# Use the real LLM (needs a key; falls back to Mock if unavailable)
$env:OPENAI_API_KEY = "sk-..."
dotnet run --project src\EngineeringCouncil.Cli -- review --path "C:\path\to\your\solution" --provider Claude

# OpenCode agentic runtime (Milestone 013.2/013.3) — launches `opencode` against the repo
# root with one discipline-specific read-only instruction. Needs the `opencode`
# executable on PATH; no API key. Optional explicit model: set Evidence:OpenCode:Model
# to an OpenCode provider/model id (e.g. opencode/deepseek-v4-flash-free), passed as `--model`.
# M14.1: Evidence:OpenCode:Port=0 (shipped default) makes every run start its OWN isolated
# local server via a concrete `--port <n>` instead of competing for the shared default
# server an interactive session may hold (a busy shared server can queue the run to the
# timeout — the M14.1 hang). The CLI loads ITS appsettings.json from the app base
# directory, so this works regardless of the launch directory.
dotnet run --project src\EngineeringCouncil.Cli -- review --path "C:\path\to\your\solution" --provider OpenCode --disciplines Security

# Codex agentic runtime (Milestone 013.4) — launches `codex exec -s read-only` against the
# repo root with one discipline-specific read-only instruction. Needs the `codex`
# executable on PATH and `codex login` done once; no API key. Optional explicit model:
# set Evidence:Codex:Model (e.g. gpt-5.4-mini), passed as `-m`.
dotnet run --project src\EngineeringCouncil.Cli -- review --path "C:\path\to\your\solution" --provider Codex --disciplines Security

# Claude Code agentic runtime (Milestone 013.5) — launches `claude -p` against the repo
# root with one discipline-specific read-only instruction (read-only tool restriction
# `--tools "Read,Glob,Grep"`). Needs the `claude` executable on PATH and `claude auth`
# done once; no API key. Optional explicit model: set Evidence:ClaudeCode:Model
# (e.g. claude-sonnet-5 or sonnet), passed as `--model`.
dotnet run --project src\EngineeringCouncil.Cli -- review --path "C:\path\to\your\solution" --provider ClaudeCode --disciplines Security

# Multi-agent COUNCIL run (Milestone 014.1) — all three real agentic runtimes in ONE
# AnalysisRun over the same discipline(s), consolidated by the deterministic reconciler.
# M15.1: independent acquisition steps may overlap. The shipped default
# (Evidence:Execution:MaxConcurrency=1) is strictly sequential. For a 3-provider council,
# set 3 so OpenCode/Codex/Claude Code overlap instead of serializing (~150s → slowest
# provider, an observed ~62% wall-clock reduction; see MILESTONE-015.1). Only the
# published/run-time appsettings.json needs the change — see Section 6.
dotnet run --project src\EngineeringCouncil.Cli -- review --path "C:\path\to\your\solution" --providers OpenCode,Codex,ClaudeCode --disciplines Security
```

Run with no arguments to print the help:
`dotnet run --project src\EngineeringCouncil.Cli`

### CLI options

| Flag | Meaning |
|------|---------|
| `--path`, `-p` | Target solution/repo folder (read-only). Also accepts a bare positional path. |
| `--outputs`, `-o` | Output directory (default: `outputs`). |
| `--provider` | A single evidence provider (e.g. `Claude`, `Mock`). Overrides config. |
| `--providers` | Comma-separated providers, e.g. `Claude,Sonar`. |
| `--disciplines` | Comma-separated disciplines to analyze (default: all). Values: `Architecture, CodeQuality, Reliability, Security, Testing, Documentation, Observability`. |
| `--mock` | Shortcut for `--provider Mock`. |

### Environment variables (for the `Claude`/LLM provider)

| Var | Purpose |
|-----|---------|
| `OPENAI_API_KEY` | Enables the LLM provider (OpenAI-compatible backend). |
| `OPENAI_MODEL` | Model id (default `gpt-4o-mini`). |
| `OPENAI_ENDPOINT` | Optional base endpoint (e.g. an Ollama / OpenAI-compatible gateway). |

PowerShell env-var syntax: `$env:OPENAI_API_KEY = "sk-..."` (this shell only).

---

## 4. Where the results go

Each run writes to `outputs/{runId}/`:

| File | What |
|------|------|
| `engineering-review.md` | **Primary human artifact** — the full review document. |
| `engineering-review-package.json` | **Primary machine artifact** — the Engineering Review Package. |
| `observations.json` | Normalized observations (with acquisition provenance). |
| `findings.json` | Consolidated findings (the dashboard contract). |
| `raw-findings.json` | Original per-analyzer findings (audit trail). |
| `provider-execution.json` | Acquisition plan + per-step telemetry (scope, discipline, timings, failures, and per-execution token usage when the runtime reports it — currently only ClaudeCode: `inputTokens`/`outputTokens`/`tokensUsed`, M15.2C cache fields `cacheReadInputTokens`/`cacheCreationInputTokens` + run totals, and M15.2D derived metrics `contextTokenActivity`/`freshContextTokens`/`cacheReuseRatio` + run totals. `TotalTokens` never includes cache tokens; derived metrics are activity accounting, not cost. OpenCode/Codex report unknown usage as null, never 0. M15.3B adds `attemptCount`/`retryExhausted` per record). |
| `provider-comparison.json` / `.md` | Descriptive multi-LLM provider comparison (written only when ≥2 LLM providers ran a discipline). |
| `calibration-diagnostics.json` | Calibration diagnostics — exclusive / severity-disagreement / low-agreement / observation-type-disagreement / location-disagreement signals (written only when ≥2 LLM providers ran a discipline). |
| `run.json` | The full run, for reload — including the `repositoryIdentity` provenance block (Milestone 015.3C). |

The reconciled `reconciliation` summary in `run.json` and the package now carries three
dedup diagnostics (Milestone 015.3D): `preDedupFindingCount` (consolidated findings
WITHOUT the deterministic dedup stage), `postDedupFindingCount` (after; equals
`consolidatedFindingCount`), and `deduplicatedFindingCount` (removed — preDedup − postDedup;
each join removes exactly one finding). A non-zero `deduplicatedFindingCount` means the
report already collapsed genuine cross-provider duplicates, so consumers should not
re-dedup `findings` by hand; `postDedupFindingCount == consolidatedFindingCount` always.

Open the review quickly:
```powershell
$latest = (Get-ChildItem outputs -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName
Invoke-Item "$latest\engineering-review.md"
```

### Interpreting discipline coverage (Milestone 015.3A)

`engineering-review.md` and the package's `disciplineCoverage` distinguish three states
per requested discipline — **read the words literally**, they mean different things:

- **CoveredWithFindings** (`coveredWithFindings`) — evidence acquired, findings exist.
- **CoveredNoFindings** (`coveredNoFindings`) — evidence acquired, but the review found
  no findings. This is the ONLY "clean" signal.
- **NoEvidence** (`noEvidence`) — zero successful evidence acquisitions (a timeout or
  failure is NOT evidence). A discipline in this state was **not** successfully reviewed;
  "absence of findings" there must never be read as assurance. Look at
  `provider-execution.json` to see which providers timed out/failed.

The review shows `Evidence coverage: N/M providers` per discipline. Partial coverage
(e.g. Security 2/3) is still valid evidence. If a run ends with many `noEvidence`
disciplines, the acquisition is failing (timeouts) — fix provider availability before
trusting the summary; the health/risk scores exclude un-evidenced disciplines and the
report states this limitation inline.

### Interpreting repository provenance (Milestone 015.3C)

Every run now records exactly what source state it analyzed, captured **before**
acquisition began and verified **once** when acquisition ended. It answers "what
exact source state produced this finding?".

The provenance lives in `run.json` (`repositoryIdentity`) and, as a compact
`repositorySnapshot`, in the external package:

| Field | Meaning |
|-------|---------|
| `versionControl` | `"git"` when positively identified, null otherwise (non-Git). |
| `commitSha` | Full HEAD SHA (Git). Null for non-Git / git unavailable. |
| `branch` | Current branch; null on a detached HEAD or non-Git. |
| `isDirty` | Tracked working-tree/index differs from HEAD (`true`/`false`/null). |
| `hasUntrackedFiles` | Untracked files exist in the analyzed selection (`true`/`false`/null). |
| `snapshotFingerprint` | Deterministic hash of the post-ignore-rule analyzed file selection + content (relative paths only). Dirty tracked and relevant untracked files change it — **a commit SHA alone is never sufficient identity for a dirty tree**. |
| `repositoryChangedDuringRun` | `true` when the end-of-acquisition fingerprint differs from the start (the repository mutated mid-run). The start fingerprint is always retained; this only warns. |

`engineering-review.md` shows a compact **Repository State** block (commit prefix,
branch, working tree, untracked files, snapshot, changed-during-review).

- If `repositoryChangedDuringRun: true`, the run warns — it does **not** fail. Providers
  may have observed different states; re-run against a stable tree before trusting the
  finding set.
- If `versionControl` is null, the target had no Git identity; the Council still analyzed
  it and the `snapshotFingerprint` still identifies the analyzed files.
- Scope note: this is **identification**, not a transactional filesystem snapshot. Start/end
  equality detects most mutations but cannot guarantee every provider read the same bytes.

---

## 5. Run the HTTP API (optional)

Same endpoints either way:
- `POST /runs` with body `{ "targetPath": "C:\\path\\to\\solution" }`
- `GET /runs` → list run ids
- `GET /runs/{id}` → the run's `run.json`
- `GET /health`

Provider selection for the API comes from its `appsettings.json`
(`Evidence:Providers`) / environment, same rules as the CLI.

**Swagger UI** is available at `/swagger` (and `/` redirects to it); the OpenAPI
document is at `/swagger/v1/swagger.json`.

### 5a. Directly (simplest — no Aspire, no DCP)

```powershell
dotnet run --project src\EngineeringCouncil.Api
# the console prints the URL, e.g. http://localhost:5xxx  → open /swagger
```

### 5b. Via Aspire (AppHost + dashboard)

```powershell
dotnet run --project src\EngineeringCouncil.AppHost
```

Aspire needs its orchestration component (**DCP**, `dcp.exe`) and dashboard, which
ship as separate platform-specific NuGet packages. Our AppHost only orchestrates
the API project, so **Docker is not required**. If startup fails with:

```
The Aspire orchestration component is not installed at "...aspire.hosting.orchestration.win-x64\...\tools\dcp.exe"
Dashboard DLL not found: "...aspire.dashboard.sdk.win-x64\...\tools\Aspire.Dashboard.dll"
```

…the flaky temp NuGet cache dropped those tool packages (not a code problem). Fix:

```powershell
$cache = "C:\Users\User\AppData\Local\Temp\dotnet-cli-home\.nuget\packages"
Remove-Item -Recurse -Force "$cache\aspire.hosting.orchestration.win-x64","$cache\aspire.dashboard.sdk.win-x64" -ErrorAction SilentlyContinue
dotnet restore src\EngineeringCouncil.AppHost\EngineeringCouncil.AppHost.csproj --force
```

Then re-run the AppHost. If you don't need the dashboard, prefer **5a**.

---

## 6. Configuration (optional)

`Evidence:Providers` in `appsettings.json` accepts plain names or objects with a
per-provider acquisition scope:

```json
{
  "Evidence": {
    "Providers": [
      { "Name": "Claude", "Scope": "Discipline" }
    ]
  }
}
```

- **Scope `Discipline`** (LLMs and agentic sources like OpenCode/Codex): one focused
  request per discipline.
- **Scope `Repository`** (static tools like Sonar/SARIF/Roslyn/Git/coverage): one
  pass over the whole repo. Defaults come from each provider's metadata, so you
  normally don't set `Scope` at all.

### Parallel execution (Milestone 015.1)

`Evidence:Execution:MaxConcurrency` bounds how many independent acquisition steps
run concurrently. **Default `1`** = strictly sequential (identical behavior to
before M15.1). A 3-provider Council run sets `3` so all three agent runtimes
overlap:

```json
{
  "Evidence": {
    "Execution": { "MaxConcurrency": 3, "ProviderTimeout": 120, "MaxAttempts": 2 }
  }
}
```

- The CLI/API load THEIR appsettings.json from the app base directory, so edit the
  run-time config (`src/EngineeringCouncil.Cli/appsettings.json`, or the published
  copy next to the CLI binary), not the repo default.
- Results are always aggregated in plan order — parallel execution never changes
  artifacts, the package (`schemaVersion` stays `1.1`), or the consumer contract.
- Semantics are unchanged: `ProviderFailureMode` (`Continue`/`FailRun`),
  per-provider timeouts, and run-cancellation behavior all behave as documented.
- **Timeout precedence (M15.2B):** each step's timeout is per-step and
  independent — `EffectiveTimeout = Provider.TimeoutSeconds` when set, else the
  global `Execution:ProviderTimeout` fallback (default 120 s). A provider's own
  `TimeoutSeconds` ALWAYS wins (ClaudeCode's 300 s is honored, never cut at the
  120 s fallback — the M15.2A blocker). A step timeout terminates the process
  tree as a `Timeout` failure; a run/user cancellation propagates as
  cancellation and is never converted to a timeout.
- **Bounded retry (M15.3B):** `Evidence:Execution:MaxAttempts` (default 2) lets a
  step retry ONCE after a `Timeout` failure (only timeouts are retried — never
  cancellation, auth/config, schema-validation or unexpected errors). The retry
  uses the SAME effective timeout (no doubling), never runs a third attempt, and
  stays inside its step's `MaxConcurrency` permit. `1` disables step-level retry.
  Per-step telemetry records `attemptCount`/`retryCount`/`retryExhausted`.

---

## 7. Troubleshooting

- **"Running with the offline MOCK provider only…"** — no `OPENAI_API_KEY`;
  expected. Set the key (and optionally `OPENAI_MODEL`) to use `Claude`.
- **`OpenCode could not be started`** — the `opencode` executable is not installed
  or not on PATH (or the working directory does not exist). Install/point
  `Evidence:OpenCode:Executable` at the right path, then re-run
  `--provider OpenCode`. OpenCode is disabled by default; selecting it opts in.
- **Run hangs / times out** — two causes. (1) **Stale CLI binary**: after source
  changes you must rebuild before a real run (`dotnet build EngineeringCouncil.slnx`),
  otherwise a stale binary won't pass `--model` and OpenCode may fall back to a
  blocked default model and wait until the provider timeout. (2) **Config not loaded /
  shared server queue (M14.1)**: the CLI loads ITS `appsettings.json` from the app base
  directory (`SetBasePath(AppContext.BaseDirectory)`) so `Evidence:OpenCode:Port=0`
  is effective and each run gets a concrete isolated `--port <n>`; if the file or the
  `Port` key is missing/removed, `opencode run` competes for the shared default server
  an interactive session holds and queues to the timeout. Configure an explicit working
  model, e.g. `Evidence__OpenCode__Model=opencode/deepseek-v4-flash-free`.
- **A specific provider times out while others succeed** — M15.2B: per-provider
  `TimeoutSeconds` is now honored (ClaudeCode 300 s is no longer cut at the 120 s
  global fallback). M15.3B: the step retries ONCE after a timeout
  (`Evidence:Execution:MaxAttempts=2`), so a slow-but-healthy run gets a second
  chance with the same timeout budget; `retryExhausted: true` in
  `provider-execution.json` means it timed out on both attempts. If a slow-but-healthy
  step is still cut at its OWN timeout after the retry, raise that provider's
  `TimeoutSeconds` (e.g. OpenCode needs more than 120 s for a real 100+ file
  Security analysis). The global `Evidence:Execution:ProviderTimeout` only applies to
  providers without their own `TimeoutSeconds`.
- **`OpenCode could not be started` / model blocked** — the model id must be in
  OpenCode's own `provider/model` format (see `opencode models`); paid models may
  require billing. Credentials live in OpenCode's auth (e.g. `DEEPSEEK_API_KEY`),
  never in `Evidence:OpenCode`.
- **`Codex could not be started`** — the `codex` executable is not installed or not
  on PATH. Install it and point `Evidence:Codex:Executable` at the right path.
- **`Codex exited with code ...` / auth error** — run `codex login` once; Codex
  authenticates via its own account and the council never holds a key. If the
  configured `Evidence:Codex:Model` (e.g. `gpt-5.4-mini`) is not available to the
  account, unset it so Codex uses its own default model.
- **`ClaudeCode could not be started`** — the `claude` executable is not installed
  or not on PATH. Install it and point `Evidence:ClaudeCode:Executable` at the right
  path.
- **`ClaudeCode exited with code ...` / auth error** — run `claude auth` once;
  Claude Code authenticates via its own account and the council never holds a key.
  If the configured `Evidence:ClaudeCode:Model` (e.g. `claude-sonnet-5`) is not
  available to the account, unset it so Claude Code uses its own default model.
- **`ClaudeCode ... failed validation`** — the CLI returned an error/empty/malformed
  result envelope (or the agent's final message was not the structured
  `observations` JSON). Re-run and inspect `provider-execution.json`; there is no
  repair call by design.
- **`targetPath does not exist`** — pass a real folder path (absolute is safest).
- **Empty findings with a real model** — the model returned no valid observations
  JSON; check `provider-execution.json` and the discipline prompts.
- **NuGet cache errors** (`MSB3030: Could not copy the file ... because it was not
  found`, or `CS2001 ... Microsoft.NET.Test.Sdk.Program.cs could not be found`) —
  this machine's temp NuGet cache is flaky; it is **not** a code problem. Fix:
  ```powershell
  # remove the package it complains about, then force a restore
  Remove-Item -Recurse -Force "C:\Users\User\AppData\Local\Temp\dotnet-cli-home\.nuget\packages\<package-name>"
  dotnet restore EngineeringCouncil.slnx --force
  ```
  `Directory.Build.props` already sets `SatelliteResourceLanguages=en` to reduce it.

---

## 8. Safety

The tool never writes to, modifies, or deletes anything under the target
repository. All output goes to this POC's `outputs/` directory.
