# ADR-016 — Context Budget Correctness

- **Status:** Accepted
- **Date:** 2026-08-07
- **Milestone:** [MILESTONE-011.3](../milestones/MILESTONE-011.3-context-budget-correctness.md)
- **Builds on:** [ADR-009](./ADR-009-discipline-aware-evidence-acquisition.md), [ADR-015](./ADR-015-domain-metrics-correctness.md)

## Context

Two context-budget correctness issues were confirmed by reading the code:

- **C4 — context-selection budgeting and the rendered context disagree.**
  `RuleBasedAnalysisContextSelector` sized every file by its **full** content length
  (`file.Content?.Length`) when applying the character budget, while
  `RepositoryContextBuilder` truncates each rendered file to a **separate** per-file
  limit (a magic constant `8000`). A file of 50K characters counted 50K toward the
  selection budget but rendered as ~8K. Selection accounting (`EstimatedContentSize`,
  surfaced per step as `ContextCharacterCount`) therefore overstated the actual context,
  and a file whose full length sat near the budget could wrongly exclude later files
  that would have fit at their effective rendered size. The per-file limit lived in two
  unrelated places with no shared rule.
- **C6 — `AcquisitionCoverage.ContextFilesConsidered` has misleading semantics.**
  `AcquisitionCoverage.From` computed `ContextFilesConsidered = Max(r.ContextFileCount)`
  over execution records — i.e. the largest **single-step selection** — and reported it
  as a run-level "files considered" figure. That number depends on per-step limits and
  undercounts the scope the selector actually ranks; it conflated "selected" with
  "considered".

## Decision

Apply the smallest corrective fixes that keep the architecture and the external
`engineering-review-package.json` consumer contract unchanged (`schemaVersion` stays
**1.1**; `ContextFilesConsidered` per record is additive internal telemetry, not part of
the consumer contract).

### C4 — one authoritative content rule: effective size = min(actual, per-file limit)

- New `EngineeringCouncil.Core.Analysis.ContextContentPolicy` is the **single
  authoritative rule** for context budgeting and rendering:
  `EffectiveContextCharacters(file) = min(file.Content.Length, MaxCharactersPerFile)`.
- `RuleBasedAnalysisContextSelector` takes a `ContextContentPolicy` and budgets each file
  by its **effective** size (`_policy.EffectiveContextCharacters(file)`) instead of its
  full length. `EstimatedContentSize` now equals the sum of the effective sizes — exactly
  what the renderer will produce.
- `RepositoryContextBuilder` takes the **same** `ContextContentPolicy` and truncates each
  file at `_policy.MaxCharactersPerFile`. Selection accounting and rendered context can
  never disagree again.
- Config: new `Evidence:Context:MaximumCharactersPerFile` (default `8000`), wired through
  `EvidenceOptions` and `CouncilOptions`, parsed by the CLI (`CliArgs.ParseContextLimits`)
  and API (`EvidenceConfig.ParseContextLimits`), and applied via a single DI-registered
  `ContextContentPolicy` shared by the selector and the renderer.
- Behavior preserved: files are still included whole (never silently truncated by the
  selector) and the first file is still always included even if it exceeds the budget.

### C6 — `ContextFilesConsidered` = the repository scope the selector ranks

- `ProviderExecutionRecord` gains additive `ContextFilesConsidered`, stamped by
  `EvidenceAcquisitionExecutor` from `selection.TotalRepositoryFiles` — the files the
  selector ranks before applying limits (constant across steps in a run).
- `AcquisitionCoverage.From` reports that repository scope for `ContextFilesConsidered`
  (instead of `Max(r.ContextFileCount)`), with `ContextFilesSelected` remaining the sum
  of per-step selections. The two are now clearly distinct: **considered** = the scope
  evaluated; **selected** = what was actually included.

## Trade-offs and decisions

- **One policy type, not two constants.** A shared `ContextContentPolicy` (rather than a
  second magic number in the renderer) makes the "selection == render" invariant
  structural: both consumers read the same configured value and the same effective-size
  rule.
- **Policy over interface.** A `ContextContentPolicy` value (with its
  `EffectiveContextCharacters` rule) is the smallest abstraction that carries both data
  and the shared rule; an `IContextContentPolicy` adds a layer with no second
  implementation.
- **Per-step stamping, not a summary-only fix.** Recording `ContextFilesConsidered` on
  each execution record keeps `provider-execution.json` self-contained and lets the
  coverage rollup derive the figure from data already persisted per step.
- **Keep "considered" as repository scope.** The selector ranks every scanned file
  before limits, so the repository file total is the honest "what could have been
  selected" figure; any per-step selection count would just reintroduce the C6 confusion.

## Consequences

- Context-selection accounting matches the rendered context exactly: a file contributes
  only what will actually be sent to the model, so `MaximumCharacters` is no longer
  "wasted" on files that render far shorter.
- The per-file truncation limit is configurable and shared; the renderer's old magic
  constant is gone.
- `ContextFilesConsidered` is a correct, deterministic, run-level figure (repository
  scope) and is clearly distinct from `ContextFilesSelected` (sum of selections).
- `engineering-review-package.json` is unchanged (`schemaVersion` stays **1.1**; the
  consumer-DTO contract test passes unchanged); `provider-execution.json` gains one
  additive field per record.

## Explicitly out of scope (deferred)

Parallel execution, provider calibration, councils, LLM reconciliation, run delta/trends,
background jobs, caching, new exporters, stable evidence IDs, and unrelated refactoring.
See MILESTONE-011.3.
