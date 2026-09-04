# MILESTONE-009 — Native SARIF Evidence Source

- **Status:** ✅ Complete
- **Date:** 2026-07-21
- **Version:** v9 (first real deterministic evidence source)
- **ADR:** [ADR-010](../adr/ADR-010-native-sarif-provider.md)

## Goal

Validate the architecture by integrating the first **real, deterministic** evidence
source. Prove that heterogeneous evidence sources participate in the same pipeline
**without modifying analyzers, interpreters, or the Engineering Review Package**. Not
about analysis quality — about the seams holding.

## New flow

```
Repository → SARIF Evidence Provider → Evidence[] (one per run)
   → SARIF Evidence Interpreter → EngineeringObservation[]
   → existing discipline analyzers → Findings → Engineering Review Package
```

No analyzer knows SARIF exists; only the interpreter understands the schema.

## What changed

- **`SarifEvidenceProvider`** — Static, Repository-scoped, all disciplines, no analyzer
  instructions. Imports one or many SARIF 2.1.0 files; produces **one `Evidence` per
  run**, preserving tool name/version, invocation metadata, artifact/result counts, and
  the original SARIF payload. Malformed/missing files are skipped (no throw); an empty
  run yields evidence with zero results.
- **`SarifEvidenceInterpreter`** — the only SARIF-aware component. Maps each `result`
  to one observation with deterministic discipline/type/severity/confidence
  (`SarifMappings`), file/line/column references, evidence excerpt (SARIF snippet only —
  repository files are not re-opened), rule metadata (RuleId/RuleName/Description/
  HelpUri/Tags/Properties in `Metadata`), and full provenance.
- **Provider contract** — `IEvidenceProvider.CollectAsync` now returns
  `IReadOnlyList<Evidence>` so one repository step can yield one-evidence-per-run. The
  executor stamps provenance on each item and records `EvidenceCount = n`. LLM
  providers return a one-item list.
- **Domain** — `EngineeringObservation.ColumnReferences`; `FindingCategory.Unknown` and
  `ObservationTypes.Unknown` (unmapped rules stay Unknown); `AnalysisRun.Evidence`;
  `StaticAnalysisSource` + `EngineeringReviewPackage.StaticAnalysisSources`.
- **Package + markdown** — `StaticAnalysisSources` (Tool, Version, Results Imported,
  Observations Generated) in `engineering-review-package.json` and a concise
  **## Static Analysis Sources** appendix in `engineering-review.md` (no raw SARIF).
- **Config/CLI/API** — `Evidence:Sarif:{Enabled,Files}`; CLI `--sarif <file>`
  (repeatable) enables the source and merges with config; the SARIF source is
  auto-included whenever it is active. API reads the same config section.
- **DI** — real `SarifEvidenceProvider` (was a stub) + registered
  `SarifEvidenceInterpreter` (was an unregistered skeleton).

## Deterministic mappings

| Aspect | Rule |
|--------|------|
| Severity | error→Critical · warning→High · note→Medium · none→Low · missing→High (+ confidence penalty) |
| Discipline | keyword scan of id+name+description+tags; Security→Reliability→Testing→Observability→Documentation→Architecture→CodeQuality; else **Unknown** |
| Observation type | keyword scan → known `ObservationTypes`, else **Unknown** |
| Confidence | 0 deficiencies→High (0.95) · 1→Medium (0.60) · 2+→Low (0.30) |

## Tests (all green — 88/88, +22 new)

`SarifTests`: valid SARIF (one evidence, tool metadata + payload preserved) · malformed
(skipped, no throw) · empty run (evidence, zero results, zero observations) · multiple
runs → multiple evidence · multiple files → all imported · repository scope + IsAvailable ·
repository scope executes once · interpreter claims only SARIF static evidence ·
discipline+type+severity mapping · complete severity table (theory) · unknown fallback ·
confidence high vs reduced · line/column/file/rule metadata · rule-by-index resolution ·
multiple results → multiple observations · provenance stamped & carried · StaticAnalysisSources
metric · markdown appendix. `SarifEndToEndTests`: full DI pipeline — one scan, SARIF once,
observations from SARIF, **existing analyzers consume them unmodified**, package + appendix +
Finding→Observation→Evidence→SARIF-rule traceability.

## Exit criteria

| Criterion | Result |
|-----------|--------|
| Build (0 errors, 0 warnings) | ✅ |
| Tests pass | ✅ 88/88 |
| Repository scanned once | ✅ |
| SARIF provider executed once (Repository scope) | ✅ |
| Observations generated from SARIF | ✅ |
| Existing analyzers consume them without modification | ✅ |
| Engineering Review Package generated | ✅ |
| `engineering-review.md` has Static Analysis Sources appendix | ✅ |
| Finding → Observation → Evidence → SARIF rule traceable | ✅ |

## Verification

```
dotnet build EngineeringCouncil.slnx        # 0 errors, 0 warnings
dotnet test  EngineeringCouncil.slnx        # Passed: 88
dotnet run --project src/EngineeringCouncil.Cli -- review \
    --provider Sarif --sarif ./artifacts/results.sarif --path .
#   → scan once; SARIF once; 3 results → 3 observations →
#     Security (Critical) / Reliability (High) / CodeQuality (Medium) analyzers each
#     produce a finding; Static Analysis Sources appendix (ExampleScanner 3.1.0, 3→3).
```

## Explicitly out of scope (future milestones)

Reconciliation · Engineering Council · Discipline Councils · provider weighting/voting/
comparison · recommendation engine · duplicate merging · ticket generation · parallel
execution · re-opening repository files to enrich SARIF snippets.
