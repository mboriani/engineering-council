# MILESTONE-003 — Finding Merger and Council Summary

- **Status:** ✅ Complete
- **Date:** 2026-07-06
- **Version:** v3 (first consolidation layer; still pre multi-LLM council)
- **ADR:** [ADR-004](../adr/ADR-004-finding-merger-and-council-summary.md)

## Goal

Introduce the first council-like behavior: consolidate, de-duplicate, reconcile
severity/confidence, and produce an executive summary. The final output is no
longer a raw concatenation of analyzer results. **Not** a multi-LLM council.

## New flow

```
Repository Scanner → Specialized Analyzers → Raw Findings
   → Finding Merger → Consolidated Findings
   → Council Summary Generator → Markdown / JSON / Run Summary
```

## What changed

- **`Finding`** extended with: `Status` (`New|Merged|Duplicate|Discarded`),
  `DuplicateOf`, `MergedFromFindingIds`, `SourceAgents` (future-facing;
  `SourceAgent` retained for backward compatibility), `SeverityRationale`,
  `ConfidenceRationale`.
- **`IFindingMerger`** + **`RuleBasedFindingMerger`** (no LLM) implementing the
  8 merge rules (same-category similar-title merge; file-overlap + similar-
  recommendation merge; same-title-different-category kept separate with a note;
  highest severity wins; corroboration-based confidence; preserve evidence and
  source agents; record rationale).
- **`ICouncilSummaryGenerator`** + **`RuleBasedCouncilSummaryGenerator`** (no LLM)
  producing **`CouncilSummary`** (executive summary, key risks, next actions,
  category/severity/confidence breakdowns, merge summary).
- **`AnalysisRun`** extended with `RawFindings` and `Summary`.
- **Orchestrator** now assigns globally-unique raw ids (`RAW-NNN`) so
  consolidation can reference provenance.
- **`AnalysisPipeline`** now: scan → analyze → store raw → merge → summary →
  persist.
- **Reporting** exporters extended: JSON adds `raw-findings.json`; Markdown adds
  `council-summary.md` and restructures `findings.md`.

## Output files (per run)

| File | Contents |
|------|----------|
| `raw-findings.json` | Original per-analyzer findings (audit trail) |
| `findings.json` | **Consolidated** findings (dashboard contract) |
| `findings.md` | Full council report (see structure below) |
| `council-summary.md` | Executive council summary |
| `run-summary.md` | Short run summary |
| `run.json` | Full run for reload |

`findings.md` structure:
`# Engineering Council Findings Report` → `## Council Summary` →
`## Key Risks` → `## Recommended Next Actions` → `## Consolidated Findings` →
`## Merge Notes` → `## Raw Findings Reference`.

## Tests (all green)

Added `FindingMergerTests`: exact-duplicate merge · similar-title merge ·
severity escalation · confidence escalation on corroboration · source-agent &
evidence preservation · no-merge across unrelated categories · same-title-
different-category note · input-not-mutated. Updated `PipelineEndToEndTests` to
assert raw vs consolidated separation, the council summary, and all six
artifacts.

## Exit criteria

| Criterion | Result |
|-----------|--------|
| Build (0 errors, 0 warnings) | ✅ |
| Tests pass | ✅ 15/15 |
| Output is consolidated, not concatenated | ✅ `findings.json` = consolidated |
| Raw findings preserved separately | ✅ `raw-findings.json` (RAW-NNN, status New) |
| Council summary generated | ✅ `council-summary.md` + `CouncilSummary` on run |
| `findings.md` uses the required section layout | ✅ verified |

## Verification

```
dotnet build EngineeringCouncil.slnx        # 0 errors, 0 warnings
dotnet test  EngineeringCouncil.slnx        # Passed: 15
dotnet run --project src/EngineeringCouncil.Cli -- analyze --path src/EngineeringCouncil.Core --mock
# → 7 raw → 7 consolidated (distinct disciplines, no merges), 6 artifacts written
```

## Explicitly out of scope (future milestones)

Multi-provider execution · semantic/LLM merging · duplicate/discarded emission ·
calling this a full Engineering Council.
