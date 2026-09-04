# MILESTONE-015.2B — Agentic Provider Timeout Precedence Fix

- **Status:** ✅ Complete
- **Date:** 2026-08-18
- **Builds on:** [MILESTONE-015.2A](./MILESTONE-015.2A-agentic-token-usage-telemetry.md)
  (which identified the blocker live), [MILESTONE-015.1](./MILESTONE-015.1-parallel-agent-execution.md)
- **ADR:** none — a configuration-bound default + per-step timeout precedence
  inside the existing acquisition executor; no contract or architecture decision
  changed.

## Goal

Fix the acquisition blocker proven in M15.2A's live validation: the executor's
hard per-step cap `EvidenceOptions.ProviderTimeout` (default 120 s) was **not
bound from configuration**, so every real agentic step was cut at 120 s
regardless of each provider's own `TimeoutSeconds` (ClaudeCode is configured
300 s). After this milestone:

1. **Per-provider `TimeoutSeconds` wins** at the acquisition boundary (the
   configured intent), and
2. **`Evidence:Execution:ProviderTimeout` is a real, config-bound fallback**
   used only when a provider has no timeout of its own,
3. timeout / run-cancellation / failure remain three distinct outcomes,
4. MaxConcurrency behavior is untouched,
5. leftover `[CP]` diagnostic probes are gone.

## Root cause (from M15.2A)

`EvidenceOptions.ProviderTimeout` (default 120 s) was the executor's cap but the
CLI/API constructed `EvidenceOptions` without binding it
(`CouncilServiceCollectionExtensions` built it from other options only), so
`Evidence:Execution:ProviderTimeout` had no effect. Each provider's
`TimeoutSeconds` (bound into provider options) reached the process runners, but
the executor's `CancelAfter(_options.ProviderTimeout)` always overrode them —
ClaudeCode's configured 300 s never got a chance. The M15.2A real run
(`20260818-172513-2cb592`) timed out all three providers at exactly 00:02:00.

## Changes

### Timeout precedence (`EvidenceAcquisitionExecutor.cs`)

The executor now derives the per-step timeout once:

```csharp
var stepTimeout = provider.Metadata.Timeout ?? _options.ProviderTimeout;
```

- `timeoutCts.CancelAfter(stepTimeout)` and the timeout log/failure message
  ("Timed out after {stepTimeout}.") both use `stepTimeout`.
- A provider with an explicit `TimeoutSeconds` uses it; otherwise the step falls
  back to `EvidenceOptions.ProviderTimeout` (default 120 s).
- Cancellation semantics are unchanged and were already correct: the timeout
  CTS is linked to the run token, and on `OperationCanceledException` the
  executor reports `ErrorCategory = Timeout` only when the timeout fired while
  the run token was NOT cancelled; genuine run cancellation propagates as
  `OperationCanceledException` and never becomes a timeout/failure.

### Timeout carrier (`EvidenceProviderMetadata.cs` + the three agentic providers)

`EvidenceProviderMetadata` gains `public TimeSpan? Timeout { get; init; }`.
`OpenCodeEvidenceProvider`, `CodexEvidenceProvider`, and `ClaudeCodeEvidenceProvider`
each set `Timeout = _options.Timeout` in their metadata, so the executor gets the
effective per-provider timeout without knowing provider names (no branching).

### Configuration binding

- `CouncilOptions.ProviderTimeout { get; set; } = TimeSpan.FromSeconds(120)` and
  the `EvidenceOptions` DI construction now passes `ProviderTimeout =
  options.ProviderTimeout` (`CouncilServiceCollectionExtensions.cs`).
- CLI: `CliArgs.ParseProviderTimeout(context.Configuration)` (seconds, default
  120) wired into `o.ProviderTimeout` (`Cli/Program.cs`); help line added.
- API: `EvidenceConfig.ParseProviderTimeout(builder.Configuration)` wired the
  same way (`Api/Program.cs`).
- `src/EngineeringCouncil.Cli/appsettings.json` and
  `src/EngineeringCouncil.Api/appsettings.json` ship `"Execution": { ..., "ProviderTimeout": 120 }`.

### `[CP]` diagnostic probe removal

- `Cli/Program.cs` (~line 109-111): the temporary OpenCode-options diagnostic
  (`ocOptions` + `Console.Error.WriteLine`) is removed.
- `AnalysisPipeline.cs`: the `Cp` helper method and all 6 call sites
  (`AFTER_SCAN`, `BEFORE_ACQUISITION_PLAN`, `BEFORE_PROVIDER_RESOLUTION`,
  `AFTER_ACQUISITION_PLAN`, `BEFORE_ACQUISITION_EXECUTOR`, `AFTER_ACQUISITION`)
  are removed. Grep confirms zero `[CP]` / `Cp(` references remain.

No contract change: `engineering-review-package.json` stays `schemaVersion 1.1`
and carries NO timeout field (verified by test + live run). No new artifact, no
new telemetry model, no ADR.

## Tests

New `ProviderTimeoutPrecedenceTests.cs` (11 tests, fully offline — scripted fake
runners, `TaskCompletionSource`/short delays, no network/credentials/paid
calls):

1. Provider-specific timeout (500 ms) overrides the global fallback (150 ms) →
   a 250 ms step succeeds.
2. Global `ProviderTimeout` is used when a provider has no own timeout (null →
   150 ms global → 250 ms step times out).
3. OpenCode effective timeout (90 s) propagates via metadata.
4. Codex effective timeout (90 s) propagates via metadata.
5. ClaudeCode effective timeout (300 s) propagates via metadata.
6. A provider without its own timeout exposes null metadata `Timeout` (so the
   executor falls back).
