# Consumo elevado de tokens — diagnóstico y alternativas

> Análisis de **por qué** los agentes del Engineering Council Agent POC queman
> muchos tokens y qué se puede hacer para reducirlo. Se basa en el código fuente
> (`src/`) y complementa `improvement.md`.

---

## 1. Dónde se consumen los tokens (y dónde no)

**Importante:** los 7 analizadores (Architecture, CodeQuality, Reliability, Security,
Testing, Documentation, Observability) son **rule-based y corren localmente — no
consumen tokens**. Los tokens solo se queman en la **adquisición de evidencia LLM**
(`LlmEvidenceProvider`), es decir, las llamadas a Claude/OpenAI. El consumo total es:

```
consumo ≈ contextoEnviado × disciplinas × providers  +  (repairs + retries) × contextoEnviado  +  output
```

El problema no son los analizadores: es **el contexto de repositorio que se
re-envía completo, una vez por cada combinación (provider × disciplina)**.

### Fórmula de coste por run (defaults de `appsettings.json`)

| Variable | Default | Fuente |
|---|---|---|
| Contexto máximo por disciplina | 200 archivos / **500.000 chars** | `Evidence:Context`, `RuleBasedAnalysisContextSelector.cs:18` |
| Truncación por archivo | 8.000 chars | `RepositoryContextBuilder.cs:16` |
| Disciplinas | 7 (todas) | `LlmEvidenceProvider.cs:23-28` |
| `MaxOutputTokens` por llamada | 8.000 | `Cli/appsettings.json:20,29` |
| Repair | 1 reintento por respuesta inválida | `LlmEvidenceProvider.cs:81-98` |
| Retries | 2 (transitorios) | `Cli/appsettings.json:22,31` |

**Estimación con presupuesto completo (peor caso realista):**
- ~500.000 chars ≈ **125.000–150.000 tokens** de entrada por llamada de disciplina (el
  código va a ~3,5 chars/token).
- 7 disciplinas × 1 provider ≈ **875K–1M tokens de entrada por run**.
- Con Claude **+** OpenAI (14 llamadas) ≈ **1,75M–2M tokens de entrada por run**.
- Cada *repair* o *retry* **re-envía el mismo contexto completo** (~+125–150K tokens).
- Output: hasta 7 × 8.000 = **56K tokens** de salida por provider.

---

## 2. Por qué consume tanto — causas raíz

### R1. El contexto se re-envía N veces (multiplicador principal)
`EvidenceAcquisitionExecutor` ejecuta **un step por (provider × disciplina)** y cada
step invoca `IAnalysisContextSelector` + `RepositoryContextBuilder`, que re-arma el
contexto con los archivos seleccionados. El mismo contenido del repositorio se envía
**7× (o 14× con 2 providers)**, no una sola vez. La propia plataforma lo mide:
`EvaluationMetricsCollector.RepeatedFileSelections` se calcula como
`Σ archivos seleccionados − archivos del repo` (`EvaluationMetricsCollector.cs:116`) y el
reporte de evaluación lo señala como *"redundant selections — a selector-tuning candidate"*
(`EvaluationReportExporter.cs:298-301`).

### R2. El presupuesto es **por paso**, no por run
`MaximumCharacters = 500.000` se aplica a **cada** step, no al total del run. El run
completo puede enviar hasta `7 × 500K` chars (14× con 2 providers) sin ningún límite
global. No hay un "token budget" total configurable.

### R3. El selector presupuesta contra contenido **sin truncar**
El selector suma `file.Content.Length` completo (`RuleBasedAnalysisContextSelector.cs:76-82`),
pero el builder trunca cada archivo a 8.000 chars (`RepositoryContextBuilder.cs:34,41-42`).
Consecuencias:
- El presupuesto de 500K se llena con archivos contados a tamaño real → se **excluyen**
  archivos que sí cabrían truncados → el modelo ve menos contexto (calidad), y
- el contenido enviado nunca se aproxima a lo que cabría → el límite "de facto" está
  mal calibrado.

