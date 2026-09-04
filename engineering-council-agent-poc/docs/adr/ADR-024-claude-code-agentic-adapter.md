# ADR-024 — Claude Code Agentic Adapter

- **Status:** Accepted
- **Date:** 2026-08-09
- **Milestone:** [MILESTONE-013.5](../milestones/MILESTONE-013.5-claude-code-agentic-adapter.md)
- **Builds on:** [ADR-021](./ADR-021-agentic-evidence-contract.md),
  [ADR-022](./ADR-022-open-code-agentic-adapter.md),
  [ADR-023](./ADR-023-codex-agentic-adapter.md),
  [ADR-005](./ADR-005-evidence-provider-layer.md)

## Context

ADR-021 established the provider-neutral **Agentic** evidence contract, ADR-022
proved it with the **OpenCode** runtime, and ADR-023 with the **Codex** CLI. The
contract is runtime-agnostic: *repository location + one discipline-specific
read-only analysis instruction + the agent's own exploration, structured output in
the existing `observations` envelope, never a fabricated `ContextFingerprint`*.

M13.5 adds a **third real agentic runtime**: the **Claude Code** CLI (the native
`claude` executable). Claude Code is the second of the two "big" coding-agent CLIs
alongside Codex, and the natural sibling of the Council's existing direct **Claude**
API provider (ADR-012). The core questions are: how to map Claude Code's
non-interactive invocation onto the existing runner/telemetry contract, and how to
keep the runtime's read-only boundary as strong as Codex's sandbox given that Claude
Code does **not** ship an OS sandbox on Windows.

## Decision

### Same agentic contract, third runtime

Claude Code is added as another infrastructure/provider-layer agentic source. The
domain (`Evidence`, `EngineeringObservation`, analyzers, reconciler,
`EngineeringReviewPackage`) is untouched. There is no `ClaudeCodeObservation` or
`ClaudeCodeFinding`. The provider is `EvidenceProviderType.Agentic`,
**Discipline**-scoped, and writes the SAME `observations` envelope as every other
source, validated by the existing `LlmEvidenceResponseValidator`.

### Naming: ClaudeCode ≠ Claude

`ClaudeCode` is the agentic Claude Code CLI runtime. `Claude` is the DIRECT Anthropic
API provider (controlled context, API key, `LlmProviderOptions`). They are two
unambiguous providers and are never silently conflated — distinct `ProviderId`s
(`claudecode` vs `claude`), distinct `ProviderName`s, distinct configuration
sections, and the `Agentic` vs `Api` provider types.

### A purpose-built runner mirroring the OpenCode/Codex seam

`IClaudeCodeProcessRunner` / `ClaudeCodeProcessRunner` is deliberately small and
mirrors the OpenCode/Codex runner shape — it is not a generic command executor:

- start the Claude Code executable;
- set the working directory to the analyzed repository root;
- provide the instruction;
- capture bounded stdout and stderr;
- capture the exit code;
- honor `CancellationToken` and enforce a configured timeout;
- on timeout **and** on cancellation, terminate the COMPLETE process tree
  (the M13.3A lesson), never leaving an orphaned child.

### No shell string execution

The runner uses `ProcessStartInfo` with `UseShellExecute = false` and passes every
argument through `ProcessStartInfo.ArgumentList` (the .NET equivalent of argv).
Neither the repository path nor the instruction is ever concatenated into a
`cmd /c`, `powershell -Command`, `bash -c`, or `sh -c` string. This preserves the
ADR-022 argument-injection guarantee.

### `claude -p` non-interactive invocation with the read-only tool restriction

The provider invokes Claude Code's supported non-interactive mode
(`--print`), verified against `claude --help` (v2.1.223):

```
claude -p --output-format json --no-session-persistence
       --tools "Read,Glob,Grep" [--model <model>] -- "<instruction>"
```

- `-p` (print) is the non-interactive mode; `--output-format json` wraps the
  agent's final message in a single result envelope.
- `--tools "Read,Glob,Grep"` is the CLI's own supported read-only mechanism: it
  REMOVES write-capable built-in tools (Edit/Write/Bash/MultiEdit) from the
  available toolset entirely. Combined with the prompt's instruction-level boundary,
  this is the runtime-enforced analogue of Codex's `-s read-only` sandbox. It is
  documented as NOT an OS-level sandbox (Claude Code sandboxing is unsupported on
  Windows); OS sandboxing/containers remain deferred.
- `--no-session-persistence` keeps the run session-local (mirrors Codex's
  `--ephemeral`).
- `--` terminates option parsing BEFORE the instruction so the variadic
  `--tools <tools...>` option cannot swallow the prompt as another tool name.
  Verified against real v2.1.223 behavior (without `--`, the CLI errors with
  "Input must be provided either through stdin or as a prompt argument").
- Everything travels via `ArgumentList`.

### Minimal JSON envelope adapter — not a new parser

