# Improvement Plan — Engineering Council Agent (POC)

> Análisis del POC `.NET` basado en la documentación de `docs/` (ADRs 001–013,
> milestones, `integration-contract.md`, `provider-configuration.md`,
> `external-provider-data-boundary.md`, `PROJECT_MEMORY.md`, `README.md`) y en una
> lectura del código fuente (`src/`).

---

## 1. Objetivo de la aplicación

**Qué es.** Un agente de ingeniería **aislado y de solo lectura** (ADR-001) que
escanea un repositorio/solución .NET local y produce, como entregable de primera
clase, un **Engineering Review Package** (`engineering-review-package.json`,
`schemaVersion 1.1`) más un documento humano (`engineering-review.md`) con
**oportunidades de mejora accionables** y con salud/riesgo de ingeniería
cuantificados — listo para ser consumido por un futuro *Engineering Dashboard*
(ADR-007, `docs/integration-contract.md`).

**Pipeline (v12).** Escanear (read-only) → **planificar + adquirir evidencia**
(providers LLM Claude/OpenAI por disciplina + SARIF por repositorio, con plan
explícito, ADR-009/010/012) → **interpretar** a observaciones normalizadas
(ADR-008) → **7 analizadores** especializados por disciplina (ADR-003) →
**reconciliar** hallazgos de forma determinista (ADR-011) → **resumen de consejo**
→ **empaquetar** (ADR-007) → persistir en `outputs/{runId}/`. Fuera de alcance
hoy: consejos/disciplinas con voto, ranking de providers, recomendación/tickets,
exportadores HTML/PDF/dashboard y ejecución paralela (ADR-011/012/013).

**Contrato externo.** La aplicación externa consume **solo**
`engineering-review-package.json`; el resto (`findings.json`, `observations.json`,
`provider-execution.json`, `run.json`) son diagnósticos internos.

---

## 2. Fortalezas que hay que preservar

- Límites estrictos `Evidence → Observation → Finding → Package` con
  trazabilidad completa (ADR-008).
- Núcleo determinista y sin LLM (salud/riesgo, reconciliación, resumen).
- Providers/interprets desacoplados por metadatos, sin *switch* por nombre
  (con excepción señalada abajo).
- Instrumentación de evaluación aditiva (ADR-013), sin mutar el paquete.
- Suite amplia (157 tests) sin credenciales ni red.

---

## 3. Mejoras propuestas

Agrupadas en: **A) Correcciones críticas** (bugs/seguridad), **B) Valor de
producto**, **C) Robustez/calidad de código**, **D) Alineación docs–código**.
Al final hay una matriz de priorización.

### A) Correcciones críticas (bugs y seguridad)

**A1. Los *timeouts* de los providers LLM nunca se reintentan ni se reportan como timeout.**
- Cadena: `SendWithRetryAsync` crea un CTS con `CancelAfter(_options.Timeout)`
  (`Evidence/LlmEvidenceProvider.cs:126-127`) y lo pasa al cliente. El cliente
  atrapa el `OperationCanceledException` del token de timeout y lo re-clasifica
  como `LlmProviderException(Cancelled)` (`Llm/ClaudeClient.cs:59-62`,
  `OpenAiClient.cs` análogo). De vuelta en `SendWithRetryAsync`, ese `Cancelled`
  **no** casa con el filtro `when (ex.IsTransient)` (solo RateLimit/Timeout/Network/
  ServerError, `Llm/LlmClientContracts.cs:32-33`), así que **escapa sin reintento**;
  el branch `OCE → Timeout` (`LlmEvidenceProvider.cs:138-142`) queda muerto y el
  mensaje "timed out" nunca se produce (el step falla como "cancelled").
- Fix: que el cliente distinga timeout del token cancelado (p.ej. comparar el
  token del timeout vs el del run) y propague `Timeout`; o eliminar el filtro de
  re-clasificación en el cliente y dejar que `SendWithRetryAsync` maneje el OCE.
  Añadir test con fake que lance timeout.

**A2. La cancelación se convierte en un run "Failed" persistido, y la API devuelve HTTP 200.**
- `AnalysisPipeline.RunAsync` atrapa **todo** `Exception` incluyendo
  `OperationCanceledException` (`Core/Application/AnalysisPipeline.cs:214-224`),
  marca `Failed` y **continúa** construyendo y persistiendo el package. La API
  responde 200 con `status=failed` (`Api/Program.cs:96-111`).
