# MILESTONE-014.3 — Council Assessment Evaluation

- **Status:** ✅ Complete
- **Date:** 2026-08-12
- **Builds on:** [MILESTONE-014.2](./MILESTONE-014.2-deterministic-council-assessment.md)

## Goal

Evaluate the usefulness of the deterministic Council using the real M14.1 result
shape and produce an explicit, evidence-based decision about whether another
reconciliation layer is actually necessary. Small milestone: no new merge rules, no
semantic reconciliation, no LLM judge.

## 1. Evaluating the M14.1-shaped fixture

Using the deterministic fixture already in `CouncilAssessmentTests.cs`
(`BuildM141ShapedRun`, which replays the EXACT finding shape of the real M14.1
Council run `20260811-011804-d3eb0a`), the 13 consolidated findings classify as:

| Type | Count | % |
|------|------:|--:|
| SingleSource | 7 | 53.8% |
| StrongAgreement | 1 | 7.7% |
| AgreementWithDifferences | 5 | 38.5% |
| PotentialConflict | 0 | 0.0% |

**Per-finding breakdown for the 5 `AgreementWithDifferences` findings** (which
existing disagreement type(s) caused the classification):

| Finding | Providers | Severity | ObservationType | Location | Contradiction |
|---------|-----------|:--------:|:----------------:|:--------:|:--------------:|
| S-02 | OpenCode, Codex, ClaudeCode (3) | ✓ | | ✓ | |
| S-03 | Codex, ClaudeCode (2) | ✓ | | ✓ | |
| S-04 | OpenCode, ClaudeCode (2) | | ✓ | ✓ | |
| S-05 | OpenCode, Codex (2) | | ✓ | ✓ | |
| S-06 | ClaudeCode, Codex (2) | | | ✓ | |

Difference-type totals across those 5 findings:

- **Severity:** 2 (S-02, S-03)
- **ObservationType:** 2 (S-04, S-05)
- **Location:** 5 (all 5 — every `AgreementWithDifferences` finding carries a
  location difference; this tracks the M12.4 `LocationDisagreement` definition,
  where independent sources rarely report identical line numbers for the same issue)
- **Contradiction (explicit):** 0

No classification rule was changed to shape these numbers — they are read directly
off the existing M14.1-shaped fixture, unmodified from M14.2.

## 2 & 3. Council Assessment Summary

**`CouncilAssessmentSummary`** (`EngineeringCouncil.Core.Domain`) — four required
`int` counts (`SingleSourceCount`, `StrongAgreementCount`,
`AgreementWithDifferencesCount`, `PotentialConflictCount`) plus a computed
`TotalAssessed`. Counts only; no rates, no scoring.

**One authoritative calculation** — `CouncilAssessmentBuilder.Summarize(findings)`
is a pure aggregation of each finding's own, already-computed
`Finding.CouncilAssessment.Type` (findings without an assessment, e.g. raw findings,
are excluded from every count — never guessed). `EngineeringReviewPackageBuilder`
calls `Summarize` exactly once, over the same assessed findings it just produced via
`CouncilAssessmentBuilder.Apply`, and stores the result on
`EngineeringReviewPackage.CouncilAssessmentSummary`. No exporter recomputes it —
`EngineeringReviewMarkdownExporter` reads `package.CouncilAssessmentSummary`
directly.

**Package (additive, `schemaVersion` unchanged at `1.1`):**

```json
"councilAssessmentSummary": {
  "singleSourceCount": 7,
  "strongAgreementCount": 1,
  "agreementWithDifferencesCount": 5,
  "potentialConflictCount": 0,
  "totalAssessed": 13
}
```

**Markdown** (`## Council Assessment`, compact, right after the findings section):

```
## Council Assessment

- Strong agreement: 1
- Agreement with differences: 5
- Single source: 7
- Potential conflicts: 0
```

**Consumer DTO** — `CouncilAssessmentSummaryContract` added to
`Contracts/EngineeringReviewPackageContract.cs`; `PackageContractTests` (the
consumer-DTO gate) remains green.

**Real offline verification** (not just the fixture): `review --provider Mock
--path evaluation/dataset/02-vulnerable-payments` produced an actual package with
`councilAssessmentSummary: {singleSourceCount:7, strongAgreementCount:0,
agreementWithDifferencesCount:0, potentialConflictCount:0, totalAssessed:7}` (all
single-provider, as expected for a one-provider run) and the matching markdown
block — confirming the wiring end to end.

