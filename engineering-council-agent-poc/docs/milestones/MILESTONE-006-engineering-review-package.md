# MILESTONE-006 — Engineering Review Package

- **Status:** ✅ Complete
- **Date:** 2026-07-07
- **Version:** v6 (package as primary artifact)
- **ADR:** [ADR-007](../adr/ADR-007-engineering-review-package.md)

## Goal

Make the **Engineering Review Package** the platform's first-class deliverable —
the complete engineering assessment of a repository at a point in time. Markdown
becomes one projection of that package.

## New flow

```
Repository → Evidence Sources → Observations → Analyzers → Findings
   → Finding Merger → Council Summary → Engineering Review Package
   → Markdown/JSON Export → Engineering Review Board
```

## What changed

- **`EngineeringReviewPackage`** domain model (Version, Repository, Branch,
  Commit, GeneratedAt, AnalysisRunId, AnalysisDuration, ExecutiveSummary,
  OverallEngineeringHealth, OverallRisk, KeyStrengths, KeyRisks,
  RecommendedNextActions, Findings, CouncilSummary, ProviderExecution,
  EvidenceSummary, Metrics, Appendix).
- **`EngineeringHealth`** (`Excellent…Critical`) and **`EngineeringRisk`**
  (`Low…Critical`) via a documented rule-based **`HealthRiskScorer`** (no LLM).
- **`EngineeringMetrics`** and **`EvidenceSummary`** projections.
- **`IEngineeringReviewPackageBuilder`** + `EngineeringReviewPackageBuilder`
  (assembles the package; no exporter logic).
- Exporters: `MarkdownReportGenerator` → **`EngineeringReviewMarkdownExporter`**
  (renders the full review document); `JsonReportGenerator` gains `RenderPackage`.
- Git branch/commit captured (read-only `GitProbe`); project count added.
- CLI verb **`review`** surfaces the package.

## Review document structure (`engineering-review.md`)

`# Engineering Review Package` → Repository Information → Repository Statistics →
Executive Summary → Engineering Health → Overall Risk → Engineering Metrics →
Evidence Summary → Council Findings (per-discipline reviews) → Recommended Backlog
(Quick Wins / Strategic Improvements) → Appendix (Provider Execution / Merge Notes
/ Raw Findings Reference).

## Output files (per run)

`engineering-review-package.json` (primary), `engineering-review.md` (primary),
`findings.json`, `raw-findings.json`, `provider-execution.json`, `run.json`.
(`run-summary.md` / `council-summary.md` are subsumed by the review document.)

## Tests (all green — 41/41)

New `EngineeringReviewPackageTests`: health scoring (all tiers), risk scoring
(all tiers), metrics counting, package builder + integrity, Markdown export
structure. Existing pipeline test updated for the package + new artifact set.

## Exit criteria

| Criterion | Result |
|-----------|--------|
| Build (0 errors, 0 warnings) | ✅ |
| Tests pass | ✅ 41/41 |
| Package is a first-class model | ✅ `EngineeringReviewPackage` |
| Health + risk rule-based & documented | ✅ `HealthRiskScorer` + ADR-007 |
| Metrics + evidence summary | ✅ `EngineeringMetrics`, `EvidenceSummary` |
| Builder has no exporter logic | ✅ `IEngineeringReviewPackageBuilder` |
| Markdown is a projection of the package | ✅ `EngineeringReviewMarkdownExporter` |
| `review` CLI verb | ✅ outputs the package |

## Verification

```
dotnet build EngineeringCouncil.slnx        # 0 errors, 0 warnings
dotnet test  EngineeringCouncil.slnx        # Passed: 41
dotnet run --project src/EngineeringCouncil.Cli -- review --path src/EngineeringCouncil.Core --provider Mock
#   Health: Good · Risk: Low · 7 findings · engineering-review.md + engineering-review-package.json written
```

## Explicitly out of scope (future milestones)

Discipline councils · multi-provider consensus · voting · recommendation engine ·
ticket generation. The package is the object these will enrich.
