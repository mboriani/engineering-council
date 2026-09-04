# ADR-007 — The Engineering Review Package is the primary product

- **Status:** Accepted
- **Date:** 2026-07-07
- **Milestone:** [MILESTONE-006](../milestones/MILESTONE-006-engineering-review-package.md)
- **Builds on:** [ADR-004](./ADR-004-finding-merger-and-council-summary.md), [ADR-006](./ADR-006-multi-provider-execution.md)

## Context

Until now the platform's "output" was a set of findings plus some Markdown/JSON
files. That framing makes Markdown feel like *the* product and leaves the actual
deliverable — a complete engineering assessment of a repository at a point in
time — implicit and scattered across files. As we add HTML, PDF, and dashboard
outputs, we need one authoritative object they all render.

## Decision

Introduce **`EngineeringReviewPackage`** as the first-class domain artifact and
the official deliverable consumed by an Engineering Review Board. Everything the
platform produces contributes to it: repository identity (repo/branch/commit),
provenance (run id, duration, generated-at), an executive view (executive
summary, **overall engineering health**, **overall risk**, key strengths/risks,
recommended next actions), the assessment body (consolidated findings, council
summary, provider execution, evidence summary, metrics) and an appendix.

Supporting decisions:

- **`EngineeringHealth`** (`Excellent…Critical`) and **`EngineeringRisk`**
  (`Low…Critical`) are computed by a **documented, rule-based** scorer
  (`HealthRiskScorer`) from the severity distribution — **no LLM**. Thresholds
  are explicit and reproducible (see below).
- **`EngineeringMetrics`** and **`EvidenceSummary`** are pure projections of the
  run and its provider execution.
- **`IEngineeringReviewPackageBuilder`** assembles the package and contains **no
  exporter logic**.
- Exporters *project* the package: `MarkdownReportGenerator` becomes
  **`EngineeringReviewMarkdownExporter`** (rendering a true review document),
  `JsonReportGenerator` gains `RenderPackage`. HTML/PDF/Dashboard exporters are
  future work behind the same idea.
- The package is written as `engineering-review-package.json` (primary machine
  artifact) and `engineering-review.md` (primary human artifact); findings,
  raw-findings, provider-execution and run JSON remain for tooling/reload.
- A new CLI verb **`review`** surfaces the package instead of raw findings.

### Health scoring rules (worst matching rule wins)

| Health | Rule |
|--------|------|
| Critical | ≥1 Critical, or ≥5 High |
| NeedsAttention | ≥1 High, or ≥8 Medium |
| Fair | ≥1 Medium, or ≥8 Low |
| Good | ≥1 Low (below Fair thresholds) |
| Excellent | no findings above Info |

### Risk scoring rules (worst matching rule wins)

| Risk | Rule |
|------|------|
| Critical | ≥1 Critical |
| High | ≥1 High, or ≥5 Medium |
| Moderate | ≥1 Medium, or ≥8 Low |
| Low | otherwise |

## Why the package is the product and Markdown is only a projection

1. **One source of truth.** A single typed object means every representation
   (Markdown, JSON, future HTML/PDF/dashboard) is guaranteed consistent — they
   render the same data instead of each re-deriving it.
2. **Stable contract for the Board.** The Review Board consumes a versioned
   `EngineeringReviewPackage` (v1.0), not a Markdown layout that changes whenever
   we tweak formatting. Presentation can evolve without breaking consumers.
3. **Composability / enrichment.** Every future capability (discipline councils,
   consensus, a recommendation engine, ticket generation) *enriches the same
   package* rather than inventing a parallel output. The package is the spine.
4. **Separation of concerns.** The builder computes the assessment; exporters
   only format. Neither leaks into the other, which keeps both testable.

## Consequences

- The repository now persists the package alongside the run; `SaveAsync` takes
  both. `AnalysisResult` carries the package.
- Health/risk are intentionally simple and rule-based; they are explainable and
  reproducible, and can be replaced by a richer (or LLM-informed) scorer later
  behind the same fields.
- `run-summary.md` / `council-summary.md` are subsumed by `engineering-review.md`
  (the review document contains their content), reducing artifact sprawl.

## Explicitly out of scope

Discipline councils, multi-provider consensus, voting, a recommendation engine,
and ticket generation. The package is the object those will enrich.
