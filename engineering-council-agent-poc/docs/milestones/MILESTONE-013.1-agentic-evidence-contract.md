# MILESTONE-013.1 — Agentic Evidence Contract

- **Status:** ✅ Complete
- **Date:** 2026-08-08
- **Version:** v17 (agentic evidence contract)
- **ADR:** [ADR-021](../adr/ADR-021-agentic-evidence-contract.md)
- **Builds on:** [MILESTONE-012.4](./MILESTONE-012.4-observation-calibration-diagnostics.md)

## Goal

Establish the minimal, provider-neutral contract required to support **agentic
evidence sources** — an external agent (Codex, Claude Code, opencode, …) that
explores the repository itself — safely and honestly. The council does **not**
bundle a controlled context for such a source, and it must **never fabricate a
context fingerprint** it cannot vouch for. The structured result reuses the
existing `Evidence` abstraction and `StructuredLlmEvidenceInterpreter`; no new
interpretation logic, no new interfaces, no provider-name branching.

## Scope

Exactly one new first-class concept — `EvidenceProviderType.Agentic` — plus the
smallest executor/interpreter changes to honor its semantics:

1. **Provider type** — `EvidenceProviderType.Agentic = 6` (additive; existing
   values `LLM…Custom` unchanged, so persisted enum data stays stable).
2. **Agentic acquisition semantics** — an agentic provider receives enough to
   perform the task but explores the repository itself:
   - The `EvidenceRequest` carries the **repository identity** (root, solution,
     branch/commit) and **file structure** (paths/extensions/sizes/line counts)
     with **all file CONTENT stripped** — the council does not embed the entire
     repository contents into the request.
   - `ContextSelection` is an explicit, **empty** "agentic" selection (`Strategy =
     "agentic"`, zero files, zero characters) that still records the repository
     scope in `TotalRepositoryFiles`. No file is bundled.
   - The request keeps discipline, instructions (the output contract), scope,
     correlation id, run id and provider names — unchanged.
3. **No fabricated `ContextFingerprint`** — the council never saw the agent's
   effective context, so agentic steps leave `ContextFingerprint = ""` on the
   execution record **and** on every `Evidence` item (success and failure paths).
   The empty value already means "unavailable" platform-wide; nothing is invented.
4. **Structured result reuse** — agentic sources return the same
   `{ "observations": [ … ] }` envelope; `StructuredLlmEvidenceInterpreter`
   now also interprets `Agentic` evidence (its `CanInterpret` gate widens from
   `LLM` to `LLM or Agentic`) with **zero** interpretation-logic changes
   (the "do not invent files" guard, confidence lowering, and the
   discipline-mismatch rule all apply identically).
5. **M12 comparison is untouched** — `ProviderComparisonBuilder` still filters
   `ProviderType == LLM` only, so agentic executions are **never compared**
   (a new enum value is structurally excluded). No code change required; covered
   by a regression test.
6. **Planner is untouched** — it already branches on metadata
   (`DefaultAcquisitionScope`/`SupportedDisciplines`), never provider names, so an
   `Agentic` + `Discipline`-scoped provider automatically gets one step per
   selected discipline. Covered by regression tests.
7. **Minimal abstraction** — no `IAgenticEvidenceProvider` interface is created.
   `IEvidenceProvider` + `EvidenceProviderMetadata.ProviderType = Agentic` is the
   entire contract; the executor and interpreter branch on metadata, never names.
8. **Real (no-network) provider** — `AgenticEvidenceProvider` (`Infrastructure/
   Evidence/AgenticEvidenceProvider.cs`, name `"Agentic"`, registered in DI)
   demonstrates the full path end to end: a deterministic rule-based agent reads
   the file map (root + structure only — it has no content to read) and returns
   one structured observation per file it deems relevant to the discipline.
   Metadata carries the agent runtime (`Version = "agentic-explorer-v1"`) and a
   `filesExplored` count. A real adapter (Codex / Claude Code / opencode) does the
   same thing by reading from disk at `RepositorySnapshot.RootPath`.

## Changes

- **`Core/Domain/EvidenceProviderType.cs`** — additive `Agentic = 6` (documented
  as an external agent that explores the repository itself).
