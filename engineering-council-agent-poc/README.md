# Engineering Council Agent — POC

An **isolated** proof-of-concept agent that reads a local .NET solution, analyzes
it with a [Microsoft Agent Framework](https://learn.microsoft.com/agent-framework/)
agent, and produces **actionable engineering improvement opportunities** as
Markdown + JSON — ready to be consumed by a future *Engineering Dashboard*.

> **Isolation:** everything lives under `engineering-council-agent-poc/`. This
> POC does not modify any existing project, does not touch the current
> dashboard, and shares **no** code, names, or domain with the FM project. It
> reuses only FM's *workflow pattern* (memory, decision log, ADRs, milestones,
> prompts, documented outputs). See
> [ADR-001](./docs/adr/ADR-001-reuse-workflow-pattern.md).

## What it does (v12.4 — observation calibration diagnostics)

1. **Scans** a target repo in **read-only** mode, ignoring
   `bin, obj, .git, node_modules, packages, artifacts, .vs`.
2. **Plans + acquires evidence** by scope: **repository-wide** sources run once;
   **discipline-scoped** sources run once per discipline — the LLM providers (**Claude**,
   **OpenAI** — real, disabled by default) with a shared provider-neutral prompt +
   discipline-selected context, and the **OpenCode** agentic runtime (**Agentic**, disabled
   by default), which launches the `opencode` executable against the repository root with
   one discipline-specific read-only instruction. Both return a validated structured schema;
   the repository-wide source is the native **SARIF 2.1.0** import (`--sarif`): one
   `Evidence` per tool run. An explicit
   **acquisition plan** drives the topology; then each raw `Evidence` is **interpreted**
   into normalized **`EngineeringObservation`s** — a source-neutral language every source
   maps onto. Only interpreters understand a source's format; analyzers never do.
3. **Analyzes** the observations with **seven specialized analyzer agents**
   (Architecture · CodeQuality · Reliability · Security · Testing · Documentation ·
   Observability). Each selects its discipline's observations, correlates them, and
   produces findings that carry provenance back to the observations, rules and providers.
4. **Reconciles** the raw findings deterministically (**Finding Reconciler**): findings
   from different sources (Claude, Codex, SARIF, …) that describe the same issue are
   consolidated with provider-independent agreement counting, severity/confidence
   reconciliation, and contradiction flags — no LLM, no voting, no weights. Then generates
   a **Council Summary**.
5. **Assembles** the **Engineering Review Package** — the first-class deliverable and the
   ONLY artifact the external Engineering Review app consumes (`schemaVersion 1.1`):
   repo identity, engineering **health** + **risk** (documented rule-based scores),
   metrics, evidence/observation summary, consolidated findings, a reconciliation summary,
   and an appendix.
6. **Writes** per-run artifacts to `outputs/{runId}/`:
   - `engineering-review-package.json` — **the package** (the ONLY external integration artifact)
   - `engineering-review.md` — the package as a review document (primary human artifact)
   - `observations.json` — normalized observations (diagnostic)
   - `findings.json` — consolidated (reconciled) findings (diagnostic)
   - `raw-findings.json` — original per-analyzer findings, pre-reconciliation (diagnostic)
   - `provider-execution.json` — which providers ran, timings, failures, tokens (diagnostic)
   - `provider-comparison.json` / `.md` — descriptive multi-LLM comparison, only when ≥2 providers ran a discipline (diagnostic)
   - `calibration-diagnostics.json` — calibration diagnostics (exclusive / severity-disagreement / low-agreement / observation-type-disagreement / location-disagreement), only when ≥2 providers ran a discipline (diagnostic)
   - `run.json` — full run for reload (diagnostic)

   The external application consumes **only** `engineering-review-package.json`; the rest
   are internal diagnostics. See [the integration contract](docs/integration/engineering-review-package-contract.md)
   and the sample at [`artifacts/samples/engineering-review-package.sample.json`](artifacts/samples/engineering-review-package.sample.json).