7. Provider-specific timeout produces `ErrorCategory = Timeout` (150 ms provider
   under a 500 ms global; asserts category AND duration < 350 ms — proves the
   provider's tighter timeout, not the global, fired).
8. Run cancellation stays cancellation (never converted to timeout/failure).
9. Parallel steps keep independent timeout values (short step 300 ms times out
   while a long-step provider 2000 ms succeeds under MaxConcurrency 2; both run
   concurrently — `maxActive == 2`).
10. Provider timeout does not leak into the external package contract (full
    pipeline; `schemaVersion` 1.1; records carry no `timeout`/`timeoutSeconds`
    property).
11. `ProviderTimeout` defaults (EvidenceOptions/CouncilOptions 120 s) and the
    shipped CLI + API config keys (`ProviderTimeout: 120`) are bound.

Build 0/0 warnings/errors; full suite **479/479 tests pass** (11 new + 468
existing green).

## Live validation (2026-08-18)

### Council run `20260818-181449-72ca35` (Security, OpenCode+Codex+ClaudeCode, MaxConcurrency 3)

Real run against `RedirectToService` (122 files, 6 projects, `master @
4a7d279aaa99`), the same target that timed out 0/3 in M15.2A:

| Provider | Effective timeout | Duration | Result | InputTokens | OutputTokens | TokensUsed |
|----------|-------------------|----------|--------|:-----------:|:------------:|:----------:|
| OpenCode  | 120 s (own)       | 00:02:00.03 | timeout (`Timeout`) | null | null | null |
| Codex     | 120 s (own)       | 00:01:44.24 | **success** (7 obs) | null | null | null |
| ClaudeCode | 300 s (own)      | 00:03:18.56 | **success** (8 obs) | 179 | 17605 | 17784 |
| **Run**   | —                 | acquisition ≈ 3 m 19 s wall-clock | **2/3 success** | `TotalInputTokens: 179` | `TotalOutputTokens: 17605` | `TotalTokens: 17784` |

- **Acceptance met:** ClaudeCode (configured 300 s) ran **198.6 s** and
  completed — the old 120 s hard cap would have terminated it (as it did in
  M15.2A). Codex also completed under its own 120 s. OpenCode alone hit its own
  120 s timeout in this environment (0 evidence, `ErrorCategory = Timeout`,
  honest null tokens).
- Run facts: 2 evidence items → 15 observations → 8 security findings
  (C0/H1/M6/L1), 2 multi-provider agreements, 0 contradictions, 0 retries, 0
  repairs; `knownTokenExecutionCount: 1`; one package `schemaVersion` **1.1**
  with **no timeout field** in `providerExecution.records`; no credential/secret
  text in any artifact (verified: only benign finding prose quoting
  "authorization"/"api_key" as analysis subjects, no `sk-`/Bearer/token-shaped
  strings); no orphan provider processes after the run.
- ClaudeCode cache metadata: the envelope's `cache_read_input_tokens` /
  `cache_creation_input_tokens` are observed only (not persisted/mapped — M15.2A
  scope); this run's mapped counts are the 179/17605 above.

## Exit criteria

| Criterion | Result |
|-----------|--------|
| Provider-specific `TimeoutSeconds` wins over the global cap | ✅ (ClaudeCode 300 s ran 198.6 s; Codex 120 s ran 104 s) |
| `Evidence:Execution:ProviderTimeout` bound from config in CLI + API | ✅ (default 120 s, shipped in both appsettings) |
| Global value used only as fallback when provider has no own timeout | ✅ + regression-tested (test 2) |
| Timeout / cancellation / failure stay distinct | ✅ + regression-tested (tests 7-8) |
| MaxConcurrency unchanged (per-step timeouts, one bound) | ✅ + regression-tested (test 9) |
| No `[CP]` diagnostic probes remain | ✅ (grep clean) |
| No contract change (`schemaVersion` 1.1, no timeout field in package) | ✅ + regression-tested (test 10) |
| Offline tests green (build 0/0, 479/479) | ✅ |
| No ADR (binding + precedence inside existing executor, no architecture decision) | ✅ |

## Known gaps (documented, not fixed)

- **OpenCode**: its own 120 s timeout is still too tight for a real Security
  analysis of 122 files in this environment (timed out at 120 s while the other
  two providers succeeded). Raising OpenCode's `TimeoutSeconds` is configuration,
  not a code change — but no adaptive/smarter timeout exists.
- **Codex/OpenCode token telemetry**: still null (M15.2A documented gap — the
  captured stdout carries no machine-readable usage).
- **ClaudeCode cache-token mapping** and **cost** remain deliberately unmapped.
- **M15.3 follow-ups** (from M15.2/M15.2A) are still open: acquisition
  robustness beyond timeouts (retries/repair), snapshot pinning, dedup + coverage
  gating.

## Recommended follow-up

1. **M15.3 robustness**: give OpenCode (and any slow agentic step) a realistic
   budget — either raise its `TimeoutSeconds` for real analyses or add
   bounded retries/repair for slow-but-healthy agentic steps; then re-run the
   full 3×7 Council run and compare per-runtime `KnownTokenExecutionCount`,
   `TotalInputTokens`/`TotalOutputTokens`/`TotalTokens`.
2. **Cache-token mapping**: capture ClaudeCode `cache_read_input_tokens` /
   `cache_creation_input_tokens` if total-context sizing is wanted.