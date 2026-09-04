# MILESTONE-013.5 — Claude Code Agentic Adapter

- **Status:** ✅ Complete
- **Date:** 2026-08-09
- **ADR:** [ADR-024](../adr/ADR-024-claude-code-agentic-adapter.md)
- **Builds on:** [MILESTONE-013.4](./MILESTONE-013.4-codex-agentic-adapter.md)

## Goal

Add a **third real external coding-agent runtime**: the **Claude Code CLI**. The
council launches the `claude` executable against the local repository, gives it **one
discipline-specific read-only analysis instruction**, receives the agent's structured
JSON (wrapped in the CLI's own result envelope), and converts it into **existing
`Evidence`**. Everything after `Evidence` — interpreter → observations → analyzers →
reconciliation → package — remains unchanged. The milestone proves the agentic
contract (ADR-021) is runtime-agnostic: OpenCode (M13.2/13.3), Codex (M13.4), and
Claude Code all plug into the same seam with the same downstream.

```
EvidenceAcquisitionStep
        ↓
ClaudeCodeEvidenceProvider
        ↓
ClaudeCodeProcessRunner
        ↓
claude -p --output-format json --no-session-persistence
       --tools "Read,Glob,Grep" [--model <model>] -- "<instruction>"
        ↓
local repository exploration (read-only tool restriction)
        ↓
result envelope ({"type":"result","is_error":false,"result":"…"})
        ↓
ClaudeCodeOutputExtractor (unwrap ONE documented field)
        ↓
existing LlmEvidenceResponseValidator (observations envelope)
        ↓
Evidence
        ↓
existing StructuredLlmEvidenceInterpreter
        ↓
EngineeringObservation
        ↓
existing analyzers
        ↓
reconciliation
        ↓
EngineeringReviewPackage
```

## Scope

1. **Process runner** — `IClaudeCodeProcessRunner` / `ClaudeCodeProcessRunner`
   (`Infrastructure/Evidence/`): start the executable, set the working directory to
   the analyzed repository root, provide the instruction, capture **bounded**
   stdout/stderr, capture the exit code, honor `CancellationToken`, enforce a
   configured timeout, and terminate the **complete** process tree on both timeout
   and cancellation (M13.3A lesson). Deliberately **not** a generic shell or command
   executor.
2. **No shell string execution** — `ProcessStartInfo { UseShellExecute = false }` +
   `ArgumentList`; repository path and prompt are never concatenated into a
   `cmd /c` / `sh -c` string.
3. **Claude Code invocation** — non-interactive mode
   `-p --output-format json --no-session-persistence --tools "Read,Glob,Grep"
   [--model <model>] -- "<instruction>"`. `--tools "Read,Glob,Grep"` is the CLI's own
   supported read-only mechanism (write-capable built-in tools removed from the
   toolset; the prompt is the second boundary); `--no-session-persistence` keeps the
   run stateless; `--` stops the variadic `--tools <tools...>` from swallowing the
   prompt (verified against real v2.1.223 behavior).
4. **Analysis prompt** — `ClaudeCodePromptBuilder.Build(request)` reuses the existing
   discipline instructions (which carry the structured `observations` envelope); it
   declares the agent role, repository availability (not embedded in the prompt), the
   READ-ONLY access boundary, and evidence-only output (no final review, no
   modifications).
5. **Minimal envelope adapter** — `ClaudeCodeOutputExtractor` unwraps exactly the one
   documented `result` envelope field and hands the text to the existing
   `LlmEvidenceResponseValidator`. Not a new parser; no prose salvage; no repair
   call.
6. **Claude Code's own authentication** — Claude Code authenticates through its own
   login (`claude auth` / the configured account). The Council never reads, stores, or
   forwards a Claude credential; there is no API-key option. The offline Mock remains
   the zero-config default.
7. **Optional model selection** — `ClaudeCodeOptions.Model` (e.g. `claude-sonnet-5`
   or an alias like `sonnet`) passed as `--model <id>` via `ArgumentList`; when unset
   Claude Code uses its own default. Never mandatory, never guessed, never hardcoded.
   When set, preserved as `Metadata["model"]` telemetry. `ProviderVersion` stays
   `claudecode-adapter-v1`.
8. **Failure semantics** — envelope failures (`is_error`, non-envelope, missing/empty
   `result`) and malformed structured output are **categorized** `SchemaValidation`
   failures (existing validator, no repair call); non-zero exit is a categorized
   `ProcessError` failure with bounded stderr; timeout terminates the complete process
   tree and surfaces a `Timeout` failure; run/user cancellation terminates the
   complete process tree and propagates `OperationCanceledException` (never an
   ordinary failure). `ContextFingerprint` stays `""` — never fabricated.
9. **Non-parallel test collection** — the OpenCode, Codex, and ClaudeCode real-process
   tests share one NON-PARALLEL xUnit collection (`ProcessRunnerTests`) so their
   short-lived `cmd /c ping` orphan-survivor probes cannot observe each other's
   still-running children.

## Changes

- **`Core/Abstractions/ClaudeCodeOptions.cs`** (new) — `Enabled` (default false),
  `Executable` (default `claude`), `Model` (optional), `TimeoutSeconds` (default 300),
  `IsUsable`, `UnavailableReason`, `Timeout`.
- **`Infrastructure/Evidence/ClaudeCodeProcessRunner.cs`** + **`IClaudeCodeProcessRunner`**
  (new) — argv-based process runner mirroring the OpenCode/Codex seam; bounded output,
  timeout, cancellation, complete process-tree termination; process-start failures
  become a categorized provider failure.
- **`Infrastructure/Evidence/ClaudeCodePromptBuilder.cs`** (new) — the one-focused-
  instruction prompt (existing discipline instructions + existing structured schema;
  repository location note; READ-ONLY access boundary).
- **`Infrastructure/Evidence/ClaudeCodeOutputExtractor.cs`** (new) — unwraps the ONE
  documented CLI result-envelope field; rejects error/non-envelope/missing/empty
  results; bounded error surfacing.
- **`Infrastructure/Evidence/ClaudeCodeEvidenceProvider.cs`** (new) — agentic,
  discipline-scoped provider; builds `claude -p ...` args; reuses
  `LlmEvidenceResponseValidator`; failure/cancellation semantics; telemetry metadata.
- **`Infrastructure/DependencyInjection/CouncilServiceCollectionExtensions.cs`** —
  added `ClaudeCode` to `CouncilOptions`; enabled-on-explicit-selection; registered
  the runner and provider in DI.
- **`Cli/Program.cs`** — wired `ClaudeCodeOptions` (bound from `Evidence:ClaudeCode`)
  and the `--provider ClaudeCode` help line.
- **`Cli/appsettings.json`** — documented `Evidence:ClaudeCode` section (disabled by
  default; comment that credentials live in Claude Code's own auth, never here).
- **`Tests/ClaudeCodeEvidenceTests.cs`** (new, **22 tests**) — offline, fake runner,
  no Claude Code/network/keys:
  1. Metadata is agentic and discipline-scoped
  2. Disabled by default
  3. Uses the configured executable
  4. Uses the repository root as the working directory
  5. Launches non-interactive with the read-only tool restriction
     (`-p`, `--output-format json`, `--no-session-persistence`, `--tools`,
     `Read,Glob,Grep`, `--`; no Write/Bash tools)
  6. Prompt contains discipline and analyzer instructions
  7. Prompt declares the read-only boundary
  8. Prompt requests the existing structured schema
  9. Configured model reaches the process invocation safely (`--model <id>`,
     last-arg instruction contract intact)
  10. Configured model appears in evidence telemetry (`Metadata["model"]`,
      `agentRuntime: claude-code`)
  11. Without a configured model, Claude Code default behavior is preserved (no
      `--model`)
  12. Model is optional and never hardcoded
  13. Valid result envelope becomes evidence
  14. `is_error` envelope is a schema failure
  15. Non-envelope output is a schema failure
  16. Empty `result` is a schema failure
  17. Non-zero exit code is a process failure
  18. Timeout uses the existing timeout failure semantics
  19. Cancellation propagates and is not an ordinary failure
  20. Never fabricates a `ContextFingerprint` when unavailable
  21. No credential or secret is written to telemetry or artifacts
  22. End to end produces observations, findings, and a consumer-compatible package
- **`Tests/ClaudeCodeProcessRunnerTests.cs`** (new, **5 tests**) — Windows-safe
  real-process tests (no Claude Code install needed): stdout+exit-code capture;
  timeout terminates the process; cancellation propagates and terminates the process;
  timeout and cancellation each terminate the **complete** process tree
  (`cmd /c ping 127.0.0.1 -n 30`; snapshot pre-existing ping PIDs, assert no new
  survivor). Shares the non-parallel `ProcessRunnerTests` collection with the OpenCode
  and Codex runner tests.

## Investigation — real smoke

- **Environment verified:** Claude Code CLI v2.1.223 installed and authenticated via
  its own login (`claude auth status`: `loggedIn true`, first-party). Secret values
  never inspected and never appear in Council telemetry/artifacts.
- **CLI facts verified against real v2.1.223 behavior:**
  - `--output-format json` emits a single result envelope with `is_error` and
    `result` string fields (confirmed with a trivial probe).
  - `--tools <tools...>` is **variadic**: without `--`, the trailing prompt is
    consumed as another tool name and the CLI fails with *"Input must be provided
    either through stdin or as a prompt argument"*. `--` before the prompt fixes it.
  - `--tools "Read,Glob,Grep"` is the supported way to restrict the built-in toolset
    to read-only tools.
- **Smoke repo:** `cc-smoke` (temp) — a tiny `net10.0` project with a hardcoded SQL
  connection string (`P@ssw0rd123!`) and a presence-only token check.
- **Successful real run** (`20260810-002540-89a591`): evidence acquisition ~30 s,
  total ~31 s; **2 Security observations** → **2 findings** (1 Critical, 1 High) →
  reconciled → package; **no orphan processes** left.
  - Package: `schemaVersion **1.1**`, `findings: 2`, no `providerComparison`, no
    `model` field, `ClaudeCode` attributed in the package.
  - Observations: `HardcodedSecret` (critical) and `BrokenAuthentication` (high),
    both `sourceProvider: ClaudeCode`, `sourceProviderType: agentic`, with correct
    `fileReferences` (line 6 / line 8 of `src/AuthService.cs`).
  - Telemetry record: `providerId claudecode`, `providerVersion claudecode-adapter-v1`,
    `providerType agentic`, `contextFingerprint ""` (correct — never fabricated for an
    agentic source), `strategy agentic`, `evidenceCount 1`, `observationsProduced 2`,
    `success true`, `duration 00:00:30.2169360`.
  - Orphan check: no `claude` process from the run's PATH executable survived; the
    only `claude` processes present (2) were the user's pre-existing VSCode Claude
    Code extension helper processes from ~34 minutes earlier.
- **Operational rule reaffirmed (M13.3 lesson):** the CLI was rebuilt before the real
  run; a stale binary would not carry the ClaudeCode provider.

## Verification

| Criterion | Result |
|-----------|--------|
| Build (0 errors, 0 warnings) | ✅ |
| All tests pass | ✅ 371/371 (27 new: 22 provider, 5 process-runner; 344 prior) |
| ClaudeCode disabled by default; explicit `--provider ClaudeCode` opts in | ✅ |
| `claude -p --output-format json --no-session-persistence --tools "Read,Glob,Grep"` via `ArgumentList`, never a shell string | ✅ |
| `--` separates the variadic `--tools` from the prompt (last-arg instruction contract intact) | ✅ |
| Optional model → `--model <id>`; unset → Claude Code default; never hardcoded | ✅ |
| `Metadata["model"]` on evidence when set; absent when unset | ✅ |
| `ProviderVersion` stays `claudecode-adapter-v1` | ✅ |
| No API key or credential option; Claude Code uses its own auth | ✅ |
| Envelope failures are categorized `SchemaValidation` (is_error, non-envelope, empty) | ✅ |
| Timeout AND cancellation terminate the complete process tree | ✅ (2 real-process tests) |
| OpenCode + Codex + ClaudeCode runner tests never run in parallel (non-parallel collection) | ✅ |
| Real smoke: Claude Code → observations → findings → package | ✅ (no orphans) |
| `ContextFingerprint` never fabricated for agentic | ✅ `""` in telemetry |
| External package unchanged (`schemaVersion` **1.1**) | ✅ |

```
dotnet build EngineeringCouncil.slnx        # 0 errors, 0 warnings
dotnet test  EngineeringCouncil.slnx        # Passed: 371 (no Claude Code, no network, no keys)
```

## Deferred work (M13.6+)

Multiple Claude Code models/agents, agent comparison/calibration, tool-call and
files-explored telemetry, command history, full OS sandboxing, containers, VM
isolation, provider ranking/scoring, prompt calibration, parallel execution, and
councils.
