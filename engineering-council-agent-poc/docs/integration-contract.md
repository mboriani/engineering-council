# Integration Contract — entry point

The external Engineering Review application consumes exactly **one** artifact:

```
engineering-review-package.json
```

Full field-by-field reference:
**[docs/integration/engineering-review-package-contract.md](./integration/engineering-review-package-contract.md)**
Machine-checked example:
**[artifacts/samples/engineering-review-package.sample.json](../artifacts/samples/engineering-review-package.sample.json)**

## Current version

`schemaVersion` = **1.1** — unchanged by Milestones 011, 011.2, 011.3, 012.1, 012.2, 012.3,
014.2, 014.3, 014.4, and 015.3A. Integrating real Claude
and OpenAI providers added no consumer-visible field, the domain/metrics correctness
milestone (011.2) added only **additive** fields inside `metrics`/`evidenceSummary`
(`partialProviders`, `unclaimedObservations`), the context-budget correctness milestone
(011.3) only clarified an internal context metric (`contextFilesConsidered`) that consumers
already ignore, the comparison/calibration milestones (012.2/012.3) produced only
internal diagnostic artifacts, the deterministic Council assessment milestone (014.2)
added only the **additive**, optional `findings[].councilAssessment` object, the
Council assessment evaluation milestone (014.3) added only the **additive**, optional
root-level `councilAssessmentSummary` object, and the targeted semantic reconciliation
milestone (014.4) added only the **additive**, optional `findings[].semanticReview`
object — and the evidence coverage gates milestone (015.3A) added only the **additive**,
optional root-level `disciplineCoverage` object (per-requested-discipline coverage state
`coveredWithFindings` / `coveredNoFindings` / `noEvidence` + successful/attempted provider
counts) — so no version bump was required.

## What real LLM providers changed for consumers

Nothing structural. Model-backed evidence enters the same pipeline and ends as the same
consolidated findings:

- `findings[].supportingProviders` may now contain `"Claude"` or `"OpenAI"` alongside
  `"SARIF"` / `"Mock"` — treat these as opaque source names.
- `findings[].agreementCount` may rise when a model and a deterministic source corroborate
  the same issue.
- `providerExecution` may report models and token usage. **Token and cost fields are
  optional** — never depend on their presence.

There is deliberately **no** `claude-review.json`, `openai-review.json`, or any other
provider-specific final result. Provider payloads, prompts, credentials, headers and SDK
types never appear in the package.

## What deterministic Council assessment (Milestone 014.2) changed for consumers

Each consolidated finding may now carry an optional `councilAssessment` object — a
deterministic, provider-neutral classification (`singleSource` / `strongAgreement` /
`agreementWithDifferences` / `potentialConflict`) of how strongly independent sources
agree, computed entirely from data already present on the finding. No LLM judge, no
voting, no provider ranking. See the field reference for details; the field is purely
additive and safe to ignore.

## What Council assessment evaluation (Milestone 014.3) changed for consumers

The package root may now carry an optional `councilAssessmentSummary` object — deterministic
counts of the four `councilAssessment.type` values across all consolidated findings
(`singleSourceCount` / `strongAgreementCount` / `agreementWithDifferencesCount` /
`potentialConflictCount` / `totalAssessed`). It is a pure aggregation of the per-finding
values above, computed once, with no independent recalculation anywhere. Purely additive
and safe to ignore.

## What targeted semantic reconciliation (Milestone 014.4) changed for consumers

A consolidated finding may now carry an optional `semanticReview` object — present
ONLY on the narrow subset whose `councilAssessment.differences` includes
`observationType` (2/13 findings in the real reference run), and only when the
opt-in, disabled-by-default feature was enabled. It never changes the finding's
severity, confidence, recommendation, or grouping; it is advisory provenance only
(`sameIssue` / `differentIssues` / `inconclusive` + a short reason). No LLM judge
over the whole Council, no voting, no provider ranking — see the field reference for
details. Purely additive and safe to ignore.

## What evidence coverage gates (Milestone 015.3A) changed for consumers

The package root may now carry an optional `disciplineCoverage` object — deterministic
per-requested-discipline evidence coverage, computed once from authoritative run facts:
each entry has a `status` (`coveredWithFindings` / `coveredNoFindings` / `noEvidence`)
plus `successfulProviders` / `attemptedProviders` counts. Use it to correctly interpret
**zero findings**: a discipline with `noEvidence` (zero successful acquisitions —
timeouts/failures never count) was NOT successfully reviewed, so "no findings" there
must not be treated as assurance. A discipline with `coveredNoFindings` WAS reviewed and
had no findings. Purely additive and safe to ignore (the package is still fully
consumable without it). See the field reference for details.

## Consumer rules (unchanged)

- Read `findings` as the actionable list; ignore unknown properties.
- Check only the **major** component of `schemaVersion`; additive changes bump the minor.
- Never read the diagnostic artifacts (`findings.json`, `raw-findings.json`,
  `observations.json`, `provider-execution.json`, `provider-comparison.json`/`.md`,
  `calibration-diagnostics.json`, `run.json`). The multi-LLM provider comparison
  (Milestone 012.2) and finding calibration diagnostics (Milestone 012.3) are
  strictly internal and may change without notice — never depend on them.

A reference consumer DTO (`Contracts/EngineeringReviewPackageContract.cs`) and the
`PackageContractTests` gate live in the test project; the milestone is not accepted unless
that DTO deserializes the produced package.