- **`Infrastructure/Acquisition/EvidenceAcquisitionExecutor.cs`** — provider
  resolution moved before context selection; when
  `provider.Metadata.ProviderType == Agentic` the step uses an empty
  `AgenticSelection(repository)`, no fingerprint, and a content-stripped snapshot
  (`WithoutContents(repository)`) in the request. Non-agentic steps are unchanged.
  All decisions are metadata-driven — never by provider name.
- **`Infrastructure/Interpretation/StructuredLlmEvidenceInterpreter.cs`** —
  `CanInterpret` accepts `LLM or Agentic`; no other logic changes.
- **`Infrastructure/Evidence/AgenticEvidenceProvider.cs`** (new) — no-network
  agentic provider demonstrating the contract end to end.
- **`Infrastructure/DependencyInjection/CouncilServiceCollectionExtensions.cs`** —
  registers `AgenticEvidenceProvider` (selectable via `--provider Agentic`).
- **No pipeline / comparison / reconciliation / package change.**

## Regression tests

New tests in `src/EngineeringCouncil.Tests/AgenticEvidenceTests.cs`
(**14 focused tests**, offline / deterministic / no keys / no network):

1. `Agentic` is a distinct provider type (value 6; not LLM/StaticAnalyzer/Custom)
2. Agentic provider metadata declares Discipline scope + analyzer instructions +
   a non-empty agent-runtime version, and is available
3. Planner creates one Discipline step per selected discipline (metadata-driven)
4. Planner never branches on provider name — an agentic provider named like a
   static tool is still Discipline-scoped from metadata
5. Agentic request does **not** embed repository contents (empty `ContextSelection`,
   stripped snapshot content, scope still recorded in `TotalRepositoryFiles`)
6. Agentic request carries repository identity (root/solution), discipline,
   instructions, correlation id and scope
7. Executor does **not** fabricate `ContextFingerprint` on agentic records/evidence
8. Agentic telemetry marks `ProviderType = Agentic`, `ContextSelectionStrategy =
   "agentic"`, zero files/chars selected, repository scope considered
9. An unavailable agentic provider still never fabricates a fingerprint
10. Agentic evidence flows through `StructuredLlmEvidenceInterpreter` (real file
    kept, invented file dropped)
11. Discipline-mismatch rule still applies to agentic evidence (tag + lower)
12. Real `AgenticEvidenceProvider` explores the file map and returns a structured
    envelope referencing a real file (agent runtime in `Metadata`)
13. M12 provider comparison **excludes** agentic executions (`ComparedProviders`
    and execution metrics contain no `Agentic`)
14. End to end: real pipeline + agentic provider → agentic observations →
    Security/Testing findings → `engineering-review-package.json`
    (`schemaVersion **1.1**`, no `providerComparison`)

## Verification

| Criterion | Result |
|-----------|--------|
| Build (0 errors, 0 warnings) | ✅ |
| All tests pass | ✅ 289/289 (14 new; 275 prior) |
| Agentic requests carry repository identity but no contents | ✅ |
| Agentic steps never fabricate `ContextFingerprint` | ✅ success + failure paths |
| Structured result reused with no new interpretation logic | ✅ |
| Planner topology from metadata, no name branching | ✅ |
| M12 comparison excludes agentic executions | ✅ |
| External package unchanged (`schemaVersion` **1.1**) | ✅ |

```
dotnet build EngineeringCouncil.slnx        # 0 errors, 0 warnings
dotnet test  EngineeringCouncil.slnx        # Passed: 289 (no credentials required)
```

## Limitations

- This milestone establishes the **contract** and a no-network demonstration
  provider. It does **not** implement any real agent adapter (no Codex / Claude
  Code / opencode transport yet).
- No detailed tool-call telemetry exists yet — agentic telemetry is limited to the
  existing provider/version/discipline/duration/success/error/observation surface
  plus a `filesExplored` metadata count.
- No autonomous agent-resource/time budget, no agent sandboxing, and no
  result-verification (the interpreter still trusts the structured envelope like
  it trusts LLM output).

## Deferred work

Real agentic adapters and their auth/transport, detailed agent tool-call
telemetry, agent resource budgets/sandboxing, agentic result verification,
provider ranking/scoring/weighting, automatic provider selection, councils,
semantic similarity/embeddings, LLM comparison/reconciliation, and parallel
execution remain on the roadmap and were deliberately **not** changed by this
milestone.
