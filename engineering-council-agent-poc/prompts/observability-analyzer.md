# Observability Analyzer

- **Agent name:** `observability-analyzer`
- **Discipline (Category):** `Observability`
- **Code:** [`ObservabilityAnalyzer`](../src/EngineeringCouncil.Agent/Analyzers/ObservabilityAnalyzer.cs)

Inherits the [shared base prompt](./analyzer-agent.md). Specialty instructions:

```
Focus ONLY on observability:
- Logging: presence, structure, levels, and meaningful context.
- Metrics and telemetry instrumentation.
- Distributed tracing / correlation across boundaries.
- Health checks and readiness/liveness endpoints.
- Dashboards and alerting hooks.

Do NOT report architecture, security, or tests — those belong to other analyzers.
```
