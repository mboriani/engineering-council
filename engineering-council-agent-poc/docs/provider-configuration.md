# Provider Configuration

How to configure the evidence sources. **Real LLM providers are disabled by default** —
an unconfigured run stays completely local (Mock / SARIF).

## Environment variables

| Variable | Used by | Purpose |
|----------|---------|---------|
| `ANTHROPIC_API_KEY` | Claude | API key. Name is configurable; the value is read from the environment at call time and is never stored, logged, or written to artifacts. |
| `OPENAI_API_KEY` | OpenAI | Same, for the OpenAI provider. |
| `RUN_CLAUDE_INTEGRATION_TESTS` | tests | Set to `true` (with a key) to enable the opt-in real Claude integration test. |
| `RUN_OPENAI_INTEGRATION_TESTS` | tests | Same, for OpenAI. |
| `EC_CLAUDE_MODEL` / `EC_OPENAI_MODEL` | tests | Optional model override for the integration tests. |

Never commit a key. Never put a key in `appsettings.json` — only the *name* of the
environment variable is configuration.

## appsettings.json

```json
{
  "Evidence": {
    "ProviderFailureMode": "Continue",
    "Execution": { "MaxConcurrency": 1, "ProviderTimeout": 120 },
    "Providers": [ "Mock" ],
    "Context": { "MaximumFiles": 200, "MaximumCharacters": 500000, "MaximumCharactersPerFile": 8000 },

    "Claude": {
      "Enabled": false,
      "ApiKeyEnvironmentVariable": "ANTHROPIC_API_KEY",
      "Model": "claude-sonnet-5",
      "MaxOutputTokens": 8000,
      "TimeoutSeconds": 120,
      "MaxRetries": 2,
      "EnableStructuredRepair": true
    },

    "OpenAI": {
      "Enabled": false,
      "ApiKeyEnvironmentVariable": "OPENAI_API_KEY",
      "Model": "gpt-4o-mini",
      "MaxOutputTokens": 8000,
      "TimeoutSeconds": 120,
      "MaxRetries": 2,
      "EnableStructuredRepair": true
    },

    "Sarif": { "Enabled": true, "Files": [ "./artifacts/results.sarif" ] },

    "OpenCode": {
      "Enabled": false,
      "Executable": "opencode",
      "TimeoutSeconds": 120
    },

    "Codex": {
      "Enabled": false,
      "Executable": "codex",
      "TimeoutSeconds": 120,
      "Model": ""
    },

    "ClaudeCode": {
      "Enabled": false,
      "Executable": "claude",
      "TimeoutSeconds": 300,
      "Model": ""
    }
  }
}
```