### R4. Cada *repair* duplica el coste del step
Cuando la respuesta inválida (JSON malformado, schema fuera, truncado…), el provider
hace **otra llamada completa re-enviando el contexto + toda la respuesta inválida**
(`LlmEvidenceProvider.cs:89-90`, `BuildRepairContent:157-171`). Si la tasa de respuestas
inválidas es alta, esto casi duplica los tokens de ese step.

### R5. Los *retries* re-enviarán el contexto completo
`SendWithRetryAsync` reintenta (2×, backoff) transitorios re-enviando la misma
request completa (`LlmEvidenceProvider.cs:144-153`). Cada timeout/rate-limit cuesta un
contexto entero más.

### R6. `MaxOutputTokens = 8.000` por llamada
El tope de salida es alto; además el modelo puede generar JSON verbose
(`description`, `evidenceExcerpt`, `recommendationHint`, `tags` para cada observación)
que infla la salida y el coste.

### R7. Prompt "fijo" relativamente largo re-enviado en cada llamada
`SystemInstructions` (~260 tokens) + `OutputContract` (~800 tokens) + objetivo de
disciplina se envían en cada una de las 7–14 llamadas. Pequeño frente al contexto,
pero suman.

### R8. Sin caché ni análisis incremental entre runs
Cada run **rescanza el repo y re-invoca los providers** aunque no haya cambiado nada.
No hay diffs git, no hay caché de respuestas, no hay comparación con el run anterior
(también señalado como B1 en `improvement.md`). Para un uso tipo *dashboard* (correr
cada noche), esto multiplica el coste por el número de runs.

---

## 3. Alternativas para reducir el consumo

Ordenadas por **impacto estimado ÷ esfuerzo**. No todas requieren código; las
primeras son cambios de configuración inmediatos.

### 3.1 Quick wins — solo configuración (0 código, hoy mismo)

| Cambio | Cómo | Impacto estimado |
|---|---|---|
| **Reducir presupuesto por disciplina** | `Evidence:Context:MaximumCharacters` 500K → **100–150K** y `MaximumFiles` 200 → **40–60** | −60 a −75% de entrada |
| **Limitar disciplinas por run** | `--disciplines Security,Reliability` o `Evidence:Disciplines` | proporcional (÷7 por disciplina) |
| **Desactivar repair** | `Evidence:OpenAI:EnableStructuredRepair=false` (y Claude) | elimina el duplicado de cada respuesta inválida |
| **Bajar `MaxOutputTokens`** | 8000 → **2000–3000** | acota el peor caso de salida |
| **Un solo provider LLM** | activar Claude o OpenAI, no ambos | ÷2 |
| **Modelo más barato por disciplina** | p.ej. `gpt-4o-mini` ya configurado; `claude-haiku`-clase para disciplinas "ligeras" | hasta −80% por token |

> Estos cambios respetan el diseño (ADR-009/012): no tocan analizadores ni el package.
> El `external-provider-data-boundary.md` ya recomienda *"prefer starting with a single
> discipline to observe cost and volume"* — exactamente esta palanca.

### 3.2 Cambios de código de bajo esfuerzo (alto impacto)

1. **Compartir el contexto en vez de re-enviarlo por disciplina (dedupe de envíos).**
   Hoy cada step re-arma el mismo contenido. Opciones:
   - *Presupuesto global por run*: un `Evidence:TotalContextCharacters` que se reparte
     entre steps (el selector recibe el presupuesto restante).
   - *Caché de selección por disciplina*: `RuleBasedAnalysisContextSelector` ya es
     determinista → memoizar `(disciplina → selección)` y reutilizar el texto del
     contexto en pasos con los mismos límites (elimina el trabajo y permite dedupe
     si algún día se agrupan disciplinas).
   - Impacto: no reduce tokens enviados, pero es el paso previo para 3.3.

2. **Arreglar R3 (presupuestar contra contenido truncado).** Medir el tamaño que
   **realmente se envía** (truncado a 8K) en `RuleBasedAnalysisContextSelector`.
   Mejora calidad y, al calibrar mejor el límite, permite bajarlo con confianza.

3. **Prompt más compacto.** Recortar `SystemInstructions` + `OutputContract`
   (`DisciplinePrompts.cs:67-112`) a lo esencial y usar JSON Schema
   (`response_format`) en vez de explicar el contrato en prosa. Impacto: pequeños, pero
   se aplica a las 7–14 llamadas.

