# MILESTONE-012.4 — Observation Calibration Diagnostics

- **Status:** ✅ Complete
- **Date:** 2026-08-08
- **Version:** v16 (observation calibration diagnostics)
- **ADR:** [ADR-020](../adr/ADR-020-observation-calibration-diagnostics.md)
- **Builds on:** [MILESTONE-012.3](./MILESTONE-012.3-finding-calibration-diagnostics.md)

## Goal

Extend the internal calibration diagnostics one level lower — from reconciled
findings to the **normalized EngineeringObservations** that support them — by
identifying, for Comparable multi-provider executions, cases where providers
support the **same reconciled finding** but disagree about the **ObservationType**
or the **source location**. A pure projection of existing completed-run data; no
provider calls, no rescan, no re-interpretation, no semantic/fuzzy matching, and
no change to the external package.

## Scope

Exactly **two** new diagnostic types, added additively to the existing
`calibration-diagnostics.json` artifact (no new artifact, no new exporter):

- **`ObservationTypeDisagreement`** — a shared finding (≥2 compared providers)
  whose attributed supporting observations expose **different normalized
  ObservationTypes**. Exact values compared; no equivalence rules; never decides
  which type is correct.
- **`LocationDisagreement`** — a shared finding whose attributed supporting
  observations reference **meaningfully different normalized locations** (different
  file, or same file with different explicit lines). Exact existing location
  semantics; no fuzzy matching, no line-distance tolerance, no semantic inference.

Both apply **only** when `ProviderComparisonStatus = Comparable`; `NonComparable`
and `Incomplete` keep the existing limitation behavior (never diagnostics).

M12.3 diagnostics (`ExclusiveFinding`, `SeverityDisagreement`, `LowAgreement`) are
**unchanged** — same thresholds, same semantics.

## Provenance path

Provider attribution comes exclusively from existing provenance — never from text,
titles, ids, or ordering:

```
Consolidated Finding → SupportingFindingIds → Raw Findings → ObservationIds
  → EngineeringObservations → Evidence / Provider
    (Observation.SourceProvider, else SourceEvidenceId → Evidence.ProviderName)
```

The existing reconciler remains authoritative about which provider findings belong
together; un-attributable observations are **skipped**, never guessed.

## Changes

- **`Core/Domain/CalibrationDiagnostics.cs` (additive)** —
  `CalibrationDiagnosticType` + `ObservationTypeDisagreement`,
  `LocationDisagreement`; new `ProviderObservationType` (Provider + sorted
  ObservationTypes) and `ProviderLocation` (Provider + sorted NormalizedLocations)
  records; `FindingDiagnostic` + `Providers`, `ObservationIds`,
  `ObservationTypesByProvider`, `LocationsByProvider` (populated only for the new
  types). M12.3 fields untouched.
- **`Core/Application/CalibrationDiagnosticsBuilder.cs` (additive)** — the
  Comparable branch now also emits the two new signals over
  `sharedFindings` (same ≥2-compared-provider gate as SeverityDisagreement).
  `AttributeObservations` walks SupportingFindingIds → raw findings →
  ObservationIds and attributes each observation via `ObservedProvider`
  (SourceProvider, else evidence). Type disagreement compares per-provider
  distinct type sets; location disagreement compares per-provider normalized
  locations (`NormalizeLocationPath` mirrors the reconciler; missing-line is
  compatible; missing location contributes nothing). All collections sorted.
- **No pipeline / reporting / persistence change** — `calibration-diagnostics.json`
  is written by the existing M12.3 wiring; the new signals simply appear in it.

## Regression tests

New tests in `src/EngineeringCouncil.Tests/CalibrationDiagnosticsTests.cs`
(**14 focused tests**, offline / deterministic / no keys / no network):

1. Different ObservationTypes on a shared finding ⇒ `ObservationTypeDisagreement`
2. Same ObservationType ⇒ no signal
3. Provider attribution comes from provenance (blank `SourceProvider` resolved via
   `SourceEvidenceId` → `Evidence.ProviderName`; an un-attributable observation is
   skipped, never guessed)
4. Different files ⇒ `LocationDisagreement`
5. Same file, different explicit lines ⇒ `LocationDisagreement`
6. Same normalized location ⇒ no signal
7. One provider missing location ⇒ no false `LocationDisagreement`
8. Provider-exclusive finding ⇒ neither new disagreement diagnostic (M12.3
   `ExclusiveFinding` still surfaces)
9. NonComparable ⇒ neither new diagnostic (limitation preserved)
10. Incomplete ⇒ neither new diagnostic (limitation preserved)
11. Existing `ExclusiveFinding` tests remain green (unchanged)
12. Existing `SeverityDisagreement` tests remain green (unchanged)
13. Existing `LowAgreement` tests remain green (unchanged)
14. Deterministic output regardless of observation/raw-finding ordering
15. `calibration-diagnostics.json` serializes the new signals
16. `engineering-review-package.json` never carries the new signals (unchanged)
17. Consumer-DTO gate green (`schemaVersion` 1.1)

### Acceptance fixture

One deterministic fixture (Claude + OpenAI · Security · Comparable) with:

| Finding | Scenario | Expected |
|---------|----------|----------|
| A (`REL-abc123`) | shared · different ObservationTypes · same location | `ObservationTypeDisagreement` |
| B (`SEC-xyz789`) | shared · different locations · same ObservationType | `LocationDisagreement` |
| C (`REL-def456`) | shared · same type and location | no new diagnostic |

Result: exactly the two new diagnostics; Finding C clean. No reconciliation rules
were modified — existing run data was constructed to represent the scenario.

## Verification

| Criterion | Result |
|-----------|--------|
| Build (0 errors, 0 warnings) | ✅ |
| All tests pass | ✅ 275/275 (14 new; 261 prior) |
| Both new diagnostics generated from existing provenance | ✅ |
| M12.3 diagnostics unchanged | ✅ same thresholds/semantics; tests green |
| No additional provider execution | ✅ pure projection; call-count tests green |
| `calibration-diagnostics.json` deterministic | ✅ ordering-independence test |
| `engineering-review-package.json` unchanged | ✅ no calibration content; `schemaVersion` **1.1** |
| Consumer-DTO gate green | ✅ `PackageContract` deserializes; `schemaVersion` **1.1** |

```
dotnet build EngineeringCouncil.slnx        # 0 errors, 0 warnings
dotnet test  EngineeringCouncil.slnx        # Passed: 275 (no credentials required)
```

## Limitations

- An observation whose provider cannot be reliably attributed is skipped (recorded
  as nothing) rather than guessed.
- A missing location is never treated as a disagreement; no `MissingLocation`
  diagnostic type exists yet.
- Location equivalence treats a missing explicit line as compatible on the same
  path; no line-distance tolerance exists.
- Observation types are compared by exact normalized value only — no synonym or
  semantic rules.

## Deferred work

Observation-count divergence, retry/repair/truncation/token divergence, context
utilization diagnostics, provider ranking/scoring/weighting, automatic provider
selection, councils, parallel execution, semantic similarity/embeddings, fuzzy path
matching, LLM comparison/reconciliation, reconciliation/prompt/context changes, and
a dedicated `MissingLocation` diagnostic remain on the diagnostic backlog and were
deliberately **not** changed by this milestone.
