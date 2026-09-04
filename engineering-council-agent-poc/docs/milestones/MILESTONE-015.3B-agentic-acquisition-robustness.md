# MILESTONE-015.3B — Agentic Acquisition Robustness (Bounded Retry)

Date: 2026-08-18 · Decision: `D-106` · No ADR (executor-internal bounded policy)

## Context

The first real M15.2 evaluation (`20260813-230240-b3e36e`, RedirectToService)
failed **Architecture 0/3 and Testing 0/3** — every agentic step timed out at the
then-120 s hard cap. M15.2B fixed timeout *precedence* (per-provider
`TimeoutSeconds` wins) and the M15.3A follow-up run (`20260818-223806-aa8b1e`
was a *fresh* run after raising timeouts) showed the timeouts themselves were the
problem: with OpenCode/ClaudeCode at 300 s and Codex at 180 s, all six steps
succeeded on their **first** attempt (OpenCode took 184–215 s — the old 120 s cap
would have killed it).

M15.3B adds the **second** robustness layer: a **bounded acquisition-level retry**
so a slow-but-healthy step that *does* hit its effective timeout once can recover,
without exponential/adaptive/background retry or an unbounded third attempt.

## What changed

### Policy (the core of the milestone)

A logical acquisition step gets **at most one retry**
(`Evidence:Execution:MaxAttempts`, default **2**, clamped to `[1, 5]`):

- **Retry-eligible:** `Timeout` — whether executor-detected (the step's linked
  timeout CTS fired) or provider-reported (`LlmProviderException(Timeout)`, e.g. an
  agentic process that exceeded its own bound).
- **Never retried:** run/user cancellation (propagates as `OperationCanceledException`,
  never becomes a Timeout/Failure/RetryExhausted); `Authentication`, `Configuration`,
  `SchemaValidation`, `ProcessError` and any unexpected error (deterministic — retrying
  cannot help); success.
- The retry **reuses the same effective provider timeout** (M15.2B
  `EffectiveTimeout = Provider.TimeoutSeconds ?? Execution:ProviderTimeout`) — no
  doubling, no adaptive growth.
- The retry **belongs to its logical step**: same provider/request, held under the
  step's `MaxConcurrency` permit (never a second parallel slot, never a third attempt).
- **Token telemetry is never fabricated:** usage is lifted only from the successful
  final response; unknown stays `null` (never 0). This is unchanged.

### Telemetry

`Evidence` and `ProviderExecutionRecord` gain two additive fields:

