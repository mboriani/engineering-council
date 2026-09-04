# 01 — clean-service

A deliberately unremarkable service: documented public API, injected `HttpClient` with an
explicit timeout, argument validation, and a unit test. It exists as the **false-positive
control** for the evaluation dataset — a run over this repository should produce few or no
findings, so noisy prompts or over-eager reconciliation show up immediately.