| Setting | Meaning |
|---------|---------|
| `Enabled` | Off by default. Selecting the provider explicitly (CLI/config provider list) also opts in. |
| `ApiKeyEnvironmentVariable` | The NAME of the variable holding the key. |
| `Model` | Fully configurable. A code-focused OpenAI model is simply this value — there is no separate "Codex" provider (`Codex` is accepted as an alias for `OpenAI`). |
| `Endpoint` | Optional base URL override (compatible gateways / self-hosted). |
| `MaxOutputTokens`, `TimeoutSeconds`, `MaxRetries` | Per-request limits and the bounded retry budget (transient failures only). |
| `EnableStructuredRepair` | Allows at most ONE structured-output repair attempt. |
| `ProviderFailureMode` | `Continue` (default) or `FailRun` — see below. |
| `Execution:MaxConcurrency` | Bounded parallelism (M15.1): how many INDEPENDENT acquisition steps run concurrently. Default `1` = strictly sequential. A 3-provider Council run sets `3` so OpenCode/Codex/Claude Code overlap (observed ~62% wall-clock reduction on the M14.1 fixture). Results still aggregate in plan order — never reorders or changes artifacts/contract. |
| `Execution:ProviderTimeout` | Global **fallback** per-step timeout in seconds (M15.2B). Default `120`. Used ONLY when a provider has no timeout of its own — a provider's `TimeoutSeconds` always wins (ClaudeCode's 300 s is honored, not cut at 120 s). Bound from config in both CLI and API; per-step timeouts stay independent under MaxConcurrency. |
| `Execution:MaxAttempts` | Bounded acquisition-level retry (M15.3B): max attempts per logical step. Default `2` = one initial attempt + ONE retry of a retry-eligible (Timeout-only) failure; the retry reuses the SAME effective provider timeout, belongs to its logical step (never bypasses `MaxConcurrency`), and never runs a third attempt. `1` disables step-level retry. Clamped to `[1, 5]`. Cancellation, authentication/configuration, schema validation and unexpected errors are NEVER retried. The provider-internal transient retry loop (chat clients, `MaxRetries`) is unchanged and separate. |
| `Evidence:OpenCode:Enabled` | Off by default. Selecting OpenCode explicitly (CLI/config provider list) also opts in. |
| `Evidence:OpenCode:Executable` | The `opencode` executable (a name on PATH or an absolute path). Never concatenated into a shell string — passed via `ProcessStartInfo.ArgumentList`. |
| `Evidence:OpenCode:TimeoutSeconds` | Per-execution timeout (default 120) before the process is terminated. Distinct from run/user cancellation. |
| `Evidence:Codex:Enabled` | Off by default. Selecting Codex explicitly (CLI/config provider list) also opts in. |
| `Evidence:Codex:Executable` | The `codex` executable (a name on PATH or an absolute path). Never concatenated into a shell string — passed via `ProcessStartInfo.ArgumentList`. |
| `Evidence:Codex:TimeoutSeconds` | Per-execution timeout (default 120) before the process is terminated. Distinct from run/user cancellation. |
| `Evidence:Codex:Model` | Optional Codex model id (e.g. `gpt-5.4-mini`) passed as `-m <model>` via `ArgumentList`. Unset/empty ⇒ Codex uses its own configured/default model. Never mandatory, never hardcoded, never a credential. |
| `Evidence:ClaudeCode:Enabled` | Off by default. Selecting ClaudeCode explicitly (CLI/config provider list) also opts in. |
| `Evidence:ClaudeCode:Executable` | The `claude` executable (a name on PATH or an absolute path). Never concatenated into a shell string — passed via `ProcessStartInfo.ArgumentList`. |
| `Evidence:ClaudeCode:TimeoutSeconds` | Per-execution timeout (default 300) before the process is terminated. Distinct from run/user cancellation. |
| `Evidence:ClaudeCode:Model` | Optional Claude model id or alias (e.g. `claude-sonnet-5` / `sonnet`) passed as `--model <id>` via `ArgumentList`. Unset/empty ⇒ Claude Code uses its own configured/default model. Never mandatory, never hardcoded, never a credential. |

### Timeout precedence (Milestone 015.2B)

Each acquisition step's timeout is **per-step and independent**:
`EffectiveTimeout = Provider.TimeoutSeconds` when set, otherwise the global
`Execution:ProviderTimeout` fallback (default 120 s). So a step configured for
ClaudeCode with `TimeoutSeconds: 300` runs up to 300 s even though the global
fallback is 120 s — the old behavior cut every step at the unbound 120 s cap.
A timeout terminates the process tree and is recorded as a `Timeout` failure;
a run/user cancellation propagates as cancellation and is never converted to a
timeout. This precedence is enforced in `EvidenceAcquisitionExecutor` and
covered by offline regression tests (`ProviderTimeoutPrecedenceTests`).

### Bounded retry (Milestone 015.3B)

`Execution:MaxAttempts` (default 2) allows at most ONE retry of a `Timeout`
failure per logical step. The retry reuses the same effective per-step timeout
(no doubling), stays inside the step's `MaxConcurrency` permit, and never runs a
third attempt. Only `Timeout` is retried — cancellation, authentication/
configuration, schema-validation and unexpected errors never are. Telemetry:
`attemptCount`, `retryCount` (step-level + the final attempt's provider-internal
retries) and `retryExhausted` per record in `provider-execution.json` and the
package's `providerExecution.records` (additive; `schemaVersion` stays 1.1).
Coverage (M15.3A) is unaffected: a retried success is successful evidence; an
exhausted retry is `NoEvidence`. Covered by offline regression tests
(`BoundedRetryTests`).

