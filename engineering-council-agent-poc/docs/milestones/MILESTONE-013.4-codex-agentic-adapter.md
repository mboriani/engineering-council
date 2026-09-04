# MILESTONE-013.4 — Codex Agentic Adapter

- **Status:** ✅ Complete
- **Date:** 2026-08-09
- **ADR:** [ADR-023](../adr/ADR-023-codex-agentic-adapter.md)
- **Builds on:** [MILESTONE-013.3](./MILESTONE-013.3-open-code-model-selection-and-real-smoke.md)

## Goal

Add a **second real external coding-agent runtime**: the OpenAI **Codex CLI**. The
council launches the `codex` executable against the local repository, gives it **one
discipline-specific read-only analysis instruction**, receives structured JSON, and
converts it into **existing `Evidence`**. Everything after `Evidence` — interpreter →
observations → analyzers → reconciliation → package — remains unchanged. The
milestone proves the agentic contract (ADR-021) is runtime-agnostic: OpenCode
(M13.2/13.3) and Codex both plug into the same seam with the same downstream.

```
EvidenceAcquisitionStep
        ↓
CodexEvidenceProvider
        ↓
CodexProcessRunner
        ↓
codex exec -s read-only --ephemeral --skip-git-repo-check [-m <model>] "<instruction>"
        ↓
local repository exploration (read-only sandbox)
        ↓
structured JSON (observations envelope)
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

1. **Process runner** — `ICodexProcessRunner` / `CodexProcessRunner`
   (`Infrastructure/Evidence/`): start the executable, set the working directory to
   the analyzed repository root, provide the instruction, capture **bounded**
   stdout/stderr, capture the exit code, honor `CancellationToken`, enforce a
   configured timeout. Deliberately **not** a generic shell or command executor.
2. **No shell string execution** — `ProcessStartInfo { UseShellExecute = false }` +
   `ArgumentList`; repository path and prompt are never concatenated into a
   `cmd /c` / `sh -c` string.
3. **Codex invocation** — non-interactive mode
   `exec -s read-only --ephemeral --skip-git-repo-check [-m <model>] "<instruction>"`.
   `-s read-only` enforces the read-only boundary **at the Codex sandbox level** (in
   addition to the prompt's instruction-level boundary); `--ephemeral` keeps the run
   stateless; `--skip-git-repo-check` keeps the adapter usable on plain directories
   (fixture repos are not necessarily git checkouts).
4. **Analysis prompt** — `CodexPromptBuilder.Build(request)` reuses the existing
   discipline instructions and the existing structured `observations` envelope; it
   declares the agent role, repository availability, the READ-ONLY boundary, the
   requested discipline, and evidence-only output (no final review, no modifications,
   no commits).
5. **Codex's own authentication** — Codex authenticates through its own login
   (ChatGPT account / `codex login`). The Council never reads, stores, or forwards a
   Codex credential; there is no API-key option. The offline Mock remains the
   zero-config default.
6. **Optional model selection** — `CodexOptions.Model` (e.g. `gpt-5.4-mini`) passed
   as `-m <model>` via `ArgumentList`; when unset Codex uses its own default. Never
   mandatory, never guessed, never hardcoded. When set, preserved as
   `Metadata["model"]` telemetry. `ProviderVersion` stays `codex-adapter-v1`.
7. **Failure semantics** — malformed structured output is a **categorized**
   `SchemaValidation` failure (existing `LlmEvidenceResponseValidator`, no repair
   call); non-zero exit is a categorized `ProcessError` failure with bounded stderr;
   timeout terminates the process and surfaces a `Timeout` failure; run/user
   cancellation terminates the process and propagates `OperationCanceledException`
   (never an ordinary failure). `ContextFingerprint` stays `""` — never fabricated.
8. **Non-parallel test collection** — the OpenCode and Codex real-process tests share
   one NON-PARALLEL xUnit collection (`ProcessRunnerTests`) so their short-lived
   `cmd /c ping` orphan-survivor probes cannot observe each other's still-running
   children.

## Changes

- **`Core/Abstractions/CodexOptions.cs`** (new) — `Enabled` (default false),
  `Executable` (default `codex`), `Model` (optional), `TimeoutSeconds` (default 120),
  `IsUsable`, `UnavailableReason`, `Timeout`.
- **`Infrastructure/Evidence/CodexProcessRunner.cs`** + **`ICodexProcessRunner`**
  (new) — argv-based process runner mirroring the OpenCode seam; bounded output,
  timeout, cancellation, process-tree termination; process-start failures become a
  categorized provider failure.
- **`Infrastructure/Evidence/CodexPromptBuilder.cs`** (new) — the one-focused-
  instruction prompt (existing discipline instructions + existing structured schema).
- **`Infrastructure/Evidence/CodexEvidenceProvider.cs`** (new) — agentic,
  discipline-scoped provider; builds `codex exec ...` args; reuses
  `LlmEvidenceResponseValidator`; failure/cancellation semantics; telemetry metadata.
- **`Core/Abstractions/IProviderRegistry.cs` / `ProviderRegistry`** (or equivalent
  registry) — registered `Codex` as a resolvable provider.
- **`Cli/Program.cs`** — wired `CodexOptions` (bound from `Evidence:Codex`) and the
  Codex runner/provider into DI.
- **`Cli/appsettings.json`** — documented `Evidence:Codex` section (disabled by
  default; comment that credentials live in Codex's own auth, never here).
- **`Tests/CodexEvidenceTests.cs`** (new, **20 tests**) — offline, fake runner, no
  Codex/network/keys:
  1. Metadata is agentic and discipline-scoped
  2. Disabled by default
  3. Uses the configured executable
  4. Uses the repository root as the working directory
  5. Prompt contains discipline and analyzer instructions
  6. Prompt declares the read-only boundary
  7. Prompt requests the existing structured schema
  8. Configured model reaches the process invocation safely (`-m <id>`, last-arg
     instruction contract intact)
  9. Configured model appears in evidence telemetry (`Metadata["model"]`)
  10. Without a configured model, Codex default behavior is preserved (no `-m`, no
      model metadata)
  11. Model is optional and never hardcoded
  12. Valid process output becomes evidence
  13. Malformed output is a schema failure
  14. Non-zero exit code is a process failure
  15. Timeout uses the existing timeout failure semantics
  16. Cancellation propagates and is not an ordinary failure
  17. Never fabricates a `ContextFingerprint` when unavailable
  18. No API key or credential is written to telemetry or artifacts
  19. End to end produces observations, findings, and a consumer-compatible package
  20. Consumer-DTO gate stays green for the package contract
- **`Tests/CodexProcessRunnerTests.cs`** (new, **5 tests**) — Windows-safe
  real-process tests (no Codex install needed): stdout+exit-code capture; timeout
  terminates the process; cancellation propagates and terminates the process;
  timeout and cancellation each terminate the **complete** process tree
  (`cmd /c ping 127.0.0.1 -n 30`; snapshot pre-existing ping PIDs, assert no new
  survivor). Shares the non-parallel `ProcessRunnerTests` collection with the OpenCode
  runner tests.

## Investigation — real smoke

- **Environment verified:** native Codex CLI at
  `C:\Users\User\AppData\Roaming\npm\node_modules\@openai\codex\node_modules\@openai\codex-win32-x64\vendor\x86_64-pc-windows-msvc\bin\codex.exe`.
  Model identifiers are plain names (`gpt-5.4-mini`). Codex authenticates via its own
  login; secret values never inspected and never appear in Council telemetry/artifacts.
- **Smoke repo:** `m13-4\codex-smoke-repo` (temp) — a tiny `net8.0` console app with
  deliberate Security issues (hardcoded live Stripe API key, embedded `P@ssw0rd!2026`
  connection string, unsalted SHA1 hashing with an insecure fallback default, plaintext
  connection-string logging).
- **Successful real run** (`20260809-052551-9bb061`): evidence acquisition ~59.6 s,
  total ~61 s; **3 Security observations** → **3 findings** (2 High, 1 Medium) →
  reconciled → package; **no orphan processes** left.
  - Package: `schemaVersion **1.1**`, `repository: codex-smoke-repo`,
    `overallHealth: NeedsAttention`, `risk: High`, `totalFindings: 3` — high
    "Production credentials and API key are embedded in source", medium "Migration
    path logs the full connection string", high "Password hashing uses SHA1 with a
    default fallback password".
  - Telemetry: `provider Codex/agentic`, `contextFingerprint ""` (correct — never
    fabricated for an agentic source), `strategy agentic`, `evidenceCount 1`,
    `observationsProduced 3`, `success true`, `duration 00:00:59.6453160`.
  - Secret-leak check: the live Stripe key is **redacted in the Codex output itself**
    (`sk-live-...`) and no adapter credential appears anywhere; the connection-string
    password appears only because Codex quoted the source excerpt as evidence (expected
    passthrough for a review tool — the secret still never reaches telemetry or
    configuration).
- **Operational rule reaffirmed (M13.3 lesson):** the CLI was rebuilt before the real
  run; a stale binary would not carry the Codex provider.

## Verification

| Criterion | Result |
|-----------|--------|
| Build (0 errors, 0 warnings) | ✅ |
| All tests pass | ✅ 344/344 (25 new: 20 provider, 5 process-runner; 319 prior) |
| Codex disabled by default; explicit `--provider Codex` opts in | ✅ |
| `codex exec -s read-only --ephemeral --skip-git-repo-check` via `ArgumentList`, never a shell string | ✅ |
| Instruction stays the last argument (runner/telemetry contract intact) | ✅ |
| Optional model → `-m <model>`; unset → Codex default; never hardcoded | ✅ |
| `Metadata["model"]` on evidence when set; absent when unset | ✅ |
| `ProviderVersion` stays `codex-adapter-v1` | ✅ |
| No API key or credential option; Codex uses its own auth | ✅ |
| Timeout AND cancellation terminate the complete process tree | ✅ (2 real-process tests) |
| OpenCode + Codex runner tests never run in parallel (non-parallel collection) | ✅ |
| Real smoke: Codex → observations → findings → package | ✅ (no orphans) |
| `ContextFingerprint` never fabricated for agentic | ✅ `""` in telemetry |
| External package unchanged (`schemaVersion` **1.1**) | ✅ |

```
dotnet build EngineeringCouncil.slnx        # 0 errors, 0 warnings
dotnet test  EngineeringCouncil.slnx        # Passed: 344 (no Codex, no OpenCode, no network, no keys)
```

## Deferred work (M13.5+)

Claude Code adapter, multiple Codex models/agents, agent comparison/calibration,
tool-call and files-explored telemetry, command history, full OS sandboxing,
containers, VM isolation, provider ranking/scoring, prompt calibration, parallel
execution, and councils.
