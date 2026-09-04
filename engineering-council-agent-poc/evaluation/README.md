# Evaluation Dataset

A small, deterministic, publicly shareable set of fixture repositories used to measure and
calibrate the platform (Milestone 012). Every repository is authored here — no third-party
code, no secrets, no personal data — so results are reproducible and the dataset can be
shared.

Each repository carries an `evaluation.json` manifest describing **why it exists** and which
disciplines it is expected to exercise. Expectations are informational: they are never
asserted, because this milestone measures the platform rather than grading model output.

| Repository | Why it exists | Expected signals | SARIF |
|------------|---------------|------------------|-------|
| `01-clean-service` | **False-positive control.** Documented, tested, explicit timeouts, argument validation. Findings here indicate over-reporting. | — | — |
| `02-vulnerable-payments` | **Intentionally vulnerable.** Hardcoded credential (CWE-798), SQL concatenation (CWE-89), unauthenticated refund endpoint. | Security | ✅ `results.sarif` |
| `03-tangled-architecture` | Domain reaches into infrastructure and up into the UI; two modules depend on each other. | Architecture | — |
| `04-fragile-reliability` | No timeout, no cancellation, no retry, swallowed exception, fire-and-forget loop. | Reliability | — |
| `05-undocumented-untested` | No README, no tests, no XML docs, one long branch-heavy method. | Documentation, Testing, CodeQuality | — |

`02-vulnerable-payments` bundles a SARIF file so the deterministic reconciler has a genuine
multi-source case (a static tool and a model describing the same issue).

## Running an evaluation

```bash
# Offline (no credentials): measures the platform end to end
dotnet run --project src/EngineeringCouncil.Cli -- evaluate \
    --dataset ./evaluation/dataset --providers Mock,Sarif --outputs ./outputs/evaluations

# Provider comparison on identical inputs (needs the corresponding API keys)
dotnet run --project src/EngineeringCouncil.Cli -- evaluate \
    --dataset ./evaluation/dataset --compare Claude --compare OpenAI \
    --disciplines Security,Reliability --outputs ./outputs/evaluations
```

`--compare` runs each named provider **separately over the same repositories**, which is what
makes the provider comparison table meaningful. `--providers` runs one combined selection.

The run writes `evaluation-report.md` (internal calibration artifact) plus the normal
per-run artifacts. The external Engineering Review application continues to consume only
`engineering-review-package.json`.

## Safety

Fixture code in `02`, `03` and `04` is deliberately defective. It is scanned as text, never
compiled or executed by the platform, and must never be copied into real software.
