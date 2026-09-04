# MILESTONE-010 — Deterministic Multi-Source Reconciliation

- **Status:** ✅ Complete
- **Date:** 2026-07-22
- **Version:** v10 (one consolidated package from many sources)
- **ADR:** [ADR-011](../adr/ADR-011-deterministic-multi-source-reconciliation.md)

## Goal

Reconcile findings from multiple evidence sources (Claude, Codex, SARIF, future
providers) into ONE consolidated finding collection **before** building the Engineering
Review Package, keeping `engineering-review-package.json` the single, stable,
machine-consumable integration artifact for the external Engineering Review application.
Deterministic only — no LLM, no voting, no provider weights, no councils.

## Pipeline

```
… → Discipline Analyzers → Raw Findings
      → Deterministic Reconciliation (IFindingReconciler)
      → Consolidated Findings → EngineeringReviewPackageBuilder
      → engineering-review-package.json → External Engineering Review Application
```

## What changed

- **`IFindingReconciler` + `RuleBasedFindingReconciler`** — staged, explicit grouping
  (1: exact-rule-location · 2: type-symbol · 3: title-location · 4: keep-separate),
  provider-independent `AgreementCount`, severity/confidence reconciliation, contradiction
  flags, stable ids. Runs before the package builder; prefers false negatives to bad merges.
- **`ReconciliationResult / ReconciliationGroup / ReconciliationSummary`** — result +
  internal traceability + provider-neutral summary.
- **`TitleNormalizer`** — deterministic lowercasing, provider-prefix + file-path stripping,
  punctuation/whitespace folding, and an explicit technical-synonym dictionary. No embeddings.
- **`Finding` (additive)** — `SupportingFindingIds`, `AgreementCount`, `SeverityRange`,
  `ConfidenceRange`, `ReconciliationReason`, `ReconciliationStrategy`, `IsConsolidated`,
  `HasContradiction`, `ContradictionReasons`, `SymbolReferences`, `LineReferences`, `Metadata`.
- **`AnalysisRun` (additive)** — `ReconciliationSummary`, `ReconciliationGroups`
  (`Findings` = consolidated, `RawFindings` preserved).
- **`EngineeringReviewPackage` (additive)** — `SchemaVersion "1.1"`, `Reconciliation`
  summary, `Appendix.ReconciliationGroups` (legacy `Version "1.0"` retained).
- **Pipeline** consolidates via the reconciler (the `IFindingMerger` stays registered +
  tested for backward compatibility but is off the pipeline).
- **Markdown** renders consolidated findings only and a concise `## Multi-Source
  Reconciliation` section (raw/consolidated/multi-provider/contradictions/providers), with
  per-finding provider support, agreement count and severity range.
- **Integration contract** — a consumer-facing DTO (`PackageContract`) + gate test that
  deserializes the package, and a preserved
  `artifacts/samples/engineering-review-package.sample.json`.

## Reconciliation rules (documented)

| Aspect | Rule |
|--------|------|
| Grouping | same discipline required; stage 1 rule+file+close-lines → 2 type+file+symbol → 3 title(Jaccard≥0.6)+file/symbol → else separate |
| Agreement | distinct provider identities, not finding count |
| Severity | keep highest; record `Min–Max` range; agreement never inflates severity |
| Confidence | start = highest source; +1 for ≥2 providers, +2 for deterministic+LLM; capped High; location disagreement → Medium; discipline-mismatch → −1; never an average |
| Contradiction | severity spread ≥2 levels or disjoint files → flagged (`HasContradiction`), not silently merged |
| Stable id | SHA-256 of discipline+primaryRule+primaryFile+primarySymbol+normalizedTitle → `SEC-…`; order-independent |

## Tests (all green — 112/112, +24 new)

`ReconciliationTests` (15): exact rule+location · type+symbol · title+location · different
disciplines don't merge · different locations don't merge · single-provider duplicates
don't inflate agreement · multi-provider agreement preserved · severity range · confidence
determinism + single-provider not inflated · contradictions flagged · stable ids ·
deterministic output order · raw findings unmutated · summary metrics. `PackageContractTests`
(8): schema version · consolidated-not-raw · findings.json=consolidated / raw-findings.json=raw
· backward compatibility · no leaked .NET types + deterministic serialization · consumer DTO
deserializes · markdown provider support · sample generation. `ReconciliationEndToEndTests`
(1): Claude-/Codex-/SARIF-style sources reconcile through the real pipeline; package uses
consolidated findings; written JSON accepted by the consumer contract.

## Exit criteria

| Criterion | Result |
|-----------|--------|
| Build (0 errors, 0 warnings) | ✅ |
| Tests pass | ✅ 112/112 |
| Raw findings reconciled before package | ✅ |
| Package builder receives consolidated findings | ✅ |
| `engineering-review-package.json` generated | ✅ |
| Stable integration contract (schemaVersion, camelCase, no leaked types) | ✅ |
| Consumer DTO deserializes the package | ✅ |
| Sample package produced | ✅ |
| External app needs no diagnostic artifact | ✅ |

## Verification

```
dotnet build EngineeringCouncil.slnx        # 0 errors, 0 warnings
dotnet test  EngineeringCouncil.slnx        # Passed: 112
dotnet run --project src/EngineeringCouncil.Cli -- review \
    --provider Mock --sarif ./artifacts/results.sarif --path .
#   → raw findings reconciled; package + markdown carry a Multi-Source Reconciliation
#     summary; schemaVersion 1.1; a Mock+SARIF pair consolidates across providers.
```

## Explicitly out of scope (future milestones)

LLM reconciliation · voting · provider weights · Discipline Councils · Engineering Council
deliberation · recommendation engine · ticket generation · parallel execution · embeddings
· vector databases.
