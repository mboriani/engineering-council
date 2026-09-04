# MILESTONE-011.3 — Context Budget Correctness

- **Status:** ✅ Complete
- **Date:** 2026-08-07
- **Version:** v12.3 (context budget correctness)
- **ADR:** [ADR-016](../adr/ADR-016-context-budget-correctness.md)

## Goal

A corrective milestone addressing exactly two verified issues — C4 (context-selection
budgeting uses raw file length while rendering truncates per file, so selection and the
rendered context disagree) and C6 (`ContextFilesConsidered` is a per-step max, misleading
as a run-level figure). No new capability, no architecture change. The external
application still consumes only `engineering-review-package.json` (`schemaVersion 1.1`).

## Scope

- **C4** — one authoritative content rule (effective size = `min(actual, per-file
  limit)`) shared by the selector's budgeting and the renderer's truncation
- **C6** — `ContextFilesConsidered` reports the repository scope the selector ranks, not
  the largest single-step selection

Explicitly **out of scope** (documented, deferred): parallel execution · provider
calibration · councils · LLM reconciliation · run delta/trends · background jobs ·
caching · new exporters · stable evidence IDs · unrelated refactoring.

## Reproduced findings

| # | Reproduction | Before |
|---|--------------|--------|
| C4 | `RuleBasedAnalysisContextSelector` budgets by `file.Content?.Length` (full length); `RepositoryContextBuilder` truncates each file at a separate magic constant (`8000`). | A 50K-char file counted 50K toward the selection budget but rendered ~8K; `EstimatedContentSize`/`ContextCharacterCount` overstated the real context, and files that would fit at effective size could be wrongly excluded. Two unrelated per-file limits with no shared rule. |
| C6 | `AcquisitionCoverage.From` computes `ContextFilesConsidered = Max(r.ContextFileCount)` over execution records. | "Files considered" = the largest single-step SELECTION — depends on per-step limits and undercounts the repository scope the selector ranks; "selected" and "considered" were conflated. |

Both reproduced by inspection; each is now guarded by regression tests.

## Fixes

- **C4 (`ContextContentPolicy`)** — new `Core/Analysis/ContextContentPolicy` is the
  single authoritative rule: `EffectiveContextCharacters(file) = min(file.Content.Length,
  MaxCharactersPerFile)`. `RuleBasedAnalysisContextSelector(ContextContentPolicy)`
  budgets each file by its effective size; `RepositoryContextBuilder(ContextContentPolicy)`
  truncates each file at the same policy's `MaxCharactersPerFile`. New config
  `Evidence:Context:MaximumCharactersPerFile` (default 8000) is wired through
  `EvidenceOptions`/`CouncilOptions`, parsed by CLI (`CliArgs.ParseContextLimits`) and API
  (`EvidenceConfig.ParseContextLimits`), and applied through one DI-registered policy
  shared by selector and renderer. Selection accounting now equals what is actually
  rendered; whole-file inclusion and "first file always included" behavior preserved.
- **C6 (`ProviderExecutionRecord` + `AcquisitionCoverage`)** — execution records gain
  additive `ContextFilesConsidered`, stamped by the executor from
  `selection.TotalRepositoryFiles` (the scope the selector ranks; constant across steps).
  `AcquisitionCoverage.From` reports that repository scope for `ContextFilesConsidered`
  instead of `Max(r.ContextFileCount)`; `ContextFilesSelected` stays the sum of per-step
  selections.

## Regression tests

- **C4 (`AcquisitionTests`)** — `Selector_budgets_effective_size_not_raw_file_length`
  (files of 100 chars with a 60-char per-file limit and a 150-char budget ⇒ 2 files,
  `EstimatedContentSize == 120`, character-limit reason recorded);
  `Selection_budget_matches_the_rendered_context_size` (selection's `EstimatedContentSize`
  equals the sum of per-file-limited sizes the renderer actually produces, truncation
  marker present).
- **C6 (`AcquisitionTests`)** — `Coverage_context_files_considered_is_the_repository_scope_not_the_max_selected`
  (considered = 4 repo files while per-step selections are 2 and 3; selected = 5 sum;
  considered > max selected); `Executor_stamps_context_files_considered_from_the_repository_scope`
  (a real execution record carries `ContextFilesConsidered == Snapshot.Files.Count`).

## Verification

| Criterion | Result |
|-----------|--------|
| Build (0 errors, 0 warnings) | ✅ |
| All tests pass | ✅ 203/203 (4 new) |
| Consumer-DTO contract test green | ✅ `schemaVersion` unchanged at **1.1** |
| Offline Mock smoke | ✅ package produced; `contextFilesConsidered=7` (repository scope) vs `contextFilesSelected=26` (sum across 7 steps); per-step records carry `contextFilesConsidered=7` |
| `engineering-review-package.json` compatible | ✅ unchanged (`schemaVersion 1.1`); only `provider-execution.json` gains one additive field per record |

```
dotnet build EngineeringCouncil.slnx        # 0 errors, 0 warnings
dotnet test  EngineeringCouncil.slnx        # Passed: 203 (no credentials required)
```

## Deferred diagnostic findings

Parallel execution · provider calibration · councils · LLM reconciliation · run
delta/trends · background jobs · caching · new exporters · stable evidence IDs. These
remain on the diagnostic backlog and were deliberately **not** changed by this milestone.
C4 and C6 were the verified scope and are now closed.
