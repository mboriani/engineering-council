# MILESTONE-015.2 — Post-Run Output Quality (First Full 7-Discipline Council Run)

- **Status:** ✅ Complete
- **Date:** 2026-08-13
- **Builds on:** [MILESTONE-015.1](./MILESTONE-015.1-parallel-agent-execution.md)
- **ADR:** none — this milestone evaluates and records a real run; the discovered
  gaps (evidence coverage transparency, reproducibility) are recommendations for
  M15.3, not contract changes made here.

## Goal

Execute the **first full-scale Council run** — all three agentic providers
(OpenCode, Codex, Claude Code) over **all 7 disciplines** against a real
repository — and produce an **evidence-based verdict on output quality**: are the
findings accurate, actionable, and honest about their own coverage? The prior
Council self-assessment says *"output quality is acceptable"*; this milestone
tests that claim the way MILESTONE-014.1 did for the single-discipline Security
run, but now for a 3×7 parallel acquisition.

- **RunId:** `20260813-230240-b3e36e` · status `completed`
- **Target:** `C:\Repositories\Paramo\RedirectTo\redirect-to-service`
  (`RedirectToService.slnx` → solution `RedirectToService`, **6 projects**,
  **122 files**, branch `master` @ commit `4a7d279aaa99`)
- **Scope:** providers `OpenCode,Codex,ClaudeCode` × disciplines
  `architecture, codeQuality, reliability, security, testing, documentation, observability`
  (plan + acquisition, `Evidence:Execution:MaxConcurrency=3`)
- **Timing:** started `23:02:40.47Z` → completed `23:16:12.04Z` ·
  `analysisDuration` **00:13:31.57** · externally measured wall-clock **~814 s**
  (0.4% drift vs recorded duration — parallel acquisition, so the SUM aggregator
  in `provider-execution.json` (00:36:54.26) is not wall-clock, per M15.1 note)

## 1. Case log — the run

### 1.1 Acquisition outcomes (21 steps)

| Provider | Success | Failures | Timeouts | Observations | Raw findings |
|----------|--------:|---------:|---------:|-------------:|-------------:|
| OpenCode   | 5/7 | 2 | 2 | 51 | 17 |
| Codex      | 4/7 | 3 | 3 | 18 |  8 |
| ClaudeCode | 2/7 | 5 | 5 | 13 |  4 |
| **Total**  | **11/21** | **10** | **10** | **82** | **29** |

Discipline coverage (successful steps per discipline): CodeQuality 2/3 ·
Reliability 3/3 · Security 2/3 · Testing **0/3** · Architecture **0/3** ·
Documentation 3/3 · Observability 2/3. All 10 failures were provider **timeouts**
(no retries, no repairs attempted — `retries=0`, `repairAttempts=0`).
**Every provider was `partial`** (at least one timed-out discipline).

### 1.2 Council output

- **29 consolidated findings** (29 raw, **0 merged**, 0 contradictions):
  severity `critical 1 / high 6 / medium 13 / low 9`.
- Discipline mix: codeQuality 10 · reliability 8 · security 5 · observability 5 ·
  documentation 1. (No architecture or testing findings — see §2.)
- **Council assessment:** singleSource **23** · strongAgreement **6** ·
  agreementWithDifferences **0** · potentialConflict **0**.
  Findings supported by: OpenCode 18 · Codex 14 · ClaudeCode 5.
- **Artifacts:** 21 files, exactly **one** `engineering-review-package.json`
  (`schemaVersion` **1.1**), plus `engineering-review.md`, `findings.json`,
  `raw-findings.json`, `observations.json`, `provider-execution.json`,
  `run.json`, summary, evidence, plans, `*.sarif`/`*.csproj*` in `outputs/`.
  All JSON parses; package health **`Critical`**.
- **Hygiene:** no provider/API credentials in any artifact — the only
  secret-looking value is the **target repo's own committed** `SONAR_TOKEN`
  (`9cf5ecf0…d00`), which is correctly surfaced *as a finding* and appears only
  as evidence passthrough. No orphan provider processes remain after the run.
- **Context contract (M11/M13):** agentic acquisitions were scoped by the
  request's context selection (agentic providers receive the file index, not
  embedded file bodies), matching the M13 agentic-evidence contract.

## 2. Output quality evaluation

Verdict scores (1 = worst, 5 = best) are assigned against the Council's own
quality dimensions from the M12.1 case-log template, applied to the full run.

