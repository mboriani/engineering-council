# ADR-012 — Real LLM Evidence Providers

- **Status:** Accepted
- **Date:** 2026-07-23
- **Milestone:** [MILESTONE-011](../milestones/MILESTONE-011-real-llm-evidence-providers.md)
- **Builds on:** [ADR-005](./ADR-005-evidence-provider-layer.md), [ADR-009](./ADR-009-discipline-aware-evidence-acquisition.md), [ADR-010](./ADR-010-native-sarif-provider.md), [ADR-011](./ADR-011-deterministic-multi-source-reconciliation.md)

## Context

The platform has been validated with the offline Mock source and the deterministic
SARIF import. Milestone 011 integrates the first REAL model-backed sources — Anthropic
Claude and OpenAI — to prove the architecture holds with genuinely probabilistic
evidence, without changing analyzers, interpretation, reconciliation, or the external
integration contract.

## Decision

Implement `ClaudeEvidenceProvider` and `OpenAiEvidenceProvider` as ordinary
`IEvidenceProvider` infrastructure adapters behind the existing acquisition
architecture, sharing one prompt, one response schema, one validator, and one
retry/telemetry implementation.

- **Metadata-driven**: both declare `ProviderType = LLM`,
  `DefaultAcquisitionScope = Discipline`, `SupportsRepositoryWideAnalysis = false`,
  `RequiresAnalyzerInstructions = true`, and support all seven disciplines. The planner
  therefore emits one step per provider per selected discipline with **no
  provider-name branching anywhere** — `Claude + OpenAI × {Architecture, Security}`
  yields exactly four discipline steps, while SARIF still executes once.
- **One shared prompt** (`IEvidencePromptBuilder` → `DisciplineEvidencePromptBuilder`)
  and **one shared response schema** (`schemaVersion` + `discipline` + `observations[]`),
  so Claude and OpenAI are asked for exactly the same provider-neutral evidence.
  Adapters only translate the prompt into their own message objects.
- **Validation before interpretation**: a conservative extractor accepts direct JSON or
  exactly ONE fenced block (rejecting multiple documents, unbalanced JSON, or prose-heavy
  responses), then a validator checks schema version, discipline match, observation
  shape, severity/confidence vocabularies, file references, and truncation. At most
  **one** repair attempt is allowed; there is never a repair loop.
- **Transport seams** (`IClaudeClient` / `IOpenAiClient`) isolate all HTTP/SDK concerns
  so the entire unit suite runs on scripted fakes.
- **Categorized failures** (`LlmErrorCategory`) drive a bounded retry policy: only rate
  limits, timeouts, network blips and server errors are retried; authentication, invalid
  requests, unsupported models and schema failures are not.
- **`ProviderFailureMode`**: `Continue` (default) isolates a failed provider and keeps
  the run going; `FailRun` fails the whole run. No quorum policies.
- **Disabled by default**; explicit selection opts in; API keys are read from a
  *configurable environment variable name*, never stored in configuration, artifacts,
  or logs.

## Why real providers remain evidence sources

A model is one more heterogeneous source, not the reviewer. Its system instruction says
so explicitly: produce candidate observations only — never a review package, never
reconciliation, comparison, voting, tickets, or Markdown. Validation, interpretation,
analysis, reconciliation, scoring, and reporting stay in deterministic platform code, so
adding a model cannot move domain responsibility into a prompt.

## Why shared structured output is provider-neutral

Two schemas would mean two interpreters, two validators, and provider-specific knowledge
leaking downstream. One versioned schema means the existing
`StructuredLlmEvidenceInterpreter` handles both providers unchanged, and observations are
indistinguishable by source once normalized — exactly what reconciliation needs.

## Why providers execute per discipline

A single repository-wide model request flattens depth (ADR-009). Discipline scope gives
each request focused instructions plus context selected for that discipline, and makes
cost explicit and plannable. Repository-scoped sources like SARIF still run once.

## Why repository scanning remains outside providers

The scanner and context selector are the authoritative boundary for what leaves the
machine. Providers receive only `AnalysisContextSelection` — never file-system access —
so the data boundary is auditable, budgeted, and testable, and a provider cannot silently
widen it.

## Why external providers are disabled by default

Enabling a real provider sends selected repository content to a third party. That must be
a deliberate act, never a side effect of running the tool. An unconfigured run stays fully
local (Mock/SARIF).

## Why unit tests use client abstractions

Normal CI must be free, offline, deterministic, and safe. Scripted fakes exercise success,
malformed output, rate limits, timeouts, authentication failures, truncation, usage
reporting, and repair — cases that are impossible to trigger reliably against a live API.

## Why integration tests are opt-in

Real calls cost money, need credentials, and vary in wording. They are gated behind
`RUN_CLAUDE_INTEGRATION_TESTS` / `RUN_OPENAI_INTEGRATION_TESTS` plus a key, use a tiny
fixture and one discipline, and assert structural invariants only — never exact phrasing.

## Why provider failures may be isolated

Multi-source review degrades gracefully: one timing-out discipline step should not discard
another provider's valid evidence or the SARIF import. Failures are recorded per step with
a secret-safe reason and surface in provider execution coverage; strict users choose
`FailRun`.

## Why the Engineering Review Package remains the only external artifact

Nothing about model integration is the external application's concern. No
`claude-review.json` / `openai-review.json` exists; provider payloads never enter the
package; `schemaVersion` stays **1.1** because no consumer-visible field changed. The
consumer DTO gate still passes.

## Consequences

- The previous Agent-Framework-based "Claude" provider (an OpenAI-compatible client) is
  replaced by two real adapters; the `Codex` stub is retired and "Codex" is accepted as a
  configuration **alias** for `OpenAI` (a code-focused model is just a configured model).
- An explicitly selected real provider is no longer silently swapped for Mock: it stays
  selected and reports a clear, categorized reason (superseding the ADR-002 fallback for
  explicit selection). The zero-config default remains the offline Mock.
- `IEvidenceProvider` gained a defaulted `UnavailableReason`; `EvidenceOptions` gained
  `ProviderFailureMode`.

## Explicitly out of scope

Councils, LLM reconciliation/arbitration, voting, provider ranking or weights,
recommendation or ticket generation, PR creation, code modification, parallel provider
execution, embeddings, vector databases, long-term provider memory, chat interfaces.
