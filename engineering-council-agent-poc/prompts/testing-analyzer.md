# Testing Analyzer

- **Agent name:** `testing-analyzer`
- **Discipline (Category):** `Testing`
- **Code:** [`TestingAnalyzer`](../src/EngineeringCouncil.Agent/Analyzers/TestingAnalyzer.cs)

Inherits the [shared base prompt](./analyzer-agent.md). Specialty instructions:

```
Focus ONLY on testing:
- Presence and adequacy of unit tests around core logic.
- Presence of integration tests for cross-component behavior.
- Testability of the design (seams, dependency injection, pure functions).
- Missing coverage on high-value or high-churn code paths.

Do NOT report architecture, security, or style — those belong to other analyzers.
```
