# 02 — vulnerable-payments

An **intentionally vulnerable** fixture: a hardcoded database credential (CWE-798), a SQL
query built by string concatenation from untrusted input (CWE-89), and a refund endpoint
with no authorization attribute.

It exists to exercise the **Security** discipline and — because it ships a matching
`results.sarif` — to give the deterministic reconciler a genuine multi-source case where a
static tool and a model can describe the same issue.

> Fixture code only. It is never executed, and it must never be copied into real software.