4. **Repair barato.** En `BuildRepairContent` no re-enviar el contexto completo; enviar
   solo errores de validación + JSON inválido (el contexto ya no se necesita para
   corregir formato). Y/o usar un modelo más barato para el repair.

### 3.3 Cambios de mayor alcance (el mayor ahorro real)

5. **Contexto estructural en vez de fuente cruda (Roslyn).** En lugar de enviar
   archivos completos, extraer **firmas/símbolos/resumen por archivo** (usando Roslyn,
   cuyo stub ya existe en `FutureEvidenceProviders.cs`) y enviar solo el código
   completo de los archivos candidatos. Un archivo de 800 líneas puede resumirse en
   ~40 líneas de firma. Impacto: **−70 a −90% del contexto**, con mejor precisión.
   Es la alternativa con mejor relación calidad/coste a medio plazo.

6. **Detección de cambios incremental (git diff).** Para repos analizados
   periódicamente: usar `GitProbe` para comparar con el commit anterior y enviar solo
   los archivos cambiados + referencias. Elimina re-análisis de código intacto.
   Impacto: proporcional a la tasa de cambio (en un repo estable, **−90%+**).

7. **Caché de respuestas por hash de prompt+modelo.** Si el contexto seleccionado y el
   prompt son idénticos entre runs/disciplinas, reutilizar la respuesta validada
   guardada en `run.json`/`outputs`. Combinado con 6, evita llamadas redundantes.

8. **Cascada de modelos (dos fases).** Un modelo barato hace un *pre-screen* con
   contexto pequeño para señalar archivos/símbolos de interés; el modelo de gama alta
   revisa **solo** esos archivos con contexto acotado. Típicamente **−50 a −80%** del
   coste manteniendo calidad en las disciplinas críticas.

9. **Límite global de tokens del run.** Añadir un presupuesto total (input+output)
   configurable que el planner/executor respete, asignándolo por step — convierte el
   coste en predecible y plafoneado.

### 3.4 No ayuda (aclaración)

- **Ejecución paralela (`Task.WhenAll`)** reduce el **tiempo**, no los **tokens**
  (se envían las mismas llamadas). Combinar con dedupe/caché sí reduce ambos.
- **Mejorar solo los analizadores** no afecta el consumo: ellos no llaman al LLM.

---

## 4. Cómo medir (la plataforma ya lo instrumenta)

El harness de evaluación (ADR-013) mide exactamente esto:

```bash
dotnet run --project src/EngineeringCouncil.Cli -- evaluate \
    --dataset ./evaluation/dataset --compare Claude --compare OpenAI \
    --discipline Security --outputs ./outputs/evaluations
```

- Métricas útiles: `ContextFileCount`, `SelectedFilesAverage/Max`, `OmittedFilesAverage`,
  **`RepeatedFileSelections`** y **`ResponseTruncations`** (`EvaluationModels.cs:62`,
  `EvaluationMetricsCollector.cs:95-116`) y token usage/coste opcional.
- Usar **`RepeatedFileSelections` como KPI del problema**: un valor alto = el mismo
  contenido se re-envía entre disciplinas.
- Comparar antes/después de cada cambio de configuración con `--compare` sobre
  inputs idénticos.

---

## 5. Resumen de recomendación

1. **Hoy, sin código:** bajar `MaximumCharacters`/`MaximumFiles`, activar una sola
   disciplina a la vez, `EnableStructuredRepair=false`, `MaxOutputTokens` menor.
   *Fácil y reversible → −50 a −75%.*
2. **Corto plazo (código bajo esfuerzo):** presupuesto global por run + caché de
   selección, presupuestar contra contenido truncado, prompt compacto con JSON Schema,
   repair sin re-enviar contexto.
3. **Medio plazo (máximo ahorro):** contexto estructural con Roslyn + análisis
   incremental por git diff + caché de respuestas + cascada de modelos.
4. **No olvidar:** medir con `evaluate` antes y después; el objetivo es reducir
   tokens **manteniendo** la tasa de observaciones válidas (no sacrificar el
   `RepeatedFileSelections` a cambio de quedarse ciego).
