# Evaluation Report

> Generated 2026-07-24 20:25:00Z · internal calibration artifact

This report measures the existing platform. It introduces no analysis capability and
does not influence `engineering-review-package.json`, which remains the only artifact the
external Engineering Review application consumes. **Measurements only — no provider is
declared better, ranked, or weighted.**

## Scope

- **Repositories evaluated:** 5 (01-clean-service, 02-vulnerable-payments, 03-tangled-architecture, 04-fragile-reliability, 05-undocumented-untested)
- **Providers evaluated:** Mock, Sarif
- **Evaluation runs:** 5

| Repository | Why it is in the dataset |
|------------|--------------------------|
| `01-clean-service` | Baseline / false-positive control: a small, documented, tested service with no deliberate defects. Findings here indicate over-reporting. |
| `02-vulnerable-payments` | Intentionally vulnerable: hardcoded credential, SQL built by concatenation, and an unauthenticated endpoint. Exercises the Security discipline and, via the bundled SARIF, deterministic multi-source reconciliation. |
| `03-tangled-architecture` | Architecture stress case: the domain layer reaches into infrastructure and the UI, and two modules depend on each other. Exercises layering-violation and coupling signals. |
| `04-fragile-reliability` | Reliability stress case: an outbound call with no timeout, a swallowed exception, no retry policy, and a fire-and-forget background loop. Exercises the Reliability discipline. |
| `05-undocumented-untested` | Documentation and testing gaps: a public API with no XML docs, no README, no tests, and a long branch-heavy method. Exercises the Documentation, Testing and CodeQuality disciplines. |

## Execution summary

| Repository | Providers | Status | Files | Obs | Raw | Consolidated | Total ms |
|------------|-----------|--------|------:|----:|----:|-------------:|---------:|
| `01-clean-service` | Mock, Sarif | Completed | 7 | 6 | 6 | 6 | 198.7 |
| `02-vulnerable-payments` | Mock, Sarif | Completed | 5 | 8 | 7 | 7 | 32 |
| `03-tangled-architecture` | Mock, Sarif | Completed | 4 | 6 | 6 | 6 | 7.5 |
| `04-fragile-reliability` | Mock, Sarif | Completed | 3 | 6 | 6 | 6 | 7.8 |
| `05-undocumented-untested` | Mock, Sarif | Completed | 3 | 6 | 6 | 6 | 7 |

## Provider comparison

Averages across the same repositories (identical inputs per repository).

| Provider selection | Runs | Avg exec ms | Avg observations | Avg raw | Avg consolidated | Dup. reduction % | Avg confidence(High) | Tokens |
|--------------------|-----:|------------:|-----------------:|--------:|-----------------:|-----------------:|---------------------:|-------:|
| Mock, Sarif | 5 | 50.6 | 6.4 | 6.2 | 6.2 | 0 | 2 | — |

> Duplicate rate is expressed as the reconciliation duplicate-reduction percentage.
> No ranking is implied: these are measurements of different sources on the same inputs.

## Context selection

| Repository | Providers | Repo files | Avg selected | Max selected | Avg chars | Avg omitted | Repeated sends | Truncations |
|------------|-----------|-----------:|-------------:|-------------:|----------:|------------:|---------------:|------------:|
| `01-clean-service` | Mock, Sarif | 7 | 3.2 | 5 | 1664 | 3.8 | 12 | 0 |
| `02-vulnerable-payments` | Mock, Sarif | 5 | 3.1 | 5 | 1567.7 | 1.9 | 17 | 0 |
| `03-tangled-architecture` | Mock, Sarif | 4 | 2.8 | 4 | 1671.7 | 1.2 | 13 | 0 |
| `04-fragile-reliability` | Mock, Sarif | 3 | 2 | 3 | 1552.5 | 1 | 9 | 0 |
| `05-undocumented-untested` | Mock, Sarif | 3 | 2.3 | 3 | 1449.7 | 0.7 | 11 | 0 |

_Signals only — the context selector is deliberately unchanged in this milestone._

## Prompt calibration

| Repository | Providers | LLM steps | Schema failures | Invalid % | Repairs | Repair success | Hallucinated refs | No location | Unsupported types |
|------------|-----------|----------:|----------------:|----------:|--------:|---------------:|------------------:|------------:|------------------:|
| `01-clean-service` | Mock, Sarif | 6 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `02-vulnerable-payments` | Mock, Sarif | 6 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `03-tangled-architecture` | Mock, Sarif | 6 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `04-fragile-reliability` | Mock, Sarif | 6 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `05-undocumented-untested` | Mock, Sarif | 6 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |

_Prompts are NOT redesigned in this milestone; evidence is collected first._

## Reconciliation calibration

