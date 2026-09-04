# ADR-002 — Provider abstraction, Agent Framework runtime, and mock fallback

- **Status:** Accepted
- **Date:** 2026-07-06

## Context

v1 must run with a single analyzer agent built on **Microsoft Agent Framework**,
work **without an API key** (mock provider), and be designed to grow into a
council of many agents backed by many different LLMs/tools (Claude Code, Codex,
Ollama, static analyzers). Those future backends are heterogeneous: some are
`IChatClient`-shaped, some are CLIs, some are non-LLM analyzers.

## Decision

Two layered seams:

1. **`ILLMProvider` (Core)** — the *backend* seam. It is intentionally
   text-in/text-out and free of any SDK type, so **any** future backend can
   implement it (mock, OpenAI-compatible, Ollama, a Claude Code CLI wrapper,
   Codex, or a static analyzer that emits findings JSON).

2. **Microsoft Agent Framework (`AIAgent`)** — the *runtime*. `AnalyzerAgent`
   builds an `AIAgent` from an `IChatClient` via `AsAIAgent(...)` and runs it.
   The `IChatClient` it uses is
   [`LlmProviderChatClient`](../../src/EngineeringCouncil.Agent/LlmProviderChatClient.cs),
   a thin adapter over the selected `ILLMProvider`.

**Fallback:** the composition root selects the OpenAI provider only when
`OPENAI_API_KEY` is set; otherwise it registers `MockLLMProvider`. The mock
returns well-formed findings JSON so the whole pipeline is exercisable offline.

## Why the adapter (and not "just use IChatClient")

The `ILLMProvider → IChatClient` adapter is the deliberate join point that lets
non-`IChatClient` backends (CLIs, static analyzers) participate in the *same*
agent runtime later. That heterogeneity is the entire point of the future
council, so we pay one small indirection now to unlock it.

## Consequences

- For the OpenAI path there is a minor double-hop
  (`IChatClient` → text → `IChatClient`); acceptable for a POC and documented.
- Adding a backend = implement `ILLMProvider` + register it. Adding a lens =
  add a prompt + an agent; the runtime and contract are unchanged.
- Guardrail: the normalizer discards file references not in the snapshot, so a
  provider cannot cause "invented files" to reach an output.
