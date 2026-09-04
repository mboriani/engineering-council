# ADR-001 — Reuse the FM Director *workflow pattern*, not its code or domain

- **Status:** Accepted
- **Date:** 2026-07-06
- **Deciders:** Engineering Council POC owners

## Context

An existing internal project ("FM Director", referred to here as *FM*) has a
working, well-liked way of *operating*: it keeps a project memory, a decision
log, ADRs, milestones, versioned prompts, and documented outputs. We want the
Engineering Council Agent POC to inherit that discipline.

However, the POC must be **completely isolated**:

- It must not modify any existing project or the current dashboard.
- It must not reuse FM's names, folders, code, or domain model.
- It must live entirely inside `engineering-council-agent-poc/`.

## Decision

We reuse **only the workflow/operating pattern** from FM:

- `PROJECT_MEMORY.md` + `docs/memory/` — durable project context.
- `DECISION_LOG.md` + `docs/decision-log/` — running log of decisions.
- `docs/adr/` — architecture decision records (this file is the first).
- `docs/milestones/` — milestone definitions and their exit criteria.
- `prompts/` — versioned, reviewable agent prompts.
- `outputs/` — documented, machine-readable run artifacts.

We do **not** reuse:

- Any FM source code, assemblies, or packages.
- FM's domain model (its entities, enums, or contracts).
- FM's project/folder names or namespaces. This POC uses its own
  `EngineeringCouncil.*` namespaces and a fresh domain
  (`AnalysisRun`, `Finding`, …).

## Consequences

- **Positive:** familiar operating rhythm; a future reviewer can navigate the
  POC the same way they navigate FM. Zero blast radius on existing systems.
- **Positive:** the POC's domain is free to evolve toward a multi-agent,
  multi-LLM council without being constrained by FM's model.
- **Negative:** some documentation scaffolding is duplicated rather than shared.
  Accepted deliberately — isolation is worth more than DRY here.

## Related

- [ADR-002 — Provider abstraction & mock fallback](./ADR-002-provider-abstraction-and-mock-fallback.md)
