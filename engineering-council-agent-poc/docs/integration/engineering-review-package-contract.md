# Engineering Review Package — Integration Contract

This document is for the **external Engineering Review application**. It describes the
only artifact you need to consume:

```
engineering-review-package.json
```

You do **not** need to read, understand, or depend on any other file the platform
writes (`findings.json`, `raw-findings.json`, `observations.json`,
`provider-execution.json`, `provider-comparison.json`, `calibration-diagnostics.json`,
`run.json`). Those are
internal diagnostics and may change.

A representative, machine-checked example lives at
[`artifacts/samples/engineering-review-package.sample.json`](../../artifacts/samples/engineering-review-package.sample.json).

## Versioning

The package carries an explicit `schemaVersion` (currently **`1.1`**). A legacy
`version` field (`1.0`) is also present for older consumers.

- **Additive** fields (new optional properties) bump the **minor** version. Your reader
  must ignore unknown properties, so a minor bump never breaks you.
- **Breaking** changes (removing or renaming a field, changing a type or enum encoding)
  bump the **major** version and are announced.

Guarantees:

- Stable, `camelCase` JSON property names.
- Enums are serialized as `camelCase` **strings** (e.g. `"high"`, `"security"`,
  `"codeQuality"`), never integers.
- No implementation-specific .NET type names, `$type` discriminators, or polymorphic
  framework metadata are ever emitted.
- Serializing the same package twice yields identical JSON.

## Root shape (fields you can rely on)

| Property | Type | Meaning |
|----------|------|---------|
| `schemaVersion` | string | Integration schema version (`"1.1"`). |
| `version` | string | Legacy package version (`"1.0"`). |
| `repository`, `branch`, `commit` | string | Repository identity. |
| `repositorySnapshot` | object? | Deterministic reproducibility identity of the analyzed repository state (Milestone 015.3C; see below). Additive — a consumer may ignore it, but it answers "what exact source state does this review describe?". |
| `generatedAt` | ISO-8601 | When the package was produced. |
| `analysisRunId` | string | Unique run id. |
| `executiveSummary` | string | One-paragraph human summary. |
| `overallEngineeringHealth` | enum string | `excellent` … `critical`. |
| `overallRisk` | enum string | `low` … `critical`. |
| `keyStrengths`, `keyRisks`, `recommendedNextActions` | string[] | Executive lists. |
| `findings` | Finding[] | **Consolidated** findings (see below). |
| `reconciliation` | object | Multi-source reconciliation summary. |
| `acquisitionCoverage` | object | Which disciplines/sources were covered. |
| `disciplineCoverage` | object? | Deterministic per-discipline evidence coverage (Milestone 015.3A; see below). Additive — a consumer may ignore it, but it is the authoritative way to interpret "zero findings". |
| `staticAnalysisSources` | object[] | Deterministic sources imported (e.g. SARIF tools). |
| `metrics` | object | Counts (findings by severity, observations, …). |
| `evidenceSummary` | object | Provider health + observation rollup. |
| `councilSummary` | object? | Narrative summary (optional). |
| `councilAssessmentSummary` | object? | Deterministic Council-assessment counts across all consolidated findings (Milestone 014.3; see below). Additive — a consumer may ignore it. |
| `appendix` | object | Diagnostics (raw findings, reconciliation groups) — optional to read. |

### Finding (consolidated)

Each entry in `findings` is a **consolidated** finding — equivalent findings from
multiple sources have already been reconciled into one. Key fields:

