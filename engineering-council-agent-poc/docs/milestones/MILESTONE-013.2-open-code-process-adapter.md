# MILESTONE-013.2 — OpenCode Process Adapter

- **Status:** ✅ Complete
- **Date:** 2026-08-08
- **Version:** v18 (OpenCode agentic adapter)
- **ADR:** [ADR-022](../adr/ADR-022-open-code-agentic-adapter.md)
- **Builds on:** [MILESTONE-013.1](./MILESTONE-013.1-agentic-evidence-contract.md)

## Goal

Implement the **first real external coding-agent runtime**: OpenCode. The council
launches the `opencode` executable against the local repository, gives it **one
discipline-specific read-only analysis instruction**, receives structured JSON, and
converts it into **existing `Evidence`**. Everything after `Evidence` — interpreter →
observations → analyzers → reconciliation → package — remains unchanged. The milestone
is intentionally small and proves only the OpenCode runtime adapter; no DeepSeek-
specific behavior exists.

```
EvidenceAcquisitionStep
        ↓
OpenCodeEvidenceProvider
        ↓
OpenCodeProcessRunner
        ↓
opencode
        ↓
local repository exploration
        ↓
structured JSON
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

1. **Process runner** — `IOpenCodeProcessRunner` / `OpenCodeProcessRunner`
   (`Infrastructure/Evidence/`): start the executable, set the working directory,
   provide the instruction, capture **bounded** stdout/stderr, capture the exit code,
   honor `CancellationToken`, enforce a configured timeout. Deliberately **not** a
   generic shell or command executor — this milestone needs OpenCode only.
2. **No shell string execution** — `ProcessStartInfo { UseShellExecute = false }` +
   `ArgumentList`; repository path and prompt are never concatenated into a
   `cmd /c` / `sh -c` string. The OpenCode invocation is `["run", instruction]`.
3. **Working directory** — `WorkingDirectory = analyzed repository root`; the agent
   explores that repository itself (no copy, no whole-repo stdin, no M11.3 controlled
   context rebuild).
4. **Analysis prompt** — `OpenCodePromptBuilder.Build(request)` reuses the existing
   discipline instructions and the existing structured `observations` envelope; it
   declares the agent role, the repository availability, the READ-ONLY boundary, and
   "report observations/evidence only". There is deliberately **no** OpenCode-specific
   finding format.
5. **Read-only instruction** — the prompt explicitly forbids modifying/creating/
   deleting files, formatting, committing, and patches. This is an
   **instruction-level** boundary, documented as **not** a security sandbox
   (OS-level sandboxing/containers remain deferred).
6. **Structured output** — the provider accepts only output compatible with the
   existing structured evidence contract and reuses `LlmEvidenceResponseValidator`
   (`SchemaValidation` failures follow existing failure-isolation semantics). No
   `OpenCodeObservation`/`OpenCodeFinding`/`OpenCodeResult` types; no prose
   interpretation; no LLM repair call.
7. **Provider metadata** — `OpenCodeEvidenceProvider` is `ProviderType = Agentic`,
   `Name = OpenCode`, `DefaultAcquisitionScope = Discipline`,
   `RequiresAnalyzerInstructions = true`, `SupportsRepositoryWideAnalysis = false`,
   `SupportedDisciplines` = the existing engineering disciplines, `Version =
   "opencode-adapter-v1"` (the adapter's version — no model is hardcoded).
8. **Configuration** — `Evidence:OpenCode { Enabled: false, Executable: "opencode",
   TimeoutSeconds: 120 }`, **disabled by default** (offline Mock remains the default).
   No API keys; no DeepSeek API configuration.
9. **CLI provider selection** — the existing mechanism resolves `--provider OpenCode`;
   the `review` workflow remains authoritative; no new top-level CLI command.
10. **Timeout vs cancellation** — the runner's own timeout terminates the process and
    reports `TimedOut` → existing `Timeout` failure category → `Continue`/`FailRun`
    per `ProviderFailureMode`; a run/user cancellation terminates the process and
    propagates `OperationCanceledException` (never an ordinary failure, never left
    running intentionally).
11. **Exit codes** — `0` → parse stdout as structured result; non-zero → categorized
    `ProcessError` with a bounded stderr diagnostic (≤ 8 KiB; no secrets, env vars, or
    machine details; unlimited stderr never persisted).
12. **Model identity** — preserved as diagnostic metadata only if reliably exposed by
    the runtime; otherwise `Model = unknown`. Never guessed, never hardcoded (M13.3
    configures/validates a specific model explicitly).
13. **Context fingerprint** — `ContextFingerprint` remains `""` (unavailable) on
    records and `Evidence`, success and failure; never generated from repo hash,
    snapshot files, prompt, or working directory. M12 comparison never treats OpenCode
    as comparable to Claude/OpenAI API executions.

## Changes

- **`Core/Abstractions/OpenCodeOptions.cs`** (new) — `Evidence:OpenCode`
  configuration (ProviderName, Enabled=false, Executable, TimeoutSeconds, IsUsable,
  UnavailableReason).
- **`Infrastructure/Evidence/IOpenCodeProcessRunner.cs`** (new) — the minimal process
  seam: `OpenCodeProcessRequest` (Executable, WorkingDirectory, Arguments, Timeout),
  `OpenCodeProcessResult` (ExitCode, StandardOutput, StandardError, Duration, TimedOut),
  and the runner interface.
- **`Infrastructure/Evidence/OpenCodeProcessRunner.cs`** (new) — real process
  execution with `UseShellExecute = false` + `ArgumentList`, bounded stream capture
  (stdout 128 KiB, stderr 8 KiB), own-timeout vs cancellation, best-effort tree kill,
  categorized start failures.
- **`Infrastructure/Evidence/OpenCodePromptBuilder.cs`** (new) — the single focused,
  read-only, discipline-specific instruction (existing schema + existing instructions).
- **`Infrastructure/Evidence/OpenCodeEvidenceProvider.cs`** (new) — the
  `Agentic`/`Discipline` provider that launches OpenCode, validates the structured
  output with `LlmEvidenceResponseValidator`, and produces existing `Evidence`
  (`ProviderType = Agentic`, `ContextFingerprint = ""`, model identity absent).
- **`Infrastructure/DependencyInjection/CouncilServiceCollectionExtensions.cs`** —
  registers `OpenCodeOptions`, `IOpenCodeProcessRunner` → `OpenCodeProcessRunner`, and
  `OpenCodeEvidenceProvider`; explicit selection (`--provider OpenCode` / config) opts
  in.
- **`Cli/Program.cs`** — `BindOpenCode` binds the `Evidence:OpenCode` section;
  provider selection flows through the existing mechanism.
- **`Cli/appsettings.json`** — `Evidence:OpenCode` section (disabled by default).
- **No pipeline / comparison / reconciliation / package change.**

## Tests

New tests in `src/EngineeringCouncil.Tests/` — normal tests need **no OpenCode
installation, no network, no API keys** (a fake `IOpenCodeProcessRunner`):

**`OpenCodeEvidenceTests.cs` (14 tests)**
1. OpenCode metadata is Agentic and discipline-scoped
2. OpenCode is disabled by default
3. The configured executable is used
4. The repository root becomes the WorkingDirectory
5. The prompt contains the discipline and the analyzer instructions
6. The prompt declares the READ-ONLY boundary
7. The prompt requests the existing structured evidence schema
8. Valid process output becomes existing `Evidence` (Agentic, no fingerprint)
9. Malformed JSON → `SchemaValidation` provider failure
10. Non-zero exit code → `ProcessError` provider failure
11. Timeout → existing `Timeout` failure semantics
12. Run cancellation propagates (never an ordinary failure)
13. `ContextFingerprint` is never fabricated when unavailable
14. End to end: OpenCode → observations → findings → consumer-compatible package
    (`schemaVersion 1.1`, no `providerComparison`)

**`OpenCodeProcessRunnerTests.cs` (3 real-process tests, no OpenCode needed)** — use
the dotnet CLI (always present) for stdout/exit-code capture and, on Windows, `ping`
to prove timeout and cancellation actually terminate a long-running child.

## Verification

| Criterion | Result |
|-----------|--------|
| Build (0 errors, 0 warnings) | ✅ |
| All tests pass | ✅ 306/306 (17 new; 289 prior) |
| No shell-string execution (`UseShellExecute = false` + `ArgumentList`) | ✅ |
| WorkingDirectory = repository root | ✅ |
| One discipline-specific read-only instruction | ✅ |
| Output reuses the existing structured schema + validator | ✅ |
| Malformed / non-zero / timeout follow existing failure semantics | ✅ |
| Timeout and cancellation distinguished; no orphan process | ✅ |
| `ContextFingerprint` never fabricated | ✅ success + failure paths |
| No DeepSeek-specific behavior; model identity unknown | ✅ |
| Normal tests need no OpenCode/network/credentials | ✅ |
| External package unchanged (`schemaVersion` **1.1**) | ✅ |

```
dotnet build EngineeringCouncil.slnx        # 0 errors, 0 warnings
dotnet test  EngineeringCouncil.slnx        # Passed: 306 (no OpenCode, no network, no keys)
```

## Optional OpenCode smoke

If `opencode` is installed locally, an opt-in smoke run may execute OpenCode + one
discipline + a tiny fixture repository and verify: process starts, repository working
directory is correct, structured evidence is returned, observations are produced, and
the package is generated. **Not** required for Definition of Done and not part of the
normal suite (CI never depends on OpenCode/network/keys/paid calls).

## Security limitations

The read-only boundary in M13.2 is **instruction-level only** — the prompt tells the
agent not to modify anything. It is **not** an OS-level sandbox: an agent runtime that
misbehaves or is compromised could in principle write to the repository. OS-level
sandboxing, containers, and VM isolation are explicitly deferred and must be enforced
outside the prompt in a later milestone.

## Deferred work (M13.3+)

DeepSeek-specific configuration and API integration, explicit model selection/validation
for OpenCode, Codex and Claude Code adapters, multiple OpenCode models, agent
comparison/calibration, tool-call and files-explored telemetry, command history, OS
sandboxing/containers/VM isolation, provider ranking/scoring, reconciliation changes,
prompt calibration, parallel execution, and councils.
