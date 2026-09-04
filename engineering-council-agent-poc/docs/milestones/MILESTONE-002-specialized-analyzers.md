# MILESTONE-002 — Introduce Specialized Analyzer Agents

- **Status:** ✅ Complete
- **Date:** 2026-07-06
- **Version:** v2 (specialized analyzers; still pre-Council)
- **ADR:** [ADR-003](../adr/ADR-003-specialized-analyzers.md)

## Goal

Refactor the single generic `AnalyzerAgent` into a collection of specialized
analyzer agents coordinated by an orchestrator that aggregates their findings.
Existing functionality (scan → analyze → Markdown/JSON outputs, CLI, API, mock
fallback) must keep working. **No Council yet.**

## New architecture

```
Repository → Repository Scanner → Analysis Orchestrator → [ 7 specialized analyzers ] → List<Finding> → Report Generators
```

The orchestrator simply executes all registered analyzers and aggregates their
findings — it does not merge, de-duplicate, or reconcile.

## What changed

- **`IAnalyzerAgent`** is now `{ Name, Category, AnalyzeAsync(...) }` and returns
  only `IReadOnlyList<Finding>`. No analyzer renders Markdown, writes files, or
  knows about the dashboard.
- **`AnalysisOrchestrator`** now takes `IEnumerable<IAnalyzerAgent>`, runs each
  (sequentially, isolating failures), and returns one aggregated `List<Finding>`.
  Designed so parallel execution is a later drop-in.
- **`AnalysisPipeline`** (new) owns the end-to-end flow (scan → orchestrate →
  persist), keeping the CLI/API behavior identical.
- **Seven analyzers** created under `src/EngineeringCouncil.Agent/Analyzers/`,
  all sharing `ChatAnalyzerAgent` (Microsoft Agent Framework wiring):
  Architecture, CodeQuality, Reliability, Security, Testing, Documentation,
  Observability.
- **Prompts** split into one focused file per analyzer under `prompts/`
  (plus a shared base in `analyzer-agent.md`). The old `*-review.md` stubs were
  removed.
- **Reporting** moved to dedicated exporters: `MarkdownReportGenerator` and the
  new `JsonReportGenerator` (`IJsonReportGenerator`). The run repository uses
  both; the orchestrator knows nothing about report formats.
- **DI**: analyzers register as `IEnumerable<IAnalyzerAgent>` via
  `AddAnalyzer<T>()`. The orchestrator discovers them automatically — adding a
  discipline needs no orchestrator change.

## Exit criteria

| Criterion | Result |
|-----------|--------|
| Solution builds (0 errors, 0 warnings) | ✅ |
| Unit tests pass | ✅ 7/7 |
| All 7 analyzers run and aggregate | ✅ CLI shows 7 findings, one per discipline |
| Analyzers produce only findings (no reporting/IO) | ✅ enforced by `IAnalyzerAgent` |
| Reporting is in dedicated exporters | ✅ `MarkdownReportGenerator` + `JsonReportGenerator` |
| Adding an analyzer = implement + register only | ✅ `AddAnalyzer<T>()`, orchestrator untouched |
| Existing CLI/API/mock behavior intact | ✅ |

## Verification

```
dotnet build EngineeringCouncil.slnx        # 0 errors, 0 warnings
dotnet test  EngineeringCouncil.slnx        # Passed: 7
dotnet run --project src/EngineeringCouncil.Cli -- analyze --path src/EngineeringCouncil.Core --mock
# → Completed, 7 findings across Architecture/CodeQuality/Reliability/
#   Security/Testing/Documentation/Observability, each attributed to its analyzer
```

## Explicitly out of scope (future milestones)

Council · finding merger · duplicate detection · multiple LLM providers ·
parallel execution.

## Notable fix

Microsoft Agent Framework delivers an agent's instructions via
`ChatOptions.Instructions`, **not** as a system `ChatMessage`. The
`LlmProviderChatClient` adapter had to fold `options.Instructions` into the
system prompt — otherwise every specialty prompt (and the real provider's system
prompt) would be silently dropped.
