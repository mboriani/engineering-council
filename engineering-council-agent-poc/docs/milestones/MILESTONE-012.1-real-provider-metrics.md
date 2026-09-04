# MILESTONE-012.1 — Real Provider Metrics

- **Status:** ✅ Complete
- **Date:** 2026-08-07
- **Version:** v13 (real provider metrics)
- **ADR:** [ADR-017](../adr/ADR-017-real-provider-metrics.md)

## Goal

Capture reliable, provider-neutral per-execution and per-run metrics for real LLM
providers (Claude, OpenAI) by reusing the existing telemetry surface. Facts only — no
provider comparison/ranking, no calibration, no cost optimization, no tokenizers, no
parallel execution, no new providers/exporters/CLI commands. The external application
still consumes only `engineering-review-package.json` (`schemaVersion` stays **1.1**);
real executions simply populate the existing per-run telemetry.

## Scope

- **Token usage** — provider-reported only; unknown ⇒ `null` (never `0`);
  `TotalTokens = Input + Output` when both known
- **Reliability telemetry** — `RetryCount`, `RepairAttemptCount`, `RepairSucceeded?`,
  `FinishReason?`, `ResponseTruncated`, `ErrorCategory?` per execution
- **Run-level aggregates** — total input/output/total tokens, known-token execution
  count, total retries, total repair attempts, successful repairs, timeout count
- **Model stamping** — carried by the existing `ProviderVersion`, from provider metadata

Explicitly **out of scope** (documented, deferred): provider comparison/ranking ·
calibration · cost optimization · tokenizers · parallel execution · new providers ·
new exporters · new CLI commands.

## Changes

- **Model (additive, no renames):**
  - `Core/Domain/Evidence.cs` — `InputTokens?`, `OutputTokens?`, `RetryCount`,
    `RepairAttemptCount`, `RepairSucceeded?`, `FinishReason?`, `ResponseTruncated`,
    `ErrorCategory?`
  - `Core/Domain/ProviderExecution.cs` — `ProviderExecutionRecord` gains the same
    per-execution fields; `ProviderExecutionReport` gains
    `TotalInputTokens?`/`TotalOutputTokens?`/`TotalTokens?`/`KnownTokenExecutionCount`,
    `TotalRetries`, `TotalRepairAttempts`, `SuccessfulRepairs`, `TimeoutCount`, plus a
    `SumKnown` helper (sums only known nullable values; `null` when none known)
  - `Infrastructure/Llm/LlmClientContracts.cs` — `LlmProviderException` constructor +
    properties carry `RetryCount?`, `RepairAttemptCount?`, `RepairSucceeded?`,
    `ResponseTruncated?` (`IsTransient` unchanged)
- **Executor** (`EvidenceAcquisitionExecutor`) — lifts metrics from the response into
  `Record` on success, from the `LlmProviderException` on the throw path, stamps
  `ProviderVersion` from `provider.Metadata.Version ?? string.Empty` (never hardcoded),
  and maps the run-token timeout path to `ErrorCategory = "Timeout"`.
- **Provider** (`LlmEvidenceProvider`) — records repair success and attaches metrics in
  `BuildEvidence` (repair succeeded ⇒ `1`/`true`; repair failed ⇒ `1`/`false` +
  `ErrorCategory="SchemaValidation"`), and attaches retry/truncation context in the
  `SendWithRetryAsync` throw paths.
- **Observation aggregation** — preserved: the pipeline enriches each record
  `with { ObservationsProduced = count }` by `AcquisitionStepId` after interpretation.
- **Context metrics** — M11.3 semantics reused verbatim: `ContextFilesConsidered =
  selection.TotalRepositoryFiles`, `ContextFileCount = SelectedFileCount`,
  `ContextCharacterCount = (int)EstimatedContentSize`, selector budgets via
  `ContextContentPolicy.EffectiveContextCharacters`.
- **`EngineeringMetrics`** — untouched; reuse over new run-summary architecture.

## Regression tests

New `src/EngineeringCouncil.Tests/ProviderMetricsTests.cs` — **20 focused tests** on
scripted fakes (no network). Coverage:

- Claude/OpenAI token mapping to evidence + record; unknown usage ⇒ null, never 0;
  `TotalTokens` sum; zero retries / one retry / timeout retries counted
- Repair success / repair failure / no repair; model stamped on each execution;
  positive duration
- Auth failure (ErrorCategory/Duration/RetryCount=0; error message never contains the
  key); truncation only from provider metadata (validator treats it invalid); finish
  reason capture/null
- M11.3 context reuse (considered=2, fileCount=1, effective chars); deterministic run
  aggregation (failures=2, partial=2, totalTokens=1650, knownTokenCount=2, totalRetries=4,
  repair totals, timeoutCount=1, totalDuration=10s, `FromRecords` re-runnable)
- Token totals null when none known; `provider-execution.json` carries metrics and never
  the secret; package stays `schemaVersion 1.1` (consumer DTO gate) and no secret;
  run-report observations aggregate per acquisition step

## Verification

| Criterion | Result |
|-----------|--------|
| Build (0 errors, 0 warnings) | ✅ |
| All tests pass | ✅ 223/223 (20 new) |
| Consumer-DTO contract test green | ✅ `schemaVersion` unchanged at **1.1** |
| Offline Mock smoke (7-step run `20260807-225926-50a23e`) | ✅ package produced; `provider-execution.json` carries run aggregates `totalExecutions: 7, successfulExecutionCount: 7, failures: 0, contextFilesSelected: 537, knownTokenExecutionCount: 0, totalRetries: 0, totalRepairAttempts: 0, successfulRepairs: 0, timeoutCount: 0, totalDuration: 00:00:00.13`; 7 per-step records with `providerVersion: mock-observer-v2`, `retryCount: 0`, `repairAttemptCount: 0`, `responseTruncated: false`, `contextFilesConsidered: 254` (repo scope); `schemaVersion: 1.1`; no metrics/secrets leaked into the package |
| `engineering-review-package.json` compatible | ✅ unchanged (`schemaVersion 1.1`); only `provider-execution.json` gains additive fields |

```
dotnet build EngineeringCouncil.slnx        # 0 errors, 0 warnings
dotnet test  EngineeringCouncil.slnx        # Passed: 223 (no credentials required)
dotnet run --project src/EngineeringCouncil.Cli -- review --path . --provider Mock
```

## Deferred diagnostic findings

Provider comparison/ranking, calibration, cost optimization, tokenizers, parallel
execution, new providers/exporters/CLI commands remain on the diagnostic backlog and were
deliberately **not** changed by this milestone. Real executions now simply record their
metrics; using those metrics to rank, calibrate, or optimize is M12.2+ work.