Claude Code's `--output-format json` returns ONE result envelope
(`{"type":"result","is_error":false,"result":"<agent final message>", ...}`).
`ClaudeCodeOutputExtractor` unwraps exactly that documented `result` string and hands
the text to the existing `LlmEvidenceResponseValidator`. It is NOT a new
parser/domain language and never heuristically interprets arbitrary prose: anything
that is not exactly one valid result envelope with a `result` string (including an
`is_error: true` envelope) is a categorized `SchemaValidation` failure — no LLM
repair call, no prose salvage.

### Claude Code's own authentication — no API keys in the Council

Claude Code authenticates through its own login (`claude auth` / the configured
account); the Council never reads, stores, or forwards a Claude credential. There is
no `apiKey` option, and `appsettings.json` documents that credentials live in Claude
Code's own auth. The offline Mock remains the zero-config default.

### Optional, never-guessed model selection

`ClaudeCodeOptions.Model` is optional configuration (e.g. `claude-sonnet-5`, or an
alias like `sonnet`) passed through the safe invocation as `--model <id>`. When
unset, Claude Code uses its own configured/default model — the provider never guesses
or hardcodes a model (the M13.3 convention). When set, the model is stamped as
`Metadata["model"]` telemetry and appears in the success log. `ProviderVersion`
stays `claudecode-adapter-v1` (the adapter's version, not the model's).

### Timeout/cancellation/exit-code semantics match OpenCode/Codex

- **Timeout** → the runner terminates the complete process tree, the provider
  surfaces a `Timeout` categorized failure (existing `ProviderFailureMode`
  semantics).
- **Cancellation** → the runner terminates the complete process tree if running and
  propagates `OperationCanceledException` — never converted into an ordinary provider
  failure.
- **`ExitCode != 0`** → categorized `ProcessError` failure with a bounded stderr
  diagnostic; unlimited stderr is never persisted and diagnostics never expose
  secrets/environment/machine details.
- **Envelope failures** (`is_error`, non-envelope, missing/empty `result`) →
  categorized `SchemaValidation` failure.

### `ContextFingerprint` remains unavailable

Claude Code executions leave `ContextFingerprint = ""`, exactly as ADR-021/022/023 —
the council cannot vouch for the effective context an external agent explored, and a
fingerprint is never fabricated. Agentic executions stay structurally excluded from
M12 controlled-context comparison.

### Configuration is minimal, disabled by default

```json
"Evidence": {
  "ClaudeCode": {
    "Enabled": false,
    "Executable": "claude",
    "TimeoutSeconds": 300,
    "Model": ""
  }
}
```

Explicit selection (`--provider ClaudeCode` or the config provider list) opts in.
The longer default timeout (300 s) reflects that Claude Code cold-start caches a
system prompt (a trivial probe cost ~1.7 s API time, ~30 s total with startup).

## Trade-offs and decisions

- **Copy the runner seam, not the provider.** The OpenCode/Codex/ClaudeCode runners
  share a shape (argv-based, bounded output, complete-tree timeout/cancellation) but
  target different executables with different flags; a shared base class would couple
  three runtimes. The small duplication is the deliberate cost of keeping each
  adapter readable and independently testable.
- **Tool restriction over prompt-only boundary.** Claude Code does not ship Codex's
  `-s read-only` OS-level sandbox (and sandboxing is unsupported on Windows), so the
  runtime boundary is `--tools "Read,Glob,Grep"` (write tools removed from the
  built-in set) PLUS the prompt instruction. Honest, supported, and materially
  stronger than OpenCode's prompt-only boundary; still explicitly not an OS sandbox.
- **`--` before the instruction.** Required because `--tools <tools...>` is variadic
  and would otherwise consume the prompt; verified empirically against v2.1.223.
- **Envelope adapter, not a tolerant parser.** Claude Code's `result` is a plain
  string field; unwrapping it and delegating to the existing validator reuses all
  validation, preserves the no-repair-call rule, and keeps the adapter tiny.
- **No model probing.** Reliable model identity would require extra Claude Code
  machinery; preserving "unknown when not configured" is honest and matches M13.3.

## Consequences

- `--provider ClaudeCode` runs a real external agent end to end: planner → executor
  → Claude Code `-p` process → result envelope → existing `Evidence` → interpreter →
  analyzers → reconciliation → package.
- The engineering domain is unchanged; only the infrastructure/provider layer knows
  Claude Code exists.
- Claude Code executions never carry a fabricated `ContextFingerprint` and are never
  part of M12 comparison.
- The external contract is unchanged: `engineering-review-package.json` stays
  `schemaVersion 1.1` and consumer-compatible (the consumer-DTO gate remains green).
- No API key or credential is ever written to telemetry or artifacts; Claude Code's
  auth stays entirely inside the Claude Code runtime.
- The OpenCode, Codex, and ClaudeCode real-process tests share a NON-PARALLEL xUnit
  collection (`ProcessRunnerTests`) so their short-lived `cmd /c ping`
  orphan-survivor probes cannot observe each other's children.

## Explicitly out of scope (deferred)

Multiple Claude Code models/agents, agent comparison/calibration, tool-call and
files-explored telemetry, command history, full OS sandboxing, containers, VM
isolation, provider ranking/scoring, reconciliation changes, prompt calibration,
parallel execution, and councils. M13.5 proves only the Claude Code runtime adapter.
See MILESTONE-013.5.