| Property | Type | Meaning |
|----------|------|---------|
| `id` | string | Stable id (e.g. `SEC-1a2b3c4d5e`); same issue → same id across runs. |
| `title`, `description`, `summary` | string | Human text. |
| `category` | enum string | Engineering discipline (e.g. `security`). |
| `severity` | enum string | `info` … `critical` (the highest across sources). |
| `severityRange` | string | e.g. `"Medium–High"` when sources differed. |
| `confidence` | enum string | `low` / `medium` / `high` (reconciled, capped). |
| `recommendation` | string | Actionable guidance. |
| `fileReferences` | {path,startLine,endLine}[] | Locations. |
| `symbolReferences`, `lineReferences` | string[] / int[] | Optional. |
| `supportingProviders` | string[] | Distinct providers that corroborate it. |
| `agreementCount` | int | Number of **independent providers** (not findings). |
| `isConsolidated` | bool | True when ≥2 raw findings were reconciled. |
| `hasContradiction` | bool | True when sources materially disagree (still surfaced). |
| `contradictionReasons` | string[] | Why, when `hasContradiction`. |
| `reconciliationReason` / `reconciliationStrategy` | string | Why/how it was grouped. |
| `observationIds`, `sourceRules`, `supportingFindingIds` | string[] | Provenance. |
| `councilAssessment` | object? | Deterministic agreement classification (Milestone 014.2; see below). Additive — a consumer may ignore it. |
| `semanticReview` | object? | Targeted semantic review result (Milestone 014.4; see below). Present ONLY on the narrow subset that was reviewed. Additive — a consumer may ignore it. |

### councilAssessment (Milestone 014.2)

A pure, provider-neutral classification of how strongly independent sources agree on
the finding — computed deterministically from the same reconciliation data as the
fields above (never a new heuristic, never an LLM judge, never provider ranking).

| Property | Type | Meaning |
|----------|------|---------|
| `type` | enum string | `singleSource` / `strongAgreement` / `agreementWithDifferences` / `potentialConflict`. |
| `supportingProviders` | string[] | Mirrors the finding's own `supportingProviders`. |
| `agreementCount` | int | Mirrors the finding's own `agreementCount`. |
| `differences` | string[] | Which aspects differed across sources: `severity`, `observationType`, `location`. Empty when sources fully agree. |
| `contradictionReasons` | string[] | Populated only when `type` is `potentialConflict`. |

Interpretation: `agreementWithDifferences` means sources agree the issue exists but
described it slightly differently (e.g. a severity or location spread) — **not** a
conflict. `potentialConflict` is reserved for an explicit, unexplained contradiction;
a run with zero `potentialConflict` findings is normal and expected. A single-source
finding (`singleSource`) is never discounted — its severity/confidence stand as reported.

### semanticReview (Milestone 014.4)

Present ONLY when the finding was a semantic-review candidate — i.e. its
`councilAssessment.type` is `agreementWithDifferences` AND `differences` includes
`observationType` — AND the (opt-in, disabled-by-default) semantic reconciliation
feature was enabled for the run. Absent on every other finding.

| Property | Type | Meaning |
|----------|------|---------|
| `decision` | enum string | `sameIssue` / `differentIssues` / `inconclusive`. |
| `reason` | string | A short rationale. Never chain-of-thought, never raw model output. |

Interpretation: this is advisory provenance layered on top of the ALREADY-reconciled
finding — it never changes `severity`, `confidence`, `recommendation`, or the
finding's grouping. `differentIssues` flags a possible false merge for review; the
finding is never automatically split, so `supportingFindingIds` still reflects the
original deterministic grouping.

### councilAssessmentSummary (Milestone 014.3)

Deterministic counts of the four `councilAssessment.type` values across ALL
consolidated findings in `findings` — a pure aggregation, always consistent with
the per-finding `councilAssessment` values above.

| Property | Type | Meaning |
|----------|------|---------|
| `singleSourceCount` | int | Findings with `type: "singleSource"`. |
| `strongAgreementCount` | int | Findings with `type: "strongAgreement"`. |
| `agreementWithDifferencesCount` | int | Findings with `type: "agreementWithDifferences"`. |
| `potentialConflictCount` | int | Findings with `type: "potentialConflict"`. |
| `totalAssessed` | int | Sum of the four counts above. |

### disciplineCoverage (Milestone 015.3A)

Deterministic per-discipline evidence coverage for each **requested** discipline,
computed once by the platform from authoritative run facts (provider-execution
success/failure + consolidated findings) — never recomputed by consumers. Its purpose:
**zero findings is only meaningful when evidence was actually acquired.**

| Property | Type | Meaning |
|----------|------|---------|
| `entries` | object[] | One entry per requested discipline, in canonical discipline order. |

Each entry:

