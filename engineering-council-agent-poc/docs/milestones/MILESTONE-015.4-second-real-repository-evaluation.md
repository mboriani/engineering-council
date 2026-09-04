# MILESTONE-015.4 — Second Real-Repository Evaluation of the M15.3 Improvements

Date: 2026-08-19 · Evaluation only — **no production code changed**, no prompts tuned,
no reconciliation rules changed, no next milestone started.

## Context

M15.2 (`20260813-230240-b3e36e`, first full 3-provider Council run) exposed four structural
defects: 10/21 acquisition timeouts with **Architecture and Testing at 0/3 (NoEvidence)** yet
reported as "no issues"; per-provider timeouts ignored (M15.2B fix); unreproducible findings
from snapshot drift (M15.3C fix); ~9 duplicate families across providers (M15.3D fix) and zero
token visibility. M15.3A–D then delivered, in order: evidence coverage gates (D-105), timeout
precedence + bounded retry (D-106/D-102), repository snapshot reproducibility (D-107), and the
deterministic `dedup-identity` reconciliation stage (D-108/ADR-026).

M15.4 re-runs the SAME full Council against the SAME repository at the SAME commit
(`RedirectToService` @ `4a7d279…`, master) and evaluates whether those fixes actually hold on a
second, independent full run. **This is an evaluation milestone: nothing was fixed, tuned, or
shipped.**

## Run configuration (runtime-only; shipped defaults untouched)

| Setting | Value |
|---------|-------|
| RunId / output dir | `20260819-195410-1086f6` / `outputs/20260819-195410-1086f6` |
| Target | `C:\Repositories\Paramo\RedirectTo\redirect-to-service` |
| Snapshot | master @ `4a7d279aaa99898ea76128af12575b70431c401a`, clean, `SNAP-23d782a35393`, `repositoryChangedDuringRun=false` |
| Providers / disciplines | OpenCode + Codex + ClaudeCode / all 7 |
| Concurrency / attempts | `Evidence__Execution__MaxConcurrency=3`, `MaxAttempts=2` |
| Timeouts (env only) | OpenCode 300s, Codex 180s, ClaudeCode 300s |
| Wall-clock | 1310.6 s (~21.8 min) |

Scan: 122 files / 103 with content / 6 projects.

## 1. Execution metrics (M15.3A + M15.3B)

