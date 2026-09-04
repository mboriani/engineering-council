# ADR-022 — OpenCode Agentic Adapter

- **Status:** Accepted
- **Date:** 2026-08-08
- **Milestone:** [MILESTONE-013.2](../milestones/MILESTONE-013.2-open-code-process-adapter.md)
- **Builds on:** [ADR-021](./ADR-021-agentic-evidence-contract.md),
  [ADR-005](./ADR-005-evidence-provider-layer.md),
  [ADR-014](./ADR-014-reliability-security-hardening.md)

## Context

ADR-021 established the provider-neutral **Agentic** evidence contract: an external
agent (Codex, Claude Code, opencode, …) explores the repository itself, the council
embeds nothing, and it never fabricates a `ContextFingerprint`. The contract was
demonstrated end to end by a no-network `AgenticEvidenceProvider`.

M13.2 implements the **first real external coding-agent runtime**: OpenCode. The
council launches the `opencode` executable against the local repository, hands it one
discipline-specific read-only analysis instruction, receives structured output, and
converts it into existing `Evidence`. Everything after `Evidence` must remain
unchanged.

The core question is where the process-execution machinery lives and how the boundary
between the external runtime and the engineering domain is drawn.

## Decision

### OpenCode is infrastructure

OpenCode is an **external runtime**. Its existence is confined to the
infrastructure/provider layer (`EngineeringCouncil.Infrastructure.Evidence`). The
downstream domain continues seeing `EvidenceProviderType.Agentic` and normal
`Evidence` — `EngineeringObservation`, `Finding`, analyzers, the reconciler, and
`EngineeringReviewPackage` never learn that OpenCode exists. There is no
`OpenCodeObservation`, `OpenCodeFinding`, or `OpenCodeResult` type.

### One small process runner

`IOpenCodeProcessRunner` / `OpenCodeProcessRunner` are deliberately **not** a generic
shell framework or a general-purpose command executor. Their only responsibilities:

- start the OpenCode executable;
- set the working directory to the analyzed repository root;
- provide the analysis instruction as an argument;
- capture bounded stdout and stderr;
- capture the exit code;
- honor `CancellationToken`;
- enforce a configured timeout.

### No shell string execution

The runner uses `ProcessStartInfo` with `UseShellExecute = false` and passes each
argument through `ProcessStartInfo.ArgumentList` (the .NET equivalent of argv).
Neither the repository path nor the analysis instruction is ever concatenated into a
`cmd /c`, `powershell -Command`, `bash -c`, or `sh -c` string. This avoids the entire
class of quoting/argument-injection problems around a path or prompt containing
special characters.

### Repository root as working directory

The process runs with `WorkingDirectory = analyzed repository root`. The agent is
expected to explore that repository itself. No repository copy, no whole-repository
stdin transfer, and no rebuilding of the M11.3 controlled context. The agentic model
remains: *repository location + analysis task + OpenCode exploration*.

### Instruction-level read-only boundary

`OpenCodePromptBuilder` produces one focused instruction that tells the agent:

- role — engineering evidence acquisition agent (not the reviewer);
- the requested discipline and the existing discipline analyzer instructions;
- the repository is available in the current working directory and is read-only;
- the expected structured output — the SAME `schemaVersion`/`discipline`/`observations`
  envelope the LLM providers use;
- observations/evidence only — never the final Engineering Review, no modifications,
  no commits, no patches.

This is an **instruction-level** boundary only. It is documented as **not** a security
sandbox: the agent runtime itself is not restricted at the OS level. OS-level
sandboxing/containers/VM isolation remain explicitly deferred.

### Structured output, existing validator

The provider accepts only output compatible with the existing structured evidence
contract and reuses `LlmEvidenceResponseValidator` (the same validator the LLM
providers use). There is no OpenCode-specific parser, no prose interpretation, and no
LLM repair call. Malformed structured output is a **categorized provider failure**
(`SchemaValidation`) following the existing failure-isolation semantics (ADR-011.1).

### Timeout and cancellation are distinct

