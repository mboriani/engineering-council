# MILESTONE-016 — Graph-Assisted Context

- **Status:** CLOSED / ACCEPTED
- **Date:** 2026-09-10
- **Milestone type:** Experimental + Production integration
- **Builds on:** Graphify 0.9.53, M15.3/M15.4 agentic infrastructure

## Summary

Graph-assisted context is accepted for production/practical Council usage.
Graphify provides deterministic local graph extraction without LLM dependency.
Navigation context injection reduces FreshContextTokens for Security and Architecture
disciplines while staying within budget constraints.

## M16.1 — Graph feasibility

Graphify validated as a local deterministic graph extractor.

RedirectToService graph metrics:

- nodes: 811
- edges: 1630
- Graphify version: 0.9.53
- No LLM required for graph extraction

## M16.2 — Graph-assisted context experiments

Provider-neutral AdditionalContext injection implemented.

Security and Architecture minimal graph navigation contexts validated:

| Discipline | Approximate chars | Budget |
|---|---|---|
| Security | 1485 | <= 3000 |
| Architecture | 2639 | <= 3000 |

Experimental FreshContextTokens reduction:

| Discipline | Reduction |
|---|---|
| Security | approximately -50% |
| Architecture | approximately -28% |

Important findings preserved:

- Graph-assisted context is navigation/supporting context only
- NOT authoritative evidence
- Agents remain free to inspect repository source outside graph suggestions

## M16.3 — Production integration

Production graph assistance implemented as opt-in.

Pipeline:

```
Repository Snapshot
    ↓
Persistent Graph Cache
    ↓
Discipline-specific minimal graph context
    ↓
EvidenceRequest.AdditionalContext
    ↓
Existing agentic provider
```

Supported graph-assisted disciplines:

- Security
- Architecture

Unsupported disciplines preserve existing behavior (AdditionalContext = null).

Graphify is optional and non-blocking. If Graphify:

- is unavailable
- fails
- times out
- produces invalid output

Council continues normally without graph assistance.

Package schema remains: 1.1

## M16.3A — Production acceptance

Real Graphify execution validated:

- Initial extraction: approximately 4-7 seconds
- Graph persisted successfully
- Architecture and Security reused same graph during one Council execution

Production issues discovered and fixed:

- `6e8f927` Make Graphify extraction timeout configurable
- `58bb654` Fix Graphify extraction path and process deadlock

Graphify timeout configurable with practical default of 5 minutes.
Graphify uses `--code-only`. No LLM API key required.

## M16.3B — Persistent cross-run cache

Final accepted commit: `194f330` Stabilize graph cache identity across Council runs.

Stable graph cache identity implemented:

- For clean tracked Git repository: `GRAPH-{commitSha}`
- Tracked modifications invalidate graph identity
- Different commits invalidate graph identity
- Non-Git repositories use deterministic content fingerprint fallback

Council/OpenCode generated analysis artifacts no longer invalidate graph cache.

Cross-process acceptance:

| Run | Result |
|---|---|
| RUN 1 | real cache miss, Graphify extraction executed, graph persisted |
| RUN 2 | separate process, same source snapshot, same GraphCacheKey, cache hit, Graphify NOT executed |

Primary production acceptance criterion: PASSED.

## Known limitation

For Git repositories with clean tracked working tree, legitimate NEW UNTRACKED
source files may not currently participate in the graph cache key.

Example:

```
?? src/NewFeature.cs
```

may leave `GRAPH-{commitSha}` unchanged, allowing a previously cached graph to be
reused even though Graphify could see newly created untracked source files.

This is a KNOWN LIMITATION. It does NOT block M16 acceptance or merge because
normal committed/CI source snapshots are correctly represented.

Future hardening only. Do not create M16.3C.

## Verification

- GraphAssistance tests: PASS
- GraphCacheIdentity tests: PASS
- GraphifySecurityContextBuilder tests: PASS
- GraphifyArchitectureContextBuilder tests: PASS
- EvidenceProvider AdditionalContext tests: PASS
- Build: 0 warnings, 0 errors
- Package schemaVersion: 1.1 (unchanged)

## Score / contract / docs

- `schemaVersion` stays **1.1**; no new package/domain fields; consumer DTO unchanged.
- GraphAssistance remains opt-in / disabled unless explicitly configured.
- Graphify remains optional and non-blocking.
- Docs: this milestone, PROJECT_MEMORY v38, DECISION_LOG D-111.
- No further M16 experiments planned.
