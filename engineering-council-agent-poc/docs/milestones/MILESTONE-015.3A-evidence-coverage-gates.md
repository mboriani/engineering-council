# MILESTONE-015.3A — Evidence Coverage Gates

- **Status:** ✅ Complete
- **Date:** 2026-08-18
- **Builds on:** [MILESTONE-015.2](./MILESTONE-015.2-post-run-output-quality.md) (the first
  real evaluation, run `20260813-230240-b3e36e`), M15.2A–E
- **ADR:** none — additive, provider-neutral coverage representation over existing run
  facts; no new architectural boundary.
- **No provider/LLM invocation occurred** in this milestone. Real-data validation reuses
  the persisted M15.2 artifacts; everything else is offline and deterministic.

## Root cause (real M15.2 evaluation)

Run `20260813-230240-b3e36e` against **RedirectToService** (3 providers × 7 disciplines,
21 steps, 11 success / 10 timeout failures) showed:

- **Architecture:** 0/3 successful (all Timeout) — 0 findings
- **Testing:** 0/3 successful (all Timeout) — 0 findings
- **Security:** 2/3 successful — 5 findings

The produced package still emitted:

```
keyStrengths:
  - No Architecture issues identified.
  - No Testing issues identified.
```

"0 successful evidence acquisitions" was reported as assurance ("No X issues").
**No evidence is not the same as evidence showing no findings.**

## Coverage model

New deterministic, provider-neutral representation in Core.Domain
(`DisciplineCoverage.cs`):

| State | Condition |
|-------|-----------|
| `CoveredWithFindings` | ≥1 successful acquisition AND consolidated findings exist |
| `CoveredNoFindings` | ≥1 successful acquisition AND zero consolidated findings |
| `NoEvidence` | zero successful acquisitions (timeout/failure never counts) |

Per requested discipline, plus **SuccessfulProviders** / **AttemptedProviders**
(distinct providers; counts only — no score/percentage). Partial success (e.g. Security
2/3) is still valid evidence → `CoveredWithFindings`. No 3/3 requirement.

## Authoritative calculation

One path only: `DisciplineCoverage.From(report, consolidatedFindings, requestedDisciplines)`
in Core.Domain, computed **once** by `EngineeringReviewPackageBuilder.Build` from existing
run facts (`ProviderExecutionReport.Records` + consolidated `run.Findings` +
`run.RequestedDisciplines`). Order-independent (entries emitted in `FindingCategory` order).
Exporters/consumers never recompute it with different rules.

## Where the misleading prose was produced — and fixed

1. **`EngineeringReviewPackageBuilder.BuildStrengths`** (the exact defect): "No {discipline}
   issues identified." was emitted for any non-flagged discipline. Now emitted ONLY when a
   coverage entry exists and its status is **not** `NoEvidence`.
2. **`EngineeringReviewPackageBuilder.BuildExecutiveSummary`**: appends a coverage
   limitation sentence when any requested discipline is `NoEvidence`.
3. **`EngineeringReviewMarkdownExporter.CouncilFindings`**: each requested discipline now
   renders a section — findings normally; `CoveredNoFindings` →
   *"No findings were identified from the available evidence."* (with `Evidence coverage:
   N/M providers`); `NoEvidence` → *"No successful evidence was acquired for this
   discipline; absence of findings must not be interpreted as assurance."* Partial
   `CoveredWithFindings` sections also show `Evidence coverage: 2/3 providers`. The old
   blanket `_No findings were produced for this run._` is gone.
4. **`EngineeringReviewMarkdownExporter.HealthAndRisk`**: adds a compact **Coverage
   limitation** note (scoring algorithm intentionally unchanged — no health-score redesign).

## Package / schema decision

**Additive package field `disciplineCoverage`** (root-level, optional, provider-neutral).
Justified by the brief's rule: consumers need coverage to correctly interpret "zero
findings", and the Markdown exporter (a package projection) needs it to render the
distinction without recomputing. Mirrors the `councilAssessmentSummary` precedent (M014.3):
**`schemaVersion` stays `1.1`** (additive, optional, safe to ignore). Consumer DTO
(`PackageContract.DisciplineCoverage`) updated and the gate is green; the integration
sample was regenerated and includes the field.