## CLI

```bash
# Scenario A — Claude, one discipline
engineering-council review --provider Claude --discipline Security --path <repo>

# Scenario B — OpenAI, one discipline
engineering-council review --provider OpenAI --discipline Reliability --path <repo>

# Scenario C — Claude + OpenAI + SARIF, two disciplines (5 provider executions)
engineering-council review \
  --provider Claude --provider OpenAI --provider Sarif \
  --sarif ./artifacts/results.sarif \
  --discipline Security --discipline Reliability \
  --path <repo>

# Fully local (no external calls)
engineering-council review --provider Mock --path <repo>
engineering-council review --provider Sarif --sarif ./artifacts/results.sarif --path <repo>

# Agentic source (Milestone 013.1) — no-network demonstration provider
engineering-council review --provider Agentic --path <repo>

# OpenCode agentic source (Milestone 013.2) — a real external coding-agent runtime
engineering-council review --provider OpenCode --disciplines Security --path <repo>

# Codex agentic source (Milestone 013.4) — a real external coding-agent runtime
engineering-council review --provider Codex --disciplines Security --path <repo>

# Claude Code agentic source (Milestone 013.5) — a real external coding-agent runtime
engineering-council review --provider ClaudeCode --disciplines Security --path <repo>
```

`--provider` and `--discipline` are repeatable; `--providers a,b` and `--disciplines a,b`
remain supported.

## Agentic sources (Milestone 013.1)

`--provider Agentic` runs a no-network **agentic evidence source**: an external
agent that explores the repository itself. The council hands it the repository
**identity and structure** (root, solution, branch/commit, file paths) and the
discipline instructions — but **not** the repository file contents and **not** a
context fingerprint (the council cannot vouch for what the agent sees). The agent
returns the same structured `observations` envelope as the LLM providers, which
flows through the existing interpreter. Agentic sources are **discipline-scoped**
(one execution per selected discipline).

No configuration or environment variable is required for the no-network `Agentic`
provider. A real agentic adapter (Codex / Claude Code / opencode) uses the identical
contract — see below for the OpenCode (Milestone 013.2), Codex (Milestone 013.4),
and Claude Code (Milestone 013.5) adapters.

## OpenCode (Milestone 013.2)

`--provider OpenCode` runs a **real external coding-agent runtime**. The council
launches the `opencode` executable against the **repository root** (the working
directory), hands it **one discipline-specific read-only analysis instruction**, and
converts its structured JSON into the same `observations` envelope as every other
source (validated with the shared validator). OpenCode is **disabled by default**;
selecting it opts in, and the only remaining blocker is the executable being installed
/ on PATH.

| Setting | Meaning |
|---------|---------|
| `Enabled` | Off by default. Selecting OpenCode explicitly (CLI/config provider list) also opts in. |
| `Executable` | The `opencode` executable (name on PATH or absolute path). Default `opencode`. |
| `TimeoutSeconds` | Per-execution timeout before the process is terminated (default 120). |
| `Model` | Optional explicit model in OpenCode's `provider/model` format (e.g. `deepseek/deepseek-v4-flash-free`), passed as `--model <id>` via `ArgumentList`. Unset/empty ⇒ OpenCode uses its own default. Never mandatory, never guessed, never a credential (keys live in OpenCode's own auth, e.g. `DEEPSEEK_API_KEY`). |

Safety properties:

- **No shell strings.** The process runs with `UseShellExecute = false` and
  `ArgumentList` (`["run", instruction]`); the repository path and the instruction are
  never concatenated into a `cmd /c` / `sh -c` string.
- **Read-only is instruction-level.** The prompt tells the agent it may list/read/search
  files but must NOT modify/create/delete files, run formatting, commit, or generate
  patches. This is **not** an OS-level security sandbox — sandboxing/containers remain
  deferred.
- **Timeout vs cancellation.** A timeout terminates the process and is recorded as a
  `Timeout` failure (honoring `ProviderFailureMode`); a run/user cancellation
  terminates the process and propagates as cancellation — never an ordinary failure,
  and the process is never left running intentionally.
