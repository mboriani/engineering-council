# MILESTONE-014.4 — Targeted Semantic Reconciliation

- **Status:** ✅ Complete
- **Date:** 2026-08-12
- **Builds on:** [MILESTONE-014.3](./MILESTONE-014.3-council-assessment-evaluation.md)
- **ADR:** [ADR-025](../adr/ADR-025-targeted-semantic-reconciliation-boundary.md)

## Goal

M14.3's Decision B identified a small, precise class of ambiguous findings — those
whose `CouncilAssessment.Differences` include `ObservationType` (2/13 in the real
M14.1 run). Introduce an OPTIONAL, narrowly-scoped semantic review step for EXACTLY
that class — never a general LLM judge, never semantic reconciliation of every
finding.

```
Consolidated Findings → CouncilAssessment → ObservationType disagreement?
                                              /            \
                                            no             yes
                                            ↓                ↓
                                        unchanged     Semantic Reviewer
                                                             ↓
                                                  SameIssue / DifferentIssues / Inconclusive
```

## 1. Candidate selection

`SemanticReconciliationBuilder.IsCandidate(Finding)` — pure, deterministic, and
independently tested:

```csharp
finding.CouncilAssessment is { Type: AgreementWithDifferences } a
    && a.Differences.Contains(ObservationType)
```

Explicitly excluded (test-verified): `SingleSource`, `StrongAgreement`,
severity-only differences, location-only differences, `PotentialConflict` (even one
that happens to carry the `ObservationType` flag), and findings with no
`CouncilAssessment` at all.

## 2. Semantic decision contract

`SemanticReconciliationDecision { SameIssue, DifferentIssues, Inconclusive }` +
`SemanticReconciliationResult { Decision, Reason }` (`Core.Domain`). No scores, no
probabilities, no provider rankings, no votes. The reviewer never decides severity,
risk, confidence, recommendation, or which provider is "right" — see ADR-025.

## 3. Reviewer input (minimal, finding-scoped)

`SemanticReconciliationRequest` carries ONLY: finding id/title/category, supporting
providers, and the attributed observations' provider/type/file-references/evidence
excerpt. It does NOT carry the repository, unrelated findings, the full package,
other Council results, provider history, or prior reasoning (`Core.Abstractions`).

## 4. Reviewer prompt