| Property | Type | Meaning |
|----------|------|---------|
| `discipline` | enum string | The discipline name (e.g. `architecture`). |
| `status` | enum string | `coveredWithFindings` / `coveredNoFindings` / `noEvidence` (see below). |
| `successfulProviders` | int | Distinct providers with a successful acquisition for the discipline. |
| `attemptedProviders` | int | Distinct providers with an acquisition step (success or failure) for the discipline. |

Status semantics:

- **`coveredWithFindings`** — at least one successful acquisition AND consolidated
  findings exist.
- **`coveredNoFindings`** — at least one successful acquisition AND zero findings. "No
  findings" is meaningful here.
- **`noEvidence`** — zero successful acquisitions (timeouts/failures never count).
  Absence of findings here must **not** be interpreted as assurance.

Interpretation: a discipline with `noEvidence` was NOT reviewed successfully; do not
report it as clean. Partial coverage (e.g. `successfulProviders: 2, attemptedProviders: 3`)
is still valid evidence. The field is absent entirely when no disciplines were requested.

### repositorySnapshot (Milestone 015.3C)

Deterministic reproducibility identity of the repository state this review describes.
Captured **before** acquisition began; the start identity is never replaced. No absolute
local paths, source contents, or secrets are ever included.

| Property | Type | Meaning |
|----------|------|---------|
| `versionControl` | string? | `"git"` when the target was positively identified as a Git working tree; null otherwise (non-Git / git unavailable). |
| `commitSha` | string? | Full HEAD SHA for Git repositories; null otherwise. |
| `branch` | string? | Current branch; null on a detached HEAD or non-Git. |
| `isDirty` | bool? | Tracked working-tree/index differs from HEAD; `false` when verified clean; null when unavailable. |
| `hasUntrackedFiles` | bool? | Untracked files exist in the analyzed selection; `false` when verified absent; null when unavailable. |
| `snapshotFingerprint` | string? | Deterministic hash (`SNAP-…`) of the post-ignore-rule analyzed file selection + content, ordered by relative path. Dirty tracked and relevant untracked files change it. |
| `repositoryChangedDuringRun` | bool | `true` when the end-of-acquisition fingerprint differed from the start — the repository mutated mid-run (the run warns, never fails). |

Interpretation: `commitSha` alone is **not** sufficient identity for a dirty or untracked
state — compare `snapshotFingerprint` values instead. A null `versionControl` means the
target had no Git identity; the `snapshotFingerprint` still identifies the analyzed files.
The field is absent for pre-M15.3C runs.

### reconciliation

| Property | Type |
|----------|------|
| `rawFindingCount` | int |
| `consolidatedFindingCount` | int |
| `multiProviderFindingCount` | int |
| `singleProviderFindingCount` | int |
| `contradictionCount` | int |
| `findingsByProvider` | { [provider]: int } |
| `preDedupFindingCount` | int — consolidated findings WITHOUT the deterministic dedup stage (M15.3D; absent in pre-M15.3D runs) |
| `postDedupFindingCount` | int — after the dedup stage, equals `consolidatedFindingCount` |
| `deduplicatedFindingCount` | int — findings removed by dedup (preDedup − postDedup) |

### staticAnalysisSources

`{ tool, version, importedResults, generatedObservations }[]` — one row per deterministic
tool run imported (e.g. a SARIF tool).

## Recommended consumer behavior

- Read `findings` as the actionable list; treat `agreementCount ≥ 2` as multi-source
  corroboration and `hasContradiction` as "review the disagreement".
- When present, `councilAssessment.type` gives the same signal pre-classified
  (`potentialConflict` ⇔ `hasContradiction`); treat its absence as "not yet assessed",
  never as a conflict.
- When present, `disciplineCoverage` tells you how to read a discipline with zero
  findings: `coveredNoFindings` = reviewed, no issues found; `noEvidence` = not reviewed,
  absence of findings is NOT assurance. Absent `disciplineCoverage` + zero findings = no
  coverage information (a run that requested nothing).
- Ignore unknown properties (forward compatibility).
- Check `schemaVersion`'s major component; refuse only on an unknown **major**.
- Never depend on the diagnostic artifacts.

A reference consumer DTO and a gate test that deserializes the package live in the test
project (`Contracts/EngineeringReviewPackageContract.cs`, `PackageContractTests`).