| Dimension | Score | Evidence |
|-----------|:-----:|----------|
| **Content fidelity** (verbatim accuracy) | **4.5** | Top findings quote code **verbatim** and mutually-consistently — `SONAR_TOKEN=9cf5ecf0e09015e5aca77720a67c30ef9dde7d00` in `docker-compose.yml`; `KeyString/IVString` hardcoded `5080808080802072/6080808080807072`; `AddHttpClient($"MarketingBonusWalletBalanceContext-{realm}", …)` with BaseAddress only; `catch (Exception e) { … fall back … }` with the literal comment *"HTTP 200 + a redirect"*; `/readyz` + `/healthy`; `BuildAuthorityServerUri` with `"Staging"/"Production"`/`highrollercasino`; the host-level Dockerfile's bogus `COPY ["RedirectToService.HttpApi/RedirectToService.HttpApi.csproj", …]`. Sampled high-severity findings verified against committed content (§3). |
| **Signal honesty** (absence-of-evidence vs. absence-of-issues) | **1.5** | **Defect:** the executive summary reports *"No Architecture issues identified"* and *"No Testing issues identified"* as validated strengths (low risk) even though **all 6 Architecture+Testing steps timed out (0% evidence coverage for those disciplines)**. Missing data is presented as an all-clear — the single most important quality problem observed. |
| **Deduplication / count integrity** | **2** | **9 of 29 findings (~31%) are the same 3 root causes reported multiple times:** RequestFilter NRE → **4 findings** (REL-ef8123defd, OBS-8bbcd944d5, REL-22007f5cbf, CQL-734939147d) across 3 disciplines; `/readyz` vs `/healthy` duplication → **2 findings** (OBS-8882af8c7e, OBS-56f1c3ecb8) + adjacent OBS-3b3fb9e380; *"Magic strings for route names in RedirectsController"* → **3 findings with identical titles** (CQL-e1b300ff83, CQL-2f79cef16c, CQL-e248c25e17). No intra-analyzer or cross-discipline dedup exists yet. |
| **Coverage balance / consensus** | **2** | OpenCode produced 51/82 observations (62%); ClaudeCode 13/82 (16%, 2/7 disciplines). 23/29 findings (79%) are single-source; only 6 (21%) reached multi-provider agreement. The signal is strongly OpenCode-weighted. |
| **Severity calibration** | **3.5** | Verified severities are broadly right (committed secret = critical; runtime NRE pipeline break = high). One overflag candidate: REL-b47d105cfd (high, Codex) — the broad `catch(Exception) → always-200 fallback` is real but **explicitly documented as intentional** in the code comment; the finding is fair, the "high" weight is debatable. |
| **Traceability / reproducibility** | **2.5** | Full provenance per observation/finding (provider, file, line refs, fixed artifact ids) is excellent **within** the run. But the run's inputs are **not replayable**: the analyzer reads the live working tree (`FileSystemRepositoryScanner`) and records branch/commit without a tree SHA; during this session the target worktree drifted again (see §3), so some line references cannot be re-verified against today's checkout. |
| **Calibration diagnostics** | N/A | M12.3/M12.4 `calibrationDiagnostics` do not apply to **agentic** (process) providers by design — no diagnostics block in the package. Per-provider precision/recall is not computable from this run; not a regression, a documented (M12) limitation. |

## 3. Independent reproduction (spot checks)

Verification was performed against the **committed** snapshot content
(`git show HEAD:…`, commit `4a7d279aaa99`), which is the deterministic,
replayable baseline for the run's branch/commit.

