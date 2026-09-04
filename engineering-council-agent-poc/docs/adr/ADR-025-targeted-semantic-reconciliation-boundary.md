# ADR-025 — Targeted Semantic Reconciliation Boundary

- **Status:** Accepted
- **Date:** 2026-08-12
- **Milestone:** [MILESTONE-014.4](../milestones/MILESTONE-014.4-targeted-semantic-reconciliation.md)
- **Builds on:** [ADR-011](./ADR-011-deterministic-multi-source-reconciliation.md), the
  M14.2 deterministic Council assessment, and
  [MILESTONE-014.3](../milestones/MILESTONE-014.3-council-assessment-evaluation.md)'s
  evidence-based **Decision B**.

## Context

M14.3 evaluated the deterministic Council against the real M14.1 run and produced an
explicit, evidence-based decision: reconciliation is sufficient for MOST findings (0
conflicts across 13 real findings), but a small, precisely-identifiable subset — the
findings whose `CouncilAssessment.Differences` include `ObservationType` (2/13 in that
run) — is genuinely ambiguous. Deterministic rules can observe THAT independent
providers labeled an issue differently, but cannot decide WHETHER that means "same
issue, different wording" or "two distinct issues merged under one consolidated
finding." M14.4 is the first point in this platform where a non-deterministic judgment
call is deliberately introduced, so the BOUNDARY of that judgment call is itself an
architectural decision worth recording.

## Decision

Introduce **targeted, optional, narrowly-scoped semantic review** — never a general
LLM judge over the Council.

- **Candidate selection is deterministic and exhaustive.** A finding is reviewed if
  and only if `CouncilAssessment.Type == AgreementWithDifferences` AND
  `Differences` contains `ObservationType` — the EXACT subset M14.3 identified.
  `SingleSource`, `StrongAgreement`, `PotentialConflict`, and severity/location-only
  differences NEVER invoke the reviewer (`SemanticReconciliationBuilder.IsCandidate`,
  independently testable and test-covered).
- **The reviewer answers one question about one finding.** `ISemanticReconciliationReviewer`
  receives ONLY the ambiguous finding's title/category/providers and its attributed
  observations (provider, type, file references, evidence excerpt) — never the
  repository, other findings, the full package, provider history, or prior reasoning.
  It returns exactly `SameIssue | DifferentIssues | Inconclusive` plus a short reason —
  never a score, probability, ranking, or vote, and it never touches severity,
  confidence, or recommendation.
- **The result is provenance, not a decision.** Every outcome — including
  `DifferentIssues` — is attached to the EXISTING consolidated finding as
  `Finding.SemanticReview`. Deterministic reconciliation output (severity, confidence,
  grouping, `SupportingFindingIds`) is authoritative regardless of what the reviewer
  says.
- **`DifferentIssues` does not split the finding.** Splitting would require
  re-deriving severity/confidence/provenance for two new sub-findings from the
  original raw-finding attribution — effectively a second, ad hoc reconciliation
  algorithm layered on top of the existing deterministic one (a de-merge rule).
  `SupportingFindingIds` alone does not disambiguate which raw findings belong to
  which semantic group with the same rigor the existing reconciler applies. Given
  M14.4's explicit smallness constraint and "prefer a safe advisory result over risky
  mutation," `DifferentIssues` is recorded as an advisory signal for human or future
  automated triage — splitting is left to a future milestone (see Consequences).
- **Failure degrades to `Inconclusive`, never to run failure.** Any reviewer
  exception — timeout, malformed output, transport error, unavailability — is caught
  by `SemanticReconciliationBuilder` and converted to
  `Inconclusive` with a secret-safe reason. A defensive catch around the whole
  enrichment step in `AnalysisPipeline` additionally guarantees that even an
  unexpected bug in the enrichment stage cannot destroy an otherwise valid,
  already-built Engineering Review Package. Only genuine run cancellation propagates.
- **Disabled by default; provider-neutral when enabled.** `Council:SemanticReconciliation:Enabled`
  defaults to `false` — offline/default Council behavior is byte-for-byte identical to
  M14.3 (verified against a real smoke run). The real implementation
  (`LlmSemanticReconciliationReviewer`) depends ONLY on the existing generic
  `ILlmChatClient` transport seam — never a specific provider — and reuses the
  existing `StructuredJsonExtractor` rather than inventing a second parsing path. It is
  deliberately NOT a general-purpose LLM framework: one prompt, one two-field output
  contract, no retries, no repair loop.

## Why this boundary, not a broader one

A general LLM judge over the whole Council would re-introduce everything the
deterministic reconciler was built to avoid: non-reproducibility, an opaque "the model
decided" step standing between evidence and the delivered package, and a much larger
prompt-injection/cost surface. M14.3's zero-conflict result is itself the argument
against a broad judge — most of the Council needs no semantic help at all. Scoping
review to EXACTLY the 2/13 ambiguous case class keeps the intervention proportional to
the evidence, keeps the reviewer's context minimal (and therefore auditable), and keeps
the blast radius of a reviewer failure or hallucination limited to a small, already-
flagged subset.

## Consequences

- `Finding` gains an additive, nullable `SemanticReview`; the package gains an
  additive `findings[].semanticReview` (present ONLY on reviewed candidates).
  `schemaVersion` stays **1.1** — no consumer-visible breaking change.
- `AnalysisPipeline` gained two OPTIONAL trailing constructor parameters
  (`ISemanticReconciliationReviewer?`, `SemanticReconciliationOptions?`), preserving
  every existing call site (16 test files construct the pipeline directly) without
  change.
- **Deferred to a future milestone:** deterministically or semantically SPLITTING a
  `DifferentIssues` finding into separate consolidated findings. This would need its
  own reconciliation-adjacent design (how severity/confidence/provenance are
  re-derived for each half) and is explicitly out of scope here.

## Explicitly out of scope

General LLM judge over all findings, agent debate, voting, provider ranking/weighting,
semantic finding discovery, embeddings/vector search, semantic similarity across ALL
findings, severity/confidence/health/risk recalibration, prompt changes to evidence
providers, new evidence providers, parallel execution.