Boundaries are strict: **Evidence** (provider-specific & raw) → **Observation**
(normalized) → **Finding** (actionable conclusion) → **Package** (deliverable).
Providers only return evidence; interpreters only normalize; analyzers only reason
over observations (they never acquire evidence); the merger/summary/health-risk
scorer are **rule-based (no LLM)**. With no API key the offline **Mock** provider
runs, so the full pipeline works with zero setup.

> This is **not** a full Engineering Council yet — no consensus, voting, provider
> weighting, LLM reconciliation, discipline councils, recommendation engine, or
> ticket generation. See [ADR-009](./docs/adr/ADR-009-discipline-aware-evidence-acquisition.md).

**Reliability & security hardening (Milestone 011.1, [ADR-014](./docs/adr/ADR-014-reliability-security-hardening.md)):**
provider **timeouts** are correctly classified and retried while run **cancellation**
is never retried and stops the pipeline without persisting a package (the API answers
`499` instead of `200`); and **run ids** are validated against the generated format and
resolved strictly under the outputs root so path traversal is blocked (`GET /runs/{runId}`
returns `400` for a malformed id). No consumer-visible change — the package stays
`schemaVersion 1.1`.

**Domain & metrics correctness (Milestone 011.2, [ADR-015](./docs/adr/ADR-015-domain-metrics-correctness.md)):**
provider metrics are now **mutually exclusive** — each provider is **Successful**,
**Partial**, or **Failed** by execution outcome (`partialProviders` is new); an
**unrecognized discipline** is mapped to **Unknown** and counted in
`metrics.unclaimedObservations` instead of being silently treated as CodeQuality; and a
**discipline with no registered analyzer fails fast** before any scan — the CLI reports
`Requested discipline 'Performance' has no registered analyzer.` and the API returns
`400`. Package stays `schemaVersion 1.1`; only additive metric fields were added.

**Context budget correctness (Milestone 011.3, [ADR-016](./docs/adr/ADR-016-context-budget-correctness.md)):**
context selection now budgets each file by its **effective** (per-file-limited) size —
one shared `ContextContentPolicy` drives both the selector and the renderer, so the
recorded character count equals the context actually sent (new
`Evidence:Context:MaximumCharactersPerFile`, default 8000). The coverage report's
`contextFilesConsidered` is the **repository scope** the selector ranks (not the largest
single-step selection), while `contextFilesSelected` remains the sum of per-step
selections. Package stays `schemaVersion 1.1`.

## Architecture

```
   target repo
        │
        ▼
   IRepositoryScanner (read-only)  ──►  RepositorySnapshot
        │
        ▼
   AnalysisPipeline
        │
        ▼
   IEvidenceAcquisitionPlanner ── plan (provider × scope × discipline) ──┐
        │   repository-wide steps (Sonar/SARIF/Roslyn/Git/coverage)      │
        │   discipline steps (Claude/Mock × each selected discipline)    │
        ▼                                                                 │
   IEvidenceAcquisitionExecutor ── per-step context selection + provider │
        │   IAnalysisContextSelector picks discipline-relevant files  ◄──┘
        ▼
   Evidence[]  (acquisition provenance stamped)
        │
        ▼
   IEvidenceInterpretationPipeline  ── resolver picks IEvidenceInterpreter ──┐
        │   StructuredLlmEvidenceInterpreter (Mock/Claude) | future: SARIF…  │
        ▼                                                                     │
   EngineeringObservation[] (OBS-NNN)  ──►  observations.json  ◄─────────────┘
        │
        ▼
   AnalysisOrchestrator  ── discovers IEnumerable<IAnalyzerAgent> ──┐
        │   each analyzer selects its discipline's observations,    │
        │   correlates them, and builds findings w/ provenance:     │
        ├─ ArchitectureAnalyzer   ├─ SecurityAnalyzer               │
        ├─ CodeQualityAnalyzer     ├─ TestingAnalyzer               │
        ├─ ReliabilityAnalyzer     ├─ DocumentationAnalyzer         │
        │                          └─ ObservabilityAnalyzer  ◄──────┘
        ▼
   Raw Findings (RAW-NNN)  ──►  raw-findings.json
        │
        ▼
   IFindingMerger (RuleBasedFindingMerger)   ──►  Consolidated Findings (C-NNN)
        │
        ▼
   ICouncilSummaryGenerator (RuleBased…)     ──►  CouncilSummary
        │
        ▼
   IEngineeringReviewPackageBuilder          ──►  EngineeringReviewPackage
     (+ HealthRiskScorer, EngineeringMetrics, EvidenceSummary)    (the deliverable)
        │
        ▼
   IAnalysisRunRepository  ──►  outputs/{runId}/
     ├─ IEngineeringReviewMarkdownExporter  (engineering-review.md)
     └─ IJsonReportGenerator (engineering-review-package.json, observations.json,
                              findings.json, raw-findings.json,
                              provider-execution.json, run.json)
```