| Repository | Providers | Raw | Consolidated | Reduction % | Multi-provider | Single-provider | Contradictions | False-merge candidates |
|------------|-----------|----:|-------------:|------------:|---------------:|----------------:|---------------:|-----------------------:|
| `01-clean-service` | Mock, Sarif | 6 | 6 | 0 | 0 | 6 | 0 | 0 |
| `02-vulnerable-payments` | Mock, Sarif | 7 | 7 | 0 | 1 | 6 | 0 | 0 |
| `03-tangled-architecture` | Mock, Sarif | 6 | 6 | 0 | 0 | 6 | 0 | 0 |
| `04-fragile-reliability` | Mock, Sarif | 6 | 6 | 0 | 0 | 6 | 0 | 0 |
| `05-undocumented-untested` | Mock, Sarif | 6 | 6 | 0 | 0 | 6 | 0 | 0 |

**Agreement distribution** (findings by number of independent providers)

- **1 provider(s):** 30
- **2 provider(s):** 1

_False-merge candidates are consolidated findings whose sources share no file; they are a
manual review queue, not defects. No reconciliation rule is changed in this milestone._

## Quality metrics (informational)

**Observations by discipline**

- **Security:** 7
- **Architecture:** 5
- **CodeQuality:** 5
- **Documentation:** 5
- **Reliability:** 5
- **Testing:** 5

**Findings by discipline**

- **Security:** 6
- **Architecture:** 5
- **CodeQuality:** 5
- **Documentation:** 5
- **Reliability:** 5
- **Testing:** 5

**Findings by provider**

- **Mock:** 30
- **SARIF:** 2

**Findings by severity**

- **Low:** 29
- **Critical:** 2

**Findings by confidence**

- **Low:** 29
- **High:** 2

- **Average provider agreement:** 2.9% of consolidated findings backed by ≥2 providers

_These metrics are informational and never affect package generation._

## Operational metrics

| Repository | Providers | Scan | Acquisition | Interpret | Analyze | Reconcile | Package | Persist | Slowest |
|------------|-----------|-----:|------------:|----------:|--------:|----------:|--------:|--------:|---------|
| `01-clean-service` | Mock, Sarif | 10.3 | 26 | 13.1 | 7.1 | 27.8 | 11.4 | 90.3 | Persistence |
| `02-vulnerable-payments` | Mock, Sarif | 1.1 | 17.5 | 5.7 | 0.2 | 1.6 | 0.4 | 5.1 | Acquisition |
| `03-tangled-architecture` | Mock, Sarif | 1.1 | 0.5 | 0.2 | 0.1 | 0.8 | 0 | 4.8 | Persistence |
| `04-fragile-reliability` | Mock, Sarif | 0.9 | 0.4 | 0.2 | 0.1 | 1.1 | 0.1 | 5 | Persistence |
| `05-undocumented-untested` | Mock, Sarif | 0.8 | 0.4 | 0.2 | 0.1 | 1 | 0 | 4.4 | Persistence |

Times in milliseconds. Provider latency (avg / max):

- `01-clean-service` [Mock, Sarif]: 3.4 ms avg · 19.5 ms max
- `02-vulnerable-payments` [Mock, Sarif]: 2.5 ms avg · 15.5 ms max
- `03-tangled-architecture` [Mock, Sarif]: 0.1 ms avg · 0.1 ms max
- `04-fragile-reliability` [Mock, Sarif]: 0.1 ms avg · 0.2 ms max
- `05-undocumented-untested` [Mock, Sarif]: 0.1 ms avg · 0.1 ms max

**Token usage and cost**

_No provider reported token usage in these runs (no pricing configured)._

## Package validation

| Repository | Providers | schemaVersion | Size (bytes) | Serialize ms | Deterministic | Root props | No type leaks |
|------------|-----------|---------------|-------------:|-------------:|---------------|------------|---------------|
| `01-clean-service` | Mock, Sarif | 1.1 | 39623 | 0.5 | yes | complete | yes |
| `02-vulnerable-payments` | Mock, Sarif | 1.1 | 46269 | 0.4 | yes | complete | yes |
| `03-tangled-architecture` | Mock, Sarif | 1.1 | 39565 | 0.4 | yes | complete | yes |
| `04-fragile-reliability` | Mock, Sarif | 1.1 | 39624 | 0.5 | yes | complete | yes |
| `05-undocumented-untested` | Mock, Sarif | 1.1 | 39649 | 0.6 | yes | complete | yes |

_The consumer-DTO gate remains the authoritative compatibility test (`PackageContractTests`)._

## Observations and recommendations for future milestones

- The slowest stage in 4/5 runs is **Persistence**.
- 5 run(s) re-send files across discipline contexts (max 17 redundant selections) — a selector-tuning candidate.
- No hallucinated file references were dropped by the guard in these runs.
- 0/30 LLM step(s) failed structured-output validation.
- Average reconciliation duplicate reduction is 0%; 0 false-merge candidate(s) await manual review.
- Every produced package kept the required root properties, deterministic serialization, and no type leaks.

These are inputs for future milestones. No selector, prompt, or reconciliation rule was
changed here, and no provider ranking or weighting is implied.