## 4. Decision Gate

**Decision: B — Deterministic reconciliation is sufficient for most findings, but
a small ambiguous subset could benefit from optional semantic review.**

### Reasoning

- **Zero `PotentialConflict` findings (0/13).** The deterministic reconciler never
  produced an unresolved, unexplained contradiction in this run. Every difference it
  detected was successfully classified as a *difference*, not a *conflict* — the
  M14.2 rule (an explicit, unexplained contradiction reason is required for
  `PotentialConflict`) held for 100% of the real M14.1 findings.
- **Most differences are low-stakes and already resolved usefully.** Every single
  `AgreementWithDifferences` finding carries a `Location` difference — independent
  providers rarely report identical line numbers for the same issue, and the
  reconciler already resolves this (keeps the highest severity, unions file/line
  references). `Severity`-only-plus-location differences (S-02, S-03) are
  well-explained: providers agree on *what* and *where*, they simply weighted
  impact differently, and "take the highest" is a safe, standard default —
  no semantic judgment is needed to act on these.
- **A small, well-defined subset is genuinely ambiguous.** The 2 findings with an
  **`ObservationType` disagreement** (S-04, S-05 — 2/13 ≈ 15%) are different: the
  providers didn't just disagree on severity or exact line, they disagreed on **what
  kind of issue it is**, and in both cases the location also differs. Deterministic
  rules can only observe *that* the type differs — they cannot determine whether
  this is (a) the same underlying issue described in different terminology by two
  sources, which is a harmless labeling difference, or (b) two genuinely different
  issues that happened to be reconciled together because they are co-located (a
  possible false merge that would quietly hide one of the two issues inside a single
  consolidated finding). That distinction requires understanding the *meaning* of
  the two observation types — outside what deterministic rules can decide.
- This is a **small** subset (2/13 findings in this run) and it is **narrowly
  identifiable** (exactly the findings whose `Differences` include
  `ObservationType`) — it does not indicate a systemic weakness across "a meaningful
  portion" of findings (which would justify **C**), and it is not simply "zero
  ambiguity" either (which would justify a clean **A**).

### Future semantic-review candidates (identified only — not implemented)

If a semantic-review layer is ever added, it should be scoped to exactly:

- **Consolidated findings whose `CouncilAssessment.Differences` includes
  `ObservationType`.** These are the cases where deterministic evidence alone
  cannot distinguish "same issue, different label" from "two distinct issues
  merged." Review would confirm (or split) the consolidation — nothing else.

Explicitly NOT candidates: `Severity`-only differences (safely resolved by "take
the highest"), `Location`-only differences (expected line-number variance for the
same issue), or `SingleSource`/`StrongAgreement` findings (nothing to review).

## Tests (all green — 420/420, +8 new)

`CouncilAssessmentTests.cs` (Summary region): counts each assessment type from a
mixed set · zero counts supported · findings without an assessment are excluded, not
guessed · deterministic across finding order · package serializes the summary
additively (`schemaVersion` unchanged) · consumer DTO deserializes it · Markdown
renders the compact section · the M14.1-shaped fixture produces the exact expected
summary AND matches an independently-called `Summarize()` (proving there is only one
authoritative calculation).

## Exit criteria

| Criterion | Result |
|-----------|--------|
| Build (0 errors, 0 warnings) | ✅ |
| All tests pass | ✅ 420/420 |
| M14.1-shaped fixture evaluated, numbers unmodified | ✅ 7/1/5/0 |
| Difference breakdown reported per `AgreementWithDifferences` finding | ✅ |
| Council Assessment Summary — one authoritative calculation | ✅ `CouncilAssessmentBuilder.Summarize` |
| Exposed additively in `engineering-review-package.json` | ✅ `schemaVersion` stays 1.1 |
| Exposed in `engineering-review.md` (compact section) | ✅ |
| Consumer DTO gate green | ✅ |
| No classification rule changed | ✅ |
| Explicit decision made | ✅ **B** |
| No next layer implemented | ✅ |

```
dotnet build EngineeringCouncil.slnx        # 0 errors, 0 warnings
dotnet test  EngineeringCouncil.slnx        # Passed: 420
```

## Non-goals (confirmed not introduced)

LLM judge, semantic reconciliation, embeddings, fuzzy matching, provider voting,
provider ranking, provider weighting, new merge rules, location-tolerance changes,
health/risk scoring changes, new providers, parallel execution, agent debate.
