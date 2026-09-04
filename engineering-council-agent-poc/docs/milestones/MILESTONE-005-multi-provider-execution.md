# MILESTONE-005 — Multi-Provider Execution

- **Status:** ✅ Complete
- **Date:** 2026-07-07
- **Version:** v5 (multi-provider evidence collection; still no council)
- **ADR:** [ADR-006](../adr/ADR-006-multi-provider-execution.md)

## Goal

Let a single analyzer obtain evidence from **multiple** providers in the same
analysis. The output remains a list of `Evidence` turned into findings. **No**
voting, consensus, comparison, council, or parallel execution.

## New flow

```
Repository → Scanner → Analyzer → Provider Executor → [Claude | Codex | Mock | …]
   → Evidence[] → Analyzer → Finding[]
```

## What changed

- **`IEvidenceExecutor`** (+ `EvidenceExecutor`): runs every configured provider
  for one analyzer request, isolating failures/timeouts, and returns all
  `Evidence`. Sequential; factored so parallel is a one-method change.
- **Analyzers** now depend on `IEvidenceExecutor` (not the factory). They
  interpret **each** evidence into findings and stamp `Finding.EvidenceProvider`.
- **`Evidence`** extended: `ProviderId`, `ProviderVersion`, `Success`,
  `ErrorMessage`, `Duration`, `CostEstimate`.
- **`IProviderExecutionLog`** + `ProviderExecutionReport`/`Record`/`Summary`:
  collected per run and written to **`provider-execution.json`**.
- **Merger** guard: no cross-provider merge (findings from different providers
  stay separate).
- **Config**: `Evidence:Providers` list (was `Evidence:DefaultProvider`); CLI
  `--providers a,b` (plus `--provider`, `--mock`). Unavailable `Claude` still
  falls back to `Mock`.

## Output files (per run)

Adds **`provider-execution.json`** to the existing six: `raw-findings.json`,
`findings.json`, `findings.md`, `council-summary.md`, `run-summary.md`,
`run.json`.

## Tests (all green — 28/28)

New `EvidenceExecutorTests`: one provider · two providers aggregated · a failing
provider does not stop the others · provider timeout recorded as failure ·
evidence aggregation · analyzer still produces findings from multi-provider
evidence. Existing pipeline/provider tests updated (executor + provider-execution
assertions).

## Exit criteria

| Criterion | Result |
|-----------|--------|
| Build (0 errors, 0 warnings) | ✅ |
| Tests pass | ✅ 28/28 |
| One analyzer runs multiple providers | ✅ `--providers Mock,Codex` → 14 executions |
| Provider failure isolated | ✅ Codex fails, Mock succeeds, run completes |
| Provider timeout handled | ✅ recorded as a failed evidence |
| Findings preserve provider + analyzer | ✅ `EvidenceProvider` + `SourceAgent` |
| `provider-execution.json` emitted | ✅ providers, executions, failures, evidence, tokens |
| No cross-provider merge | ✅ merger guards on `EvidenceProvider` |

## Verification

```
dotnet build EngineeringCouncil.slnx        # 0 errors, 0 warnings
dotnet test  EngineeringCouncil.slnx        # Passed: 28
dotnet run --project src/EngineeringCouncil.Cli -- analyze --path src/EngineeringCouncil.Core --providers Mock,Codex
#   14 executions (7 Mock ok, 7 Codex unavailable → failed), 7 findings, provider-execution.json written
```

## Explicitly out of scope (future milestones)

Provider voting · confidence comparison · consensus · council · parallel execution.
