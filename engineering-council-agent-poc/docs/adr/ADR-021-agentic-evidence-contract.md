# ADR-021 — Agentic Evidence Contract

- **Status:** Accepted
- **Date:** 2026-08-08
- **Milestone:** [MILESTONE-013.1](../milestones/MILESTONE-013.1-agentic-evidence-contract.md)
- **Builds on:** [ADR-005](./ADR-005-evidence-provider-layer.md),
  [ADR-009](./ADR-009-discipline-aware-evidence-acquisition.md),
  [ADR-018](./ADR-018-provider-comparison-semantics.md)

## Context

Every evidence source so far is **controlled-context**: the council selects a
deterministic context (`AnalysisContextSelection`), fingerprints it
(`ContextFingerprint`, ADR-018), and hands exactly that context to the provider
(LLM adapters, SARIF). A new class of source does not fit this model: an
**external agent** (Codex, Claude Code, opencode, …) that is handed a task and
explores the repository itself. For such a source the council does **not** control
what the agent sees.

Two things would be wrong with forcing an agentic source through the existing
path:

- **Embedding the repository contents** — the council would be bundling the entire
  repository (or a council-chosen slice) into a request the agent would ignore or
  duplicate anyway. The agent's whole point is autonomous exploration.
- **Fabricating a `ContextFingerprint`** — M12.2 comparability is defined as "same
  effective context". The council cannot prove what an agent saw, so stamping a
  fingerprint would claim an equivalence the system cannot back and would invite
  false `Comparable` comparisons.

## Decision

Introduce **one** new first-class concept — `EvidenceProviderType.Agentic = 6`
(additive) — and honor its semantics with the smallest possible executor and
interpreter changes. No new interface is created; `IEvidenceProvider` +
`EvidenceProviderMetadata.ProviderType = Agentic` is the whole contract.

### Agentic request semantics

An agentic `EvidenceRequest` carries:

- the **repository identity** — root, solution, branch/commit — and the **file
  structure** (paths, extensions, sizes, line counts), with **all file CONTENT
  stripped** (`WithoutContents`);
- an explicit, **empty** `ContextSelection` (`Strategy = "agentic"`, zero files,
  zero characters) that still records the repository scope in
  `TotalRepositoryFiles`;
- discipline, instructions (the output contract), scope, correlation id, run id,
  provider names — unchanged.

The external agent is responsible for exploration; the council embeds nothing.

### No fabricated fingerprint

Agentic steps leave `ContextFingerprint = ""` on the execution record **and** on
every `Evidence` item, on success **and** failure. The empty value already means
"unavailable" platform-wide; nothing is invented. Because `ProviderComparisonBuilder`
filters `ProviderType == LLM` only, agentic executions are **never compared** —
the new enum value is structurally excluded and M12.2 comparison semantics are
unchanged.

### Structured result reuse

Agentic sources return the same `{ "observations": [ … ] }` envelope.
`StructuredLlmEvidenceInterpreter.CanInterpret` widens from `LLM` to
`LLM or Agentic`; no interpretation logic changes — the "do not invent files"
guard, confidence lowering, and the discipline-mismatch rule apply identically.
This is deliberate: agentic output is just another source of normalized
observations.

### Planner untouched

The planner already branches on `EvidenceProviderMetadata`
(`DefaultAcquisitionScope` / `SupportedDisciplines`), never provider names
(ADR-009 / D-047). An `Agentic` + `Discipline`-scoped provider therefore
automatically receives one step per selected discipline. No planner code change.

### Minimal abstraction

`IAgenticEvidenceProvider : IEvidenceProvider` was considered and **rejected** as
speculative: the executor and interpreter branch on the provider's own metadata
(`ProviderType`), so no additional contract is genuinely required today. If a real
adapter later needs extra request semantics, those become additive `EvidenceRequest`
metadata — still no new interface.

## Trade-offs and decisions

- **Metadata over interfaces.** `ProviderType = Agentic` carries all the new
  semantics; the executor/interpreter branch on metadata, never names. Adding an
  interface now would be speculative.
- **Strip, don't summarize.** The agent receives the file structure but no file
  bodies. This is honest ("the council bundled nothing") and avoids a
  lossy-summary layer before the milestone has a real adapter to inform it.
- **Empty fingerprint, not a new sentinel.** Reusing `""` = "unavailable" keeps
  the comparison semantics (ADR-018) untouched and avoids inventing a new value
  that downstream code would have to special-case.
- **Reuse the structured interpreter.** A separate agentic interpreter would
  duplicate the entire envelope/guard/calibration logic for no benefit.
- **A real (no-network) demonstration provider.** `AgenticEvidenceProvider`
  proves the full path end to end (planner → executor → provider → interpreter →
  analyzers → package) without network, keys, or paid calls. A real adapter later
  only implements `CollectAsync` against the same contract.

## Consequences

- `Agentic` is a first-class provider type; agentic sources plug in like any other
  `IEvidenceProvider` without touching analyzers, reconciliation, or the package.
- Agentic requests never leak repository contents to the provider, and agentic
  executions never carry a fabricated context fingerprint.
- Agentic structured evidence becomes observations through the existing
  interpreter; M12 comparison and the external contract are byte-for-byte
  unchanged (`schemaVersion 1.1`).
- `--provider Agentic` runs the no-network demonstration provider end to end.

## Explicitly out of scope (deferred)

Real agentic adapters (Codex / Claude Code / opencode) and their auth/transport,
detailed agent tool-call telemetry, agent resource budgets/sandboxing, agentic
result verification, provider ranking/scoring/weighting, automatic provider
selection, councils, semantic similarity/embeddings, fuzzy matching, LLM
comparison/reconciliation, and parallel execution. See MILESTONE-013.1.
