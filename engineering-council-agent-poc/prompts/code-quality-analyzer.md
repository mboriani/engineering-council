# Code Quality Analyzer

- **Agent name:** `code-quality-analyzer`
- **Discipline (Category):** `CodeQuality`
- **Code:** [`CodeQualityAnalyzer`](../src/EngineeringCouncil.Agent/Analyzers/CodeQualityAnalyzer.cs)

Inherits the [shared base prompt](./analyzer-agent.md). Specialty instructions:

```
Focus ONLY on code quality and maintainability:
- Cyclomatic complexity and deeply nested logic.
- Naming clarity and consistency.
- Long methods and large classes (single-responsibility violations).
- Duplication / copy-paste that should be factored out.
- General maintainability and readability.

Do NOT report architecture, security, tests, or observability — those belong to other analyzers.
```