- **Exit codes.** `0` → stdout is parsed as the structured result; non-zero → a
  categorized `ProcessError` failure with a bounded stderr diagnostic (no secrets,
  environment variables, or machine details).
- **No fingerprint.** OpenCode is agentic: `ContextFingerprint` stays
  unavailable (the council cannot vouch for the agent's effective context), so it is
  never compared to Claude/OpenAI API executions. Model identity is optional
  configuration only (M13.3); no API keys are involved.
- **Token usage (M15.2A).** The captured `opencode run` stdout is the final
  assistant text only — no machine-readable usage — so OpenCode executions report
  token usage as **unknown (null, never 0)**. Documented gap; no usage is invented.

```bash
# OpenCode, one discipline, over a local repository
engineering-council review --provider OpenCode --disciplines Security --path <repo>
```

## Codex (Milestone 013.4)

`--provider Codex` runs a **second real external coding-agent runtime** (the OpenAI
Codex CLI). The council launches the `codex` executable against the **repository
root** (the working directory), hands it **one discipline-specific read-only analysis
instruction**, and converts its structured JSON into the same `observations` envelope
as every other source (validated with the shared validator). Codex is **disabled by
default**; selecting it opts in, and the only remaining blocker is the executable
being installed / on PATH and Codex being logged in.

| Setting | Meaning |
|---------|---------|
| `Enabled` | Off by default. Selecting Codex explicitly (CLI/config provider list) also opts in. |
| `Executable` | The `codex` executable (name on PATH or absolute path). Default `codex`. |
| `TimeoutSeconds` | Per-execution timeout before the process is terminated (default 120). |
| `Model` | Optional Codex model id (e.g. `gpt-5.4-mini`), passed as `-m <model>` via `ArgumentList`. Unset/empty ⇒ Codex uses its own configured/default model. Never mandatory, never guessed, never hardcoded. |

Codex invocation: `codex exec -s read-only --ephemeral --skip-git-repo-check
[-m <model>] "<instruction>"`.

Safety properties:

- **No shell strings.** The process runs with `UseShellExecute = false` and
  `ArgumentList`; the repository path and the instruction are never concatenated into
  a `cmd /c` / `sh -c` string.
- **Read-only is sandboxed.** `-s read-only` enforces the read-only boundary at the
  **Codex sandbox level** (in addition to the prompt's instruction-level boundary).
  This is still **not** a full OS-level security sandbox (network egress, containers,
  VM isolation remain deferred).
- **Codex owns its own auth.** Codex authenticates through its own login (ChatGPT
  account / `codex login`). No API key is configured or read by the council — set
  nothing here; just `codex login` once in the environment.
- **Timeout vs cancellation.** A timeout terminates the process and is recorded as a
  `Timeout` failure (honoring `ProviderFailureMode`); a run/user cancellation
  terminates the process and propagates as cancellation — never an ordinary failure,
  and the process is never left running intentionally.
- **Exit codes.** `0` → stdout is parsed as the structured result; non-zero → a
  categorized `ProcessError` failure with a bounded stderr diagnostic (no secrets,
  environment variables, or machine details).
- **No fingerprint.** Codex is agentic: `ContextFingerprint` stays unavailable, so it
  is never compared to Claude/OpenAI API executions.
- **Token usage (M15.2A).** The captured `codex exec` formatted stdout is the final
  message only — no machine-readable usage — so Codex executions report token usage
  as **unknown (null, never 0)**. Documented gap; no usage is invented.

```bash
# Codex, one discipline, over a local repository (authenticate first: codex login)
engineering-council review --provider Codex --disciplines Security --path <repo>
```

## Claude Code (Milestone 013.5)

`--provider ClaudeCode` runs a **third real external coding-agent runtime** (the
Claude Code CLI). The council launches the `claude` executable against the
**repository root** (the working directory), hands it **one discipline-specific
read-only analysis instruction**, and converts its structured JSON (wrapped in the
CLI's own `--output-format json` result envelope) into the same `observations`
envelope as every other source (validated with the shared validator). Claude Code is
**disabled by default**; selecting it opts in, and the only remaining blocker is the
executable being installed / on PATH and Claude Code being logged in.

| Setting | Meaning |
|---------|---------|
| `Enabled` | Off by default. Selecting ClaudeCode explicitly (CLI/config provider list) also opts in. |
| `Executable` | The `claude` executable (name on PATH or absolute path). Default `claude`. |
| `TimeoutSeconds` | Per-execution timeout before the process is terminated (default 300 — Claude Code cold-starts a cached system prompt). |
| `Model` | Optional Claude model id or alias (e.g. `claude-sonnet-5` / `sonnet`), passed as `--model <id>` via `ArgumentList`. Unset/empty ⇒ Claude Code uses its own configured/default model. Never mandatory, never guessed, never hardcoded. |

Claude Code invocation: `claude -p --output-format json --no-session-persistence
--tools "Read,Glob,Grep" [--model <model>] -- "<instruction>"`.

Safety properties:

- **No shell strings.** The process runs with `UseShellExecute = false` and
  `ArgumentList`; the repository path and the instruction are never concatenated into
  a `cmd /c` / `sh -c` string.
- **Read-only is tool-restricted.** `--tools "Read,Glob,Grep"` is the CLI's own
  supported read-only mechanism: write-capable built-in tools (Edit/Write/Bash/
  MultiEdit) are removed from the available toolset (the prompt's instruction-level
  boundary is the second layer). This is **not** an OS-level security sandbox —
  Claude Code sandboxing is unsupported on Windows; OS sandboxing/containers remain
  deferred.
- **`--` before the instruction.** `--tools <tools...>` is a **variadic** option, so
  `--` terminates option parsing before the prompt (without it the CLI errors "Input
  must be provided either through stdin or as a prompt argument").
- **Claude Code owns its own auth.** Claude Code authenticates through its own login
  (`claude auth` / the configured account). No API key is configured or read by the
  council — set nothing here; just `claude auth` once in the environment.
- **Timeout vs cancellation.** A timeout terminates the process (and its complete
  process tree) and is recorded as a `Timeout` failure (honoring
  `ProviderFailureMode`); a run/user cancellation terminates the process tree and
  propagates as cancellation — never an ordinary failure, and nothing is left running.
- **Exit codes.** `0` → the CLI's result envelope is unwrapped (one documented
  `result` field) and parsed as the structured result; non-zero → a categorized
  `ProcessError` failure with a bounded stderr diagnostic (no secrets, environment
  variables, or machine details). An error/empty/malformed envelope is a categorized
  `SchemaValidation` failure.
- **No fingerprint.** Claude Code is agentic: `ContextFingerprint` stays unavailable,
  so it is never compared to Claude/OpenAI API executions. Naming: `ClaudeCode`
  (agentic CLI runtime) is a DISTINCT provider from `Claude` (direct Anthropic API
  provider).
- **Token usage (M15.2A / M15.2C).** The `--output-format json` result envelope
  carries the CLI's own authoritative session usage (`usage.input_tokens` /
  `usage.output_tokens`, cumulative for the whole call). ClaudeCode executions
  report `InputTokens`/`OutputTokens`/`TokensUsed` + run totals
  (`TotalInputTokens`/`TotalOutputTokens`/`TotalTokens`/`KnownTokenExecutionCount`)
  in `provider-execution.json`. M15.2C also preserves the envelope's cache
  categories — `CacheReadInputTokens`/`CacheCreationInputTokens` per execution and
  `TotalCacheReadInputTokens`/`TotalCacheCreationInputTokens` at run level (same
  `SumKnown` semantics). M15.2D derives provider-neutral activity metrics from
  those raw fields: `ContextTokenActivity` = SumKnown(Input, CacheCreation,
  CacheRead), `FreshContextTokens` = SumKnown(Input, CacheCreation), and
  `CacheReuseRatio` = CacheRead / ContextTokenActivity (+ run-level totals; the
  run-level ratio uses aggregate totals, never an average). **`TotalTokens` still
  means input + output only and never includes cache tokens; the derived metrics
  are token-activity accounting, not cost or billable usage.** Envelopes without
  `usage` (or non-numeric values) report unknown (null, never 0). Cache and
  derived telemetry are operational only — they never appear in
  `engineering-review-package.json` (`schemaVersion` stays 1.1), and
  `total_cost_usd` is still deliberately NOT mapped.

```bash
# Claude Code, one discipline, over a local repository (authenticate first: claude auth)
engineering-council review --provider ClaudeCode --disciplines Security --path <repo>
```

## Execution topology

LLM providers are **discipline-scoped**: one execution per provider per selected
discipline. Agentic sources (Agentic, OpenCode, Codex, ClaudeCode) are also
**discipline-scoped** (one execution per discipline). SARIF and other
repository-scoped sources execute **once**.

```
Claude + OpenAI × {Security, Reliability} + SARIF
  → Claude/Security, Claude/Reliability, OpenAI/Security, OpenAI/Reliability, SARIF/Repository
  → 5 provider executions
```

## Failure modes

| Mode | Behaviour |
|------|-----------|
| `Continue` (default) | A provider that cannot execute is recorded as a failed step with a categorized, secret-safe reason; the run continues with the remaining providers. |
| `FailRun` | Any selected provider that cannot execute fails the whole run. |

A missing key is reported before analysis:

```
Provider configuration error:
  Claude requires the environment variable ANTHROPIC_API_KEY.
```

Retries apply to transient failures only (rate limit, timeout, network, 5xx) with bounded
exponential backoff. Authentication failures, invalid requests, unsupported models and
schema-validation failures are never retried. A provider **timeout** is distinguished from
run/user **cancellation** by token ownership: timeouts are retried within `MaxRetries`,
while cancellation propagates immediately and is never retried (see ADR-014).

## Structured output

Every LLM provider is asked for the same provider-neutral schema and it is validated
before interpretation:

```json
{
  "schemaVersion": "1.0",
  "discipline": "Security",
  "observations": [
    { "type": "HardcodedSecret", "discipline": "Security", "title": "…", "description": "…",
      "severity": "High", "confidence": "Medium",
      "fileReferences": [ { "path": "src/A.cs", "startLine": 3 } ] }
  ]
}
```

Rejected: invalid/unbalanced JSON, missing or unsupported `schemaVersion`, missing or
mismatched `discipline`, missing `observations`, invalid severity/confidence, file
references without a path, truncated output, multiple JSON documents, prose-wrapped output.
One repair attempt may be made; otherwise the step is recorded as failed.

## Cost

Token usage is recorded when the provider reports it. Cost estimation is optional and
**not** hardcoded: no pricing lives in domain logic, and the external consumer must not
depend on cost fields being present.

## Integration tests

```bash
RUN_CLAUDE_INTEGRATION_TESTS=true ANTHROPIC_API_KEY=... dotnet test
RUN_OPENAI_INTEGRATION_TESTS=true OPENAI_API_KEY=...    dotnet test
```

Without these flags the tests are inert — the normal suite never needs internet,
credentials, or paid calls.

## Targeted semantic reconciliation (Milestone 014.4)

An OPTIONAL, narrowly-scoped review step for the small subset of consolidated
findings a deterministic Council assessment flags with an `ObservationType`
disagreement (see `docs/adr/ADR-025-targeted-semantic-reconciliation-boundary.md`).
**Disabled by default** — enabling it means selected finding-scoped context (never
the repository) is sent to the configured chat client.

```json
{
  "Council": {
    "SemanticReconciliation": {
      "Enabled": false,
      "Model": "claude-sonnet-5",
      "MaxOutputTokens": 300,
      "TimeoutSeconds": 60
    }
  }
}
```

| Setting | Meaning |
|---------|---------|
| `Council:SemanticReconciliation:Enabled` | Off by default. Only findings whose Council assessment flags an `ObservationType` disagreement are ever reviewed. |
| `Council:SemanticReconciliation:Model` | Model id for the real (LLM-backed) reviewer. Never hardcoded; reuses the same generic chat-transport seam as the Claude/OpenAI providers. |
| `Council:SemanticReconciliation:MaxOutputTokens` / `TimeoutSeconds` | Per-review limits. No retries, no repair — a single attempt; any failure degrades to an `Inconclusive` result and never fails the run. |