`LlmSemanticReconciliationReviewer`'s system instruction asks exactly one question
("do these observations describe the SAME underlying issue, or DIFFERENT issues
incorrectly reconciled?") and states every guardrail from the spec verbatim: no new
findings, no severity change, no ranking, no winner, no rewriting the review, use
only supplied evidence, return `Inconclusive` when insufficient. The expected
response is the exact `{decision, reason}` contract. JSON extraction reuses the
EXISTING `StructuredJsonExtractor` (no second parsing path).

## 5. Provider neutrality

`ISemanticReconciliationReviewer` (`Core.Abstractions`) is the only abstraction; no
domain or application code references Claude/OpenAI/DeepSeek/Codex/OpenCode.
`LlmSemanticReconciliationReviewer` (`Infrastructure.Reconciliation`) depends ONLY on
the existing generic `ILlmChatClient` seam — the SAME transport interface Claude and
OpenAI evidence providers already use. Not a new LLM framework: no retries, no
repair loop, a single attempt per candidate. Normal tests run entirely on
`FakeSemanticReconciliationReviewer` (a scripted `ISemanticReconciliationReviewer`)
and `ScriptedLlmClient` (the existing fake transport, reused unmodified) — zero
network, zero credentials.

## 6. Behavior per decision

| Decision | Finding mutation |
|----------|-------------------|
| `SameIssue` | Unchanged; `SemanticReview` attached. Original `ObservationType` disagreement stays in `CouncilAssessment.Differences` — it happened and remains useful provenance. |
| `Inconclusive` | Unchanged; `SemanticReview` attached. Never guessed. |
| `DifferentIssues` | Unchanged; `SemanticReview` attached. **No split.** `SupportingFindingIds` unresolved into sub-findings would require re-deriving severity/confidence/provenance per group — a de-merge rule, which is explicitly out of M14.4's scope (ADR-025). Recorded as advisory-only; splitting is deferred to a future milestone. |

All three paths share ONE code path in `SemanticReconciliationBuilder.ApplyAsync` —
only the stamped `Decision` value differs; there is no per-decision branching that
could silently diverge.

## 7. Failure semantics

Any reviewer exception (timeout, malformed output, transport failure, unavailable)
is caught in `SemanticReconciliationBuilder.ReviewOneAsync` and converted to
`Inconclusive` with a secret-safe reason ("Semantic reviewer unavailable."). A
second, defensive `try/catch` around the whole enrichment stage in `AnalysisPipeline`
ensures even an unexpected bug there cannot destroy an already-built package — the
package simply keeps its pre-review `Findings`. Only genuine run cancellation
(`OperationCanceledException` matching the run's own token) propagates.

## 8. Package

Additive `findings[].semanticReview` (`{decision, reason}`, present ONLY on reviewed
candidates — absent, not null-valued, elsewhere). `schemaVersion` stays **1.1**.
`engineering-review-package.json` remains the sole external artifact. Consumer DTO
(`SemanticReviewContract`) added to `PackageContract`; the consumer-DTO gate stays
green. No provider SDK/runtime types leak (test-verified).

## 9. Markdown

Per candidate finding, immediately under the existing Council Assessment block:

```
Council Assessment: AgreementWithDifferences
Differences: ObservationType
Semantic Review: SameIssue
Reason: Both observations describe the same unsanitized SQL construction.
```

No chain-of-thought; the `Reason` is the reviewer's own short rationale, never raw
model output.

## 10. Configuration

`Council:SemanticReconciliation:{Enabled, Model, MaxOutputTokens, TimeoutSeconds}` —
`Enabled: false` by default in both the CLI and API `appsettings.json`. No specific
model/provider is required by the domain; the shipped default reuses the already-
configured Claude client purely as DI composition, not a domain dependency.

## Tests (all green — 441/441, +21 new)

`SemanticReconciliationTests.cs`: candidate selection (ObservationType → candidate;
severity-only / location-only / StrongAgreement / SingleSource / PotentialConflict /
unassessed → not candidates) · the exact 6-finding mixed fixture from the milestone's
own verification section (only the 2 ObservationType findings invoke the reviewer) ·
a candidate invokes the reviewer exactly once · reviewer input is minimal and
finding-scoped · `SameIssue`/`Inconclusive`/`DifferentIssues` all preserve the
deterministic finding (severity, confidence, `CouncilAssessment`,
`SupportingFindingIds` unchanged; `DifferentIssues` never splits) · a thrown failure
and an unrelated timeout both become `Inconclusive` · genuine run cancellation still
propagates · package serializes the result additively and the consumer DTO
deserializes it (including confirming absence on non-candidates) · Markdown renders
the decision and reason · semantic reconciliation is disabled by default · the REAL
`LlmSemanticReconciliationReviewer` parses a valid decision through the shared
transport and categorizes malformed/truncated responses as `SchemaValidation`.

## Verification

Using the deterministic fixture the milestone specified (one `StrongAgreement`, one
`SingleSource`, one severity-only disagreement, one location-only disagreement, two
`ObservationType` disagreements):

- **Candidates:** 2 (exactly the two `ObservationType` findings).
- **Reviewer calls:** 2 (`FakeSemanticReconciliationReviewer.CallCount == 2`).
- **Results:** stamped per script (`SameIssue`/`Inconclusive`/`DifferentIssues` all
  exercised across the suite); the other 4 findings carry no `SemanticReview`.

```
dotnet build EngineeringCouncil.slnx        # 0 errors, 0 warnings
dotnet test  EngineeringCouncil.slnx        # Passed: 441 (no network/credentials)
```

Real offline smoke (`review --provider Mock --path evaluation/dataset/02-vulnerable-payments`,
feature left at its default `Enabled: false`) produced a package **byte-identical in
shape** to the M14.3 smoke run — zero `semanticReview` occurrences, same
`councilAssessmentSummary` — confirming disabled behavior is unchanged.

## Exit criteria

| Criterion | Result |
|-----------|--------|
| Build (0 errors, 0 warnings) | ✅ |
| All tests pass | ✅ 441/441 |
| Only `ObservationType`-disagreement findings are candidates | ✅ |
| Semantic review returns `SameIssue`/`DifferentIssues`/`Inconclusive` | ✅ |
| Non-candidates never invoke the reviewer | ✅ |
| Reviewer input minimal and finding-scoped | ✅ |
| `SameIssue`/`Inconclusive` safely preserve reconciliation | ✅ |
| `DifferentIssues` has an explicit, safe (no-split) behavior | ✅ |
| Reviewer failure cannot fail the run | ✅ (2-layer catch) |
| Disabled by default | ✅ (verified by real smoke diff) |
| Package remains backward compatible | ✅ `schemaVersion` 1.1 |
| Markdown exposes the result | ✅ |

## What remains for M14.5

Deterministically or semantically **splitting** a `DifferentIssues` finding into
separate consolidated findings (needs its own severity/confidence/provenance
re-derivation design — explicitly deferred, see ADR-025). No other next step was
started.

## Post-implementation validation (2026-08-12)

A separate task validated M14.4 against real Council data to decide whether M14.5
is actually necessary, without starting M14.5 itself. Summary (full report kept in
the session, not duplicated here — this section records only the durable findings):

- **The exact documented M14.1 run** (RunId `20260811-011804-d3eb0a`, 24
  observations / 15 raw / 13 consolidated findings, `OpenCode+Codex+ClaudeCode`)
  is **not present** in `outputs/`. Three earlier/partial runs against
  `m14-1-council-smoke-repo` exist (`20260810-144843-453bc0`,
  `20260810-205517-354c46`, `20260810-213146-540b71`) but do not match that shape.
- **Structural finding, not a missing-artifact problem**: every real run found —
  and, by construction, *every* real Council run, since Council runs are agentic
  by definition — has `calibrationDiagnostics: false`. This is `D-089`'s
  Agentic/`ProviderComparison` exclusion (`ProviderComparisonBuilder` filters to
  `ProviderType == LLM` only) propagating forward: `CalibrationDiagnosticsBuilder`
  never runs without a `ProviderComparison`, so `ObservationTypeDisagreement` —
  which, unlike `LocationDisagreement`, has **no** finding-level fallback in
  `CouncilAssessmentBuilder` — can **never** be set for a real, agentic-only
  Council run. Consequently `SemanticReconciliationBuilder.IsCandidate` can never
  select a finding from real persisted data: **real candidate count = 0**, always,
  under the current architecture. (The M14.1 doc's "2 observation-type
  disagreements" was a descriptive/manual narration, not a value the M12.3/M12.4
  pipeline could have computed for that run.)
- Falling back to the deterministic M14.1-shaped fixture (Step 1's explicit
  fallback) reproduces the documented 2 candidates, but invoking the **real**
  `LlmSemanticReconciliationReviewer` requires an authenticated `ILlmChatClient`
  (`ANTHROPIC_API_KEY` or `OPENAI_API_KEY`); neither was present in the
  environment, no other configured credential path existed, and none was added —
  so the real reviewer was not invoked (Step 4 STOP; no fabricated decisions were
  produced, no fake/mocked reviewer was substituted).
- **Decision: A — skip M14.5 for now**, on the evidence available: real candidate
  count is 0, so `DifferentIssues` is 0 for all real data regardless of reviewer
  availability, and M14.5 (safe splitting of `DifferentIssues` findings) would
  have no real case to act on today. This is a narrower basis than "we ran the
  reviewer and observed few conflicts" — see `D-098`.
- Zero production code was changed by this validation.