| Finding | Claimed | Verified? |
|---------|---------|-----------|
| SEC-0513c99e8e (critical) | committed `SONAR_TOKEN` | ✅ `docker-compose.yml` contains `SONAR_TOKEN=9cf5ecf0e09015e5aca77720a67c30ef9dde7d00` |
| SEC-33892bc529 (medium) | `appsettings.secrets.json` tracked | ✅ `git ls-files` shows `host/…/appsettings.secrets.json` (content `{}`) |
| SEC-8a7f764e51 (medium) | telemetry key/IV in plaintext config | ✅ `appsettings.json` → `KeyString`/`IVString` |
| REL-91464b8d55 (high, ×3) | HttpClient no timeout/resilience | ✅ `CoreModule` `AddHttpClient` sets BaseAddress only; no Polly in `Directory.Packages.props` |
| REL-ef8123defd (high, ×2) | RequestFilter NRE after catch | ✅ catch-swallow + `telemetry!`-style dereference present |
| DOC-c1cac451ab (high, ×3) | host Dockerfile builds nonexistent project | ✅ `host/…/Dockerfile` does `COPY + restore RedirectToService.HttpApi/RedirectToService.HttpApi.csproj`; `git ls-files` proves only `RedirectToService.HttpApi.Host.csproj` exists (README.md also documents the bogus path) |
| CQL-b032c47f86 (medium, ×2) | hardcoded env/brand literals | ✅ `ApiUrlProvider.cs` → `BuildAuthorityServerUri` `"Staging"/"Production"`/`highrollercasino` |
| OBS-3b3fb9e380 / OBS-8882af8c7e | `/healthy` always `Ok()`; `/readyz`+`/healthy` duplicate | ✅ controller `[HttpPost("healthy")] return Ok()`; `/readyz` referenced in tests/appsettings |
| REL-482b5c88cb (medium) | ContextLogger drops exception when no HTTP context | ✅ returns after logging "context is null", dropping original details |
| CQL-7fe95e8d97 (medium, Codex) | `IRequestRealmProvider` never used | ⚠️ **Not reproducible:** committed code **does** use `_realmProvider.Realm` (line 52). The run-time worktree differed (see below) so the code the agent saw is unknowable; left **disputed**, not confirmed. |

**Snapshot-drift note (reproducibility evidence for M15.3):** the findings cite
`BalanceContextProvider.cs` lines 75–91 (realm wallet-health loop) and 120–131
(`GetCustomerBalanceAsync`/`SendAsync`) — a **coherent, fuller implementation**. The
committed `HEAD` file is 128 lines; the current working tree is a different
**216-line stub** (`NotImplementedException` ×5). All three states differ, so while
the finding *content* is internally consistent and non-hallucinated, a given line
reference cannot be re-validated after the fact. Fix: pin the analyzed tree.

## 4. Verdict

**Decision: B (conditional / narrowly acceptable).** *"Output quality is acceptable"*
holds **for the content** — the security/reliability/observability evidence is
verbatim-accurate, coherent, and actionable, and the highest-severity findings
verified cleanly. It does **not** hold for two structural claims the Council makes
about itself:

1. **"No Architecture / No Testing issues" is not assurance.** With 0/3 acquisition
   coverage, the summary must report *"no evidence acquired"*, never *"no issues
   identified"*. This is an interpretation-layer defect, not an analyzer defect.
2. **The finding count is inflated ~31%.** 9 findings are duplicates of 3 root
   causes; dedup (identical titles, same root cause across disciplines) must exist
   before counts are trusted.

Neither invalidates the actionable findings; both must be fixed before the Council
signs off a green health for any discipline it could not examine.

## 5. Exit criteria

| Criterion | Result |
|-----------|--------|
| Full 3×7 parallel run completed against a real repo | ✅ `20260813-230240-b3e36e` |
| Package produced: one artifact, `schemaVersion` 1.1, valid JSON | ✅ |
| All 21 acquisition steps accounted for (11 success / 10 timeout) | ✅ |
| Output quality verdict produced with per-dimension evidence | ✅ "B (conditional)" |
| Top-severity findings independently verified (verbatim) | ✅ 10/10 table rows confirmed/qualified |
| Absence-of-evidence reported vs. absence-of-issues — assessed | ✅ flagged as the key defect (coverage 0/3 → must not be "all clear") |
| Duplicate-clusters quantified | ✅ 9/29 (~31%) from 3 root causes |
| No provider secrets in artifacts / no orphan processes | ✅ |
| No analyzer/contract/ADR changes introduced by this milestone | ✅ |

## What remains (M15.3)

1. **Acquisition robustness** — architecture/testing steps took 0% coverage across
   all three providers; raise agentic step budgets, add retry/repair, or split into
   smaller per-step discipline sets so "no findings" can ever be inferred from
   "nothing was scanned."
2. **Reproducibility** — record/pin the analyzed snapshot (tree SHA or a
   `git-archive` copy) so findings are replayable after the fact.
3. **Count integrity** — dedup identical-title and same-root-cause findings across
   disciplines inside the reconciler; surface per-discipline **evidence coverage**
   in the package and gate any "no issues" statement on coverage > 0.
4. Optional: severity calibration follow-up on REL-b47d105cfd-style
   intentional-design findings (observations are already typed — needs only an
   interpretation rule to down-weight documented intent).