# MILESTONE-014.2 — Deterministic Council Assessment

- **Status:** ✅ Complete
- **Date:** 2026-08-12
- **Builds on:** [MILESTONE-014.1](./MILESTONE-014.1-multi-agent-council-run.md)

## Goal

Give every **consolidated** finding a deterministic, provider-neutral **Council
assessment** — a pure classification of how strongly independent sources agree on
it — computed ONLY from data the platform already produces: the reconciler's own
fields on the finding (`SupportingProviders`, `SeverityRange`, `ContradictionReasons`)
and the M12.3/M12.4 `CalibrationDiagnosticsReport` when present. No LLM call, no
re-reconciliation, no voting, no provider ranking or weighting, no new Council
architecture.

## The four assessment types

| Type | Rule |
|------|------|
| `SingleSource` | Exactly one distinct supporting provider. Never discounted — severity/confidence are untouched. |
| `StrongAgreement` | ≥2 distinct supporting providers, no known reconciliation/diagnostic disagreement. |
| `AgreementWithDifferences` | ≥2 distinct supporting providers AND a severity, observation-type, or location difference. Still agreement — **a difference is never a conflict**. |
| `PotentialConflict` | An EXPLICIT, unexplained contradiction reason from the reconciler. Never inferred from different severity, different location, different observation type, or a provider simply not reporting the finding. Zero conflicts is a valid, expected outcome. |

## What changed

- **`ReconciliationAssessment`** (+ `ReconciliationAssessmentType`,
  `ReconciliationAssessmentDisagreement`) — new domain record in
  `EngineeringCouncil.Core.Domain`. Carries `Type`, `SupportingProviders` (sorted),
  `AgreementCount`, `Differences` (Severity / ObservationType / Location, sorted), and
  `ContradictionReasons` (only for `PotentialConflict`).
- **`CouncilAssessmentBuilder`** (`EngineeringCouncil.Core.Application`) — the
  deterministic classifier. `Apply(findings, diagnostics)` stamps every consolidated
  finding; `Assess(finding, diagnostics)` classifies one. Provider counting reuses the
  reconciler's own `SupportingProviders`/`AgreementCount` verbatim — it is never
  recomputed. Severity disagreement reads the reconciler's own en-dash `SeverityRange`;
  observation-type/location disagreement read the M12.3/M12.4 diagnostics report
  (indexed by finding id) when present; a reconciler contradiction reason is treated as
  an explicit conflict only when it does NOT already match a known difference marker
  (severity/file/location), so a location-split contradiction reason is correctly
  surfaced as a `Location` difference, not double-counted as a conflict.
- **`Finding.CouncilAssessment`** — new nullable property. Populated only on
  consolidated findings, by `EngineeringReviewPackageBuilder`
  (`CouncilAssessmentBuilder.Apply(run.Findings, run.CalibrationDiagnostics)`). Raw
  findings in `Appendix.RawFindings` are never touched.
- **Package exposure (additive)** — `findings[].councilAssessment` in
  `engineering-review-package.json`: `{ type, supportingProviders, agreementCount,
  differences, contradictionReasons }`, camelCase enum strings
  (`"singleSource"` / `"strongAgreement"` / `"agreementWithDifferences"` /
  `"potentialConflict"`). `schemaVersion` stays **1.1** — purely additive. The
  consumer DTO (`Contracts/EngineeringReviewPackageContract.cs`) gained
  `CouncilAssessmentContract`, and the committed integration sample
  (`artifacts/samples/engineering-review-package.sample.json`) was regenerated to
  include it.
- **Markdown projection** — `engineering-review.md` renders, per finding with
  supporting providers: `Council Assessment: <Type>`, `Supporting Providers: …`,
  `Differences: …` (or `None`), and `Explicit Contradictions: …` when present.
- **Internal diagnostic names never leak** — `severityDisagreement`,
  `observationTypeDisagreement`, `locationDisagreement`, and `calibration` never
  appear anywhere in the package (asserted by test).

## Verification

**Deterministic replay of the real M14.1 Council run.** `CouncilAssessmentTests`
reconstructs the exact finding shape the real M14.1 run (`20260811-011804-d3eb0a`)
produced — 13 consolidated findings (7 exclusive / 2 shared by all 3 providers / 4
shared by exactly 2), severity disagreements 2, observation-type disagreements 2,
location disagreements 5, contradictions 0 — and asserts the assessment layer
classifies it as:

- 7 × `SingleSource`, 1 × `StrongAgreement`, 5 × `AgreementWithDifferences`,
  **0 × `PotentialConflict`** (matches M14.1's own `contradictions = 0` exactly).
- 2 findings with `agreementCount = 3`, 4 with `agreementCount = 2` — mirrors M14.1.
- Differences distribution: Severity 2, ObservationType 2, Location 5 — mirrors M14.1.
- Deterministic across input order and re-serialization; package stays
  `schemaVersion 1.1`; no internal diagnostic name leaks; consumer DTO deserializes it.

**Real end-to-end smoke** (offline, zero cost): `review --provider Mock --path
./evaluation/dataset/02-vulnerable-payments` produced a real
`engineering-review-package.json` with `councilAssessment` on all 7 consolidated
findings (`"singleSource"`, `agreementCount: 1` — correct for a single-provider run)
and the matching `Council Assessment: SingleSource` block in `engineering-review.md`,
confirming the wiring works through the real pipeline, not only in-memory fixtures.

## Exit criteria

| Criterion | Result |
|-----------|--------|
| Build (0 errors, 0 warnings) | ✅ |
| All tests pass | ✅ 412/412 |
| Four assessment types implemented, deterministically | ✅ |
| Uses ONLY existing reconciliation/diagnostic data (no new heuristic) | ✅ |
| Wired into consolidated `Finding`; raw findings untouched | ✅ |
| Exposed additively in `engineering-review-package.json` (`schemaVersion` unchanged) | ✅ 1.1 |
| Rendered in `engineering-review.md` | ✅ |
| Consumer DTO gate green | ✅ |
| No conflict ever inferred from a mere difference or absence | ✅ (0 false conflicts on the M14.1 replay) |
| Reconciliation merge rules, severity/confidence rules, health/risk scoring, provider execution, M12 diagnostics, prompts, agent adapters unchanged | ✅ |

```
dotnet build EngineeringCouncil.slnx        # 0 errors, 0 warnings
dotnet test  EngineeringCouncil.slnx        # Passed: 412
```

## Deferred work (post-M14.2)

Parallel Council execution, agent-to-agent deliberation, any LLM-based judge/voting,
provider ranking or weighting, semantic reconciliation, embeddings — all remain
explicitly out of scope.
