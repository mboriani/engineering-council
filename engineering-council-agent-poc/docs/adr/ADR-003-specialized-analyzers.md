# ADR-003 — Specialized analyzer agents instead of one generic reviewer

- **Status:** Accepted
- **Date:** 2026-07-06
- **Supersedes:** the single generic `AnalyzerAgent` from Milestone 001
- **Milestone:** [MILESTONE-002](../milestones/MILESTONE-002-specialized-analyzers.md)

## Context

Milestone 001 shipped a single `AnalyzerAgent` responsible for the entire
review. That works, but a generic "review everything" agent has structural
weaknesses that get worse as the system grows toward an Engineering Council.

## Decision

Replace the one generic analyzer with a set of **specialized analyzer agents**,
each owning exactly one engineering discipline, coordinated by an
`AnalysisOrchestrator` that discovers them via `IEnumerable<IAnalyzerAgent>`:

- `architecture-analyzer` · `code-quality-analyzer` · `reliability-analyzer` ·
  `security-analyzer` · `testing-analyzer` · `documentation-analyzer` ·
  `observability-analyzer`

Each analyzer:
- inherits a shared base prompt and appends only its discipline-specific
  instructions (small, focused prompts — not one giant prompt);
- returns only `IReadOnlyList<Finding>` — it never renders Markdown, writes
  files, or knows about the dashboard;
- stamps its `Name` on `Finding.SourceAgent` and its `Category` on findings.

Reporting moved to dedicated exporters (`MarkdownReportGenerator`,
`JsonReportGenerator`); the orchestrator does not know how reports are produced.

## Why specialized agents produce better engineering results

1. **Focused attention beats divided attention.** A single prompt asking for
   architecture *and* security *and* testing *and* docs forces the model to
   split a fixed attention budget across everything, so it produces shallow,
   generic findings. A prompt scoped to one discipline goes deep on that
   discipline.
2. **Less cross-talk and dilution.** Narrow instructions reduce the chance the
   model drifts between concerns or lets a strong signal in one area crowd out
   others. Each discipline is guaranteed its own pass.
3. **Consistent coverage.** Seven agents means seven guaranteed lenses every
   run — architecture is never skipped because the model "spent" its output on
   style nits. The run-summary always reports findings per discipline.
4. **Clear attribution.** `SourceAgent` + `Category` on every finding make
   results explainable and routable (e.g. security findings → the security
   channel) — essential for the future dashboard.
5. **Independent evolution & tuning.** A discipline's prompt, model, or
   thresholds can be improved without touching the others. A flaky analyzer can
   be isolated (the orchestrator already skips a failing analyzer without
   sinking the run).
6. **Foundation for the Council.** Specialized, independently-attributable
   agents are the prerequisite for a future council that merges, de-duplicates,
   and reconciles findings — and for running different agents on different LLMs.

## Consequences

- **Positive:** deeper, better-attributed, reliably-covered findings; trivial
  extensibility — a new discipline is a new `IAnalyzerAgent` + one DI line, with
  **no orchestrator change**.
- **Cost:** N model calls per run instead of 1 (one per analyzer). Acceptable
  now; mitigated later by the deferred parallel-execution and council work.
- **Explicitly deferred:** council, finding merger, duplicate detection,
  multiple LLM providers, and parallel execution are **not** in this milestone.

## Related

- [ADR-001 — Reuse the FM workflow pattern](./ADR-001-reuse-workflow-pattern.md)
- [ADR-002 — Provider abstraction & mock fallback](./ADR-002-provider-abstraction-and-mock-fallback.md)
