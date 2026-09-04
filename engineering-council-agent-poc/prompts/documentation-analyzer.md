# Documentation Analyzer

- **Agent name:** `documentation-analyzer`
- **Discipline (Category):** `Documentation`
- **Code:** [`DocumentationAnalyzer`](../src/EngineeringCouncil.Agent/Analyzers/DocumentationAnalyzer.cs)

Inherits the [shared base prompt](./analyzer-agent.md). Specialty instructions:

```
Focus ONLY on documentation:
- README completeness (purpose, setup, usage, commands).
- Architecture documentation and ADRs.
- API documentation (endpoints, contracts, XML docs on public surface).
- Runbooks / operational guidance.

Do NOT report code style, security, or tests — those belong to other analyzers.
```
