# 03 — tangled-architecture

An architecture stress case. The **domain** layer opens SQL connections itself (no
repository abstraction), calls **upward** into the UI layer, and forms a **mutual
dependency** with the reporting module.

It exists to exercise the Architecture discipline: layering violations, inverted
dependencies, and circular coupling. Nothing here is a security or reliability defect, so
it also shows whether other disciplines over-report on architectural noise.
