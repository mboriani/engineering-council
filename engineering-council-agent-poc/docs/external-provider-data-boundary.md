# External Provider Data Boundary

**Enabling a real LLM provider sends selected source code from the analyzed repository to
an external service.** This document states exactly what leaves the machine, what never
does, and how to run fully locally.

## Default posture

External providers are **disabled by default**. An unconfigured run performs no external
calls. A provider becomes active only when you explicitly select it (`--provider Claude`)
or enable it in configuration **and** the configured API-key environment variable is set.

## What is sent

For each acquisition step, the provider receives exactly one prompt containing:

- the shared system instruction (role: evidence acquisition source);
- the discipline objective and the strict response schema;
- **only the repository context chosen by the context selector** — file paths and file
  contents for the selected files, truncated to the configured per-file limit.

Selection is bounded by `Evidence:Context:MaximumFiles` (default 200),
`Evidence:Context:MaximumCharacters` (default 500000), and
`Evidence:Context:MaximumCharactersPerFile` (default 8000, per rendered file) and is
recorded per step (strategy, selected file count, character count, exclusion reasons).
Selection budgeting uses the **effective** (per-file-limited) size of each file, so the
character count equals the context that is actually rendered.

## What is never sent

- Files outside the selection (the whole repository is never uploaded automatically).
- Anything the scanner ignores (`bin`, `obj`, `.git`, `node_modules`, `packages`,
  `artifacts`, `.vs`).
- API keys of other providers, environment variables, or machine configuration.
- Analysis state: findings, observations, reconciliation results, or the review package.

Providers have **no file-system access**. They cannot rescan, widen, or re-request
context: the scanner and context selector are the authoritative boundary.

## What is never logged or persisted

- API keys, `Authorization` / `x-api-key` headers, or any secret value.
- Full request payloads (repository content) — logging is limited to counts and timings,
  e.g. `Provider=Claude Model=… Discipline=Security InputFiles=14 InputCharacters=48210
  DurationMs=8240 Success=true`.
- Raw HTTP envelopes. Error messages are sanitized and truncated, and any key-shaped text
  is redacted before it can be echoed.

The Engineering Review Package contains provider-neutral information only (which providers
ran, models used, observation/finding counts, token usage when reported). It never contains
credentials, headers, raw payloads, or SDK types.

## Diagnostic artifacts

The standard artifact set is unchanged. Provider request payloads are deliberately **not**
persisted, because they contain repository content. The validated structured response is
retained inside `Evidence.RawResponse` (diagnostic `run.json` only) — never in the external
integration artifact.

## Running fully locally

| Mode | Command |
|------|---------|
| Offline placeholders | `review --provider Mock --path <repo>` |
| Deterministic, no model | `review --provider Sarif --sarif ./artifacts/results.sarif --path <repo>` |
| Default (no config) | `review --path <repo>` → offline Mock |

These modes make no external calls at all.

## Operator checklist before enabling a real provider

1. Confirm the repository may be shared with the chosen provider under your organization's
   policy and the provider's data-retention terms.
2. Review the context limits so only what is needed is sent.
3. Set the API key in the environment (never in configuration or source control).
4. Prefer starting with a single discipline to observe cost and volume.
5. Use `Evidence:ProviderFailureMode = FailRun` if a partial run is unacceptable.