### Projects

| Project | Responsibility |
|---------|----------------|
| `EngineeringCouncil.Core` | Domain (`EngineeringReviewPackage`, `EngineeringObservation`, `Finding`, `Evidence`, `EngineeringHealth/Risk`, enums + `HealthRiskScorer`), interfaces (incl. `IEvidenceInterpreter`/resolver/pipeline, `IEvidenceExecutor`, `IEngineeringReviewPackageBuilder`), orchestrator + pipeline + package builder + `Core.Analysis` context builder. Only depends on logging abstractions. |
| `EngineeringCouncil.Agent` | `ObservationBasedAnalyzer` base + 7 specialized analyzers that reason over observations. **No Agent Framework dependency.** |
| `EngineeringCouncil.Infrastructure` | Scanner (+ `GitProbe`), evidence providers + factory, **acquisition planner + context selector + acquisition executor**, evidence interpreters + resolver + interpretation pipeline, finding merger + council summary, exporters, run repository, DI composition. |
| `EngineeringCouncil.Api` | Minimal API to trigger and query runs. |
| `EngineeringCouncil.Cli` | Command-line entry point. |
| `EngineeringCouncil.AppHost` | Aspire orchestration. |
| `EngineeringCouncil.Tests` | xUnit tests. |

Layering: `Core` ← `Agent` ← `Infrastructure` ← (`Api`, `Cli`). Only Core
interfaces cross layers; see
[ADR-002](./docs/adr/ADR-002-provider-abstraction-and-mock-fallback.md).

## Requirements

- .NET SDK **10.0+**
- (Optional) `OPENAI_API_KEY` to use the LLM (`Claude`) evidence provider.
  Without it, the `Mock` provider runs.

## Evidence providers

Providers listed in `Evidence:Providers` (config, as names or `{Name, Scope}`
objects) or `--providers` / `--provider` (CLI) are run according to an explicit
**acquisition plan**: repository-scoped sources once; discipline-scoped sources
once per selected discipline (`--disciplines a,b`, default all). Steps run
independently and are isolated — an unavailable/failing/timing-out step is recorded
in `provider-execution.json` and the others still run. Analyzers never
know which providers served them.

| Provider | Type | Status |
|----------|------|--------|
| `Claude` | LLM (Anthropic Messages API) | ✅ real, discipline-scoped · **disabled by default** · needs `ANTHROPIC_API_KEY` |
| `OpenAI` | LLM (Chat Completions) | ✅ real, discipline-scoped · **disabled by default** · needs `OPENAI_API_KEY` (`Codex` = alias) |
| `OpenCode` | Agentic (external coding-agent runtime) | ✅ real, discipline-scoped · **disabled by default** · needs the `opencode` executable on PATH (no API key) |
| `Mock` | LLM (offline) | ✅ zero-config default |
| `SARIF` | StaticAnalyzer | ✅ imports SARIF 2.1.0 files (`--sarif`), repository-scoped |
| `Ollama` | LLM | 🚧 stub (`NotImplementedException`) |
| `Roslyn`, `Sonar`, `Semgrep`, `NDepend` | StaticAnalyzer | 🚧 stub (repository-scoped) |
| `Git`, `Coverage` | Custom (repository metadata) | 🚧 stub (repository-scoped) |

