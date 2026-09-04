# ADR-017 — Real Provider Metrics

- **Status:** Accepted
- **Date:** 2026-08-07
- **Milestone:** [MILESTONE-012.1](../milestones/MILESTONE-012.1-real-provider-metrics.md)
- **Builds on:** [ADR-005](./ADR-005-evidence-provider-layer.md),
  [ADR-006](./ADR-006-multi-provider-execution.md),
  [ADR-012](./ADR-012-real-llm-evidence-providers.md),
  [ADR-014](./ADR-014-reliability-security-hardening.md),
  [ADR-015](./ADR-015-domain-metrics-correctness.md),
  [ADR-016](./ADR-016-context-budget-correctness.md)

## Context

The POC's real providers (Claude, OpenAI) execute against a shared
`engineering-review-package.json` contract (`schemaVersion` **1.1**), but nothing yet
records how those executions actually behave: token usage, retries, repair attempts,
truncation, finish reason, or error category. The telemetry surface
(`provider-execution.json` per step, `ProviderExecutionReport` per run) carried only
success/failure, duration, evidence/observation counts, and the M11.3/M11.2 context and
provider-outcome fields. Real runs would silently burn tokens with no per-execution
signal. This milestone captures reliable, provider-neutral metrics for real LLM
executions by reusing that existing telemetry — no provider comparison, no calibration,
no contract change.

## Decision

Capture reliable per-execution and per-run metrics for real providers, reusing the
existing `provider-execution.json` telemetry and the existing
`ProviderExecutionRecord` / `ProviderExecutionReport` types. Facts only — never ranking,
never calibration, never cost optimization. The external
`engineering-review-package.json` contract is unchanged (`schemaVersion` stays **1.1**).

### Token rules — provider-reported usage only

- `InputTokens` / `OutputTokens` come exclusively from provider usage responses
  (`response.Usage`). Unknown usage ⇒ `null`, **never** `0` — the run report's `SumKnown`
  helper sums only known nullable values and returns `null` when none are known.
- `TotalTokens = InputTokens + OutputTokens` when **both** are known; otherwise `null`.
- The run report exposes `TotalInputTokens?`, `TotalOutputTokens?`, `TotalTokens?`, and
  `KnownTokenExecutionCount` (how many executions actually reported usage).

### Per-execution additive fields (records / `Evidence`)

`InputTokens?`, `OutputTokens?`, `RetryCount`, `RepairAttemptCount`,
`RepairSucceeded?`, `FinishReason?`, `ResponseTruncated`, `ErrorCategory?`
(provider-neutral string, e.g. `"Timeout"`, `"SchemaValidation"`). The model is carried
by the existing `ProviderVersion` on both `Evidence` and records, stamped by the
executor from `provider.Metadata.Version ?? string.Empty` — never hardcoded.

### Repair semantics (max one)

Valid first ⇒ `RepairAttemptCount = 0`, `RepairSucceeded = null`; repair succeeds ⇒
`1` / `true`; repair fails ⇒ `1` / `false` (with `ErrorCategory = "SchemaValidation"`).

### Retry semantics

First success ⇒ `0`. One transient failure then success ⇒ `1`. Timeout retries
(M11.1: `Cancelled` reclassified as `Timeout`; the run token is still alive) count in
`RetryCount`. Non-transient failures (e.g. auth) are never retried.

### Truncation — provider metadata only

`ResponseTruncated` is surfaced only from provider metadata (`response.Truncated`,
driven by stop/finish length). The validator treats truncated output as invalid.

### Named run-level aggregates

`ProvidersExecuted`, `TotalExecutions`, `Failures`, `SuccessfulExecutionCount`,
`EvidenceCount`, `ObservationsProduced`, `ContextFilesSelected`,
`PartialProviderCount`, `TotalInputTokens?`, `TotalOutputTokens?`, `TotalTokens?`,
`KnownTokenExecutionCount`, `TotalRetries`, `TotalRepairAttempts`,
`SuccessfulRepairs`, `TimeoutCount`, `TotalDuration`, `TotalCost?`, `Plan`,
`ByProvider`, `Records`. `SumKnown` in `ProviderExecutionReport.FromRecords` sums only
known nullable values and returns `null` when none are known.

## Trade-offs and decisions

- **Reuse over new architecture.** The existing
  `ProviderExecutionRecord`/`ProviderExecutionReport` and `provider-execution.json`
  renderer carry the metrics as additive fields; `EngineeringMetrics` stays untouched.
  No new run-summary type, no new exporter, no new CLI command.
- **Null-never-zero.** Unknown usage must not look like a free call; `0` would corrupt
  downstream sums and any future cost math. `SumKnown` keeps partial data honest.
- **Provider-neutral error categories.** `ErrorCategory` is a string enum of neutral
  categories (`Timeout`, `SchemaValidation`, …), never provider-specific codes — matches
  the ADR-012 shared-schema decision and keeps downstream consumers provider-agnostic.
- **`ProviderVersion` carries the model.** The executor already stamps it; the model
  is a provider fact, not a separate contract field.
- **Single diagnostic artifact.** `provider-execution.json` remains the one detailed
  telemetry artifact; the package is unchanged and stays the only external artifact.

## Consequences

- Real executions (Claude/OpenAI) now produce populated, provider-neutral per-execution
  and per-run metrics with no contract change and no secrets in artifacts.
- Unknown usage is honestly `null`; token totals only appear when the provider reports
  usage.
- Retry/repair/timeout/truncation behavior is observable per step and aggregate per run.
- Existing review commands naturally produce the new telemetry; Mock (no usage) shows
  `knownTokenExecutionCount: 0` and `null` token totals.

## Explicitly out of scope (deferred)

Provider comparison/ranking, calibration, cost optimization, tokenizers, parallel
execution, new providers/exporters/CLI commands. See MILESTONE-012.1.