- `AttemptCount` (default 1) — attempts actually made for the logical step.
- `RetryExhausted` (default false) — true ONLY when the final attempt failed with a
  retry-eligible (Timeout) failure on the last allowed attempt ("wanted to retry, had
  none left"). Never true for a success or for a policy-prohibited failure.

`RetryCount` semantics are preserved and extended: it now counts the **bounded
step-level retries plus the FINAL attempt's provider-internal transient retries**
(0 = first attempt succeeded). The failed attempt's own internal retries are not
double-counted — the final attempt's telemetry is authoritative. The existing
provider-internal retry loop inside the Claude/OpenAI chat clients
(`LlmProviderException.IsTransient`: RateLimit/Timeout/Network/ServerError,
`MaxRetries`) is **unchanged** — this milestone reuses it, it does not build a
second subsystem.

### Configuration

New `Evidence:Execution:MaxAttempts` (default 2, clamp 1..5) bound in:

- `EvidenceOptions.MaxAttempts` (Core) + `CouncilOptions.MaxAttempts` (DI surface,
  mapped in `CouncilServiceCollectionExtensions`);
- CLI (`CliArgs.ParseMaxAttempts`) and API (`EvidenceConfig.ParseMaxAttempts`);
- shipped `appsettings.json` in **both** the CLI and API projects.

`1` disables step-level retry; the executor clamps to
`EvidenceOptions.MaxAttemptsLimit` (5).

### Coverage interaction (M15.3A stays authoritative)

No special-casing: a retried success is successful evidence; an exhausted retry is
`NoEvidence`. `DisciplineCoverage.From` reads the same records it always did
(`AttemptCount`/`RetryExhausted` never influence it).

## Files changed

| File | Change |
|------|--------|
| `src/EngineeringCouncil.Core/Abstractions/EvidenceOptions.cs` | `MaxAttempts` (default 2) + `DefaultMaxAttempts`/`MaxAttemptsLimit` consts |
| `src/EngineeringCouncil.Core/Domain/Evidence.cs` | `AttemptCount`, `RetryExhausted` (additive) |
| `src/EngineeringCouncil.Core/Domain/ProviderExecution.cs` | `AttemptCount`, `RetryExhausted` on `ProviderExecutionRecord` (additive) |
| `src/EngineeringCouncil.Infrastructure/Acquisition/EvidenceAcquisitionExecutor.cs` | Bounded retry loop around `CollectAsync`; `Failure`/`Record` stamp the new telemetry |
| `src/EngineeringCouncil.Infrastructure/DependencyInjection/CouncilServiceCollectionExtensions.cs` | `CouncilOptions.MaxAttempts` + mapping |
| `src/EngineeringCouncil.Cli/Program.cs` | `ParseMaxAttempts` + binding + help text |
| `src/EngineeringCouncil.Api/Program.cs` | `ParseMaxAttempts` + binding |
| `src/EngineeringCouncil.Cli/appsettings.json`, `src/EngineeringCouncil.Api/appsettings.json` | `MaxAttempts: 2` |
| `src/EngineeringCouncil.Tests/BoundedRetryTests.cs` | **17 new offline tests** |

## Tests

Build 0/0 warnings/errors; full suite **541/541 tests** (524 existing + 17 new).
New `BoundedRetryTests.cs` (offline, deterministic, short) covers:

success-first-attempt telemetry; executor-detected timeout → retry → success;
provider-reported timeout → retry → success; timeout+timeout → `RetryExhausted=true`;
never a third attempt; `MaxAttempts=1` disables retry; the retry reuses the SAME
effective timeout (no doubling); authentication/configuration/schema-validation/
unexpected-error never retried; run cancellation never retried; the retry stays
inside its step's `MaxConcurrency` permit; provider-internal retries counted
alongside step retries; retried success = successful coverage, exhausted =
`NoEvidence`; config defaults + shipped CLI/API keys bound.

## Live validation (2026-08-18)

### Council run `20260818-223806-aa8b1e` (Architecture + Testing, OpenCode+Codex+ClaudeCode, MaxConcurrency 3)

Real run against `RedirectToService` (122 files, 6 projects, `master @
4a7d279aaa99`) — the same target that was **0/3** in M15.2. Effective timeouts:
OpenCode 300 s, Codex 180 s, ClaudeCode 300 s; `MaxAttempts=2`.

| Step | Provider | Discipline | Attempts | Retries | Result | Duration | Effective timeout |
|------|----------|------------|:--------:|:-------:|--------|----------|-------------------|
| OpenCode#Architecture | OpenCode | Architecture | 1 | 0 | **success** (8 obs) | 03:04.0 | 300 s |
| OpenCode#Testing | OpenCode | Testing | 1 | 0 | **success** (8 obs) | 03:34.7 | 300 s |
| Codex#Architecture | Codex | Architecture | 1 | 0 | **success** (5 obs) | 02:15.4 | 180 s |
| Codex#Testing | Codex | Testing | 1 | 0 | **success** (4 obs) | 02:54.9 | 180 s |
| ClaudeCode#Architecture | ClaudeCode | Architecture | 1 | 0 | **success** (5 obs) | 01:51.1 | 300 s |
| ClaudeCode#Testing | ClaudeCode | Testing | 1 | 0 | **success** (6 obs) | 02:47.6 | 300 s |

- **6/6 logical steps succeeded, 0 failures, 0 timeouts.** No retry was needed — but
  the run proves the retry policy is **live and wired**: every package/execution
  record carries `attemptCount: 1`, `retryCount: 0`, `retryExhausted: false`.
- **Coverage (M15.3A):** Architecture `coveredWithFindings` 3/3; Testing
  `coveredWithFindings` 3/3 — both disciplines flipped from **NoEvidence (0/3)** in
  M15.2 to **CoveredWithFindings (3/3)**.
- Findings: 8 consolidated (Architecture 6, Testing 2; C0/H2/M4/L1), 4 multi-provider
  agreements, 0 contradictions.
- Tokens: `TotalTokens 28836` (2 known ClaudeCode executions); cache read
  1,871,211 / creation 127,643 / `cacheReuseRatio 0.935` (activity accounting only).
- Acquisition wall-clock ≈ 6 m 23 s (19:38:06 → 19:44:29).
- Cost/time report: **LogicalSteps 6 · PhysicalAttempts 6 · RetriedSteps 0 ·
  SuccessfulRetries 0 · ExhaustedRetries 0.** No monetary cost.
- Package `schemaVersion` **1.1**; `providerExecution.records` carry the new
  telemetry (additive); no per-record timeout leak; no orphan processes; no
  credentials/secret text.

## Exit criteria

| Criterion | Result |
|-----------|--------|
| One bounded retry (max 2 attempts), never a third | ✅ + regression-tested |
| Only Timeout retried (cancellation/auth/config/schema/unknown never) | ✅ + regression-tested |
| Retry reuses the SAME effective provider timeout | ✅ + regression-tested |
| Cancellation propagates, never retried / never a failure | ✅ + regression-tested |
| Telemetry `AttemptCount`/`RetryCount`/`RetryExhausted` with defined semantics | ✅ + regression-tested |
| Token usage never fabricated; unknown stays null | ✅ (unchanged path) |
| M15.3A coverage authoritative (retried success = evidence, exhausted = NoEvidence) | ✅ + regression-tested |
| `MaxConcurrency` preserved; retry stays inside its step's permit | ✅ + regression-tested |
| `Evidence:Execution:MaxAttempts=2` bound in CLI + API + appsettings (both) | ✅ + regression-tested |
| Real run obtains evidence for both disciplines without uncontrolled retries | ✅ 6/6 success, 0 retries needed |
| Package `schemaVersion` 1.1, no retry-internals leak beyond existing retry telemetry | ✅ |
| Offline tests green (build 0/0, 541/541) | ✅ |
| No ADR (executor-internal policy, no architectural boundary change) | ✅ |

## Known gaps (documented, not fixed)

- The step-level retry is deliberately **Timeout-only** (conservative). Transient
  `RateLimit`/`Network`/`ServerError` remain handled **inside** the chat clients only;
  a step-level retry is not added for them (deterministic failures are never retried).
- Exponential/adaptive backoff and unbounded retries are explicit non-goals.
- Per-attempt sub-durations are not recorded in metadata; `Duration` covers the whole
  logical step (sum of attempts) so run reports stay comparable to M15.2.