> Multiple providers are **collected**, not compared: no voting/consensus yet.

## Commands

```bash
# Build & test
dotnet build EngineeringCouncil.slnx
dotnet test  EngineeringCouncil.slnx

# Produce an Engineering Review Package (read-only; offline Mock provider)
dotnet run --project src/EngineeringCouncil.Cli -- review --path "C:\path\to\solution" --provider Mock

# Same pipeline, findings-oriented console output
dotnet run --project src/EngineeringCouncil.Cli -- analyze --path "C:\path\to\solution" --provider Mock

# Run several providers per analyzer (all collected; failures isolated)
dotnet run --project src/EngineeringCouncil.Cli -- review --path "C:\path\to\solution" --providers Claude,Mock

# Import SARIF as evidence (native, deterministic; --sarif is repeatable)
dotnet run --project src/EngineeringCouncil.Cli -- review --provider Sarif --sarif ./artifacts/results.sarif --path "C:\path\to\solution"
#   → repository-scoped: SARIF executes once; one observation per SARIF result;
#     Static Analysis Sources appendix in engineering-review.md.
#   Config equivalent (appsettings.json): "Evidence": { "Sarif": { "Enabled": true, "Files": ["./artifacts/results.sarif"] } }

# Real LLM providers (disabled by default; selecting one opts in — sends selected
# repository content to that service. See docs/external-provider-data-boundary.md)
$env:ANTHROPIC_API_KEY="..."      # Claude
$env:OPENAI_API_KEY="..."         # OpenAI (a code-focused model is just Evidence:OpenAI:Model)
dotnet run --project src/EngineeringCouncil.Cli -- review --provider Claude --discipline Security --path "C:\path\to\solution"
dotnet run --project src/EngineeringCouncil.Cli -- review --provider OpenAI --discipline Reliability --path "C:\path\to\solution"

# Claude + OpenAI + SARIF over two disciplines → 5 provider executions, one package
dotnet run --project src/EngineeringCouncil.Cli -- review \
  --provider Claude --provider OpenAI --provider Sarif --sarif ./artifacts/results.sarif \
  --discipline Security --discipline Reliability --path "C:\path\to\solution"

# OpenCode agentic runtime (Milestone 013.2) — launches `opencode` against the
# repository root, one discipline-specific read-only instruction. Disabled by default;
# needs the `opencode` executable on PATH (no API key, no DeepSeek wiring yet).
dotnet run --project src/EngineeringCouncil.Cli -- review --provider OpenCode --discipline Security --path "C:\path\to\solution"
#   → OpenCode explores the repository itself (WorkingDirectory = repo root);
#     structured output becomes Evidence via the existing interpreter. Read-only is
#     instruction-level only — NOT an OS sandbox (see ADR-022 / MILESTONE-013.2).

# Evaluate/calibrate the platform over the fixture dataset (internal evaluation-report.md)
dotnet run --project src/EngineeringCouncil.Cli -- evaluate \
  --dataset ./evaluation/dataset --providers Mock,Sarif --outputs ./outputs/evaluations
# Provider comparison on identical inputs (needs keys):
#   evaluate --dataset ./evaluation/dataset --compare Claude --compare OpenAI --discipline Security

# Run the API (via Aspire)
dotnet run --project src/EngineeringCouncil.AppHost
#   POST /runs        { "targetPath": "C:\\path\\to\\solution" } — a cancelled run → 499, never 200
#   GET  /runs        → list run ids
#   GET  /runs/{id}   → run.json (malformed run id → 400; unknown → 404)
#   GET  /health
```

### CLI options

| Flag | Meaning |
|------|---------|
| `--path`, `-p` | Target solution/repo folder (read-only). |
| `--outputs`, `-o` | Output directory (default `outputs`). |
| `--provider` | A single evidence provider (e.g. `Claude`, `Mock`). Overrides config. |
| `--providers` | Comma-separated providers (e.g. `Claude,Sonar`). |
| `--disciplines` | Comma-separated disciplines to analyze (default: all). |
| `--sarif` | Import a SARIF 2.1.0 file as evidence (repeatable). Enables the SARIF source. |
| `--mock` | Shortcut for `--provider Mock`. |

