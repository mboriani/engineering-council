# ADR-005 — Evidence Provider Layer

- **Status:** Accepted
- **Date:** 2026-07-06
- **Milestone:** [MILESTONE-004](../milestones/MILESTONE-004-evidence-provider-layer.md)
- **Builds on:** [ADR-003](./ADR-003-specialized-analyzers.md), [ADR-002](./ADR-002-provider-abstraction-and-mock-fallback.md)

## Context

Until now, an analyzer assumed its input came from a single LLM: it wrapped an
`ILLMProvider` in a Microsoft Agent Framework agent and read the model's text.
That baked two assumptions into the analyzer layer:

1. **Evidence always comes from a language model.** But an Engineering Council
   should also reason over Roslyn, SonarQube, Semgrep, NDepend, git history, and
   documentation — none of which are LLMs.
2. **The analyzer owns the model integration.** Microsoft Agent Framework lived
   inside the analyzer base class, coupling every analyzer to that SDK.

To prepare for a multi-provider, multi-agent council we need analyzers to reason
over **evidence**, not over LLM responses.

## Decision

Introduce an **Evidence Provider layer**:

- **`IEvidenceProvider`** — receives repository context + analyzer instructions,
  returns **`Evidence`** (`ProviderName`, `ProviderType`, `RawResponse`,
  `Confidence`, `ExecutionTime`, `TokensUsed?`, `Metadata`, `CreatedAt`). A
  provider **only returns evidence; it never creates findings.**
- **`EvidenceProviderType`** — `LLM | StaticAnalyzer | SourceControl |
  Documentation | ExternalTool | Custom`.
- **`IEvidenceProviderFactory`** — resolves a provider by name; analyzers request
  a provider **by configuration**, never by concrete type.
- Analyzers depend on `IEvidenceProviderFactory` (not `ILLMProvider`). The base
  class is now `EvidenceBasedAnalyzer`: build context + instructions → ask the
  configured provider for evidence → normalize the evidence's raw response into
  findings.
- **`ClaudeEvidenceProvider`** is the LLM provider and the **only** place
  Microsoft Agent Framework is integrated. `MockEvidenceProvider` is the offline
  default. Six future providers (`Codex`, `Ollama`, `Roslyn`, `Sonar`,
  `Semgrep`, `NDepend`) exist as `NotImplementedException` stubs so the shape is
  in place.
- Configuration: `Evidence:DefaultProvider` (default `Claude`), overridable with
  `--provider`. If the requested LLM provider has no API key, the host falls
  back to `Mock` so the POC always runs.

## Why reason over evidence instead of directly over LLM responses

1. **Sources are heterogeneous.** A finding may be best supported by a static
   analyzer's exact rule hit, git churn, or a doc gap — not only a model's
   opinion. A uniform `Evidence` contract lets all of them feed the same
   analyzers and the same consolidation layer.
2. **Provenance and trust.** `Evidence` carries `ProviderType`, `Confidence`,
   `ExecutionTime`, and `Metadata`. Reasoning over evidence (rather than opaque
   text) is what will later let the council weight a Roslyn rule hit differently
   from an LLM hunch, and explain *why*.
3. **Decoupling.** Analyzers no longer know or care which backend served them, and
   no longer reference any model SDK. Swapping Claude for a real Anthropic client,
   or adding Semgrep, is a DI change — analyzers are untouched.
4. **Future council.** Multi-provider execution, provider comparison, and voting
   all operate over `Evidence`. Establishing that contract now is the
   prerequisite; none of those behaviors are added in this milestone.

## Consequences

- The old `ILLMProvider`/`OpenAILLMProvider`/`MockLLMProvider`/`LlmProviderChatClient`
  are removed; their behavior moved into `ClaudeEvidenceProvider` (Agent
  Framework) and `MockEvidenceProvider`.
- `EngineeringCouncil.Agent` no longer references Microsoft Agent Framework; that
  dependency moved to `EngineeringCouncil.Infrastructure`.
- Providers register independently (`AddEvidenceProvider<T>()`), resolved by the
  factory — symmetric with `AddAnalyzer<T>()`.

## Explicitly out of scope

Still one provider per run. No parallel execution, no provider comparison, no
voting, no council. This milestone introduces only the abstraction layer.
