# ADR-020 — Observation Calibration Diagnostics

- **Status:** Accepted
- **Date:** 2026-08-08
- **Milestone:** [MILESTONE-012.4](../milestones/MILESTONE-012.4-observation-calibration-diagnostics.md)
- **Builds on:** [ADR-011](./ADR-011-deterministic-multi-source-reconciliation.md),
  [ADR-018](./ADR-018-provider-comparison-semantics.md),
  [ADR-019](./ADR-019-finding-calibration-diagnostics.md)

## Context

ADR-019 / Milestone 012.3 added **finding-level** calibration diagnostics
(`calibration-diagnostics.json`): exclusive findings, severity disagreements and
low-agreement signals projected from a completed run's own reconciled data. The
next diagnostic question is **one level lower — the normalized observations** that
support each shared finding: when two compared providers both surface the *same
reconciled finding*, do they disagree about **what kind of issue it is**
(ObservationType) or **where it lives** (source location)?

Like every calibration milestone before it, this must be a **pure projection of an
already-completed `AnalysisRun`** — no provider calls, no rescan, no context
rebuild, no re-interpretation of evidence, no semantic matching, no new
reconciliation rules, and no change to the external
`engineering-review-package.json` consumer contract. The existing reconciler
remains the **only** authority for deciding which provider findings belong
together.

## Decision

Extend the existing internal `calibration-diagnostics.json` artifact additively
(M12.4) with exactly **two new diagnostic types**, both emitted only for
`ProviderComparisonStatus.Comparable` disciplines and only for **shared**
consolidated findings (supported by ≥2 compared providers):

- **`ObservationTypeDisagreement`** — the supporting observations attributed to
  the compared providers expose **different normalized `ObservationType`
  values**. Exact normalized values are compared; no semantic equivalence rules
  are created between types and no judgement is made about which type is correct.
- **`LocationDisagreement`** — the supporting observations reference **meaningfully
  different normalized source locations** (different normalized file, or the same
  file with different explicit lines). Exact, existing location semantics only; no
  fuzzy path matching, no line-distance tolerance, no semantic location inference.

### Provenance path (why diagnostics operate through reconciled finding provenance)

Every new signal is derived from the existing provenance chain that Milestone 007
established and reconciliation preserved:

```
Consolidated Finding
  → SupportingFindingIds        (the reconciler's authoritative grouping)
  → Raw Findings
  → ObservationIds              (the analyzer's provenance)
  → EngineeringObservations
  → Evidence / Provider         (Observation.SourceProvider, else
                                 SourceEvidenceId → Evidence.ProviderName)
```

The reconciler already proved the provider findings describe the same issue — the
diagnostics never re-prove it, never re-run reconciliation, and never merge or
split anything. They only describe *disagreement within* an already-agreed group.

### Provider attribution

Provider identity is never inferred from text, titles, ids, or ordering. An
observation is attributed to a compared provider **only** when existing provenance
names exactly one: its `SourceProvider` (stamped from `Evidence.ProviderName` at
interpretation), or its `SourceEvidenceId` → `Evidence.ProviderName`. An
observation whose provider cannot be reliably attributed to one of the compared
providers is **skipped** — never guessed.

### ObservationTypeDisagreement semantics

- Only `Comparable` disciplines; only findings shared by ≥2 compared providers.
- Per compared provider, collect the distinct normalized `ObservationType`s of its
  attributed supporting observations.
- If fewer than two providers have attributable observations, or every provider's
  type set is identical → **no diagnostic**.
- Otherwise emit `ObservationTypeDisagreement` exposing FindingId, ObservationIds,
  providers and per-provider observation types. Never decide which type is correct.

### LocationDisagreement semantics

- Only `Comparable` disciplines; only findings shared by ≥2 compared providers.
- A provider's normalized locations are its supporting observations' file
  references: the repository-relative path normalized exactly like the reconciler
  (`\` → `/`, leading `./` trimmed, lower-cased) plus the explicit 1-based line
  when one is known (e.g. `src/payments/paymentclient.cs:42`).
- Disagreement exists when the provider location sets are not mutually equivalent.
  Two locations are equivalent when the normalized paths match **and** the explicit
  lines match **when both sides name one**; a location without an explicit line is
  line-compatible with any line on the same path (mirrors the reconciler's existing
  "no line is close" rule). No arbitrary line-distance tolerance is introduced.
- **Missing location is not a disagreement.** A provider that supplied no
  comparable location information contributes nothing; if fewer than two providers
  supplied locations, **no** `LocationDisagreement` is emitted. A
  `MissingLocation` diagnostic type is deliberately **not** added in this
  milestone.

### Comparable only

Both new diagnostics apply **only** when the discipline comparison is
`Comparable`. `NonComparable` and `Incomplete` disciplines keep the existing
limitation behavior from ADR-019 — never diagnostics.

### Disagreement does not imply incorrectness

A disagreement signal is **descriptive**, exactly like the M12.2/M12.3 artifacts.
Two providers describing the same finding with different normalized types or
slightly different locations does not mean either is wrong — it means the run
should be read with that nuance in mind. Nothing downstream changes.

### Determinism and artifact boundary

- Every collection is sorted (providers, observation ids, locations, observation
  types, and the signal collections), so the logical result is identical regardless
  of provider, raw-finding, consolidated-finding or observation enumeration order.
- `calibration-diagnostics.json` remains the single calibration artifact — no
  `observation-diagnostics.json` or competing exporter architecture is introduced.
- The package is asserted to never carry `observationTypeDisagreement`,
  `locationDisagreement` (or any calibration content); `schemaVersion` stays
  **1.1** and the consumer DTO gate remains green.

## Trade-offs and decisions

- **Projection over analysis.** Deriving everything from the reconciler's
  `SupportingFindingIds` + the observations/evidence the run already holds
  guarantees no provider calls, no rescan, no semantic matching and no
  reconciliation changes; the diagnostic can never disagree with reconciliation.
- **Attribution by provenance, not inference.** Requiring a single, authoritative
  provider per observation is stricter than guessing from file contents or ids, and
  un-attributable observations are skipped honestly instead of mislabelled.
- **Exact normalized types and locations.** No synonym rules, no path fuzzing, no
  line tolerance — two values either match exactly or they do not. This keeps the
  signal deterministic and explainable; softer proximity logic is explicitly
  deferred.
- **Missing location ≠ disagreement.** Fabricating a location conflict from one
  provider having no location would be noise, not signal.
- **Comparable-only.** Like ADR-019, signals are only meaningful where providers
  demonstrably saw the same context and both succeeded.
- **Internal only, additive.** M12.3 types, thresholds and semantics are untouched;
  the external contract is untouched.

## Consequences

- Dual-provider Comparable runs now surface observation-level type and location
  disagreements on shared findings inside the existing calibration artifact.
- M12.3 behavior (ExclusiveFinding / SeverityDisagreement / LowAgreement) is
  byte-for-byte unchanged for the same inputs.
- `calibration-diagnostics.json` serializes the two new signals; the package and
  consumer DTO are unchanged (`schemaVersion 1.1`).
- Un-attributable observations and missing locations are skipped/not-signalled
  rather than guessed.

## Explicitly out of scope (deferred)

Observation-count divergence, retry/repair/truncation/token diagnostics, context
utilization, provider ranking/scoring/weighting, automatic provider selection,
councils, semantic similarity/embeddings, fuzzy path matching, LLM comparison or
reconciliation, changes to reconciliation/prompts/context, and a dedicated
`MissingLocation` diagnostic type. See MILESTONE-012.4.