Configured via `Evidence:Providers` in `appsettings.json` — names (`["Claude"]`)
or objects (`[{ "Name": "Claude", "Scope": "Discipline" }]`). Context-selection
limits are `Evidence:Context:MaximumFiles` (default 200),
`Evidence:Context:MaximumCharacters` (default 500000), and
`Evidence:Context:MaximumCharactersPerFile` (default 8000, per rendered file).
An invalid `--disciplines`
value is rejected with a clear error before any analysis runs, and a valid discipline
with **no registered analyzer** (e.g. `Performance`) fails fast the same way —
`Requested discipline 'Performance' has no registered analyzer.`

### Environment variables (real LLM providers)

| Var | Purpose |
|-----|---------|
| `ANTHROPIC_API_KEY` | API key for the `Claude` provider (variable name is configurable). |
| `OPENAI_API_KEY` | API key for the `OpenAI` provider (variable name is configurable). |
| `RUN_CLAUDE_INTEGRATION_TESTS` / `RUN_OPENAI_INTEGRATION_TESTS` | Set to `true` (with a key) to enable the opt-in real-provider tests. |

Keys are read from the environment at call time and are never stored in configuration,
logs, or artifacts. Models, timeouts, retries, and output-token limits are configured under
`Evidence:Claude` / `Evidence:OpenAI` — see [provider configuration](docs/provider-configuration.md)
and the [external data boundary](docs/external-provider-data-boundary.md). OpenCode has
**no API key**: it is configured under `Evidence:OpenCode` (`Enabled`, `Executable`,
`TimeoutSeconds`) and needs the `opencode` executable on PATH.

## Roadmap (why the design looks like this)

v8 acquires evidence by **scope** — repository-wide static sources once,
discipline-scoped LLM sources per discipline with focused prompts — driven by an
explicit, deterministic **acquisition plan**, while analyzers stay observation-only
and telemetry is per-run. Combined with the normalized **`EngineeringObservation`**
language (v7), any future source (SonarQube, Roslyn, SARIF, Semgrep, Git, coverage,
more LLMs) participates through one provider + interpreter with zero analyzer
changes — and the **Engineering Review Package** remains the first-class deliverable
every projection renders. Next: real interpreters/providers filling the skeletons,
provider comparison / voting / consensus over per-scope observations, a
recommendation engine / ticket generation, HTML / PDF / dashboard exporters, and an
LLM-based reconciler. **Parallel execution is shipped (M15.1):** independent
acquisition steps run concurrently up to `Evidence:Execution:MaxConcurrency`
(default 1 = sequential), with plan-order-stable artifacts. Adding a
provider is one `AddEvidenceProvider<T>()` line; an interpreter is one
`AddSingleton<IEvidenceInterpreter,…>` line; a discipline is a new `IAnalyzerAgent`
+ one `AddAnalyzer<T>()` line. See [`PROJECT_MEMORY.md`](./PROJECT_MEMORY.md),
[ADR-005](./docs/adr/ADR-005-evidence-provider-layer.md),
[ADR-006](./docs/adr/ADR-006-multi-provider-execution.md),
[ADR-007](./docs/adr/ADR-007-engineering-review-package.md),
[ADR-008](./docs/adr/ADR-008-engineering-observation-layer.md) and
[ADR-009](./docs/adr/ADR-009-discipline-aware-evidence-acquisition.md).

## Documentation

- [`PROJECT_MEMORY.md`](./PROJECT_MEMORY.md) — durable project context & roadmap
- [`DECISION_LOG.md`](./DECISION_LOG.md) — running log of decisions
- [`docs/adr/`](./docs/adr/) — architecture decision records
- [`docs/milestones/`](./docs/milestones/) — milestones & exit criteria
- [`docs/runbooks/`](./docs/runbooks/) — operational how-tos
- [`prompts/`](./prompts/) — versioned agent prompts
