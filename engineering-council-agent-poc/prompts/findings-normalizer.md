# Findings Normalizer — Contract & Rules

The normalizer turns raw model output into validated
[`Finding`](../src/EngineeringCouncil.Core/Domain/Finding.cs) records. In v1 this
is deterministic code
([`FindingsNormalizer`](../src/EngineeringCouncil.Agent/FindingsNormalizer.cs)),
not an LLM call. This document specifies the behaviour so a future
LLM-based normalizer (for reconciling *multiple* agents' outputs) stays
compatible.

## Responsibilities

1. **Extract JSON** from arbitrary model text — tolerate ```json fences and
   surrounding prose (`ExtractJson` scans for the first balanced `{ … }`).
2. **Map enums leniently** — unknown/loose `category`, `severity`, `confidence`
   strings fall back to safe defaults (`CodeQuality`, `Medium`, `Medium`).
3. **Reject invented files** — drop any `fileReferences[].path` that is not in
   the scanned snapshot. This is the enforcement behind "do not invent files".
4. **Assign stable ids** — `F-001`, `F-002`, … within a run.
5. **Stamp attribution** — set `sourceAgent` from the producing agent.

## Future (v2) LLM normalizer role

```
You are a findings reconciler. You are given findings produced by several
independent review agents over the same repository. Merge duplicates, keep the
strongest evidence, prefer the higher-confidence phrasing, and never introduce a
file reference that no source agent cited. Return the merged set using the
shared findings contract.
```

## Invariants (must always hold)

- Output findings reference only real snapshot files.
- No finding is emitted without a `title`.
- Enum values are always valid domain enum members after normalization.