- Fix: dejar propagar `OperationCanceledException` (o devolver un resultado
  distinguible / HTTP 499) y no persistir un paquete de un run fallido.

**A3. Path traversal en `GET /runs/{runId}`.**
- `FileSystemAnalysisRunRepository.GetAsync` combina el `runId` **sin validar**
  con `Path.Combine(root, runId, "run.json")` (`Persistence/FileSystemAnalysisRunRepository.cs:76-83`),
  sin verificar que el path resuelto esté dentro de `OutputsRoot`. Un `runId`
  como `..\..\secret` escapa del directorio de outputs.
- Fix: validar el `runId` (formato `yyyyMMdd-HHmmss-xxxxxx`) o resolver
  `Path.GetFullPath` y exigir `StartsWith(root)`.

**A4. `SuccessfulProviders` y `FailedProviders` no son excluyentes.**
- `EngineeringMetrics.From` (`Core/Domain/EngineeringMetrics.cs:55-56`): un
  provider con ejecuciones mixtas y 0 evidencia cuenta en **ambos**. Fix:
  clasificar por estado de todas sus ejecuciones (`all ok` / `all failed` /
  `partial`) y sumar en categorías disjuntas.

**A5. Disciplina desconocida → se silencia como `CodeQuality`, no `Unknown`.**
- `StructuredLlmEvidenceInterpreter` usa `CodeQuality` como fallback
  (`Infrastructure/Interpretation/StructuredLlmEvidenceInterpreter.cs:75`; igual
  en `Finding.cs:16` y `EngineeringObservation.cs:19`). Existe
  `FindingCategory.Unknown` precisamente para esto. Un LLM que reporte una
  disciplina no soportada infla el bucket CodeQuality. Fix: fallback a `Unknown`
  y contabilizar observaciones "unclaimed" (sin analizador) en el resumen.

**A6. Una disciplina solicitada sin analizador produce un run vacío silencioso.**
- `SelectedDisciplines` intersecta con los analizadores registrados
  (`AnalysisPipeline.cs:276-283`); intersección vacía ⇒ 0 steps ⇒ 0 findings ⇒
  salud "Excellent" engañosa. El CLI valida contra el enum, la API también
  (`Api/Program.cs:78-94`) pero **no contra los analizadores registrados**
  (`--discipline Performance` pasa y no hace nada). Fix: validar contra
  `IEnumerable<IAnalyzerAgent>` y fallar rápido con error claro.

### B) Valor de producto (mayor impacto para el dashboard)

**B1. Comparación entre runs / tendencia (el hueco más grande para un dashboard).**
Cada run rescanza, reinvoca providers y escribe en un `runId` con timestamp; no
hay forma de saber qué findings son nuevos/cambiados/resueltos respecto al run
anterior. Propuesta: un `latest` estable + un artefacto `delta` (nuevos/resueltos/
re-agravados) reutilizando el id estable `SEC-…` ya calculado por el reconciler
(`RuleBasedFindingReconciler`), y una vista de tendencia de salud/riesgo en el
markdown. Es aditivo al package (schemaVersion 1.2, minor).

**B2. Ejecución paralela (`Task.WhenAll`) con concurrencia acotada.**
Los tres hotspots están secuenciales y ya tienen los seams listos: ejecución de
steps (`EvidenceAcquisitionExecutor`), interpretación
(`EvidenceInterpretationPipeline`) y analizadores (`AnalysisOrchestrator`).
Es la reducción de wall-clock más barata (los pasos LLM dominan). Usar
`SemaphoreSlim` para acotar llamadas LLM concurrentes y preservar el aislamiento
de fallos por step.

**B3. API asíncrona con jobs en background.**
`POST /runs` bloquea hasta terminar la carrera completa (minutos con LLM reales).
Propuesta: devolver 202 + `runId` inmediato, ejecutar en background, y añadir
`GET /runs/{id}/status` y `GET /runs/{id}/package` (el `AnalysisRunStatus` ya
tiene `Pending/Scanning/Analyzing/Completed/Failed`). Solo `Analyzing` se usa hoy.