## Real M15.2 regression (deterministic fixture + persisted artifacts)

- Regression fixture reproduces the exact defect shape (3×7, Architecture/Testing 0/3,
  Security 2/3) and asserts Architecture/Testing → `NoEvidence` 0/3.
- A best-effort offline test recomputes coverage from the **persisted** M15.2 artifacts
  (`outputs/20260813-230240-b3e36e/provider-execution.json` + package) and confirms the
  same shape — skipped when the artifacts are absent.

## Tests

New `DisciplineCoverageTests.cs` (20 tests, fully offline — pure facts + projections; no
providers/network/credentials):

1. successful evidence + findings → CoveredWithFindings
2. successful evidence + no findings → CoveredNoFindings
3. zero successful → NoEvidence
4. timeout does not count as evidence
5. failure does not count as evidence
6. partial success (2/3) counts as evidence
7. successful/attempted provider counts correct
8. calculation order-independent (reversed records + disciplines)
9. M15.2 Architecture regression → NoEvidence 0/3
10. M15.2 Testing regression → NoEvidence 0/3
11. M15.2 Security partial coverage → CoveredWithFindings 2/3
12. control discipline with evidence/no findings → CoveredNoFindings
13. Markdown never says "no issues" for NoEvidence
14. Markdown warns absence of evidence is not assurance (per-discipline + summary + health)
15. CoveredNoFindings wording distinct from NoEvidence (both coexist in M15.2 shape)
16. Markdown shows evidence coverage counts (0/3 and 2/3)
17. package serializes disciplineCoverage; consumer DTO round-trips; schemaVersion 1.1
18. no disciplineCoverage when nothing requested
19. findings/reconciliation unchanged by coverage gates
20. persisted M15.2 artifacts reproduce the NoEvidence shape (best-effort)

Build 0/0 warnings/errors; full suite **524/524 tests pass** (20 new + 504 existing green;
the one strengths assertion in `EngineeringReviewPackageTests` was updated to the new
"no evidence ⇒ no 'no issues' strength" semantic).

## Production behavior intentionally unchanged

- Finding generation, analyzers, and deterministic reconciliation — untouched.
- Health/risk scoring (`HealthRiskScorer`) — untouched; the report now *states* the
  coverage limitation instead.
- Acquisition, execution, provider telemetry, token/cache/derived metrics — untouched.
- No prompt/LLM changes.

## Documentation

- `PROJECT_MEMORY.md` → v32; `DECISION_LOG.md` → D-105 (the NoEvidence vs CoveredNoFindings
  semantic becomes a durable decision).
- `docs/integration-contract.md` + `docs/integration/engineering-review-package-contract.md` —
  additive `disciplineCoverage` documented; `schemaVersion` 1.1 unchanged.
- `docs/runbooks/RUNBOOK.md` — operators must interpret the three coverage states.

## Remaining limitation

- **Static-analysis-only findings:** coverage is defined strictly by *provider evidence
  acquisition* success (per the brief). A discipline whose findings came from a static
  SARIF import with no provider-execution record would still read `NoEvidence` at 0/0.
  No such case exists in current runs; the semantic is documented and consciously chosen.
- **Health/risk scores** still cannot incorporate missing evidence without a larger
  redesign (explicitly a NON-GOAL); the report now always states the limitation.
- The Council's run-level executive sentence "found no actionable engineering
  opportunities" (in `RuleBasedCouncilSummaryGenerator`) is left unchanged; the package
  builder's executive summary appends the coverage caveat that prevents misreading it.

## Recommended next step

- **M15.3B** (do NOT start yet): likely acquisition robustness (bounded retries/adaptive
  timeouts) so `NoEvidence` disciplines shrink in real runs — the coverage gates make
  exactly those gaps visible now.