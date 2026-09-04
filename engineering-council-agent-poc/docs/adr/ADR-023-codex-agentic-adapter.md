# ADR-023 — Codex Agentic Adapter

- **Status:** Accepted
- **Date:** 2026-08-09
- **Milestone:** [MILESTONE-013.4](../milestones/MILESTONE-013.4-codex-agentic-adapter.md)
- **Builds on:** [ADR-021](./ADR-021-agentic-evidence-contract.md),
  [ADR-022](./ADR-022-open-code-agentic-adapter.md),
  [ADR-005](./ADR-005-evidence-provider-layer.md)

## Context

ADR-021 established the provider-neutral **Agentic** evidence contract and ADR-022
proved it with the first real external coding-agent runtime, **OpenCode**. The
contract is deliberately runtime-agnostic: *repository location + one
discipline-specific read-only analysis instruction + the agent's own exploration,
structured output in the existing `observations` envelope, never a fabricated
`ContextFingerprint`*.

M13.4 adds a **second real agentic runtime**: the OpenAI **Codex CLI**. Codex is
interesting for two reasons: it ships a native, non-interactive `exec` mode with its
own sandbox, and it brings **Codex's own authentication** (ChatGPT account /
`codex login`) — the Council never handles an API key for it. The core question is
how much of the OpenCode adapter to duplicate versus reuse, and how Codex's
invocation semantics map onto the existing runner/telemetry contract.

## Decision

### Same agentic contract, second runtime

Codex is added as another infrastructure/provider-layer agentic source. The domain
(`Evidence`, `EngineeringObservation`, analyzers, reconciler, `EngineeringReviewPackage`)
is untouched. There is no `CodexObservation` or `CodexFinding`. The provider is
`EvidenceProviderType.Agentic`, **Discipline**-scoped, and writes the SAME
`observations` envelope as every other source, validated by the existing
`LlmEvidenceResponseValidator`.

### A purpose-built runner mirroring OpenCode's seam

`ICodexProcessRunner` / `CodexProcessRunner` is deliberately small and mirrors the
OpenCode runner's shape — it is not a generic command executor:

- start the Codex executable;
- set the working directory to the analyzed repository root;
- provide the instruction as the final argument;
- capture bounded stdout and stderr;
- capture the exit code;
- honor `CancellationToken` and enforce a configured timeout.

### No shell string execution

The runner uses `ProcessStartInfo` with `UseShellExecute = false` and passes every
argument through `ProcessStartInfo.ArgumentList` (the .NET equivalent of argv).
Neither the repository path nor the instruction is ever concatenated into a
`cmd /c`, `powershell -Command`, `bash -c`, or `sh -c` string. This preserves the
ADP-022 argument-injection guarantee.

### `codex exec` with the read-only sandbox

The provider invokes Codex's non-interactive mode:

```
codex exec -s read-only --ephemeral --skip-git-repo-check [-m <model>] "<instruction>"
```

- `-s read-only` enforces the read-only boundary **at the Codex sandbox level** (in
  addition to the prompt's instruction-level boundary) — stronger than OpenCode's
  instruction-only boundary.
- `--ephemeral` keeps the run session-local and stateless.
- `--skip-git-repo-check` lets the adapter analyze a plain directory (the fixture
  repos are not necessarily git checkouts). Everything travels via `ArgumentList`.

### Codex's own authentication — no API keys in the Council

Codex authenticates through its own login (ChatGPT account / `codex login`); the
Council never reads, stores, or forwards a Codex credential. There is no
`apiKey` option, and `appsettings.json` documents that credentials live in Codex's
own auth. The offline Mock remains the zero-config default.

### Optional, never-guessed model selection

`CodexOptions.Model` is optional configuration (e.g. `gpt-5.4-mini`) passed through
the safe invocation as `-m <model>`. When unset, Codex uses its own configured/default
model — the provider never guesses or hardcodes a model (the M13.3 convention).
When set, the model is stamped as `Metadata["model"]` telemetry and appears in the
success log. `ProviderVersion` stays `codex-adapter-v1` (the adapter's version, not
the model's).

### Timeout/cancellation/exit-code semantics match OpenCode

- **Timeout** → the runner terminates the process, the provider surfaces a `Timeout`
  categorized failure (existing `ProviderFailureMode` semantics).
- **Cancellation** → the runner terminates the process if running and propagates
  `OperationCanceledException` — never converted into an ordinary provider failure.
- **`ExitCode != 0`** → categorized `ProcessError` failure with a bounded stderr
  diagnostic (≤ 8 KiB); unlimited stderr is never persisted and diagnostics never
  expose secrets/environment/machine details.

### `ContextFingerprint` remains unavailable

Codex executions leave `ContextFingerprint = ""`, exactly as ADR-021/022 — the
council cannot vouch for the effective context an external agent explored, and a
fingerprint is never fabricated. Agentic executions stay structurally excluded from
M12 controlled-context comparison.

### Configuration is minimal, disabled by default

```json
"Evidence": {
  "Codex": {
    "Enabled": false,
    "Executable": "codex",
    "TimeoutSeconds": 120,
    "Model": ""
  }
}
```

Explicit selection (`--provider Codex` or the config provider list) opts in.

## Trade-offs and decisions

- **Copy the runner seam, not the provider.** The OpenCode and Codex runners share
  a shape (argv-based, bounded output, timeout/cancellation) but target different
  executables with different flags; a shared base class would couple two runtimes.
  The small duplication is the deliberate cost of keeping each adapter readable and
  independently testable.
- **Codex sandbox over prompt-only boundary.** `-s read-only` gives a real
  OS-sandbox-level guarantee OpenCode lacked — still documented as not a full
  security sandbox (network egress, etc. are out of scope), but stronger for
  well-behaved and misbehaving agents alike.
- **`--skip-git-repo-check`.** Codex otherwise requires a git repo; the fixture and
  ad-hoc directories are not checkouts, so the flag keeps the adapter usable on any
  directory the scanner accepts.
- **No model probing.** Reliable model identity would require extra Codex machinery;
  preserving "unknown when not configured" is honest and matches M13.3.

## Consequences

- `--provider Codex` runs a real external agent end to end: planner → executor →
  Codex `exec` process → structured JSON → existing `Evidence` → interpreter →
  analyzers → reconciliation → package.
- The engineering domain is unchanged; only the infrastructure/provider layer knows
  Codex exists.
- Codex executions never carry a fabricated `ContextFingerprint` and are never part
  of M12 comparison.
- The external contract is unchanged: `engineering-review-package.json` stays
  `schemaVersion 1.1` and consumer-compatible (the consumer-DTO gate remains green).
- No API key or credential is ever written to telemetry or artifacts; Codex's auth
  stays entirely inside the Codex runtime.
- The OpenCode and Codex real-process tests share a NON-PARALLEL xUnit collection
  (`ProcessRunnerTests`) so their short-lived `cmd /c ping` orphan-survivor probes
  cannot observe each other's children.

## Explicitly out of scope (deferred)

Claude Code adapter, multiple Codex models/agents, agent comparison/calibration,
tool-call and files-explored telemetry, command history, full OS sandboxing,
containers, VM isolation, provider ranking/scoring, reconciliation changes, prompt
calibration, parallel execution, and councils. M13.4 proves only the Codex runtime
adapter. See MILESTONE-013.4.