The runner has its **own** timeout (`OpenCodeProcessRequest.Timeout`, from
`Evidence:OpenCode:TimeoutSeconds`, default 120). When it fires, the process is
terminated and the result reports `TimedOut = true`; the provider surfaces it as a
`Timeout` categorized failure, which is `Continue` or `FailRun` per the existing
`ProviderFailureMode` semantics (same as M11.1). A **run/user cancellation** is
different: the runner terminates the process if still running and propagates
`OperationCanceledException` — it is never converted into an ordinary provider
failure. The process is never left intentionally running.

### Exit-code semantics

`ExitCode == 0` → stdout is parsed as the structured result. `ExitCode != 0` → a
categorized `ProcessError` provider failure with a bounded stderr diagnostic
(≤ 8 KiB). Diagnostics never expose secrets, environment variables, or machine
details, and unlimited stderr is never persisted.

### Model identity is unknown

The provider represents **OpenCode**, not a particular model. Which model OpenCode's
own runtime configuration resolves is out of scope; M13.2 does not hardcode DeepSeek
or any model. `ProviderVersion = "opencode-adapter-v1"` reflects the adapter, and model
metadata is deliberately absent rather than guessed. M13.3 configures and validates a
specific model explicitly.

### `ContextFingerprint` remains unavailable

The Engineering Council cannot know the complete effective context an external agent
explored, so OpenCode executions leave `ContextFingerprint = ""` on records and
`Evidence` (success and failure), exactly as ADR-021 established for agentic sources.
A fingerprint is never generated from a repository hash, snapshot files, prompt, or
working directory. Consequently M12 controlled-context comparison does not treat
OpenCode as comparable to Claude/OpenAI API executions — agentic executions are
structurally excluded (`ProviderComparisonBuilder` filters `ProviderType == LLM`).

### Configuration is minimal, disabled by default

```json
"Evidence": {
  "OpenCode": {
    "Enabled": false,
    "Executable": "opencode",
    "TimeoutSeconds": 120
  }
}
```

No API keys and no model configuration in M13.2. Explicit selection
(`--provider OpenCode` or the config provider list) opts in; the offline Mock remains
the zero-config default. The CLI `review` workflow remains authoritative — no new
top-level command.

## Trade-offs and decisions

- **Real runtime now, contract first.** M13.1 defined the contract with a no-network
  demonstration provider; M13.2 proves the same contract with a real external runtime
  while keeping the domain untouched.
- **Process runner over a generic executor.** A general command executor would invite
  shell-string reuse elsewhere; this milestone needs only OpenCode, so the seam is
  small and purpose-built.
- **`ArgumentList` over a formatted string.** Safe by construction; no quoting code to
  get wrong for paths or prompts.
- **Instruction-level read-only.** Cheap and effective against well-behaved agents, but
  explicitly documented as not a security boundary; real sandboxing is deferred so the
  limitation is visible rather than implied.
- **Reuse the LLM validator.** The envelope is identical; a parallel validator would
  drift.
- **No model probing.** Reliable model identity would require extra OpenCode machinery;
  preserving "unknown" is honest and M13.3 is the explicit place to configure/validate
  the model.

## Consequences

- `--provider OpenCode` runs a real external agent end to end: planner → executor →
  OpenCode process → structured JSON → existing `Evidence` → existing interpreter →
  analyzers → reconciliation → package.
- The engineering domain is unchanged; only the infrastructure/provider layer knows
  OpenCode exists.
- OpenCode executions never carry a fabricated `ContextFingerprint` and are never part
  of M12 comparison.
- The external contract is unchanged: `engineering-review-package.json` stays
  `schemaVersion 1.1` and consumer-compatible (the consumer-DTO gate remains green).
- No DeepSeek-specific behavior exists; model identity is deliberately unknown until
  M13.3.

## Explicitly out of scope (deferred)

DeepSeek-specific configuration and API integration, Codex, Claude Code, multiple
OpenCode models, agent comparison/calibration, tool-call and files-explored telemetry,
command history, OS sandboxing, containers, VM isolation, provider ranking/scoring,
reconciliation changes, prompt calibration, parallel execution, and councils. M13.2
proves only the OpenCode runtime adapter. See MILESTONE-013.2.
