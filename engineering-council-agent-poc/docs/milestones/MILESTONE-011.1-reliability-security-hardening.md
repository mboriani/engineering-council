# MILESTONE-011.1 — Execution Reliability & Security Hardening

- **Status:** ✅ Complete
- **Date:** 2026-08-07
- **Version:** v12.1 (reliability + security hardening)
- **ADR:** [ADR-014](../adr/ADR-014-reliability-security-hardening.md)

## Goal

A small corrective milestone addressing exactly three verified issues — A1 (LLM timeout
vs cancellation), A2 (analysis cancellation handling), A3 (runId path traversal). No new
capability, no A4/A5/A6/C4/C6 work, no architecture change. The external application
still consumes only `engineering-review-package.json` (`schemaVersion 1.1`).

## Scope

- **A1** — LLM timeout classification and retry
- **A2** — analysis cancellation handling
- **A3** — runId path traversal protection

Explicitly **out of scope** (documented, deferred): A4 provider metrics · A5 Unknown
discipline · A6 analyzer validation · C4 context budgeting · C6 context metrics ·
parallel execution · provider calibration · councils · LLM reconciliation · run
delta/trends · background jobs · caching · new exporters · ticket generation.

## Reproduced findings

| # | Reproduction | Before |
|---|--------------|--------|
| A1 | A transport timeout fires the provider's linked `timeoutCts`; the client only sees that token cancelled and reports `LlmErrorCategory.Cancelled`. Since `Cancelled` is not transient, `SendWithRetryAsync` never retried it. | Timeout → `Cancelled`, **no retry** despite `MaxRetries > 0`. |
| A2 | `AnalysisPipeline.RunAsync` caught `OperationCanceledException` in the generic `catch (Exception)` → `Failed`, then built + persisted a package and returned an `AnalysisResult`. | API returned **HTTP 200** for a cancelled run; a misleading package was persisted. |
| A3 | `FileSystemAnalysisRunRepository.GetAsync`/`SaveAsync` combined an unvalidated `runId` with `Path.GetFullPath(OutputsRoot)`. `GET /runs/{runId}` passed it straight through. | Traversal ids like `..\..\…` could resolve outside the outputs root. |

No finding was non-reproducible.

## Fixes

- **A1 (`LlmEvidenceProvider.SendWithRetryAsync`)** — token-ownership model. A
  `LlmProviderException(Cancelled)` is classified by the run token's state: cancelled ⇒
  propagate (never retry); alive ⇒ provider **timeout**, retried when `MaxRetries`
  allows, exhausted ⇒ thrown as `Timeout`. Raw `OperationCanceledException` from a
  transport keeps its existing timeout handling. Telemetry logs timeout (warning) and
  cancellation (information) distinctly. No message-string inspection.
- **A2** — `AnalysisRunStatus` gains `Cancelled = 5`. `AnalysisPipeline.RunAsync`
  rethrows `OperationCanceledException` (when the run token is cancelled) from a catch
  placed **before** the generic failure handler, marking the run `Cancelled`; the code
  after the try block (reconciliation → package build → `SaveAsync`) never runs. API
  `POST /runs` catches it and returns `499 Client Closed Request`.
- **A3** — new `EngineeringCouncil.Core.Domain.AnalysisRunId` (single source of truth:
  `New()` + `IsValid()` matching `^[0-9]{8}-[0-9]{6}-[0-9a-f]{6}$`). The pipeline
  generates ids through it. `FileSystemAnalysisRunRepository` validates the format,
  resolves via `Path.GetFullPath`, and verifies the path stays under `OutputsRoot`
  (defense in depth); `GetAsync` returns `null` (not found) for invalid/escaping ids.
  API `GET /runs/{runId}` returns **400** for a malformed id, **404** for unknown.

## Regression tests

- **A1 (`LlmProviderTests`)** — timeout reported as `Cancelled` is reclassified and
  retried (correct `retryCount`); timeout exhausting retries is thrown as `Timeout` with
  retry count correct; run cancellation reported as `Cancelled` is never retried;
  telemetry distinguishes timeout from cancellation.
- **A2 (`AnalysisCancellationTests`)** — pre-cancelled run propagates cancellation with
  **zero** reconciler/summary/package-builder/persistence calls and nothing written;
  cancellation during acquisition stops the pipeline the same way; cancellation is not
  converted into an ordinary failed run; a normal run still completes and persists the
  package.
- **A3 (`RunIdSecurityTests`)** — generated ids match the canonical format; malformed /
  `../` / `..\` / rooted / nested / empty run ids rejected as not-found; a valid id
  round-trips and resolves inside `OutputsRoot`; a traversal id cannot read a run
  planted outside the root; `SaveAsync` rejects an escaping id.

## Verification

| Criterion | Result |
|-----------|--------|
| Build (0 errors, 0 warnings) | ✅ |
| All tests pass | ✅ 182/182 (25 new) |
| Consumer-DTO contract test green | ✅ `schemaVersion` unchanged at **1.1** |
| Offline smoke (Mock / SARIF) | ✅ covered by end-to-end suites; package deserializes via `PackageContract` |
| `engineering-review-package.json` compatible | ✅ no field/version change |

```
dotnet build EngineeringCouncil.slnx        # 0 errors, 0 warnings
dotnet test  EngineeringCouncil.slnx        # Passed: 182 (no credentials required)
```

## Housekeeping note

The pre-existing `xUnit1031` analyzer warning in `EvaluationTests.cs`
(`RunAsync(...).GetAwaiter().GetResult()` on a sync test) was fixed by making the test
async/await, required to reach the milestone's 0-warning build gate. No test semantics
changed.

## Deferred diagnostic findings

A4 (provider success/failure metrics can overlap) · A5 (unknown-discipline fallback to
CodeQuality) · A6 (valid enum discipline with no registered analyzer is silently skipped)
· C4 (context budget vs 8000-char truncation mismatch) · C6 (`ContextFilesConsidered`
semantics). These remain on the diagnostic backlog and were deliberately **not** changed
by this milestone.