**B4. Caché intra-run de escaneo y contexto.**
`RuleBasedAnalysisContextSelector` se invoca por step; con N disciplinas × M
providers el mismo repositorio se procesa repetido. Cachear el snapshot y la
selección por disciplina (mismos límites `Evidence:Context`) reduce CPU y
latencia; opcionalmente un caché de respuestas LLM por hash de prompt (cuidando
la política de datos del `external-provider-data-boundary.md`).

**B5. Exportadores HTML / PDF / Dashboard.**
El package es el modelo único; `EngineeringReviewMarkdownExporter` y
`JsonReportGenerator` ya son proyecciones. Añadir `EngineeringReviewHtmlExporter`
y un `GET /runs/{id}/package` JSON bastaría para que el dashboard futuro consuma
directamente (hoy solo se sirve `run.json`).

**B6. Recomendación / tickets (roadmap explícito en ADR-007/011/013).**
El markdown ya genera un "Recommended Backlog" con `SuggestedTicketTitle`
(`Reporting/EngineeringReviewMarkdownExporter.cs:298-330`). Enriquecer el package
con `recommendedBacklog` estructurado (título, prioridad derivada de
severity+confidence+agreementCount, disciplina) prepara la generación de tickets
sin romper el contrato.

### C) Robustez y calidad de código

**C1. Una sola fuente de verdad para las disciplinas.**
La lista de 7 disciplinas está duplicada en ≥5 lugares (orden y contenido ya
divergen): `EngineeringReviewPackageBuilder.ReviewedDisciplines`, `MockEvidenceProvider.All`,
`LlmEvidenceProvider.AllDisciplines`, `EngineeringReviewMarkdownExporter.DisciplineOrder`,
`DisciplinePrompts`, además de `RuleBasedFindingReconciler.DisciplineCode` y el
selector. Propuesta: derivar del registro de `IAnalyzerAgent` (un solo lugar) y
usarlo en planner/prompt/exportador.

**C2. Eliminar el *hardcode* de nombres de provider deterministas en el reconciler.**
`RuleBasedFindingReconciler.DeterministicProviders` (`:31-32`) es la única fuga de
nombres en la capa "agnóstica". Un nuevo source estático exige editar el
reconciler. Mejor: que el provider declare en `EvidenceProviderMetadata`
`IsDeterministic` y el reconciler lea metadatos.

**C3. Constantes que deberían ser configuración.**
Confianza de evidencia LLM `0.7` (`LlmEvidenceProvider.cs:209`) y SARIF `0.95`
(`SarifEvidenceProvider.cs:120`); umbrales del reconciler (`0.6`, `LineTolerance=3`,
pesos por stage, `:27-28`); umbrales de salud/riesgo (`HealthRiskScorer.cs:25-37`);
truncación por archivo de 8000 chars (`Core/Analysis/RepositoryContextBuilder.cs:16`).

**C4. Bug del presupuesto de contexto: sobre-exclusión.**
El selector presupuesta con el contenido **sin truncar**
(`Acquisition/RuleBasedAnalysisContextSelector.cs:76-82`) mientras el builder
trunca cada archivo a 8000 chars. Un archivo de 600 KB consume todo el
presupuesto aunque solo se envíen 8 KB. Medir contra el contenido truncado.

**C5. Validación de config / disciplinas en la API y normalización de parsing.**
`CliArgs.Parse*` (`Cli/Program.cs:246-306`) y `EvidenceConfig` (`Api/Program.cs:128-193`)
duplican ~150 líneas. Extraer a un helper compartido. Además: la API no hace
pre-flight de providers desconocidos (el CLI sí).

**C6. `ContextFilesConsidered = Max`** (`AcquisitionCoverage.cs:61`) no coincide
con la semántica del nombre; decidir si es el total del repo o la suma, y
documentar.

**C7. Id de evidencia estable.**
`Evidence.Id` es un GUID aleatorio (`Domain/Evidence.cs:12`); la trazabilidad no
es estable entre runs. Derivar de provider+disciplina+hash del contenido para
poder comparar evidencia entre runs (complementa B1).

**C8. Git worktrees.**
`GitProbe.Probe` solo lee `<root>/.git` (`Scanning/GitProbe.cs:14-15`); en
`git worktree`/submodules el branch/commit aparece `(unknown)`. Soporte `.git`
file.

