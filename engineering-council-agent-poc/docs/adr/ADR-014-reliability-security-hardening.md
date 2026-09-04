# ADR-014 — Execution Reliability & Security Hardening

- **Status:** Accepted
- **Date:** 2026-08-07
- **Milestone:** [MILESTONE-011.1](../milestones/MILESTONE-011.1-reliability-security-hardening.md)
- **Builds on:** [ADR-012](./ADR-012-real-llm-evidence-providers.md), [ADR-006](./ADR-006-multi-provider-execution.md)

## Context

The pipeline works end-to-end, but three execution-reliability and security gaps
surfaced during review:

- **A1 — LLM timeout vs cancellation.** The real transports (`ClaudeClient`,
  `OpenAiClient`) only ever receive the *linked timeout token* created by
  `LlmEvidenceProvider.SendWithRetryAsync`. When that timeout fires, the token they
  were given is cancelled, so they classified the failure as
  `LlmErrorCategory.Cancelled`. Because `Cancelled` is **not** transient, the
  provider's retry loop (`catch … when (ex.IsTransient)`) never retried it: a
  provider timeout became a terminal, mislabelled `Cancelled` failure.
- **A2 — analysis cancellation.** `AnalysisPipeline.RunAsync` wrapped every stage in a
  bare `catch (Exception)` that converted *any* exception — including
  `OperationCanceledException` — into `AnalysisRunStatus.Failed`, then still built and
  persisted a package and returned an `AnalysisResult` (the API answered HTTP 200 for a
  cancelled run).
- **A3 — runId path traversal.** `FileSystemAnalysisRunRepository` combined an
  **unvalidated** `runId` directly with `Path.GetFullPath(OutputsRoot)` in both
  `GetAsync` and `SaveAsync`. A hostile id like `..\..\…` could resolve outside the
  outputs root; the API `GET /runs/{runId}` exposed it.

## Decision

Apply the smallest corrective fixes that keep the architecture and the external
`engineering-review-package.json` contract unchanged.

### A1 — timeout vs cancellation by token ownership, not message text

- The **transport keeps its behaviour**: it wraps an `OperationCanceledException`
  as `Cancelled` when the token it was handed is cancelled, and `Timeout` otherwise.
  It cannot distinguish a linked-timeout from a run cancellation, so it does not try.
- The **provider owns the classification** in `SendWithRetryAsync`. A
  `LlmProviderException(Cancelled)` is a **provider timeout** unless the *run's* token
  is genuinely cancelled:
  - run token cancelled ⇒ propagate as cancellation, **never retried**;
  - run token alive ⇒ the only source of that `Cancelled` is the provider's own linked
    timeout ⇒ classify as `LlmErrorCategory.Timeout`, **retry** when configured.
- Distinction is by token state (ownership), **never** by exception-message inspection.
- Retry budget and backoff are unchanged; a timeout that exhausts `MaxRetries` is
  thrown as `LlmErrorCategory.Timeout`.
- Telemetry distinguishes the two: timeout is logged as a warning ("timed out …"), run
  cancellation as an informational "request was cancelled by the run".

### A2 — cancellation stops the pipeline

- `AnalysisRunStatus` gains additive value `Cancelled = 5` (string-serialized, so
  `run.json` reload stays compatible).
- `AnalysisPipeline.RunAsync` gains a dedicated
  `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)`
  **before** the generic failure handler. On cancellation it marks the run `Cancelled`,
  then **rethrows**. Nothing after the try block runs: no reconciliation, no package
  build, no persistence.
- Cancellation is therefore never converted into an ordinary `Failed` run and never
  yields a package or an `AnalysisResult`.
- The API `POST /runs` catches the propagated cancellation and returns
  `499 Client Closed Request` with a non-review body — never HTTP 200.

### A3 — runId path traversal defense in depth

- New shared policy type `EngineeringCouncil.Core.Domain.AnalysisRunId` is the **single
  source of truth** for the generated run-id format: `yyyyMMdd-HHmmss-<6 hex>`.
  `AnalysisPipeline` generates ids through it; the repository and API validate through it.
- **Layer 1 — format validation.** `AnalysisRunId.IsValid` enforces
  `^[0-9]{8}-[0-9]{6}-[0-9a-f]{6}$`, which by construction admits no path separators,
  traversal components, dots, or rooted paths.
- **Layer 2 — full-path resolution.** `FileSystemAnalysisRunRepository` resolves the
  id via `Path.GetFullPath(Path.Combine(root, runId))` and verifies the result starts
  with `root + DirectorySeparatorChar` (ordinal, case-insensitive on Windows).
- **Layer 3 — fail-safe reads.** `GetAsync` treats an invalid or escaping id as
  **not found** (returns `null`, never throws, never touches the filesystem);
  `SaveAsync` rejects an invalid id with `ArgumentException` (a programming error,
  since the pipeline generates ids).
- The API `GET /runs/{runId}` returns **400** for a malformed id via the shared
  validator and **404** for an unknown valid id. Error bodies contain no filesystem
  paths or file contents.

## Trade-offs and decisions

- **Provider boundary corrected, not the transport.** Changing `ILlmChatClient` to pass
  two tokens would ripple through both transports and all fakes for no behavioral gain;
  correcting the classification at the single retry-owner is the smallest fix.
- **Rethrow, not a cancelled result.** Propagating `OperationCanceledException` is the
  idiomatic .NET pattern, lets the API map cancellation to a non-200 status, and
  structurally guarantees no package is built or persisted.
- **Strict format, one policy.** Coupling the validator to the exact generated shape
  makes traversal *impossible by construction* rather than merely filtered; any future
  format change must update `AnalysisRunId` (a single, tested location).
- **`Cancelled` is additive.** No switch on `AnalysisRunStatus` exists; JSON uses string
  enums, so existing `run.json` files remain loadable and no consumer field changes.

## Consequences

- `LlmEvidenceProvider.SendWithRetryAsync` reclassifies provider timeouts correctly and
  retries them; run cancellation is never retried. Logs distinguish the two.
- Cancelled runs produce no package and no artifacts, and the API answers non-200.
- Run ids are validated at the repository boundary and by the API; traversal is blocked.
- `AnalysisRunStatus` grows one value; no package schema change
  (`schemaVersion` stays **1.1**) — the consumer-DTO contract test passes unchanged.

## Explicitly out of scope (deferred)

A4 provider metrics, A5 Unknown-discipline handling, A6 analyzer validation, C4 context
budgeting, C6 context metrics, parallel execution, provider calibration, councils, LLM
reconciliation, run delta/trends, background jobs, caching, new exporters, ticket
generation, and unrelated refactoring. See MILESTONE-011.1.
