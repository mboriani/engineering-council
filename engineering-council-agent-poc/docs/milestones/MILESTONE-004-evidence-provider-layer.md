# MILESTONE-004 — Evidence Provider Layer

- **Status:** ✅ Complete
- **Date:** 2026-07-06
- **Version:** v4 (evidence abstraction; still single-provider)
- **ADR:** [ADR-005](../adr/ADR-005-evidence-provider-layer.md)

## Goal

Replace the "single LLM" assumption with an extensible **Evidence Provider**
architecture, so analyzers reason over engineering *evidence* from any source —
without changing the analyzer architecture. Still executes **one** provider.

## New flow

```
Repository → Repository Scanner → Analyzer → IEvidenceProvider → Evidence → Finding
```

## What changed

- **New Core abstractions:** `IEvidenceProvider`, `IEvidenceProviderFactory`,
  `EvidenceRequest`, `EvidenceOptions`; new domain `Evidence` +
  `EvidenceProviderType` (`LLM | StaticAnalyzer | SourceControl | Documentation |
  ExternalTool | Custom`).
- **Providers now return evidence, never findings.** The analyzer turns
  `Evidence.RawResponse` into `Finding`s.
- **`ClaudeEvidenceProvider`** replaces the old LLM provider and is the **only**
  place Microsoft Agent Framework is integrated. **`MockEvidenceProvider`** keeps
  the offline behavior. Six future providers are `NotImplementedException`
  stubs: `Codex`, `Ollama`, `Roslyn`, `Sonar`, `Semgrep`, `NDepend`.
- **`IEvidenceProviderFactory`** resolves providers by name; analyzers request a
  provider by configuration only.
- **Analyzers** now take `IEvidenceProviderFactory` (+ `EvidenceOptions`) instead
  of `ILLMProvider`. Base class renamed `ChatAnalyzerAgent` → `EvidenceBasedAnalyzer`.
  `EngineeringCouncil.Agent` **no longer references Microsoft Agent Framework**
  (moved to Infrastructure).
- **Removed:** `ILLMProvider`, `OpenAILLMProvider`, `MockLLMProvider`,
  `LlmProviderChatClient` and the LLM completion DTOs.
- **Configuration:** `Evidence:DefaultProvider` (default `Claude`) in
  `appsettings.json`; CLI `--provider <name>` override (and `--mock` = Mock).
  When the LLM provider has no API key, the host falls back to `Mock`.

## DI

Providers register independently via `AddEvidenceProvider<T>()` and are resolved
by the factory — symmetric with `AddAnalyzer<T>()`. Adding a provider touches no
analyzer.

## Tests (all green — 23/23)

New `EvidenceProviderTests`: provider resolution (case-insensitive) · mock
provider returns discipline-aware evidence (not findings) · invalid provider
throws · future provider throws `NotImplementedException` · configuration
override selects Mock · Claude falls back to Mock without a key · all 8 providers
registered · **analyzer independence** (an analyzer works against a fake
provider via the factory and never references a concrete provider). Existing
pipeline/merger/orchestrator/normalizer tests updated and still pass.

## Exit criteria

| Criterion | Result |
|-----------|--------|
| Build (0 errors, 0 warnings) | ✅ |
| Tests pass | ✅ 23/23 |
| Analyzers depend on the factory, not `ILLMProvider` | ✅ |
| Agent Framework only inside the LLM provider | ✅ `EngineeringCouncil.Agent` has no `Microsoft.Agents.AI` ref |
| Providers return evidence, never findings | ✅ enforced by `IEvidenceProvider` |
| Provider by config + CLI override | ✅ `Evidence:DefaultProvider`, `--provider` |
| Future providers stubbed, not implemented | ✅ 6 `NotImplementedException` providers |

## Verification

```
dotnet build EngineeringCouncil.slnx        # 0 errors, 0 warnings
dotnet test  EngineeringCouncil.slnx        # Passed: 23
dotnet run --project src/EngineeringCouncil.Cli -- analyze --path src/EngineeringCouncil.Core
#   Evidence:DefaultProvider=Claude, no key → falls back to Mock; 7 raw → 7 consolidated
dotnet run --project src/EngineeringCouncil.Cli -- analyze --path <target> --provider Mock
```

## Explicitly out of scope (future milestones)

One provider per run only. No parallel execution, no provider comparison, no
voting, no council.