**C9. Limpieza de código muerto.**
`AnalysisRun.With` (nunca llamado), `Finding.SourceAgent` (dual-path legacy),
`FindingStatus.Discarded` (reservado, sin emisores), el bloque vacío en
`RuleBasedFindingReconciler.BuildClusters` (`:124`, el "keep strongest link" no
está implementado), `Providers/OpenAIProviderOptions.cs` (sin referencias),
`IFindingMerger/RuleBasedFindingMerger` (registrado y testeado pero fuera del
pipeline), y los `prompts/*-analyzer.md` (no usados por el runtime; el prompt real
es `DisciplinePrompts`). Decidir: eliminar, o documentar y enlazar.

### D) Alineación documentación ↔ código

- **README**: el diagrama de arquitectura (línea ~104) muestra
  `IFindingMerger → Consolidated Findings` como paso activo; el pipeline real usa
  `IFindingReconciler`. Actualizar el diagrama.
- **PROJECT_MEMORY**: dice "one `Evidence` per run" para SARIF; el código produce
  **una evidencia por tool run** (`SarifEvidenceProvider`, correcto en el README).
- **README §"zero setup"**: el fallback a Mock solo ocurre cuando la config
  efectiva resuelve a Mock; si `Evidence:Providers=["Claude"]` sin key, no hay
  fallback silencioso (por diseño v11) y el run queda vacío. Aclarar el texto.
- **Comentarios**: `CouncilSummary.cs:4-5` menciona `IFindingMerger` como paso de
  consolidación; ya no está en el pipeline.
- **Prompt files**: dos "fuentes de verdad" de prompts (`.md` en `prompts/` vs
  `DisciplinePrompts.cs`) que no se referencian entre sí.

---

## 4. Tests sugeridos (los 157 actuales no cubren)

- Timeout → retry + categoría `Timeout` (fake que lanza OCE por timeout).
- Cancelación del run → no persiste package / propaga (no `Failed` + HTTP 200).
- `GET /runs/{runId}` con `runId` malicioso → 400/404, no path traversal.
- Métricas `SuccessfulProviders`/`FailedProviders` con ejecuciones mixtas.
- Intersección vacía de disciplinas → error claro, no run vacío.
- Observación con disciplina desconocida → `Unknown` + contador "unclaimed".
- Delta entre dos runs (nuevos/resueltos) si se implementa B1.

---

## 5. Priorización (impacto × esfuerzo)

| # | Mejora | Impacto | Esfuerzo | Prioridad |
|---|--------|---------|----------|-----------|
| A1 | Fix timeout/retry LLM | Alto (correctness) | Bajo | 🔴 Inmediato |
| A2 | Cancelación no persistida + HTTP correcto | Alto (correctness) | Bajo | 🔴 Inmediato |
| A3 | Path traversal en `/runs/{id}` | Alto (seguridad) | Bajo | 🔴 Inmediato |
| A4 | Métricas de providers excluyentes | Medio (datos) | Bajo | 🟠 Alta |
| A5/A6 | Fallback `Unknown` + validación disciplinas | Medio | Bajo | 🟠 Alta |
| C4/C6 | Presupuesto de contexto correcto | Medio (coste LLM) | Bajo | 🟠 Alta |
| C1 | Fuente única de disciplinas | Medio (mantenimiento) | Medio | 🟠 Alta |
| B2 | Ejecución paralela acotada | Alto (rendimiento) | Medio | 🟠 Alta |
| B1 | Delta/tendencia entre runs | Alto (producto) | Medio | 🟡 Media |
| B3 | API asíncrona con jobs | Alto (producto) | Medio | 🟡 Media |
| C2/C3 | Metadatos deterministas + config | Medio | Medio | 🟡 Media |
| C5 | Normalizar parsing CLI/API | Medio | Bajo | 🟡 Media |
| B4/B5 | Caché + exportadores HTML/JSON | Medio | Medio | 🟢 Baja |
| C7/C8/C9 | Id estable, worktrees, limpieza | Bajo | Bajo | 🟢 Baja |
| D | Alinear docs con código | Bajo (credibilidad) | Bajo | 🟢 Baja |

**Secuencia recomendada:** aplicar primero el bloque A (inmediato, bajo esfuerzo,
corrige bugs reales y un hueco de seguridad), seguir con B2 (paralelismo) y B1
(delta) que son los que más valor aportan al dashboard, y acometer el resto según
hoja de ruta.