| Metric | M15.2 | M15.4 |
|--------|-------|-------|
| Logical steps | 21 | 21 |
| Physical attempts | 31 (10 timeouts) | 23 (2 timeouts, both recovered) |
| Success / failure | 11 / 10 timeouts | **21 / 0** |
| Recovered-by-retry | 0 (no retry existed) | **2** (OpenCode#CodeQuality, OpenCode#Testing) |
| Exhausted retries / repairs | — / 0 | **0 / 0** |

`timeoutCount` (final-outcome) = 0; every requested step produced evidence. The two timed-out
OpenCode first attempts each hit the full 300 s budget and completed on retry attempt 2 — the
bounded `MaxAttempts=2` rule worked as designed with no data loss. OpenCode was the slowest
(25:56 recorded / ~35.9 min wall incl. timed-out attempts); Codex 13:35, ClaudeCode 16:06.
`provider-execution.json` `totalDuration 00:55:36.87` is the M12.1 SUM of provider durations,
not wall-clock (documented aggregator).

## 2. Coverage (M15.3A gates)

All 7 disciplines now report **`coveredWithFindings` 3/3**:

| Discipline | M15.2 | M15.4 | Obs |
|-----------|-------|-------|-----|
| Architecture | **0/3 NoEvidence** | 3/3 | 22 |
| Testing | **0/3 NoEvidence** | 3/3 | 15 |
| CodeQuality | 3/3 | 3/3 | 24 |
| Reliability | 3/3 | 3/3 | 20 |
| Security | 3/3 | 3/3 | 25 |
| Documentation | 3/3 | 3/3 | 22 |
| Observability | 3/3 | 3/3 | 26 |

The M15.2 "No Architecture / No Testing issues identified" mis-assurance is gone: no
"no issues" statement is emitted without evidence, and partial coverage is never presented as a
strength. 154 observations total (OpenCode 70 / Codex 37 / ClaudeCode 47).

## 3. Finding quality (verification against the analyzed source)

Reconciliation: 55 raw (OpenCode 27 / Codex 23 / ClaudeCode 25) → **49 consolidated**;
multi-provider 14, single-provider 35, contradictions 0. Severity C1/H3/M25/L16/I4;
agreement aggr3=8 / aggr2=6 / aggr1=35; CouncilAssessment strongAgreement 11,
agreementWithDifferences 3, singleSource 35, potentialConflict 0 (differences: severity 3).

**High-severity source validation (1 critical + 3 high) — all CONFIRMED against the
snapshot-identical worktree:**

- `SEC-45b47238ee` (critical, aggr3): plaintext symmetric `Telemetry:EncryptConfig`
  `KeyString "5080808080802072"` / `IVString "6080808080807072"` in
  `appsettings.json:107-112` + literal SonarQube token in `docker-compose.yml:19-24`. CONFIRMED.
- `REL-42ba184796` (high, aggr3, merged): `RedirectToServiceCoreModule.cs:27-33` registers
  wallet `AddHttpClient` per realm with no timeout, retry, or circuit-breaker policy. CONFIRMED.
- `OBS-aff05279c8` (high, aggr1 OpenCode): the same `appsettings.json:107-111` encryption
  material viewed as observability/PII — CONFIRMED (the `appsettings.secrets.json` reference is
  weak — that file is `{}` — but the primary claim holds; see cross-discipline note below).
- `REL-bddda24168` (high, aggr1 ClaudeCode): `RequestFilter.cs:37-53` — telemetry built in
  `try`, `catch` logs and continues, then `telemetry!.StartDate` (line 53) null-forgiving
  dereferences a null `telemetry` on the exception path → NRE on every request. CONFIRMED
  (same defect family as M15.2's RAW-012/RAW-016).

**Lower-severity sample (6, mixed providers/disciplines/agreements):**

- `ARC-b75419f661` (medium, aggr3): Core providers coupled to ASP.NET Core via
  `IHttpContextAccessor` (`RequestRealmProvider.cs:8`, `RequestGSettingProvider.cs:7`,
  `BalanceContextProvider.cs:12`). CONFIRMED.
- `SEC-303087b82c` (medium, aggr1 OpenCode): Authorization bearer + `gsetting` header forwarded
  verbatim to the downstream wallet client (`BalanceContextProvider.cs:50-51,114,123`). CONFIRMED.
- `DOC-42a59cb0bd` (medium, aggr3): documentation bundle — stale README (wrong project paths,
  outdated diagrams, `scratch.md` left in repo, `migrate-database.ps1` references a non-existent
  project). CONFIRMED overall. Softest sub-claim: "README .NET 6.0 vs net10.0" is individually
  weak — the README says "6.0 or later", and net10.0 is later.
- `REL-1c91475ad5` (medium, aggr1 ClaudeCode): `ContextLogger` silently drops log lines when
  `HttpContext` is unavailable (`ContextLogger.cs:33`). PLAUSIBLE.
- `CQL-5248df270d` (medium, aggr1 ClaudeCode): `RequestEnricher`/`ResponseEnricher` are
  mirrored near-duplicates of the same item-building logic. PLAUSIBLE.

No finding sampled was Disputed or FalsePositive; the one genuinely unreproducible M15.2 finding
(CQL-7fe95e8d97, worktree drift) is structurally impossible here because the snapshot is pinned
and confirmed unchanged during the run.

## 4. Dedup validation (M15.3D — the core evaluation target)

4 merged clusters removed **6** findings (pre-dedup 55 → post-dedup 49):

| Merged finding | Members | Defect | Verdict |
|----------------|---------|--------|---------|
| `REL-42ba184796` (high, `Medium–High`) | RAW-017+018+020+025 | wallet HttpClient no timeout/retry/circuit-breaker | **GENUINE** (14 obs, all 3 providers, evidence preserved) |
| `TST-d4244b718f` (medium) | RAW-035+036 | brand-auth pipeline untested | **GENUINE** (9 obs, OpenCode+Codex, strongAgreement) |
| `OBS-70131abcc2` (medium, `Low–Medium`) | RAW-045+049 | readiness probe probes realms sequentially, no budget | **GENUINE** (4 obs, OpenCode+Codex) |
| `CQL-c55887984d` (medium, `Low–Medium`) | RAW-010+RAW-014 | "magic string" | **FALSE MERGE** |

**The false merge (`CQL-c55887984d`).** RAW-010 (OpenCode, medium) is a multi-issue bundle
titled *"Duplicated magic string for named HttpClient construction"* (obs OBS-011..020 + OBS-077,
including secondary OBS-014 "Magic string item keys duplicated across telemetry classes"). RAW-014
(ClaudeCode, low) is a single-obs finding *"Inconsistent, uncentralized magic string keys for
context items (CustomerID/BrandID casing)"* (OBS-117, symbols `ContextLogger.WriteLog` +
`RequestFilter.OnResultExecutionAsync`). The stage merged them because (a) the two titles share
`{magic, string}` — exactly `MinFocusTitleTokens = 2` — and (b) the bundle's **secondary** OBS-014
shares the same suffix-compatible method-parts at close lines with OBS-117. The result masks
RAW-014's distinct defect (context-item key casing inconsistency) under a merged title about
HttpClient construction. The M15.3D focus guard — whose whole purpose is to stop a bundle's
secondary from absorbing a distinct single-issue finding — failed here because the two shared
tokens are a **generic adjacent topic phrase** ("magic string"), not distinctive content terms.
The ContextLogger/broad-catch traps of M15.2 were avoided only because those titles shared ZERO
tokens; the guard is not discriminating enough when the overlap is a generic category phrase.

**Near-matches that stayed separate (6 pairs examined):**

| Pair | Why separate | Classification |
|------|--------------|----------------|
| RAW-046 vs RAW-051 (no OTel/tracing) | focus passes (2 tokens) but no shared method-part | conservative false negative |
| RAW-040 vs RAW-046 (metrics/tracing) | 0 shared tokens; Program.cs lines 22 vs 5 (>3) | conservative false negative |
| RAW-050 vs RAW-055 (health-check logging suppressed, **same appsettings.json:113**) | config-only findings: symbols `{Logging}` vs `{CheckHealthAsync}` — no shared method-part; 0 shared title tokens | conservative false negative (symbol-grounded identity misses config-only duplicates) |
| RAW-026 (security, secret) vs RAW-043 (observability, key/IV) | same fact, cross-discipline | separate by design (double report across disciplines) |
| RAW-023 (reliability NRE) vs RAW-034 (testing NRE) | same RequestFilter defect, cross-discipline | separate by design |
| RAW-019 (wallet test gap) vs REL-42ba184796 (wallet policy) | related, distinct focus | correctly separate |

Verdict: 3/4 merges genuine with evidence/provenance/severity-range preserved; **1 false merge**
— a real false-positive defect in the M15.3D focus guard, exactly the failure class the milestone
was designed to prevent. The conservative near-miss behavior (false negatives accepted) is as
intended.

## 5. Repository provenance (M15.3C)

`SNAP-23d782a35393` matches the M15.3C reference run exactly (same commit, clean tree), and
`repositoryChangedDuringRun=false` — every finding this run is replayable against the commit it
cites. The unreproducible-finding failure mode of M15.2 is gone.

## 6. Token telemetry (M12.1 / M15.2A / M15.2C / M15.2D)

Authoritative for ClaudeCode only (as designed; OpenCode/Codex captured output carries no
machine-readable usage — never estimated):

| Metric | Value |
|--------|-------|
| Input / Output / Total | 236 / 91856 / 92092 |
| CacheRead / CacheCreation | 5687260 / 355333 |
| FreshContextTokens / ContextTokenActivity | 355569 / 6042829 |
| CacheReuseRatio | **0.9412** (context is cache-dominated) |
| Tokens per observation | ~1959 |
| Per-discipline split | not separable — records carry no per-step token counts (limitation, not estimated) |

## 7. Token efficiency by discipline

Per-discipline acquisition (all 3 providers per discipline, 154 obs total):

| Discipline | Obs | Recorded duration |
|-----------|-----|-------------------|
| Architecture | 22 | 8.1 m |
| CodeQuality | 24 | 8.4 m (incl. OpenCode timeout+retry, 10.0 m wall) |
| Reliability | 20 | 6.1 m |
| Security | 25 | 10.2 m (OpenCode 299.9 s — barely under budget) |
| Testing | 15 | 10.2 m (incl. OpenCode timeout+retry, 10.0 m wall) |
| Documentation | 22 | 6.0 m |
| Observability | 26 | 6.5 m |

Observations per minute: ClaudeCode 47/16.1 = 2.9; Codex 37/13.6 = 2.7; OpenCode 70/25.9 =
2.7 (recorded) or 1.9 (wall incl. timeouts). OpenCode is the least efficient on both duration and
the only provider that needed recovery — consistent with M15.3B's timeout profile.

## 8. Retry cost

2 timed-out OpenCode first attempts (CodeQuality, Testing) × full 300 s budget = ~10 min lost
OpenCode wall time, both recovered on attempt 2 at `MaxAttempts=2`. No ClaudeCode/Codex retries;
no exhausted retries; no repair calls. Token cost of the two timed-out attempts is unknown
(OpenCode has no token telemetry) — the honest gap, reported, not estimated.

## 9. Artifact verification

- Package `schemaVersion` **1.1**; reconciliation counts internally consistent (154 obs =
  records sum = report total; RAW-001..055 → 49 findings; preDedup 55 → postDedup 49 =
  deduplicated 6; multi 14 + single 35 = 49).
- `run.json` `status=completed`, providers + 7 disciplines recorded, `repositoryIdentity`
  consistent with the package snapshot.
- No secrets in any artifact (key/IV values and token absent — verified by scan).
- No orphan processes: the `claude`/`codex`/`opencode` processes observed pre-date the run
  (12:52 PM vs 19:54 run start) — environment sessions, not run leftovers; the Council CLI exited.

## 10. M15.2 vs M15.4 comparison

| Metric | M15.2 | M15.4 | Δ |
|--------|-------|-------|---|
| Success / timeouts | 11 / 10 | **21 / 0** | M15.3B |
| Observations | 82 | 154 | +88% |
| Findings (consolidated) | 29 | 49 | +69% |
| Multi-provider | 6 | 14 | M15.3D preserves agreement |
| Contradictions | 0 | 0 | — |
| Severity | C1 H6 M13 L9 | C1 H3 M25 L16 I4 | more medium/low depth |
| Architecture / Testing coverage | 0/3 NoEvidence | **3/3** | M15.3A |
| Health | Critical | Critical | unchanged verdict, deeper evidence |
| Duplicate families | ~9 (manual; later reduced to 2 genuine) | 4 dedup merges (**1 false**) | M15.3D partial |
| Unreproducible findings | 1 | **0** | M15.3C |
| Token visibility | none | cache-dominated ClaudeCode telemetry | M15.2A/C/D |

Unknowns stated honestly: per-discipline token split not separable; OpenCode/Codex tokens null;
two timed-out attempts' token cost unknown.

## 11. Verdict: B

The M15.3 improvements **delivered**: full 21/21 coverage with automatic timeout recovery
(M15.3B), the NoEvidence-as-assurance defect fixed (M15.3A), every finding replayable against a
pinned snapshot (M15.3C), and deterministic dedup that correctly merged 3 genuine cross-provider
duplicates while keeping all 6 near-match pairs separate (M15.3D — conservative behavior as
intended). Not an A because the M15.3D focus guard produced **one genuine false merge**
(CQL-c55887984d): a generic 2-token topic phrase allowed a multi-issue bundle to absorb a
distinct single-issue finding — the exact failure class M15.3D was built to prevent. Not a C: the
run is a full success, the defect is narrow, documented, and does not undermine coverage,
provenance, or the confirmed high-severity findings.

## 12. Single highest-value next problem (identified, NOT fixed)

**The `dedup-identity` focus guard's token threshold is not discriminating enough.** Any ≥2 shared
significant title tokens — including a generic category phrase like "magic string" — satisfies
`MinFocusTitleTokens = 2`, so a multi-issue bundle can still absorb a distinct finding that shares
only that phrase and one secondary symbol (CQL-c55887984d). The next milestone should make the
focus test more discriminating: require a shared DISTINCTIVE content token beyond a generic
phrase, require the shared method-part to be named in BOTH titles, and/or refuse the merge when
the absorbing member is a multi-issue bundle (evidence-count asymmetry). Per instruction this is
**documented only** — no rule change was made.

## 13. Deliverables & verification

- Evaluation performed with **zero production changes**; no prompts tuned; no reconciliation
  rules changed.
- Test suite NOT re-run (evaluation milestone per instruction); last verified state is M15.3D:
  build 0/0 warnings/errors, **600/600 tests**.
- Docs: this milestone; PROJECT_MEMORY updated (v36); DECISION_LOG `D-109`. No ADR (no
  architecture decision), no contract change (`schemaVersion` 1.1 verified correct), RUNBOOK
  unchanged (the run used existing runtime-only env vars — no new process knowledge).