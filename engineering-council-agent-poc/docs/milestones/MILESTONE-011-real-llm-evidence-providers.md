# MILESTONE-011 — Real LLM Evidence Providers

- **Status:** ✅ Complete
- **Date:** 2026-07-23
- **Version:** v11 (real model-backed evidence sources)
- **ADR:** [ADR-012](../adr/ADR-012-real-llm-evidence-providers.md)

## Goal

Integrate real Claude and OpenAI evidence providers into the EXISTING discipline-scoped
acquisition architecture and validate operationally that model-generated evidence still
ends as the same stable `engineering-review-package.json`. Provider integration only — no
councils, LLM reconciliation, voting, weighting, recommendations, or ticket generation.

## What changed

- **`ClaudeEvidenceProvider` + `OpenAiEvidenceProvider`** — real, discipline-scoped
  `IEvidenceProvider`s sharing one base (`LlmEvidenceProvider`): prompt → client → validate
  → (≤1 repair) → Evidence + telemetry. No provider-name branching anywhere in the planner,
  executor, analyzers, reconciler, or package builder.
- **Transport seams** — `IClaudeClient` / `IOpenAiClient` (+ real `ClaudeClient`
  (Anthropic Messages API) and `OpenAiClient` (Chat Completions) over `HttpClient`). All
  HTTP/SDK detail is confined here, so unit tests use scripted fakes.
- **Shared prompt** — `IEvidencePromptBuilder` → `DisciplineEvidencePromptBuilder`
  (system instruction: "you are an evidence acquisition source, not the reviewer") +
  the discipline objective + the strict schema. Providers receive ONLY the selected
  context; they never scan the repository.
- **Shared response schema (v1.0)** — `{ schemaVersion, discipline, observations[] }`,
  provider-neutral and compatible with the existing `StructuredLlmEvidenceInterpreter`.
- **Validation before interpretation** — conservative JSON extraction (direct JSON or
  exactly one fenced block; multiple documents / unbalanced JSON / prose-heavy responses
  rejected) plus schema-version, discipline-match, title/severity/confidence/file-reference
  and truncation checks. At most ONE repair attempt.
- **Resilience** — categorized errors (`LlmErrorCategory`), bounded exponential backoff for
  transient failures only, per-request timeout, cancellation propagation.
- **`Evidence:ProviderFailureMode`** — `Continue` (default, isolate the failure) or
  `FailRun` (fail the run). No quorum policies.
- **Configuration** — `Evidence:Claude` / `Evidence:OpenAI` with `Enabled`,
  `ApiKeyEnvironmentVariable`, `Model`, `Endpoint`, `MaxOutputTokens`, `TimeoutSeconds`,
  `MaxRetries`, `EnableStructuredRepair`. Disabled by default; keys only ever read from the
  named environment variable.
- **CLI** — `--provider` is now repeatable (`--provider Claude --provider OpenAI`),
  `--discipline` added as a repeatable singular form alongside `--disciplines a,b`, and a
  pre-flight check prints a categorized, secret-safe provider configuration error.
- **Codex** — the stub is retired; a code-focused model is simply the configured
  `Evidence:OpenAI:Model`, and `Codex` is accepted as a configuration alias for `OpenAI`.

## Behaviour change (documented)

An **explicitly selected** real provider without a usable key is no longer silently
swapped for Mock (ADR-002 fallback). It stays selected and the run reports:

```
Provider configuration error:
  Claude requires the environment variable ANTHROPIC_API_KEY.
```

then continues (or aborts under `FailRun`). An **unconfigured** run still defaults to the
offline Mock, so zero-setup local use is unchanged.

## Tests (all green — 150/150, +37 new; no network or credentials)

`LlmProviderTests` (32): disabled-by-default · missing-key reports the variable not the
secret · unselected provider needs no key · model/token config respected · both providers
discipline-scoped with all disciplines · Claude+OpenAI plan independent steps · prompt
carries discipline/schema/selected context and excludes omitted files, forbids Markdown and
invented references · empty observations valid · direct and fenced JSON accepted · 11
invalid-response cases rejected with categorized errors · multiple JSON documents rejected ·
truncation rejected · one repair rescues / repair failure categorized · rate-limit retried ·
auth never retried · timeout retried then categorized · cancellation propagates · telemetry
records provider/model/usage/ids/retries · unavailable usage omitted · no secret in evidence.
`LlmPipelineTests` (3): Claude+OpenAI+SARIF = 5 executions → observations from all three →
existing analyzers → reconciliation → one package the consumer DTO reads, with no secrets or
SDK types; `Continue` isolates a failed provider; `FailRun` fails the run.
`LlmIntegrationTests` (2, opt-in): real Claude / OpenAI calls behind
`RUN_CLAUDE_INTEGRATION_TESTS` / `RUN_OPENAI_INTEGRATION_TESTS` + a key.

## Exit criteria

| Criterion | Result |
|-----------|--------|
| Build (0 errors, 0 warnings) | ✅ |
| Normal tests pass without internet/keys | ✅ 150/150 |
| Both providers implement `IEvidenceProvider` and use the planner | ✅ |
| One step per provider per discipline; SARIF once | ✅ (5 executions in scenario C) |
| Providers receive only selected context | ✅ |
| Shared provider-neutral schema, validated before interpretation | ✅ |
| Retry / timeout / cancellation / repair handled explicitly | ✅ |
| Secrets never logged or written to artifacts | ✅ |
| Opt-in real integration tests available | ✅ |
| Existing analyzers + reconciliation unchanged | ✅ |
| One `engineering-review-package.json`, consumer DTO deserializes it | ✅ (schemaVersion 1.1) |

## Verification

```
dotnet build EngineeringCouncil.slnx        # 0 errors, 0 warnings
dotnet test  EngineeringCouncil.slnx        # Passed: 150 (no credentials required)

# Scenario A/B — one provider, one discipline
dotnet run --project src/EngineeringCouncil.Cli -- review --provider Claude --discipline Security --path <repo>
dotnet run --project src/EngineeringCouncil.Cli -- review --provider OpenAI --discipline Reliability --path <repo>

# Scenario C — Claude + OpenAI + SARIF, two disciplines → 5 provider executions
dotnet run --project src/EngineeringCouncil.Cli -- review \
    --provider Claude --provider OpenAI --provider Sarif --sarif ./artifacts/results.sarif \
    --discipline Security --discipline Reliability --path <repo>

# Opt-in real calls
RUN_CLAUDE_INTEGRATION_TESTS=true ANTHROPIC_API_KEY=... dotnet test
```

## Explicitly out of scope (future milestones)

Discipline Councils · Engineering Council · LLM reconciliation/arbitration · provider
voting/ranking/weights · recommendation engine · ticket generation · PR creation · code
modification · parallel provider execution · embeddings · vector databases · provider
memory · chat interfaces.
