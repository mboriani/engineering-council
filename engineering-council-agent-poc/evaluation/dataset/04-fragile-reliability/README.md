# 04 — fragile-reliability

A reliability stress case: a shared `HttpClient` with **no timeout**, an outbound call with
**no cancellation token and no retry policy**, a **swallowed exception** that hides
failures, and a **fire-and-forget** polling loop with no backoff or cancellation.

It exists to exercise the Reliability discipline and to give the reconciler a realistic
"same issue, different wording" case (a model may report *"no explicit timeout"* while a
static rule reports *"missing timeout"* on the same file).
