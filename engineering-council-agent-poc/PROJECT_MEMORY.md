# PROJECT MEMORY — Engineering Council Agent (POC)

> Durable context for this POC. Keep it current; it is the first thing a new
> contributor (human or agent) should read.

## Purpose

Build an isolated agent that reads a local .NET solution, analyzes it, and
produces actionable engineering improvement opportunities as Markdown + JSON,
consumable by a future **Engineering Dashboard**. v1 = a single analyzer agent
on **Microsoft Agent Framework**.

## Hard constraints (do not violate)

- **Isolation:** all work lives under `engineering-council-agent-poc/`.
- Do **not** modify any existing project or the current dashboard.
- Do **not** reuse the FM project's code, domain, names, or folders.
- Reuse only FM's **workflow pattern** (memory, decision log, ADRs, milestones,
  prompts, documented outputs). See `docs/adr/ADR-001`.
- The agent **only analyzes** — it never edits, runs, or mutates the target.
- Output must be consumable by the future dashboard (stable `Finding` contract).

## Current state (v38 — 2026-09-10, Milestone 016 — CLOSED / ACCEPTED)

- **M16 Graph-Assisted Context** (MILESTONE-016, D-111): graph-assisted context is accepted for
  production/practical Council usage. Graphify 0.9.53 provides deterministic local graph extraction
  without LLM dependency. Currently opt-in; Security + Architecture supported; minimal navigation
  context <= 3000 chars; persistent graph cache with cross-run reuse validated; graceful fallback
  when Graphify unavailable/fails/timeout; package schema remains 1.1; known untracked-source
  limitation documented; no further M16 experiments planned. See `MILESTONE-016-graph-assisted-context.md`.

## Previous state (v37 — 2026-08-19, Milestone 015.4A)

- **Dedup False-Merge Hardening** (MILESTONE-015.4A, D-110, ADR-026 amended): fixed the single
  concrete M15.4 defect — the false dedup merge `CQL-c55887984d` where bundle RAW-010 ("Duplicated
  magic string for named HttpClient construction") absorbed RAW-014 ("...magic string keys for context
  items (CustomerID/BrandID casing)"). Root cause: the `MinFocusTitleTokens=2` focus guard alone let a
  generic 2-token phrase {magic, string} (a shared CATEGORY, not a shared defect) plus a method-part
  inherited from the bundle's SECONDARY observation (OBS-014, `writelog`/`onresultexecutionasync`)
  become sufficient identity evidence. **Fix (deterministic, conservative): primary-focus anchor** in
  `RuleBasedFindingReconciler` — a shared dedup method-part must be a PRIMARY-focus method-part of BOTH
  findings, i.e. attached to an observation whose normalized title similarity to the finding's own
  title ≥ `PrimaryFocusAnchorSimilarity = 0.30` (exact Jaccard, `TitleNormalizer.Similarity`); the
  close-line check is scoped to primary-focus evidence; `Signature` gains `PrimaryMethods`/
  `PrimarySymbolEvidence`; observation without a title is never primary-focus; bundles still merge on
  their PRIMARY issue (only their secondary obs lose identity power). Threshold grounded in persisted
  M15.2/M15.4 runs — bimodal with a clean gap: same-defect-rephrased obs ≥ 0.37 (OBS-081 0.37,
  OBS-057 0.39, OBS-070 0.38, OBS-021 0.55, OBS-032 0.58, OBS-139 0.38), distinct-sub-issue obs
  ≤ 0.25 (RAW-010 vs OBS-014 = 0.25 max); DF/"distinctive-token" approaches TRIED and REJECTED
  ({magic,string} DF 2/55 = 0.04, same as genuinely distinctive tokens — not discriminating); no
  hardcoded blacklist. Score stays 0.9 (predicate fixed, not cosmetics). Cluster-order safety is
  intrinsic (anchor computed per finding from its own obs+title). **Replay of persisted M15.4 (no
  providers): 55 → 50 consolidated** (was 49), dedup joins 6 → 5, merged clusters 4 → 3 (all genuine:
  REL-42ba184796 4-member wallet, TST-d4244b718f brand-auth, OBS-70131abcc2 readiness) — RAW-010
  separate from RAW-014 **YES**, each standalone with own discipline/severity/provenance/obs; all 5
  named near-match pairs stay separate. M15.2 replay unchanged (29 → 27; the wallet pair merges
  through rephrased obs at 0.39/0.38, proving the anchor is NOT an exact-title requirement). Severity/
  confidence/CouncilAssessment/health scoring NOT touched; `schemaVersion` stays 1.1, no new package
  fields, consumer DTO unchanged; diagnostics reflect the real partition (PreDedup 55 / PostDedup 50 /
  Deduplicated 5 — the old 49 NOT forced). Build 0/0; **618/618 tests** (+16 `DedupFalseMergeHardeningTests`:
  persisted false-merge regression + reversed + cluster-order permutations, generic 2-token
  insufficient, secondary-inheritance insufficient, title-less obs no identity, rephrased-primary still
  anchors, all 3 genuine M15.4 merges preserved, evidence/provenance/severity/ID/diagnostics/
  schemaVersion, persisted M15.4 replay 55→50 + near-match separation; existing dedup/replay/package
  fixtures made faithful — primary obs now carry the finding's title like the real pipeline). RUNBOOK
  unchanged.

## Previous state (v36 — 2026-08-19, Milestone 015.4)

- **Second Real-Repository Evaluation of the M15.3 improvements** (MILESTONE-015.4, D-109):
  EVALUATION ONLY — no production code changed, no prompts tuned, no reconciliation rules
  changed, no next milestone started. Re-ran the SAME full 3-provider Council
  (OpenCode+Codex+ClaudeCode, all 7 disciplines, MaxConcurrency 3, MaxAttempts 2, runtime-only
  timeouts 300/180/300) on the SAME target at the SAME commit (`RedirectToService` @ master
  `4a7d279…`, clean; `SNAP-23d782a35393` = M15.3C reference; `repositoryChangedDuringRun=false`).
  Run `20260819-195410-1086f6`, 1310.6 s wall, 21/21 logical steps success (2 OpenCode timeouts
  recovered by retry at `MaxAttempts=2`, 0 exhausted, 0 repairs), 154 obs (OpenCode 70/Codex
  37/ClaudeCode 47), 55 raw → 49 consolidated, C1/H3/M25/L16/I4, aggr3=8/aggr2=6/aggr1=35,
  CouncilAssessment 11 strong / 3 with-differences (severity) / 35 single / 0 conflicts, multi 14,
  contradictions 0. **Coverage gates proven:** ALL 7 disciplines `coveredWithFindings` 3/3 —
  Architecture and Testing went 0/3 NoEvidence (M15.2) → 3/3, ending the "no issues without
  evidence" mis-assurance. High-severity validation (1 critical SEC-45b47238ee plaintext
  `Telemetry:EncryptConfig` key/IV in appsettings.json:107-112 + Sonar token in docker-compose;
  3 high REL-42ba184796 wallet HttpClient no policy, OBS-aff05279c8 PII key view, REL-bddda24168
  `RequestFilter.cs:53` null-forgiving NRE) ALL CONFIRMED against the pinned worktree; 5 lower-
  severity sample confirmed/plausible; 0 unreproducible (M15.3C). **Dedup verdict (M15.3D):
  3/4 merges genuine, 1 FALSE MERGE `CQL-c55887984d`** = RAW-010 (bundle "Duplicated magic string
  for named HttpClient construction", its secondary OBS-014 shares WriteLog/OnResultExecutionAsync)
  absorbed RAW-014 ("...magic string keys for context items (CustomerID/BrandID casing)") because
  the titles share the generic 2-token phrase {magic, string} — the `MinFocusTitleTokens=2` guard
  is not discriminating enough (the M15.2 ContextLogger/broad-catch traps were only avoided because
  those titles shared ZERO tokens). Near-matches examined (6 pairs) all correctly separate —
  RAW-046/051 focus-passes but no shared method-part, RAW-050/055 (same appsettings.json:113,
  config-only, no symbol) both conservative false negatives, plus cross-discipline-by-design pairs
  (RAW-026 security vs RAW-043 observability SAME key fact double-reported across disciplines;
  RAW-023/034 RequestFilter NRE; RAW-002/044 healthy endpoint). Token telemetry ClaudeCode-
  authoritative only: 236 in/91856 out/92092 total, cacheRead 5687260 / cacheCreation 355333,
  fresh 355569 / activity 6042829, cacheReuseRatio 0.9412, ~1959 tok/obs; per-discipline split NOT
  separable (records carry no per-step tokens — limitation, never estimated); OpenCode/Codex null.
  Retry cost: ~10 min OpenCode wall lost to the 2 × 300 s timeouts, both recovered; timed-out
  attempt tokens unknown (honest gap). Artifacts verified: `schemaVersion` 1.1, reconciliation
  counts internally consistent (55→49, dedup 6, multi 14 + single 35 = 49, 154 obs), no secrets,
  no orphan Council processes. **VERDICT: B** — M15.3A-D delivered (full coverage + recovery,
  coverage gates, pinned provenance, conservative dedup with correct near-miss separation) but the
  one real false merge keeps dedup from being trustworthy enough for an A. **Single highest-value
  next problem (documented, NOT fixed):** make the `dedup-identity` focus test more discriminating
  — require a shared DISTINCTIVE content token beyond a generic phrase, require the shared
  method-part named in BOTH titles, and/or refuse merge when the absorbing member is a multi-issue
  bundle. Docs: MILESTONE-015.4, PROJECT_MEMORY v36, DECISION_LOG D-109; no ADR, no contract
  change, RUNBOOK unchanged. Test suite NOT re-run (evaluation); last verified 600/600.

- **Deterministic Finding Deduplication** (MILESTONE-015.3D, D-108, ADR-026): new
  deterministic `dedup-identity` stage (score 0.9) in `RuleBasedFindingReconciler`, between
  `exact-rule-location` (0.95) and `type-symbol` (0.85). A same-discipline pair is the same
  finding only when ALL hold: shared suffix-compatible method-part symbol (`MethodPart` =
  last '.'-segment, so `OnActionExecutionAsync` ≡ `RequestFilter.OnActionExecutionAsync`
  even when exact symbol sets don't intersect), close lines in the SAME file for that symbol
  (per-symbol evidence, `LineTolerance=3`), focus consistency (titles share ≥2 significant
  stopword-filtered tokens — PRIMARY-ONLY guard; a verbose summary naming shared symbols is
  NEVER a merge signal, so broad-catch RAW-015 is not absorbed by the NRE pair even though
  its summary names `GetContextAsync`/`RedirectToAsync`), and no material severity
  contradiction (`|sevA−sevB| ≥ 2` refuses). Merges EXACTLY the 2 genuine persisted M15.2
  duplicates — 29 → 27: RAW-012+RAW-016 (RequestFilter NRE, High) and RAW-011+RAW-014
  (wallet HttpClient; High↔Medium recorded as `Medium–High` range, not erased). Near-misses
  stay separate: health pair RAW-028/RAW-029 (no shared symbol), magic strings RAW-001/003/005,
  ContextLogger RAW-017 (bundle carries a secondary WriteLog but its title focus is the
  RequestFilter NRE), broad-catch RAW-015, cross-discipline views RAW-026/RAW-004.
  `Signature` gains `MethodSymbols`/`SymbolEvidence`/`TitleTokens`; `Cluster.DedupJoins`;
  `ReconciliationSummary` gains DEFAULTED (non-required — pre-M15.3D persisted docs still
  deserialize) `PreDedupFindingCount`/`PostDedupFindingCount`/`DeduplicatedFindingCount`
  (PreDedup = PostDedup + Deduplicated; each join removes exactly 1); the external package
  gains the same additive fields on `reconciliation`, `schemaVersion` stays **1.1**; consumer
  DTO gate updated (`EngineeringReviewPackageContract`). Markdown shows a compact
  `Deduplicated findings: N (pre-dedup X → post-dedup Y)` line. The secondary focus guard
  (BothName) was TRIED and REMOVED — persisted-data analysis showed it would false-merge
  RAW-015 via RAW-016's summary text (it only survived by cluster-ordering luck). Build 0/0;
  **600/600 tests** (+34: `DedupReconciliationTests` 24 offline incl. negative fixtures A–H +
  bundle/focus guards + M15.2 regression pairs; `PersistedRunDedupReplayTests` 7 replaying
  the real persisted run incl. reversed-input determinism; `PackageContractTests` 3). M15.3E
  not started.

- **Repository Snapshot Reproducibility** (MILESTONE-015.3C, D-107): every run now carries
  a deterministic description of the analyzed source state — fixing the M15.2 defect where
  finding CQL-7fe95e8d97 could not be reproduced because the target changed after the run.
  New provider-neutral `RepositorySnapshotIdentity` (`Core.Domain`: `VersionControl`
  "git"/null, `CommitSha` full HEAD, `Branch`, `IsDirty`, `HasUntrackedFiles`,
  `SnapshotFingerprint`, `RepositoryChangedDuringRun`) — additive on `AnalysisRun`
  (`repositoryIdentity` in run.json) and the package (`repositorySnapshot`, additive,
  **schemaVersion stays 1.1**, no absolute local paths). `SnapshotFingerprint`
  (`Core.Analysis`) = SHA-256 aggregate over the SAME post-ignore-rule scanned selection
  (relative path + raw content hash per file, Ordinal order; bin/obj/.git/node_modules/
  packages/artifacts/.vs excluded by the existing `ScanOptions.IgnoredDirectories` — no
  second definition of "repository files"). Dirty tracked changes AND relevant untracked
  files change it → a commit SHA alone is never sufficient identity for a dirty tree.
  `ContextFingerprint` NOT repurposed (it stays provider-context identity). Read-only git
  probe `GitSnapshotMetadataProvider` (`ProcessStartInfo.ArgumentList` argv, no shell;
  only `rev-parse`/`symbolic-ref`/`status --porcelain`; never checkout/reset/stash/clean/
  add/commit); git unavailable or non-Git ⇒ metadata null, run continues, fingerprint still
  works. START capture in `AnalysisPipeline` before acquisition; END verification ONCE after
  acquisition (`ScanOptions.LoadContent=false` reuses the same selection); a detected
  mutation sets `RepositoryChangedDuringRun=true` — never fails the run, never replaces the
  start identity. No file watcher, no transactional snapshot claim (documented limitation).
  `engineering-review.md` shows a compact Repository State block (commit prefix); run.json
  keeps full values. Consumer DTO gate updated (`RepositorySnapshotContract`). Build 0/0;
  **566/566 tests** (+25: `SnapshotFingerprintTests` 8 offline + `RepositorySnapshotIdentityTests`
  16 offline/git incl. dirty/untracked/detached-HEAD/non-Git/git-unavailable/start≠end/
  selection-equality/markdown; + package serialization & privacy). Live validation
  (offline Mock, RedirectToService, target read-only): `git / 4a7d279aaa99898ea76128af12575b70431c401a /
  master / isDirty=false / hasUntrackedFiles=false / SNAP-23d782a35393 / changed=false` —
  **unchanged since M15.3B** (`master @ 4a7d279aaa99`). Dirty/untracked fixture (same HEAD
  7658a530…ca74ea): clean `SNAP-2fa72dca3011` → modified `SNAP-da40a9a59af4` → +untracked
  `SNAP-4b4acd4b18ca` (fingerprint changes proven). Known gaps: start/end equality is not a
  transactional snapshot (providers could theoretically read different bytes mid-mutation);
  fingerprint reads all selected file bytes (2× I/O on huge repos); git unavailable ⇒
  commit/branch/dirty/untracked null; remote URLs never read (may hold credentials — out of
  scope); pre-M15.3C runs carry no identity. M15.3D not started.

- **Agentic Acquisition Robustness** (MILESTONE-015.3B, D-106): ONE bounded
  acquisition-level retry so a slow-but-healthy agentic step that hits its effective
  timeout once can recover (M15.2's real defect: Architecture/Testing 0/3 all Timeout
  on `20260813-230240-b3e36e`). `Evidence:Execution:MaxAttempts` (default **2** =
  initial + ONE retry, clamped [1,5]) bound in `EvidenceOptions`, `CouncilOptions`(DI),
  CLI `CliArgs`, API `EvidenceConfig`, and BOTH shipped appsettings.json. Policy
  (executor-internal, no ADR): retry ONLY `Timeout` — executor-detected (step timeout
  CTS) OR provider-reported `LlmProviderException(Timeout)`; cancellation/auth/config/
  schema/process/unknown errors NEVER retried; the retry reuses the SAME effective
  provider timeout (M15.2B, no doubling/adaptive), stays inside its step's
  `MaxConcurrency` permit (no second slot), never a third attempt. Reuses (does NOT
  duplicate) the existing provider-internal transient retry loop in the chat clients
  (`LlmProviderException.IsTransient`, `MaxRetries`). Telemetry: additive
  `AttemptCount` (default 1) + `RetryExhausted` (true ONLY when a retry-eligible
  Timeout failure occurred on the final allowed attempt) on `Evidence` +
  `ProviderExecutionRecord`; `RetryCount` = step-level retries + the FINAL attempt's
  provider-internal retries (failed attempt's internal count not double-counted).
  Token telemetry never fabricated (unknown stays null). M15.3A coverage authoritative:
  retried success = successful evidence; exhausted = NoEvidence. Real validation run
  `20260818-223806-aa8b1e` (RedirectToService, Architecture+Testing, OpenCode 300s/
  Codex 180s/ClaudeCode 300s, MaxConcurrency 3): **6/6 success, 0 failures, 0 retries
  needed** (OpenCode took 184–215s — the old 120s cap would have killed it);
  Architecture and Testing both `coveredWithFindings` **3/3** (vs NoEvidence 0/3 in
  M15.2); 8 findings (C0/H2/M4/L1); `TotalTokens 28836`; cost/time LogicalSteps 6 ·
  PhysicalAttempts 6 · RetriedSteps 0 · SuccessfulRetries 0 · ExhaustedRetries 0;
  package `schemaVersion` **1.1**, records carry `attemptCount`/`retryExhausted`
  (additive), no per-record timeout leak, no orphan processes. Build 0/0;
  **541/541 tests** (+17 `BoundedRetryTests.cs`, offline: retry eligibility,
  exhaustion, same-timeout, no-third-attempt, cancellation/auth/config/schema/unknown
  never retried, MaxConcurrency permit, internal+step retry counting, coverage
  interaction, config binding). Known gaps (documented): step-level retry is
  Timeout-only by design; RateLimit/Network/ServerError stay chat-client-internal;
  per-attempt sub-durations not recorded (`Duration` covers the whole logical step).

- **Evidence Coverage Gates** (MILESTONE-015.3A, D-105): fixes the product-trust defect
  from the first real M15.2 evaluation (run `20260813-230240-b3e36e`, RedirectToService:
  Architecture 0/3 and Testing 0/3, all Timeout, but the package still claimed "No
  Architecture issues identified." / "No Testing issues identified."). NO provider/LLM
  invocation — validation reuses the persisted M15.2 artifacts + a deterministic
  M15.2-shaped fixture. New Core.Domain `DisciplineCoverage.cs`: per-requested-discipline
  state **CoveredWithFindings** (≥1 successful acquisition AND consolidated findings) /
  **CoveredNoFindings** (≥1 successful acquisition, zero findings) / **NoEvidence** (zero
  successful; timeout/failure NEVER counts), plus SuccessfulProviders/AttemptedProviders
  (counts only, no score). Partial success (Security 2/3) is still valid evidence. ONE
  authoritative calculation `DisciplineCoverage.From(report, consolidatedFindings,
  requestedDisciplines)`, computed by `EngineeringReviewPackageBuilder.Build` (order
  independent, enum-ordered entries); exporters never recompute. Root-cause fixes:
  `BuildStrengths` no longer emits "No {d} issues identified." for NoEvidence disciplines;
  `BuildExecutiveSummary` appends a coverage-limitation sentence; Markdown
  `CouncilFindings` renders EVERY requested discipline (NoEvidence → "No successful evidence
  was acquired for this discipline; absence of findings must not be interpreted as
  assurance."; CoveredNoFindings → "No findings were identified from the available
  evidence." + "Evidence coverage: N/M providers"; partial CoveredWithFindings also shows
  the count); `HealthAndRisk` adds a compact Coverage limitation note — scoring algorithm
  intentionally UNCHANGED. ADDITIVE package root field `disciplineCoverage` (optional,
  provider-neutral; **schemaVersion stays 1.1** per the M014.3 precedent; consumer
  `PackageContract.DisciplineCoverage` gate green; sample regenerated; integration docs
  updated). Existing finding generation/reconciliation/health-risk scoring untouched.
  Build 0/0; **524/524 tests** (+20 `DisciplineCoverageTests.cs`, offline, incl. the exact
  M15.2 regression shape Architecture/Testing → NoEvidence 0/3 and a best-effort check that
  recomputes coverage from the persisted `outputs/20260813-230240-b3e36e` artifacts; the one
  strengths assertion in `EngineeringReviewPackageTests` updated to the new no-evidence⇒no-
  "no-issues" semantic). Remaining limitation: static-analysis-only findings (no provider
  execution record) read NoEvidence at 0/0 by design; health scores still exclude missing
  evidence (report states the limitation). M15.3B COMPLETED (MILESTONE-015.3B, v33 —
  bounded Timeout-only retry; real run `20260818-223806-aa8b1e` flipped both disciplines
  to coveredWithFindings 3/3). M15.3C not started.

- **Token Efficiency Analysis** (MILESTONE-015.2D, no ADR — provider-neutral
  DERIVED metrics over the existing M12.1/M15.2C authoritative telemetry; NO
  provider invocation — reuses the M15.2C fixture `20260818-183933-db74b2`).
  New `TokenEfficiencyMetrics` (Core.Domain) computes three conservative
  "activity" metrics via SumKnown (unknown stays null, never 0): **ContextTokenActivity**
  = SumKnown(Input, CacheCreation, CacheRead) → observed input-side context-token
  ACTIVITY (NOT cost/billable/unique-context/window occupancy/TotalTokens);
  **FreshContextTokens** = SumKnown(Input, CacheCreation) → input-side activity
  not reported as cache reads; **CacheReuseRatio** = CacheRead /
  ContextTokenActivity (denominator > 0) → fraction of observed input-side
  activity served as cache reads (NOT a cost-savings %, NOT "% of repo cached").
  Stored per execution on `ProviderExecutionRecord` (set by
  `EvidenceAcquisitionExecutor.Record` from authoritative raw fields — single
  computation path); run level on `ProviderExecutionReport`:
  `TotalContextTokenActivity`/`TotalFreshContextTokens` (SumKnown) + report-level
  `CacheReuseRatio` computed from AGGREGATE authoritative totals
  (`TotalCacheReadInputTokens / TotalContextTokenActivity`), NEVER an average of
  per-execution ratios. `TotalTokens` (= SumKnown(Input, Output)) and
  `KnownTokenExecutionCount` semantics UNCHANGED — cache/derived never folded in.
  `EngineeringReviewPackageBuilder.PackageExecutionView` strips BOTH the M15.2C
  cache fields and the M15.2D derived fields from the external package →
  `schemaVersion` stays **1.1**, no derived/cache leak; derived metrics live in
  `provider-execution.json` (+ `run.json`) only. Build 0/0; **504/504 tests**
  (+13 `TokenEfficiencyMetricsTests.cs`, offline: activity/fresh/ratio
  calculations incl. M15.2C fixture 44/14325/1125952/52193 → activity **1178189**,
  fresh **52237**, ratio ≈ **0.95566**, TotalTokens **14369**; executor flow;
  null semantics never 0; TotalTokens unchanged; OpenCode/Codex → null;
  run-level ratio from aggregates (1700/3000 ≈ 0.5667, NOT avg 0.6); order
  independence; provider-execution.json serialization; package gains no
  derived/cache fields). **M15.2C ClaudeCode/Security interpretation** (~95.6%
  of input-side context-token activity was cache reads — 1,125,952 of 1,178,189):
  it is NOT "95.6% cheaper" and "Claude consumed 1.17M tokens" is only correct
  when qualified as context-token activity; `TotalTokens` 14,369 stays the M12.1
  total. See milestone; M15.3 still deferred (do not start).

- **Claude Code Complete Token Telemetry** (MILESTONE-015.2C, no ADR — additive
  provider-neutral telemetry inside the existing M12.1 surface). The
  `--output-format json` result envelope's authoritative
  `usage.cache_read_input_tokens` / `usage.cache_creation_input_tokens` (previously
  discarded) are now preserved as SEPARATE operational telemetry:
  `ClaudeCodeOutputExtractor.ParseUsage` maps them into `ClaudeCodeUsage` (+2
  nullable fields; `TotalTokens` UNCHANGED = `SumKnown(Input, Output)` — cache
  never inflates it); `ClaudeCodeEvidenceProvider` stamps `Evidence` +
  metadata (`cacheReadInputTokens`/`cacheCreationInputTokens` when present);
  generic `Evidence` and `ProviderExecutionRecord` gain `CacheReadInputTokens?` /
  `CacheCreationInputTokens?`; `EvidenceAcquisitionExecutor.Record` lifts them;
  run-level `ProviderExecutionReport` adds `TotalCacheReadInputTokens?` /
  `TotalCacheCreationInputTokens?` via existing `SumKnown` (aggregation added —
  clean, minimal; `KnownTokenExecutionCount` still means M12.1 input/output-known
  executions, cache-only usage never counts). `EngineeringReviewPackageBuilder`
  `PackageExecutionView` strips cache fields from the EXTERNAL package (nulled →
  omitted by serializer): package stays `schemaVersion` **1.1**, no cache fields
  at run level or in records; cache telemetry lives in `provider-execution.json`
  + `run.json` only. No derived metric (`EffectiveTokens`/cost/billable) — raw
  categories preserved first. OpenCode/Codex remain honestly null (no
  machine-readable usage). Build 0/0; **491/491 tests** (+12
  `ClaudeCodeCacheTokenTelemetryTests.cs`, offline: cache_read/cache_creation map
  to evidence+metadata+record, both propagate, missing/explicit-null stay null
  never 0, TotalTokens excludes cache (179+17605=17784, not 81461), existing
  input/output unchanged, provider-execution.json serialization incl. run totals,
  package gains NO cache fields, SumKnown run totals, cache-only never changes
  KnownTokenExecutionCount). **Live validation** (run `20260818-183933-db74b2`,
  Security, 3 providers, MaxConcurrency 3, `RedirectToService`; runtime-only
  env overrides `Evidence__OpenCode__TimeoutSeconds=300` +
  `Evidence__ClaudeCode__TimeoutSeconds=300`, shipped defaults unchanged):
  **3/3 success** (0 failures/timeouts). OpenCode 3m18.6s success (10 obs, tokens
  null), Codex 1m43.8s success (6 obs, tokens null), ClaudeCode 2m40.0s success
  (6 obs, envelope-authoritative: input 44 / output 14325 / total 14369 /
  cacheRead **1125952** / cacheCreation **52193**). Run aggregates:
  `KnownTokenExecutionCount 1`, `TotalInputTokens 44`, `TotalOutputTokens 14325`,
  `TotalTokens 14369`, `TotalCacheReadInputTokens 1125952`,
  `TotalCacheCreationInputTokens 52193`. Two views preserved for ClaudeCode:
  M12.1 (44/14325/14369) vs raw cache (1125952/52193) — sum NOT called
  cost/total. Package `schemaVersion` 1.1 with NO cache fields (verified), no
  credentials, no orphan processes. Note: real agentic context is cache-dominated
  (~1.13 M cached vs 44 new) — `input_tokens` alone understates context an order
  of magnitude. See milestone; M15.3 still deferred (do not start).

- **Agentic Provider Timeout Precedence Fix** (MILESTONE-015.2B, no ADR —
  config-bound default + per-step timeout precedence in the existing
  `EvidenceAcquisitionExecutor`). Fixes the M15.2A live blocker: the executor's
  hard per-step cap `EvidenceOptions.ProviderTimeout` (120 s default) was NOT
  config-bound, so every real agentic step was cut at 120 s regardless of each
  provider's own `TimeoutSeconds` (ClaudeCode 300 s). Now the executor derives
  `stepTimeout = provider.Metadata.Timeout ?? _options.ProviderTimeout`
  (`EvidenceProviderMetadata` gained `Timeout`; the three agentic providers stamp
  `_options.Timeout` in metadata — no name branching). `CouncilOptions.ProviderTimeout`
  (default 120 s) is bound into `EvidenceOptions` via DI; CLI `CliArgs.ParseProviderTimeout`
  + API `EvidenceConfig.ParseProviderTimeout` wire `Evidence:Execution:ProviderTimeout`
  (shipped `120` in both appsettings). Timeout vs run-cancellation vs failure stay
  distinct (cancellation never becomes a timeout); MaxConcurrency untouched.
  Removed ALL `[CP]` diagnostic probes (`Cli/Program.cs` ~109-111; `AnalysisPipeline.cs`
  `Cp` method + 6 call sites). No contract change — `schemaVersion` 1.1, package
  records carry NO timeout field. Build 0/0; **479/479 tests** (+11
  `ProviderTimeoutPrecedenceTests.cs`, offline: provider-over-global precedence,
  global fallback, per-provider metadata propagation for all three runtimes,
  timeout ⇒ `ErrorCategory=Timeout`, cancellation stays cancellation, parallel
  independent timeouts, no package leak, defaults + shipped config keys bound).
  **Live validation** (run `20260818-181449-72ca35`, Security, 3 providers,
  MaxConcurrency 3, `RedirectToService` — the same target that timed out 0/3 in
  M15.2A): **2/3 success**. ClaudeCode (own 300 s) ran **198.6 s** and completed
  (old 120 s cap would have killed it — acceptance met); Codex (own 120 s)
  completed in 104.2 s; OpenCode (own 120 s) timed out at exactly 00:02:00
  (0 evidence). Telemetry: `TotalInputTokens 179` / `TotalOutputTokens 17605` /
  `TotalTokens 17784` / `knownTokenExecutionCount 1`; 15 observations → 8 security
  findings (C0/H1/M6/L1), 2 multi-provider, 0 contradictions, 0 retries; one
  `schemaVersion` 1.1 package, no timeout field, no secrets (only benign finding
  prose quoting authorization/api_key as analysis subjects), no orphan processes.
  See milestone; M15.3 still deferred (do not start).

- **Agentic Token Usage Telemetry** (MILESTONE-015.2A, no ADR — adapter-internal
  mapping). Per-execution token usage for the three agentic runtimes is captured
  whenever the runtime reports authoritative usage in the machine-readable output
  the adapters ALREADY consume, reusing the M12.1 surface (`Evidence`
  `InputTokens`/`OutputTokens`/`TokensUsed`, `ProviderExecutionRecord`, run report
  `TotalInputTokens`/`TotalOutputTokens`/`TotalTokens`/`KnownTokenExecutionCount`).
  - **ClaudeCode** (`claude -p --output-format json`): the CLI's result envelope
    carries authoritative `usage.input_tokens`/`usage.output_tokens` (cumulative
    for the whole call = top-level agent loop incl. Read/Glob/Grep). **Mapped**:
    `ClaudeCodeOutputExtractor.TryExtractResult` now also lifts `usage` into a
    provider-neutral `ClaudeCodeUsage(Input?, Output?)` record; the provider
    stamps `Evidence.InputTokens/OutputTokens/TokensUsed` + metadata
    `inputTokens/outputTokens/totalTokens`. Missing/non-numeric → null (never 0).
  - **OpenCode / Codex**: the captured stdout (`opencode run` / `codex exec`
    formatted output) is final-message text only — **no machine-readable usage**,
    so tokens stay null (documented gap). No output is invented; adapters
    unchanged.
  - **Contract unchanged**: package `schemaVersion` stays **1.1**; token fields
    appear only inside the pre-existing `providerExecution` section, never in
    `findings`; no new artifact, no new telemetry model, no ADR.
  - **Tests**: build 0/0, **468/468** (+12, `AgenticTokenTelemetryTests.cs`,
    offline scripted fakes — ClaudeCode mapping/nulls, OpenCode/Codex null
    behavior, unknown-never-zero aggregation, evidence→record flow,
    `KnownTokenExecutionCount`, package 1.1 contract, serialization, no secrets).
  - **Live validation** (run `20260818-172513-2cb592`, Security, 3 providers,
    MaxConcurrency 3, `RedirectToService`): all 3 executions timed out at 00:02:00
    → telemetry correctly null (`knownTokenExecutionCount: 0`, `timeoutCount: 3`).
    Root cause = pre-existing gap: `EvidenceOptions.ProviderTimeout` (120 s
    default) is the executor's hard step cap and is NOT config-bound, so it
    overrides ClaudeCode's configured 300 s. Direct `claude -p --output-format
    json` smoke confirmed the envelope/usage shape (`input_tokens`/`output_tokens`
    liftable; cache_*_input_tokens dominate real context — 47 377 read + 16 300
    created vs 6 new; deliberately not mapped). No orphan processes. See
    milestone for M15.3 follow-ups (config-bind ProviderTimeout, optional cache
    mapping).

- **Post-Run Output Quality** (MILESTONE-015.2, no ADR — evaluation + run case
  log). First full 3×7 parallel Council run executed against a real repo
  (`RedirectToService`, run `20260813-230240-b3e36e`): 21 acquisition steps,
  11 success / 10 timeouts (Architecture & Testing 0/3 coverage → no findings),
  82 observations (OpenCode 51 / Codex 18 / ClaudeCode 13), 29 findings
  (critical 1 / high 6 / medium 13 / low 9), 6 multi-provider agreements,
  0 contradictions, one package `schemaVersion` 1.1, health Critical, no
  secrets/orphans.
  - **Verdict: B (conditional).** Content verbatim-accurate & verified; two
    structural defects: (1) summary reports "no Architecture/Testing issues" as
    assurance despite 0/3 evidence coverage; (2) 9/29 findings (~31%) duplicate 3
    root causes (RequestFilter NRE ×4, /readyz-vs-/healthy ×2, magic strings ×3).
  - **M15.3 TODO** (not started): acquisition robustness for agentic steps,
    snapshot pinning (tree SHA/copy) for reproducibility, reconciler dedup +
    per-discipline evidence-coverage in the package, gating "no issues" on
    coverage > 0.

- **Parallel Agent Execution** (MILESTONE-015.1, no ADR — execution concern).
  Independent acquisition steps of a Council run now run concurrently up to
  `Evidence:Execution:MaxConcurrency` (default **1**, i.e. strictly sequential;
  bound in both CLI and API `Program.cs`), and `1` is a pure execution change.
  - **Where**: `EvidenceAcquisitionExecutor.ExecuteAsync` runs steps through a
    `SemaphoreSlim(maxConcurrency)` (never unbounded Tasks); each step keeps its
    own provider/request/timeout CTS/per-step locals; the `WaitAsync` is OUTSIDE
    the try/finally so a cancelled waiter never releases a permit it didn't
    acquire; final `Evidence`/`records` aggregate strictly **in plan order**
    after all tasks complete, so artifacts stay deterministic. Failure isolation,
    `Continue`/`FailRun`, timeout-vs-cancellation semantics all unchanged.
  - **Contract unchanged**: `engineering-review-package.json` stays
    `schemaVersion` **1.1**, NO `maxConcurrency` field added; consumer-DTO gate
    green.
  - **Tests**: 456/456 (+15, `ParallelAcquisitionTests.cs`, all deterministic
    `TaskCompletionSource`/instant fakes, offline). Tests cover sequential
    default, `MaxConcurrency=1..3` never exceeding the bound, `Continue`/`FailRun`
    with a parallel executor, cancellation propagates (queued steps don't start),
    no step duplication/reordering, contract unchanged.
  - **Real verification** (run `20260812-222936-2229b2`, OpenCode+Codex+ClaudeCode
    / Security, `MaxConcurrency=3`): all three providers started within **87 ms**
    (22:29:36.58→.67) → true overlap; acquisition wall-clock ~57.2 s vs ~150.36 s
    sequential M14.1 baseline ≈ **62% faster**; 3/3 success, 0 failures/timeouts;
    21 obs → 13 raw → 12 consolidated (0 contradictions); exactly one package
    `1.1`, no orphans. `provider-execution.json` `totalDuration` is the M12.1 SUM
    aggregator (2:20), not wall-clock. The temporary `[CP]` diagnostic scaffold
    was removed after the run. (M15.2 is complete — see Current state above.)
- **Previous: Targeted Semantic Reconciliation** (MILESTONE-014.4, ADR-025) — acted on M14.3's
  Decision B: an OPTIONAL, narrowly-scoped semantic review for EXACTLY the ambiguous
  subset (findings whose `CouncilAssessment.Differences` include `ObservationType`) —
  never a general LLM judge, never a review of every finding.
  - **Candidate selection** (`SemanticReconciliationBuilder.IsCandidate`, pure/
    deterministic): `AgreementWithDifferences` + `Differences` contains
    `ObservationType`. Everything else — `SingleSource`, `StrongAgreement`,
    severity-only, location-only, `PotentialConflict`, unassessed — bypasses review
    entirely (test-verified against the exact 6-finding fixture the milestone spec
    describes: 2/6 invoke the reviewer).
  - **Contract**: `SemanticReconciliationDecision {SameIssue, DifferentIssues,
    Inconclusive}` + `SemanticReconciliationResult {Decision, Reason}`
    (`Core.Domain`); `ISemanticReconciliationReviewer` + minimal, finding-scoped
    `SemanticReconciliationRequest` (id/title/category/providers/attributed
    observations only — never the repository, other findings, or the package)
    (`Core.Abstractions`). No scores/probabilities/rankings/votes; the reviewer
    never touches severity/confidence/recommendation.
  - **Real implementation** `LlmSemanticReconciliationReviewer`
    (`Infrastructure.Reconciliation`) depends ONLY on the existing generic
    `ILlmChatClient` seam (same transport Claude/OpenAI already use) + reuses the
    existing `StructuredJsonExtractor` — not a new LLM framework (no retries, no
    repair, single attempt). Tests use a scripted `ISemanticReconciliationReviewer`
    fake + the existing `ScriptedLlmClient` — fully offline.
  - **All three decisions share one code path**: the finding is NEVER mutated beyond
    attaching `.SemanticReview` — `DifferentIssues` is recorded as advisory-only and
    does NOT split the finding (splitting would need its own severity/confidence/
    provenance re-derivation design; explicitly deferred).
  - **Failure → `Inconclusive`, never run failure**: reviewer exceptions (timeout,
    malformed output, unavailable) are caught per-candidate; a second defensive
    catch around the whole enrichment stage in `AnalysisPipeline` guards against
    unexpected bugs there too. Only genuine run cancellation propagates.
  - **Disabled by default** (`Council:SemanticReconciliation:Enabled=false`);
    `AnalysisPipeline` gained two OPTIONAL trailing ctor params (reviewer + options)
    so all 16 existing direct-construction test files kept compiling unchanged. A
    real offline smoke with the feature at its default produced a package
    byte-identical in shape to the M14.3 smoke (0 `semanticReview` occurrences).
  - **Additive package exposure**: `findings[].semanticReview` (`schemaVersion`
    unchanged, **1.1**); compact `Semantic Review: <Decision>` / `Reason: …` lines
    under the existing Council Assessment Markdown block; consumer DTO gained
    `SemanticReviewContract`; gate green.
  - Build 0/0, **441/441 tests** (+21, fully offline). **Deferred to M14.5**:
    deterministically/semantically splitting a `DifferentIssues` finding.
- **Post-implementation validation (2026-08-12, D-098): M14.5 skipped.** Real Council
  runs are agentic-provider-only, and `D-089` already excludes `ProviderType.Agentic`
  from `ProviderComparisonBuilder` — so `ObservationTypeDisagreement` (no finding-level
  fallback, unlike `LocationDisagreement`) can never be set for real persisted data,
  meaning `SemanticReconciliationBuilder.IsCandidate` selects **zero** real candidates
  today, always. The fixture reproduces the documented 2 candidates, but no
  authenticated `ILlmChatClient` credential was available to invoke the real reviewer,
  so none was fabricated. With real candidates fixed at 0, `DifferentIssues` is 0 for
  all real data — M14.5 has no real case to act on. **Next: M15.1 Parallel Agent
  Execution.** Zero production code changed by this validation. See
  `MILESTONE-014.4` (appended section) and `D-098`.

## Previous state (v24 — 2026-08-12, Milestone 014.3)

- **Council Assessment Evaluation** (MILESTONE-014.3) — evaluated the M14.2
  deterministic Council using the real M14.1-shaped fixture and produced an explicit
  decision on whether a semantic reconciliation layer is needed.
  - **Fixture result (13 consolidated findings, unmodified rules):** 7 SingleSource
    (53.8%), 1 StrongAgreement (7.7%), 5 AgreementWithDifferences (38.5%), **0
    PotentialConflict**. Difference-type totals across the 5 AgreementWithDifferences
    findings: Severity 2, ObservationType 2, Location 5 (every one carries a
    location difference), Contradiction 0.
  - **New `CouncilAssessmentSummary`** (`Core.Domain`) — four counts
    (`SingleSourceCount`/`StrongAgreementCount`/`AgreementWithDifferencesCount`/
    `PotentialConflictCount`) + computed `TotalAssessed`. **One authoritative
    calculation**: `CouncilAssessmentBuilder.Summarize(findings)` is a pure
    aggregation of each finding's own already-computed `CouncilAssessment.Type`
    (unassessed findings excluded, never guessed); `EngineeringReviewPackageBuilder`
    calls it once over the same assessed findings; no exporter recomputes it.
  - **Additive package exposure**: `councilAssessmentSummary` in
    `engineering-review-package.json` (`schemaVersion` unchanged, **1.1**); compact
    `## Council Assessment` section in `engineering-review.md`; consumer DTO gained
    `CouncilAssessmentSummaryContract`; gate green. Verified against both the fixture
    and a real offline `review --provider Mock` run.
  - **Decision: B** — deterministic reconciliation is sufficient for MOST findings
    (0 conflicts; severity/location differences are safely resolved by existing
    rules), but a SMALL, narrowly-identifiable subset — findings whose
    `Differences` include `ObservationType` (2/13 here) — is genuinely ambiguous:
    deterministic rules cannot tell "same issue, different label" from "two
    distinct issues merged" (a possible false merge). Future semantic-review
    candidates are scoped to EXACTLY that subset; nothing else. No next layer
    implemented; no classification rule changed to shape the numbers.
  - No architectural change — no new ADR. Reconciliation merge rules, severity/
    confidence rules, health/risk scoring, provider execution, and M12 diagnostics
    all unchanged. Build 0/0, **420/420 tests** (+8 new, offline only).

## Previous state (v23 — 2026-08-12, Milestone 014.2)

- **Deterministic Council Assessment** (MILESTONE-014.2) — every **consolidated**
  finding gets a pure, provider-neutral `ReconciliationAssessment` computed ONLY from
  data the platform already produces: the reconciler's own `SupportingProviders` /
  `SeverityRange` / `ContradictionReasons`, plus the M12.3/M12.4
  `CalibrationDiagnosticsReport` when present. No LLM, no re-reconciliation, no voting,
  no provider ranking/weighting.
  - **Four types:** `SingleSource` (1 provider, never discounted) ·
    `StrongAgreement` (≥2 providers, no known disagreement) ·
    `AgreementWithDifferences` (≥2 providers + a severity/observation-type/location
    difference — a difference is never a conflict) · `PotentialConflict` (an
    EXPLICIT, unexplained contradiction reason only — never inferred from a mere
    difference or a provider not reporting the finding; 0 is a valid outcome).
  - **`CouncilAssessmentBuilder.Apply/Assess`** (`Core.Application`) stamps
    `Finding.CouncilAssessment` on consolidated findings only (via
    `EngineeringReviewPackageBuilder`); raw findings in the appendix are untouched.
  - **Additive package exposure**: `findings[].councilAssessment` in
    `engineering-review-package.json` (`schemaVersion` unchanged, **1.1**); rendered in
    `engineering-review.md` (`Council Assessment: <Type>` block); consumer DTO
    (`CouncilAssessmentContract`) + regenerated integration sample.
  - **Verified against the real M14.1 Council run**: a deterministic replay fixture
    reproduces M14.1's exact finding shape (13 findings: 7 exclusive / 2×3-provider /
    4×2-provider; severity 2 / observation-type 2 / location 5 disagreements;
    contradictions 0) and the assessment layer classifies it as 7 SingleSource, 1
    StrongAgreement, 5 AgreementWithDifferences, **0 PotentialConflict** — matching
    M14.1's own `contradictions = 0` exactly. A real offline smoke
    (`review --provider Mock --path evaluation/dataset/02-vulnerable-payments`)
    confirmed the wiring end to end (7/7 findings `singleSource`, `agreementCount: 1`).
  - No architectural change — no new ADR; reconciliation merge rules, severity/
    confidence rules, health/risk scoring, provider execution, M12 diagnostics,
    prompts, and agent adapters are all unchanged. Build 0/0, **412/412 tests**.

## Previous state (v22 — 2026-08-10, Milestone 014.1)

- **First Multi-Agent Engineering Council Run** (MILESTONE-014.1) — **one** real
  AnalysisRun executed **OpenCode + Codex + ClaudeCode** together over the same
  repository (`m14-1-council-smoke-repo`, Security discipline) and consolidated their
  evidence through the existing deterministic reconciler into a single package:
  - **All 3 providers succeeded** (run `20260811-011804-d3eb0a`, 0 failures / 0
    timeouts): OpenCode 32.89 s / 8 obs / 6 raw, Codex 59.73 s / 6 obs / 5 raw,
    ClaudeCode 57.73 s / 10 obs / 4 raw.
  - **Council totals:** 24 observations → 15 raw findings → **13 consolidated
    findings**; supported by all 3 = 2, exactly 2 = 4, exclusive = 7 (OpenCode 3,
    ClaudeCode 3, Codex 1); severity disagreements 2, observation-type 2, location 5,
    contradictions 0. Package `schemaVersion **1.1**`, provenance preserved, **no
    secrets** in artifacts, **no orphan processes**.
  - **M14.1 hang fixed** — root cause was CONFIG LOADING, not the adapter: the host
    resolved `appsettings.json` against the process **cwd**, so launching from the
    repo root (`dotnet run --project src\EngineeringCouncil.Cli`) never loaded
    `Evidence:OpenCode:Port=0`; OpenCode then ran without `--port`, competed for the
    shared default server held by an interactive session, and queued to the 120 s
    timeout. Fix: `Cli/Program.cs` now
    `ConfigureAppConfiguration(... SetBasePath(AppContext.BaseDirectory))`; the CLI's
    own `appsettings.json` always loads and every OpenCode run emits a concrete
    isolated `--port <n>` (verified: port **58041**).
  - **Deterministic guard** — `Cli_appsettings_keeps_the_m141_isolated_port_and_explicit_model`
    (`Tests/OpenCodeModelSelectionTests.cs`) asserts the shipped appsettings.json
    keeps `Port=0` + model; existing fake-runner tests pin `Port=0 → --port <concrete>`
    argv. Effective config re-verified from the repo root: `Port=0`, model
    `opencode/deepseek-v4-flash-free`, `TimeoutSeconds=120`, `DEEPSEEK_API_KEY`
    presence-only (absent from User/Machine env; value never printed).
  - No architectural change — no new ADR; the deterministic reconciler remains
    authoritative (numbers reported as-is, nothing forced).

## Previous state (v21 — 2026-08-09, Milestone 013.5)

- **Claude Code agentic adapter** (MILESTONE-013.5, ADR-024) — a **third real external
  coding-agent runtime** behind the ADR-021 Agentic contract, proving the contract is
  runtime-agnostic (OpenCode M13.2/13.3 + Codex M13.4 + Claude Code M13.5 share one
  seam, same downstream):
  - **`ClaudeCodeEvidenceProvider`** (`Infrastructure/Evidence/`,
    `ProviderType = Agentic`, `Name = ClaudeCode`, Discipline-scoped,
    `Version = "claudecode-adapter-v1"`, `ProviderId = "claudecode"`) launches the
    `claude` executable against the repository root, hands it ONE discipline-specific
    **read-only** analysis instruction, and converts the agent's structured
    `observations` envelope (carried inside the CLI's own `--output-format json`
    result envelope) into existing `Evidence`. The domain never learns Claude Code
    exists. **Naming:** `ClaudeCode` (agentic CLI runtime) ≠ `Claude` (direct API
    provider) — unambiguous providers, never conflated.
  - **`IClaudeCodeProcessRunner` / `ClaudeCodeProcessRunner`** — a small,
    purpose-built seam mirroring the OpenCode/Codex runner: argv-based
    (`UseShellExecute = false`, `ArgumentList`, never a shell string),
    `WorkingDirectory = repository root`, bounded stdout/stderr, exit code, timeout,
    cancellation, and **complete process-tree** termination (M13.3A lesson);
    process-start failures become a categorized provider failure.
  - **Claude Code invocation** — `-p --output-format json --no-session-persistence
    --tools "Read,Glob,Grep" [--model <model>] -- "<instruction>"`. `--tools
    "Read,Glob,Grep"` is the CLI's own supported read-only mechanism (write tools
    removed from the built-in set — the runtime boundary; the prompt is the second
    boundary; NOT an OS sandbox — unsupported on Windows); `--no-session-
    persistence` keeps the run stateless; `--` stops the **variadic** `--tools
    <tools...>` from swallowing the prompt (verified against real v2.1.223 — without
    it the CLI errors "Input must be provided either through stdin or as a prompt
    argument").
  - **`ClaudeCodeOutputExtractor`** — the minimal adapter: unwraps exactly the ONE
    documented result-envelope `result` string field and hands it to the existing
    `LlmEvidenceResponseValidator`. Not a new parser; no prose salvage; no repair
    call. Envelope failures (`is_error`, non-envelope, missing/empty `result`) are
    categorized `SchemaValidation` failures.
  - **Claude Code's own auth** — no API-key option anywhere; Claude Code authenticates
    via its own login (`claude auth` / the configured account). The offline Mock
    remains the zero-config default.
  - **Optional model** — `ClaudeCodeOptions.Model` (e.g. `claude-sonnet-5` or an
    alias like `sonnet`) → `--model <id>` via `ArgumentList`; unset ⇒ Claude Code
    default. Never mandatory/guessed/hardcoded. When set: `Metadata["model"]`
    telemetry + success log; `ProviderVersion` stays `claudecode-adapter-v1`.
  - **Failure semantics** — envelope/malformed output ⇒ categorized
    `SchemaValidation` failure (no repair call); non-zero exit ⇒ `ProcessError` with
    bounded stderr; timeout terminates the complete process tree ⇒ `Timeout` failure;
    run/user cancellation terminates the complete process tree and propagates
    `OperationCanceledException` (never an ordinary failure). `ContextFingerprint`
    stays `""` — never fabricated.
  - **Config** — `Evidence:ClaudeCode { Enabled: false, Executable: "claude",
    TimeoutSeconds: 300, Model: "" }`, disabled by default; `--provider ClaudeCode`
    opts in; no new CLI command. Provider help line documents `claude auth`.
  - **Non-parallel test collection** — OpenCode + Codex + ClaudeCode real-process
    tests share one NON-PARALLEL xUnit collection (`ProcessRunnerTests`) so their
    short-lived `cmd /c ping` orphan-survivor probes never observe each other's
    children.
- Verified: build **0/0**, **371/371 tests** (27 new: 22 in `ClaudeCodeEvidenceTests.cs`
  incl. the read-only tool restriction, envelope-failure categories, model
  passthrough + end-to-end, 5 real-process tests in `ClaudeCodeProcessRunnerTests.cs`
  — no Claude Code/OpenCode/network/keys), package unchanged
  (`schemaVersion **1.1**`).
- **Real smoke proven** — Claude Code CLI v2.1.223 installed + authenticated via its
  own login (`claude auth status`: loggedIn true, first-party); against a tiny
  fixture repo (`cc-smoke`, hardcoded `P@ssw0rd123!` connection string + presence-only
  token check) run `20260810-002540-89a591` produced Evidence → **2 Security
  observations** (`HardcodedSecret` critical, `BrokenAuthentication` high, both
  `sourceProvider: ClaudeCode`/`sourceProviderType: agentic`, correct file/line refs)
  → **2 findings** → package (`schemaVersion **1.1**`, no `providerComparison`, no
  `model` field) in ~31 s with **no orphan processes**; `contextFingerprint ""`
  (correctly never fabricated for agentic). The only `claude` processes present were
  the user's pre-existing VSCode extension helpers (~34 min before the run), none from
  the run's PATH executable. CLI rebuilt before the run (M13.3A operational rule
  reaffirmed).

## Previous state (v20 — 2026-08-09, Milestone 013.4)

- **Codex agentic adapter** (MILESTONE-013.4, ADR-023) — a **second real external
  coding-agent runtime** behind the ADR-021 Agentic contract, proving the contract is
  runtime-agnostic (OpenCode M13.2/13.3 + Codex M13.4 share one seam, same downstream):
  - **`CodexEvidenceProvider`** (`Infrastructure/Evidence/`, `ProviderType = Agentic`,
    `Name = Codex`, Discipline-scoped, `Version = "codex-adapter-v1"`) launches the
    `codex` executable against the repository root, hands it ONE discipline-specific
    **read-only** analysis instruction, validates its structured JSON with the SAME
    `LlmEvidenceResponseValidator`, and converts it into existing `Evidence`. The
    domain never learns Codex exists.
  - **`ICodexProcessRunner` / `CodexProcessRunner`** — a small, purpose-built seam
    mirroring the OpenCode runner: argv-based (`UseShellExecute = false`,
    `ArgumentList`, never a shell string), `WorkingDirectory = repository root`,
    bounded stdout/stderr, exit code, timeout, cancellation, and process-tree
    termination; process-start failures become a categorized provider failure.
  - **Codex invocation** — `exec -s read-only --ephemeral --skip-git-repo-check
    [-m <model>] "<instruction>"`. `-s read-only` enforces the read-only boundary
    **at the Codex sandbox level** (stronger than OpenCode's instruction-only
    boundary); `--ephemeral` keeps the run stateless; `--skip-git-repo-check` keeps
    it usable on plain directories.
  - **Codex's own auth** — no API-key option anywhere; Codex authenticates via its
    own login (ChatGPT account / `codex login`). The offline Mock remains the
    zero-config default.
  - **Optional model** — `CodexOptions.Model` (e.g. `gpt-5.4-mini`) → `-m <model>`
    via `ArgumentList`; unset ⇒ Codex default. Never mandatory/guessed/hardcoded.
    When set: `Metadata["model"]` telemetry + success log; `ProviderVersion` stays
    `codex-adapter-v1`.
  - **Failure semantics** — malformed output ⇒ categorized `SchemaValidation`
    failure (no repair call); non-zero exit ⇒ `ProcessError` with bounded stderr;
    timeout terminates the process ⇒ `Timeout` failure; run/user cancellation
    terminates the process and propagates `OperationCanceledException` (never an
    ordinary failure). `ContextFingerprint` stays `""` — never fabricated.
  - **Config** — `Evidence:Codex { Enabled: false, Executable: "codex",
    TimeoutSeconds: 120, Model: "" }`, disabled by default; `--provider Codex` opts
    in; no new CLI command. Provider help line documents `codex login` auth.
  - **Non-parallel test collection** — OpenCode + Codex real-process tests share one
    NON-PARALLEL xUnit collection (`ProcessRunnerTests`) so their short-lived
    `cmd /c ping` orphan-survivor probes never observe each other's children.
- Verified: build **0/0**, **344/344 tests** (25 new: 20 in `CodexEvidenceTests.cs`
  incl. model passthrough + end-to-end + consumer-DTO gate, 5 real-process tests in
  `CodexProcessRunnerTests.cs` — no Codex/OpenCode/network/keys), package unchanged
  (`schemaVersion **1.1**`).
- **Real smoke proven** — native Codex CLI at
  `...\@openai\codex-win32-x64\vendor\x86_64-pc-windows-msvc\bin\codex.exe`; against
  a tiny fixture repo (`m13-4\codex-smoke-repo`, hardcoded live Stripe key /
  `P@ssw0rd!2026` connection string / unsalted SHA1 + insecure default) run
  `20260809-052551-9bb061` produced Evidence → **3 Security observations** → **3
  findings** (2 High, 1 Medium) → package (`schemaVersion **1.1**`, health
  NeedsAttention, risk High) in ~61 s with **no orphan processes**;
  `contextFingerprint ""` (correctly never fabricated for agentic). The live Stripe
  key was **redacted by Codex itself** (`sk-live-…`); the connection-string password
  appears only because Codex quoted the source excerpt as evidence (expected
  passthrough — no adapter credential ever reaches telemetry/artifacts). CLI rebuilt
  before the run (M13.3A operational rule reaffirmed).

## Previous state (v19 — 2026-08-09, Milestone 013.3)

- **OpenCode model selection + real smoke** (MILESTONE-013.3) — builds on v18:
  - **Explicit model selection** — optional `Evidence:OpenCode:Model`
    (`provider/model` format) is passed to the process as `--model <id>` via the
    existing safe `ArgumentList` invocation (`UseShellExecute = false`, never a
    shell string); the instruction stays the last argument so the
    runner/telemetry contract is unchanged. When set, `OpenCodeEvidenceProvider`
    stamps `Metadata["model"]` on evidence (built before the init-only
    initializer) and logs the model; when unset, model identity stays absent
    (never `"unknown"`). `ProviderVersion` stays `opencode-adapter-v1` — model is
    config, never domain logic, never hardcoded, never a credential (keys live in
    OpenCode's own auth, e.g. `DEEPSEEK_API_KEY`).
  - **Real smoke proven** — first live end-to-end OpenCode + DeepSeek run:
    OpenCode **1.18.15** (npm), model `opencode/deepseek-v4-flash-free` (paid
    `opencode/deepseek-v4-flash` blocked by billing), auth via OpenCode Zen `api`
    credential. Against a tiny fixture repo (hardcoded key / `P@ssw0rd!2026`
    connection string / unsalted SHA1) the run produced Evidence → **5
    observations** → **4 Security findings** → package (`schemaVersion **1.1**`,
    health/risk critical) in ~27.6 s with **no orphan processes**;
    `contextFingerprint ""` (correctly never fabricated for agentic).
  - **Process-runner lifecycle proven sound** — a hang was investigated and the
    root cause was NOT the runner: the deployed CLI binary was **stale** (built
    before M13.3), so it never passed `--model` and OpenCode fell back to its
    billing-blocked default model. Rebuilding the CLI fixed it (no production
    code change). Timeout/cancellation already terminate the **complete** process
    tree (verified with a `cmd /c ping` tree probe); two new Windows-safe
    regression tests in `OpenCodeProcessRunnerTests.cs` lock that in.
  - **Note:** the executor's `EvidenceOptions.ProviderTimeout` (default 120 s)
    is DI-hardcoded, not env-configurable; `Evidence:OpenCode:TimeoutSeconds`
    only bounds the process level.
- Verified: build **0/0**, **319/319 tests** (14 new: 12 in
  `OpenCodeModelSelectionTests.cs` — no OpenCode/network/keys, 2 process-tree
  termination tests), package unchanged (`schemaVersion **1.1**`). See
  MILESTONE-013.3.

## Previous state (v18 — 2026-08-08, Milestone 013.2)

- **OpenCode agentic adapter** (ADR-022) — the **first real external coding-agent
  runtime** behind the ADR-021 Agentic contract: `OpenCodeEvidenceProvider`
  (`Infrastructure/Evidence/`, `ProviderType = Agentic`, `Name = OpenCode`,
  Discipline-scoped, `RequiresAnalyzerInstructions = true`,
  `SupportsRepositoryWideAnalysis = false`) launches the `opencode` executable
  against the local repository root, hands it ONE discipline-specific **read-only**
  analysis instruction, validates its structured JSON with the SAME
  `LlmEvidenceResponseValidator` the LLM providers use, and converts it into existing
  `Evidence`. Everything downstream (interpreter → observations → analyzers →
  reconciliation → package) never learns OpenCode exists:
  - **Process runner** — `IOpenCodeProcessRunner` / `OpenCodeProcessRunner` (a
    small, purpose-built seam, NOT a generic shell/command executor): starts the
    executable, sets `WorkingDirectory = analyzed repository root`, passes the
    instruction, captures **bounded** stdout (128 KiB) / stderr (8 KiB), exit code,
    timeout, and cancellation.
  - **No shell strings** — `ProcessStartInfo { UseShellExecute = false }` +
    `ArgumentList` (`["run", instruction]`); repo path and prompt are never
    concatenated into `cmd /c` / `sh -c` — safe against quoting/injection.
  - **Read-only is instruction-level only** — the prompt forbids modifying/creating/
    deleting files, formatting, commits, and patches; explicitly documented as NOT a
    security sandbox (OS sandboxing/containers/VM isolation remain deferred).
  - **Timeout vs cancellation** — the runner's own `Timeout` (`Evidence:OpenCode:
    TimeoutSeconds`, default 120) → existing `Timeout` failure category →
    `Continue`/`FailRun` per `ProviderFailureMode`; a run/user cancellation
    terminates the process and propagates `OperationCanceledException` (never an
    ordinary failure, never left running intentionally).
  - **Exit codes** — `0` → parse stdout as structured result; non-zero → categorized
    `ProcessError` with a bounded stderr diagnostic (no secrets/env/machine details).
  - **`ContextFingerprint` stays `""`** — never generated from repo hash/snapshot/
    prompt/working directory; M12 comparison never treats OpenCode as comparable to
    Claude/OpenAI API executions.
  - **No DeepSeek / no model** — the provider represents OpenCode, not a model;
    model identity is unknown (never guessed, never hardcoded); M13.3 configures and
    validates a specific model explicitly.
  - **Config** — `Evidence:OpenCode { Enabled: false, Executable: "opencode",
    TimeoutSeconds: 120 }`, **disabled by default** (offline Mock remains the
    zero-config default); `--provider OpenCode` opts in via the existing mechanism;
    no new CLI command.
- Verified: build **0/0**, **306/306 tests** (17 new: 14 in `OpenCodeEvidenceTests.cs`
  incl. a real-pipeline end-to-end run → consumer-compatible package, 3 real-process
  tests in `OpenCodeProcessRunnerTests.cs` using dotnet/ping — no OpenCode install
  needed), package unchanged (`schemaVersion **1.1**`, no `providerComparison`).
  See MILESTONE-013.2, ADR-022.

## Previous state (v17 — 2026-08-08, Milestone 013.1)

- **Agentic evidence contract** (ADR-021) — first-class support for **agentic
  sources** (an external agent — Codex / Claude Code / opencode — that explores the
  repository itself) via ONE new additive enum value
  `EvidenceProviderType.Agentic = 6` and the smallest executor/interpreter changes.
  No new interface (`IEvidenceProvider` + `Metadata.ProviderType` is the whole
  contract), no provider-name branching, no pipeline/package change:
  - **Agentic request semantics** — the `EvidenceRequest` carries the repository
    **identity** (root/solution/branch/commit) + **file structure** (paths/
    extensions/sizes/line counts) with **all file CONTENT stripped**, plus an
    explicit **empty** `ContextSelection` (`Strategy = "agentic"`, zero files,
    zero chars, scope still in `TotalRepositoryFiles`) — the council does **not**
    embed repository contents; the agent explores.
  - **No fabricated `ContextFingerprint`** — agentic steps leave `""` on records
    AND `Evidence` (success + failure); the council can't vouch for the agent's
    context. M12 comparison filters `ProviderType == LLM` only, so agentic
    executions are **structurally excluded** (unchanged code, regression-tested).
  - **Structured result reuse** — agentic sources return the same `observations`
    envelope; `StructuredLlmEvidenceInterpreter.CanInterpret` widens
    `LLM → LLM or Agentic` with **zero** interpretation-logic changes (file guard,
    confidence lowering, discipline-mismatch rule all apply).
  - **Planner untouched** — metadata-driven, so an Agentic+Discipline provider
    automatically gets one step per discipline (regression-tested incl. a
    static-looking name staying discipline-scoped).
  - **Real no-network provider** — `Infrastructure/Evidence/AgenticEvidenceProvider.cs`
    (`"Agentic"`, DI-registered, `--provider Agentic`) demonstrates the full path:
    a deterministic rule-based agent reads the file map and returns one structured
    observation per relevant file; metadata carries agent runtime
    (`agentic-explorer-v1`) + `filesExplored`. No real adapter yet (deferred).
- Verified: build **0/0**, **289/289 tests** (14 new in `AgenticEvidenceTests.cs`
  incl. a real-pipeline agentic run), package unchanged (`schemaVersion **1.1**`,
  no `providerComparison`), M12 comparison excludes agentic, agentic requests carry
  repository identity but never file contents and never a fabricated fingerprint.
  See MILESTONE-013.1, ADR-021.

## Previous state (v16 — 2026-08-08, Milestone 012.4)

- **Observation calibration diagnostics** (ADR-020) — extends the M12.3
  `calibration-diagnostics.json` artifact **additively** with exactly **two new
  diagnostic types**, both on **shared findings** (≥2 compared providers) in
  **Comparable** disciplines only:
  - **`ObservationTypeDisagreement`** — attributed supporting observations expose
    **different normalized `ObservationType` sets per provider**; exact values
    compared, no equivalence rules, never decides which type is correct.
  - **`LocationDisagreement`** — attributed supporting observations reference
    **meaningfully different normalized locations** (different file, or same file
    with different explicit lines); reconciler-style path normalization, exact line
    when both name one, missing-line compatible, missing location contributes
    nothing (never a false disagreement; no `MissingLocation` type).
  - **Provider attribution comes ONLY from existing provenance**
    (`Consolidated Finding → SupportingFindingIds → Raw Findings → ObservationIds →
    EngineeringObservations → SourceProvider | SourceEvidenceId → Evidence.ProviderName`);
    un-attributable observations are **skipped**, never guessed from text/titles/
    ids/ordering.
  - `NonComparable` / `Incomplete` keep the existing limitation behavior (never
    diagnostics). M12.3 types/thresholds unchanged; pure projection (no provider
    calls, no rescan, no re-interpretation, no semantic/fuzzy matching, no
    reconciliation/context/prompt changes).
  - **New (additive):** `Core/Domain/CalibrationDiagnostics.cs`
    `CalibrationDiagnosticType` + `ObservationTypeDisagreement`/`LocationDisagreement`,
    `ProviderObservationType`, `ProviderLocation`, `FindingDiagnostic` +
    `Providers`/`ObservationIds`/`ObservationTypesByProvider`/`LocationsByProvider`;
    `Core/Application/CalibrationDiagnosticsBuilder.cs` emits the two signals over
    the M12.3 `sharedFindings` gate. No pipeline/reporting/persistence change —
    the existing wiring writes `calibration-diagnostics.json`.
- Verified: build **0/0**, **275/275 tests** (14 new in `CalibrationDiagnosticsTests.cs`
  incl. the acceptance fixture A→`ObservationTypeDisagreement`, B→`LocationDisagreement`,
  C→none), consumer-DTO gate green, package unchanged (`schemaVersion **1.1**`,
  never carries `observationTypeDisagreement`/`locationDisagreement`); determinism
  asserted regardless of observation/raw-finding/consolidated-finding ordering. See
  MILESTONE-012.4, ADR-020.

## Previous state (v15 — 2026-08-07, Milestone 012.3)

- **Finding calibration diagnostics** (ADR-019) — a deterministic,
  provider-neutral, **descriptive** finding-calibration artifact
  (`calibration-diagnostics.json`) projected from a multi-LLM run's OWN
  already-reconciled data — no provider calls, no rescan, no semantic matching,
  no re-interpretation, no ranking/scoring/weighting/calibration. Strictly
  internal; `engineering-review-package.json` stays `schemaVersion **1.1**` and
  is asserted to never carry `calibration`/`exclusiveFinding`/`severityDisagreement`.
  - **Exactly three diagnostic types, Comparable disciplines only:**
    - **`ExclusiveFinding`** — consolidated finding supported by exactly ONE
      compared provider; reuses M12.2 `FindingMetrics.ExclusiveFindingIds`
      (same exclusivity definition, so the two artifacts can never disagree),
      resolved through the consolidated `Findings` (never a raw id — the
      reconciler's `StableId` may have renamed it).
    - **`SeverityDisagreement`** — a SHARED finding whose reconciler
      `SeverityRange` contains an en dash (`–`); per-provider severities
      attributed via `SupportingFindingIds` → raw findings → a SINGLE compared
      provider (`EvidenceProvider`, else exactly-one `SupportingProvider`), so
      attribution can never disagree with reconciliation.
    - **`LowAgreement`** — per discipline when
      `AgreementMetrics.ConsolidatedFindingCount ≥ MinimumSampleFindings (3)`
      and `AgreementRate < LowAgreementThreshold (0.5)`; counts copied verbatim
      from M12.2 `AgreementMetrics`.
    - One diagnostic per finding at most; `NonComparable` / `Incomplete`
      disciplines become `Limitations` notes only (never dropped, never failing
      the report); thresholds are explicit `CalibrationDiagnosticCriteria`
      constants; deterministic sort `Type`→`Discipline`→`Provider`→`FindingId`→
      `Title`; `GeneratedAt=UtcNow`.
  - **New files:** `Core/Domain/CalibrationDiagnostics.cs`,
    `Core/Application/CalibrationDiagnosticsBuilder.cs` (static, deterministic).
    Wired: `AnalysisPipeline` step 7c (success path, after the comparison) builds
    `run.CalibrationDiagnostics`; `FileSystemAnalysisRunRepository` writes
    `calibration-diagnostics.json` only when non-null (between
    `provider-comparison.*` and `run.json`); `run.json` round-trips it
    additively.
- Verified: build **0/0**, **261/261 tests** (14 new in
  `src/EngineeringCouncil.Tests/CalibrationDiagnosticsTests.cs` incl. a
  real-pipeline dual-provider run with **no extra provider calls**), consumer-DTO
  gate green, package unchanged (`schemaVersion 1.1`); offline Mock smoke
  (single provider) writes **no** `calibration-diagnostics.json`, package has no
  `providerComparison`. Determinism asserted regardless of enumeration order.
  See MILESTONE-012.3, ADR-019.

## Previous state (v14 — 2026-08-07, Milestone 012.2)

- **Real provider comparison** (ADR-018) — a deterministic, provider-neutral,
  **descriptive** comparison of the LLM providers executed in a run, projected from
  the run's OWN already-produced data — no provider calls, no rescan, no context
  rebuild, no re-interpretation. It is a strictly internal diagnostic artifact
  (`provider-comparison.json` + `.md`); `engineering-review-package.json` stays
  `schemaVersion **1.1**` and is asserted to never carry the comparison.
  - **Comparability = same discipline + same effective context.** New
    `ContextFingerprint.Compute(selection, policy?)` = SHA-256 over the selected
    files ordered by relative path with per-file **effective** content
    (`ContextContentPolicy.MaxCharactersPerFile`), `"CTX-"` + 12 hex; excludes
    absolute paths, provider, model, keys, timestamps, RunId. The executor stamps it
    on every record and `Evidence` (success AND failure paths; default `""`).
  - **Status per discipline:** `Comparable` (all succeeded + one fingerprint) ·
    `NonComparable` (succeeded but fingerprints differ — execution metrics kept,
    agreement withheld) · `Incomplete` (an execution failed — output comparison
    unavailable, `Limitations` note, never fails the report).
  - **Metrics:** execution = M12.1 telemetry reused verbatim (tokens/retries/
    repairs/model via `ProviderVersion`; unknown stays unknown); observations =
    per-provider counts + sorted type/severity/confidence distributions,
    `ReferencedContextFileRate` (null when no context files); findings/agreement =
    derived ONLY from the existing `RuleBasedFindingReconciler` output
    (`SupportingProviders`/`AgreementCount`) — shared = supported by ≥2 compared
    providers, exclusive = exactly one, `AgreementRate = shared ÷ consolidated`
    (null when no findings; never "accuracy"). SARIF/static-analysis executions are
    excluded from the comparison.
  - **New files:** `Core/Analysis/ContextFingerprint.cs`,
    `Core/Domain/ProviderComparison.cs`, `Core/Application/ProviderComparisonBuilder.cs`
    (static, deterministic, sorted), `Infrastructure/Reporting/ProviderComparisonMarkdownExporter.cs`.
    Wired: `AnalysisPipeline` step 7b builds `run.ProviderComparison`;
    `FileSystemAnalysisRunRepository` writes the two artifacts only when non-null.
- Verified: build **0/0**, **247/247 tests** (24 new in
  `src/EngineeringCouncil.Tests/ProviderComparisonTests.cs` incl. a real-pipeline
  dual-provider run), consumer-DTO gate green, package unchanged (`schemaVersion 1.1`);
  offline Mock smoke (single provider) writes **no** `provider-comparison.*`, records
  carry `contextFingerprint` (`CTX-…`), package has no `providerComparison`. Determinism
  asserted regardless of enumeration order. See MILESTONE-012.2, ADR-018.

## Previous state (v13 — 2026-08-07, Milestone 012.1)

- **Real provider metrics** (ADR-017) — reliable, provider-neutral per-execution and
  per-run metrics for real LLM providers (Claude, OpenAI), reused over the existing
  `provider-execution.json` telemetry. Facts only — no provider comparison/ranking, no
  calibration, no cost optimization, no contract change (`schemaVersion` stays **1.1**):
  - **Token usage** — provider-reported only; unknown ⇒ `null` (never `0`);
    `TotalTokens = Input + Output` when both known. Run report adds
    `TotalInputTokens?`/`TotalOutputTokens?`/`TotalTokens?`/`KnownTokenExecutionCount`
    via a `SumKnown` helper (`ProviderExecutionReport.FromRecords`) that sums only known
    nullable values.
  - **Per-execution additive fields** (records/`Evidence`): `InputTokens?`,
    `OutputTokens?`, `RetryCount`, `RepairAttemptCount`, `RepairSucceeded?`,
    `FinishReason?`, `ResponseTruncated`, `ErrorCategory?` (provider-neutral, e.g.
    `Timeout`, `SchemaValidation`). Model carried by the existing `ProviderVersion`,
    stamped from `provider.Metadata.Version ?? string.Empty`.
  - **Run-level aggregates**: `ProvidersExecuted`, `TotalExecutions`, `Failures`,
    `SuccessfulExecutionCount`, `EvidenceCount`, `ObservationsProduced`,
    `ContextFilesSelected`, `PartialProviderCount`, token totals + known-token count,
    `TotalRetries`, `TotalRepairAttempts`, `SuccessfulRepairs`, `TimeoutCount`,
    `TotalDuration`, `TotalCost?`, `Plan`, `ByProvider`, `Records`.
  - **Semantics**: repair max one (success `1`/`true`; failure `1`/`false` +
    `ErrorCategory="SchemaValidation"`); retries count transient+timeout retries only;
    truncation surfaced only from provider metadata (`response.Truncated`) and the
    validator treats it invalid. `EngineeringMetrics` untouched; M11.3 context fields
    and the pipeline's per-step observation aggregation preserved.
- Verified: build **0/0**, **223/223 tests** (20 new in
  `src/EngineeringCouncil.Tests/ProviderMetricsTests.cs`), consumer-DTO gate green,
  `engineering-review-package.json` unchanged (`schemaVersion 1.1`, no secret leakage);
  offline Mock smoke `20260807-225926-50a23e`: `provider-execution.json` populated
  (`totalExecutions: 7, successfulExecutionCount: 7, failures: 0, contextFilesSelected: 537,
  knownTokenExecutionCount: 0, totalRetries: 0, totalRepairAttempts: 0, successfulRepairs: 0,
  timeoutCount: 0, totalDuration: 00:00:00.13`, per-record `providerVersion:
  mock-observer-v2`, `contextFilesConsidered: 254`). See MILESTONE-012.1, ADR-017.

## Previous state (v12.3 — 2026-08-07, Milestone 011.3)

- **Context budget correctness** (ADR-016) — exactly two corrective fixes; no capability
  added, no contract change (`schemaVersion` stays **1.1**):
  - **C4 — selection budgets effective size, matches the rendered context**
    (`Core/Analysis/ContextContentPolicy`): ONE authoritative rule
    `EffectiveContextCharacters(file) = min(actualLength, perFileLimit)` now drives BOTH
    `RuleBasedAnalysisContextSelector` (character budget) and `RepositoryContextBuilder`
    (per-file truncation at the same limit). `EstimatedContentSize`/`ContextCharacterCount`
    equal what the renderer actually produces; a file never counts more than it will
    render. New `Evidence:Context:MaximumCharactersPerFile` (default 8000), wired through
    `EvidenceOptions`/`CouncilOptions` and parsed by CLI + API; a single DI-registered
    policy is shared by selector and renderer.
  - **C6 — `ContextFilesConsidered` is the repository scope, not a per-step max**
    (`ProviderExecutionRecord` + `AcquisitionCoverage`): each execution record now stamps
    `ContextFilesConsidered = selection.TotalRepositoryFiles` (the files the selector
    ranks before limits; constant across steps). The coverage rollup reports that scope
    instead of `Max(ContextFileCount)`; `ContextFilesSelected` stays the sum of per-step
    selections.
- Verified: build **0/0**, **203/203 tests** (4 new), consumer-DTO gate green,
  `engineering-review-package.json` unchanged (`schemaVersion 1.1`); offline Mock smoke
  `contextFilesConsidered=7` (repo scope) vs `contextFilesSelected=26` (sum of 7 steps).
  See MILESTONE-011.3, ADR-016.

## Previous state (v12.2 — 2026-08-07, Milestone 011.2)

- **Domain & metrics correctness** (ADR-015) — exactly three corrective fixes; no
  capability added, no contract change (`schemaVersion` stays **1.1**; new fields are
  additive):
  - **A4 — provider metrics are mutually exclusive** (`EngineeringMetrics`): providers
    with ≥1 execution are exactly one of **Successful** (all executions succeeded),
    **Partial** (`PartialProviders`, ≥1 success AND ≥1 failure), or **Failed** (all
    failed) — classified by execution outcome, never by evidence count; zero executions
    is never counted. `SuccessfulProviders`/`FailedProviders` retained for compatibility.
    Markdown shows `Successful / partial / failed providers`.
  - **A5 — unknown discipline is never CodeQuality**: `StructuredLlmEvidenceInterpreter`
    maps an unrecognized discipline string to `FindingCategory.Unknown`; the
    `EngineeringObservation.Discipline` / `Finding.Category` defaults are `Unknown`. No
    Unknown analyzer; unknown observations stay persisted, provenance-preserved,
    unconsumed by any analyzer. New additive `UnclaimedObservationCount`
    (interpretation summary) → `EvidenceSummary.UnclaimedObservations` and
    `EngineeringMetrics.UnclaimedObservations` (count of `Unknown` observations).
  - **A6 — shared analyzer-registration validation, fail fast**: new
    `AnalyzerDisciplineValidator` (`Core.Application`) + `UnsupportedDisciplineException`
    report ALL unsupported disciplines (e.g. `Requested discipline 'Performance' has no
    registered analyzer.`). `AnalysisPipeline.RunAsync` validates the effective set
    (`request ?? config`) before the scan and refuses zero registered analyzers; the CLI
    exits **1** before analysis; the API returns **400** for unsupported request and
    config-driven disciplines. An empty effective discipline set can never produce an
    Excellent "no findings" run.
- Verified: build **0/0**, **199/199 tests** (17 new), consumer-DTO gate green,
  `engineering-review-package.json` unchanged (`schemaVersion 1.1`) with additive
  metric fields; offline Mock smoke disjoint (`successful=1 partial=0 failed=0
  unclaimed=0`); CLI `--disciplines Performance` fails fast (exit 1, no output). See
  MILESTONE-011.2, ADR-015.

## Previous state (v12.1 — 2026-08-07, Milestone 011.1)

- **Execution reliability & security hardening** (ADR-014) — exactly three corrective
  fixes; no capability added, no contract change:
  - **A1 — LLM timeout vs cancellation** (`LlmEvidenceProvider.SendWithRetryAsync`):
    token-ownership classification. A failure the transport reports as `Cancelled` is a
    provider **timeout** unless the run token is genuinely cancelled (never message-text
    inspection) — so real-provider timeouts are retried per `MaxRetries` and, once
    exhausted, surface as `LlmErrorCategory.Timeout`; run/user cancellation propagates
    and is never retried. Telemetry logs timeout (warning) vs cancellation
    (information) distinctly.
  - **A2 — cancellation stops the pipeline** (`AnalysisPipeline.RunAsync` + API):
    `OperationCanceledException` (run token cancelled) is caught **before** the generic
    failure handler, the run is marked `AnalysisRunStatus.Cancelled` (new additive enum
    value), and the exception rethrows — no reconciliation, no package build, no
    persistence, never an ordinary `Failed` run. API `POST /runs` returns
    `499 Client Closed Request`, never HTTP 200, for a cancelled run.
  - **A3 — runId path traversal** (`AnalysisRunId` + `FileSystemAnalysisRunRepository` +
    API): one source of truth for the generated run-id format
    (`yyyyMMdd-HHmmss-<6 hex>`); strict format validation plus `Path.GetFullPath`
    resolution verified to stay under `OutputsRoot` (defense in depth). `GetAsync`
    returns not-found for invalid/escaping ids (never throws, never touches the
    filesystem); `SaveAsync` rejects them; `GET /runs/{runId}` returns 400 (malformed) /
    404 (unknown).
- Verified: build **0/0**, **182/182 tests** (25 new), consumer-DTO gate green,
  `engineering-review-package.json` unchanged (`schemaVersion 1.1`). See
  MILESTONE-011.1, ADR-014.

## Previous state (v12 — 2026-07-24, Milestone 012)

- **Evaluation + calibration harness** (ADR-013): measures the existing platform on real
  repositories. No new analysis capability, no councils, no ranking. `engineering-review-
  package.json` is untouched (still `schemaVersion 1.1`); the consumer-DTO gate is unchanged.
- **Additive diagnostic instrumentation:** `AnalysisRun.StageTimings` (`RunStageTimings`,
  per-stage wall-clock, `SlowestStage`) and `ObservationInterpretationSummary.
  InvalidFileReferencesDropped`. Neither is in the package; neither changes analysis.
- **`EngineeringCouncil.Infrastructure.Evaluation`:** `EvaluationMetricsCollector` (pure,
  read-only — collecting metrics never mutates the run or package), `EvaluationRunner`
  (runs the existing pipeline per case; records failures, never throws; per-provider
  comparison on identical inputs), `EvaluationReportExporter` (internal `evaluation-
  report.md`; measurements only, never a winner/rank/weight — test-guarded), `ModelPricing`
  (optional config-only cost; absent pricing/usage ⇒ cost unavailable, never invented).
- **Metrics:** operational timings + provider latency, context selection (selected/omitted
  files, chars, repeated sends, truncations), prompt calibration (schema failures, invalid
  %, repair success, hallucinated refs, missing locations, unsupported types), reconciliation
  (duplicate reduction %, agreement distribution, contradictions, false-merge candidates),
  quality distributions, usage/cost, package validation (size, serialize time, determinism,
  root props, no type leaks).
- **Dataset** `evaluation/dataset/` — 5 deterministic self-authored fixtures w/ documented
  `evaluation.json`: `01-clean-service` (false-positive control), `02-vulnerable-payments`
  (Security + bundled `results.sarif`), `03-tangled-architecture`, `04-fragile-reliability`,
  `05-undocumented-untested`. Fixture defect code is scanned as text, never compiled/run.
- **CLI `evaluate` verb:** `--dataset --outputs --providers --compare (repeatable, per-
  provider identical inputs) --disciplines/--discipline`. Committed sample report at
  `evaluation/evaluation-report.md`.
- Verified: build 0/0, **157/157 tests** (no keys/network), offline evaluation over the
  5-repo dataset produced `evaluation-report.md`; package contract intact. See
  MILESTONE-012, `evaluation/README.md`.

## Previous state (v11 — 2026-07-23, Milestone 011)

- **Real LLM evidence providers** (ADR-012): `ClaudeEvidenceProvider` (Anthropic Messages
  API) and `OpenAiEvidenceProvider` (Chat Completions) as ordinary discipline-scoped
  `IEvidenceProvider` adapters sharing one base `LlmEvidenceProvider` — **no
  provider-name branching anywhere**. The `Codex` stub is retired: a code-focused model
  is just `Evidence:OpenAI:Model` (`Codex` remains a config alias for `OpenAI`).
- **Transport seams** `IClaudeClient` / `IOpenAiClient` isolate all HTTP; the entire unit
  suite runs on `ScriptedLlmClient` fakes — no network, credentials, or paid calls.
- **One shared prompt** (`IEvidencePromptBuilder` → `DisciplineEvidencePromptBuilder`;
  system instruction = "evidence acquisition source, NOT the reviewer") and **one
  versioned schema** `{schemaVersion:"1.0", discipline, observations[]}`, compatible with
  the existing `StructuredLlmEvidenceInterpreter`. Providers get ONLY the selected context
  (no file-system access, no rescanning).
- **Validation before interpretation**: conservative extraction (direct JSON or exactly
  one fenced block; multiple documents / unbalanced / prose-heavy rejected) + schema
  version, discipline match, title/severity/confidence/file-ref checks, truncation. At
  most ONE repair attempt. Retries only for transient categories (rate limit, timeout,
  network, 5xx) with bounded backoff; auth/invalid-request/unsupported-model/schema are
  never retried. Per-request timeout + cancellation propagation.
- **Disabled by default**; explicit selection opts in. A selected provider without a key
  is NOT silently swapped for Mock — it reports `Claude requires the environment variable
  ANTHROPIC_API_KEY.` `Evidence:ProviderFailureMode` = `Continue` (default) | `FailRun`.
  Unconfigured runs still default to offline Mock.
- **Telemetry** (provider-neutral, secret-safe): provider, model, discipline, retryCount,
  repairAttempted, finishReason, truncated, context file/char counts, and token usage /
  response+request ids **only when reported** (absent, never invented). Cost is optional
  and never hardcoded.
- **CLI**: `--provider` repeatable; `--discipline` repeatable added alongside
  `--disciplines a,b`; pre-flight categorized provider-configuration errors.
- Package unchanged at **schemaVersion 1.1** — no consumer-visible field changed; no
  provider-specific final report exists. Verified: build 0/0, **150/150 tests** without
  credentials, and scenario C (`Claude+OpenAI+Sarif`, 2 disciplines) = **5 provider
  executions** with failures isolated. See MILESTONE-011,
  `docs/provider-configuration.md`, `docs/external-provider-data-boundary.md`.

## Previous state (v10 — 2026-07-22, Milestone 010)

- **Deterministic multi-source reconciliation** (ADR-011). Raw analyzer findings from
  heterogeneous sources (Claude/Codex/SARIF/…) are consolidated into ONE finding set
  **before** the package builder, so `engineering-review-package.json` stays the single
  stable integration artifact for the external Engineering Review app.
- **`IFindingReconciler` / `RuleBasedFindingReconciler`**: staged, explicit grouping
  (1 exact-rule-location · 2 type-symbol · 3 title-location · 4 keep-separate; same
  discipline required, first match wins). No LLM/voting/weights. Prefers false negatives
  to incorrect merges. `TitleNormalizer` = deterministic (provider-prefix + path strip,
  explicit synonym dict, Jaccard); no embeddings.
- **Agreement** counts distinct PROVIDERS, not findings. **Severity** keeps highest +
  records `SeverityRange` (agreement never inflates severity). **Confidence** starts at
  highest source; +1 for ≥2 providers, +2 for deterministic+LLM; capped High; location
  disagreement→Medium; discipline-mismatch→−1; never averaged. **Contradictions**
  (severity spread ≥2 or disjoint files) are flagged, not silently merged.
- **Stable ids**: SHA-256 of discipline+primaryRule+primaryFile+primarySymbol+
  normalizedTitle → `SEC-…`, order-independent (collisions get a numeric suffix).
- **`Finding` (additive)**: SupportingFindingIds, AgreementCount, Severity/ConfidenceRange,
  ReconciliationReason/Strategy, IsConsolidated, HasContradiction/ContradictionReasons,
  Symbol/LineReferences, Metadata. **`AnalysisRun`**: +ReconciliationSummary/Groups
  (`Findings`=consolidated, `RawFindings` preserved). Pipeline consolidates via the
  reconciler; `IFindingMerger` kept (registered+tested) for backward compat, off the pipeline.
- **Package (additive)**: `SchemaVersion "1.1"` (legacy `Version "1.0"` kept),
  provider-neutral `Reconciliation` summary, `Appendix.ReconciliationGroups`. Serialization
  is camelCase, string enums, no leaked .NET types, deterministic. `findings.json`=
  consolidated, `raw-findings.json`=raw; both stay diagnostic — external app consumes only
  the package.
- **Markdown**: consolidated-only findings + `## Multi-Source Reconciliation` section;
  per-finding provider support/agreement/severity range.
- **Integration**: consumer DTO `PackageContract` + gate test deserializes the package;
  sample at `artifacts/samples/engineering-review-package.sample.json`.
- Verified: build 0/0, **112/112 tests**, `review --provider Mock --sarif ./artifacts/results.sarif`
  → raw findings reconciled, package+markdown carry the reconciliation summary, schemaVersion
  1.1, a Mock+SARIF pair consolidates across providers. See MILESTONE-010.

## Previous state (v9 — 2026-07-21, Milestone 009)

- **Native SARIF evidence source** (ADR-010) — the first real deterministic source,
  proving heterogeneous sources plug into the pipeline **without touching analyzers,
  interpreters, or the package**.
- **`SarifEvidenceProvider`** (Static, Repository-scoped): imports existing SARIF 2.1.0
  files (no scanner execution); **one `Evidence` per run**, preserving tool/version/
  invocation/artifact+result counts/original payload. Skips malformed/missing files.
- **`SarifEvidenceInterpreter`** is the ONLY SARIF-aware component: each `result` → one
  observation with deterministic mappings in `SarifMappings` (severity error→Critical/
  warning→High/note→Medium/none→Low; discipline via keyword scan of id+name+desc+tags,
  else `Unknown`; type else `Unknown`; confidence 0.95 default, reduced per concrete
  deficiency). File/line/**column** refs, snippet excerpt (repo files not re-opened),
  rule metadata (RuleId/Name/Description/HelpUri/Tags/Properties) in `Metadata`.
- **Provider contract**: `IEvidenceProvider.CollectAsync` → `IReadOnlyList<Evidence>`
  (one repo step can yield one-evidence-per-run); executor stamps each + `EvidenceCount=n`.
- **Domain**: `FindingCategory.Unknown`, `ObservationTypes.Unknown`,
  `EngineeringObservation.ColumnReferences`, `AnalysisRun.Evidence`,
  `StaticAnalysisSource` + `EngineeringReviewPackage.StaticAnalysisSources`.
- **Package/markdown**: `## Static Analysis Sources` appendix (Tool/Version/Results
  Imported/Observations Generated); no raw SARIF dumped. `observations.json` identifies
  provider/ruleId/file/line/tool/version/sourceEvidenceId.
- **Config/CLI/API**: `Evidence:Sarif:{Enabled,Files}`; CLI `--sarif <file>` (repeatable),
  auto-included when active. Sample at `artifacts/results.sarif`.
- Verified: build 0/0, **88/88 tests**, `review --provider Sarif --sarif ./artifacts/results.sarif`
  → scan once, SARIF once, 3 results → Security/Reliability/CodeQuality findings via the
  existing analyzers, appendix rendered, full Finding→Observation→Evidence→rule trace.
  See MILESTONE-009. Everything below still holds.

## Previous state (v8 — 2026-07-16, Milestone 008)

- **Discipline-aware evidence acquisition** (ADR-009). Two scopes:
  **Repository** (static sources — Sonar/SARIF/Roslyn/Git/coverage — run once) and
  **Discipline** (LLM sources run once per discipline with a focused prompt +
  discipline-selected context). Each provider declares an `EvidenceProviderMetadata`
  (scope, supported disciplines); config `Evidence:Providers` accepts `{Name,Scope}`
  overrides; CLI `--disciplines a,b` (default all). LLM sources (Claude, Mock) are
  Discipline-scoped.
- Flow: scan → **`IEvidenceAcquisitionPlanner`** builds an explicit deterministic
  **`EvidenceAcquisitionPlan`** from an `AnalysisRunConfiguration` + the resolved
  providers (steps = provider×scope×discipline, branching only on metadata, never
  provider names; unsupported provider/discipline combos create no step and land in
  `Plan.UnsupportedCombinations`) → **`IEvidenceAcquisitionExecutor`** runs each step
  (**`IAnalysisContextSelector`** picks discipline-relevant files; provider
  `CollectAsync`; stamps provenance; isolates failures) → interpret → observations →
  analyzers → merge → summary → package.
- **`EvidenceProviderMetadata`** is the provider's only identity: `Name`,
  `ProviderType`, `DefaultAcquisitionScope`, `SupportedDisciplines`,
  `RequiresAnalyzerInstructions`, `SupportsRepositoryWideAnalysis`, `Version`
  (`IEvidenceProvider` no longer has `Name`/`ProviderType`).
- **`EvidenceRequest`** is first-class (RunId/RepositorySnapshot/Scope/Discipline/
  Instructions/ContextSelection/CorrelationId). **`AnalysisContextSelection`** is rich
  (Strategy/Files/TotalRepositoryFiles/SelectedFileCount/EstimatedContentSize/
  SelectionReasons); the selector honors `Evidence:Context:{MaximumFiles,
  MaximumCharacters}`, including whole files until a budget is exceeded and recording
  exclusions. Focused prompts restored via `DisciplinePrompts` (Core.Analysis; shared
  constraints treat repo content as untrusted). Observation + Evidence carry
  AcquisitionScope, RequestedDiscipline, correlation id, **AcquisitionStepId**,
  ContextFileCount, ContextSelectionStrategy → full trace.
- **Discipline-mismatch rule**: a discipline-scoped observation for a *different*
  discipline is not rewritten — confidence lowered + `discipline-mismatch` tag +
  counted in the interpretation summary.
- **Executor owns telemetry**: `ExecuteAsync(plan, repo, ct)` returns
  `EvidenceAcquisitionResult { Evidence, Executions, Plan }` — no
  `IProviderExecutionCollector`, no process-global state, structural run isolation.
  `ProviderExecutionRecord` extended (RunId, StepId, ProviderType, scope, discipline,
  correlationId, contextFile/CharacterCount, evidenceCount, observationsProduced).
- **`AcquisitionCoverage`** (provider-agnostic) on the package: repo-wide vs
  discipline sources executed, disciplines requested/covered, steps, failed steps,
  context files selected, unsupported combos.
- New repository-scoped provider stubs: SARIF, Git, Coverage (`IsAvailable=false`).
- Outputs unchanged (7 files): the plan + context summary ride inside
  `provider-execution.json`, `run.json`, `engineering-review-package.json`; review doc
  gains an **Evidence Acquisition Coverage** appendix.
- Verified: build 0 errors, **66/66 tests**, `review --provider Mock` → 7 discipline
  steps → 7 findings; `--disciplines Architecture,Security` → scan once, Mock executes
  exactly twice → 2 findings. See MILESTONE-008. Everything below still holds.

## Prior state (v7 — 2026-07-16, Milestone 007)

- **Normalized `EngineeringObservation` layer** (ADR-008) sits between evidence
  and findings. Flow: scan → **acquire evidence once (central)** → **interpret →
  observations** → analyzers reason over observations → findings → merge →
  summary → package.
- **Interpreters** convert provider-specific `Evidence` → `EngineeringObservation`:
  `IEvidenceInterpreter` (+ `StructuredLlmEvidenceInterpreter` for Mock/Claude),
  `IEvidenceInterpreterResolver` (discovery via `CanInterpret`, no name switch),
  `IEvidenceInterpretationPipeline` (isolates failures, records unsupported,
  telemetry via `ObservationInterpretationSummary`). Future interpreters (SARIF,
  Sonar, Roslyn, Semgrep, NDepend, Git, coverage) are unregistered non-throwing
  skeletons.
- **Analyzers** (`ObservationBasedAnalyzer` + 7 thin subclasses) consume
  `IReadOnlyList<EngineeringObservation>` via `IAnalyzerAgent.AnalyzeAsync(obs,
  ctx)`, select their discipline, correlate by type, and emit findings with
  provenance (`ObservationIds`, `SourceRules`, `SupportingProviders`,
  `SupportingObservationCount`). Full trace: Finding→Observation→Evidence→provider.
- `Evidence.Id` added; `MockEvidenceProvider` emits an `observations` envelope
  (not findings). `FindingsNormalizer` removed (parsing moved into the interpreter;
  `ExtractJson`→`Core.Serialization.JsonExtraction`). `RepositoryContextBuilder` +
  collection options moved to `Core.Analysis`.
- Artifacts per run: `engineering-review-package.json`, `engineering-review.md`,
  **`observations.json`**, `findings.json`, `raw-findings.json`,
  `provider-execution.json`, `run.json`. Review doc gained an **Observation and
  Evidence Traceability** appendix.
- Verified: build 0/0, **48/48 tests**, `review` → 7 observations → 7 findings,
  each traced to OBS-xxx/rule/provider. See MILESTONE-007. Everything below holds.
- Env note: the temp NuGet cache is flaky (MSB3030 "file not found" on satellite/
  testhost DLLs). Fix: `Directory.Build.props` sets `SatelliteResourceLanguages=en`;
  reinstall the named package + `dotnet restore --force` if it recurs.

## Prior state (v6 — 2026-07-07, Milestone 006)

- **Engineering Review Package is the primary deliverable** (ADR-007):
  `EngineeringReviewPackage` (Version 1.0; Repository/Branch/Commit, run
  provenance, ExecutiveSummary, OverallEngineeringHealth, OverallRisk,
  KeyStrengths/Risks, RecommendedNextActions, Findings, CouncilSummary,
  ProviderExecution, EvidenceSummary, Metrics, Appendix). Built by
  `IEngineeringReviewPackageBuilder` (no exporter logic). `EngineeringHealth`
  (Excellent…Critical) + `EngineeringRisk` (Low…Critical) via documented
  rule-based `HealthRiskScorer` (thresholds in ADR-007). `EngineeringMetrics` +
  `EvidenceSummary` are projections. Git branch/commit via read-only `GitProbe`.
- Exporters project the package: `EngineeringReviewMarkdownExporter` (was
  `MarkdownReportGenerator`) renders `engineering-review.md`;
  `JsonReportGenerator.RenderPackage` → `engineering-review-package.json`.
- Artifacts per run: `engineering-review-package.json` + `engineering-review.md`
  (primary), `findings.json`, `raw-findings.json`, `provider-execution.json`,
  `run.json`. CLI verb **`review`** surfaces the package.
- Verified: build 0/0, 41/41 tests, `review` → Health Good / Risk Low on the
  mock run. See MILESTONE-006. Everything below still holds.

## Prior state (v5 — Milestone 005)

- 7-project solution (`EngineeringCouncil.slnx`), TFM `net10.0`.
- Read-only scanner ignoring `bin, obj, .git, node_modules, packages,
  artifacts, .vs`.
- Domain: `AnalysisRun` (`RawFindings`, `Summary`, **`ProviderExecution`**),
  `Finding` (`Status`, `SourceAgents`, rationales, **`EvidenceProvider`**),
  `Evidence` (+ `ProviderId`, `ProviderVersion`, `Success`, `ErrorMessage`,
  `Duration`, `CostEstimate`), `EvidenceProviderType`, **`ProviderExecutionReport`/
  `Record`/`Summary`**, `CouncilSummary`, `FindingStatus`, enums.
- Interfaces: `IAnalyzerAgent`, `IRepositoryScanner`, `IMarkdownReportGenerator`,
  `IJsonReportGenerator`, `IAnalysisRunRepository`, `IFindingMerger`,
  `ICouncilSummaryGenerator`, `IEvidenceProvider`, `IEvidenceProviderFactory`,
  **`IEvidenceExecutor`**, **`IProviderExecutionLog`**.
- **Multi-provider execution (v5):** analyzers depend on **`IEvidenceExecutor`**
  (not the factory). The executor runs ALL configured providers
  (`Evidence:Providers`) for each analyzer request, isolates failures/timeouts as
  failed `Evidence`, and returns `IReadOnlyList<Evidence>`. The analyzer
  interprets each evidence into findings, stamping `Finding.EvidenceProvider`.
  Sequential; factored for a one-method switch to `Task.WhenAll`. No voting/
  consensus. `IProviderExecutionLog` → `provider-execution.json`.
- **Evidence Provider layer (v4):** `ClaudeEvidenceProvider` (LLM — the ONLY place
  Microsoft Agent Framework lives), `MockEvidenceProvider` (offline default), 6
  `NotImplementedException` stubs. Resolved by name via `EvidenceProviderFactory`.
- Config: `Evidence:Providers` (default `["Claude"]`) + CLI `--provider` /
  `--providers a,b`; `Claude` falls back to `Mock` without an API key.
- **Consolidation layer (v3):** `RuleBasedFindingMerger` (now guards: **no
  cross-provider merge**) + `RuleBasedCouncilSummaryGenerator`.
- **`AnalysisPipeline`**: scan → analyze (orchestrator, `RAW-NNN` ids) → store
  raw → merge → council summary → attach provider-execution report → persist.
- Artifacts per run: `raw-findings.json`, `findings.json`,
  **`provider-execution.json`**, `findings.md`, `council-summary.md`,
  `run-summary.md`, `run.json`.
- Verified: build clean (0/0), 28/28 tests pass, CLI `--providers Mock,Codex`
  → 14 executions (7 ok, 7 failed), 7 findings, 7 artifacts. See MILESTONE-005.

### History
- v1 (M001): single generic `AnalyzerAgent` (ADR-003 superseded it).
- v2 (M002): 7 specialized analyzers + orchestrator (ADR-003).
- v3 (M003): finding merger + council summary (ADR-004).
- v4 (M004): evidence provider layer; Agent Framework isolated (ADR-005).

## Key design decisions

- **Discipline-aware acquisition + explicit plan** (ADR-009): repository-wide
  static sources run once; LLM sources run per discipline with focused prompts +
  context selection. An explicit deterministic plan (branching on provider
  metadata, not names) drives topology; analyzers stay observation-only; telemetry
  is per-run (no global singleton). Provenance is traceable end to end.
- **Normalized observation layer** (ADR-008): analyzers reason over
  `EngineeringObservation`s, not raw evidence. Interpreters localize all
  provider-format knowledge (Evidence→Observation); a new source = one interpreter
  + registration, zero analyzer changes. Boundaries: Evidence (raw) → Observation
  (normalized) → Finding (conclusion) → Package (deliverable); full provenance
  preserved end to end.
- **Engineering Review Package is the product** (ADR-007): one versioned domain
  object (`EngineeringReviewPackage`) every representation renders; Markdown/JSON
  (and future HTML/PDF/dashboard) are projections. Health/risk are documented
  rule-based scores (`HealthRiskScorer`). The builder assembles; exporters only
  format. Future capabilities enrich the same package.
- **Consolidation layer, rule-based first** (ADR-004): `IFindingMerger` +
  `ICouncilSummaryGenerator` sit between analyzers and reporters. v3 uses
  deterministic rule-based impls (no LLM) — the exact seam an LLM reconciler
  will later drop into. `findings.json` = consolidated; `raw-findings.json` =
  audit trail. Still **not** a multi-LLM council.
- **Multi-provider execution** (ADR-006): analyzers depend on `IEvidenceExecutor`,
  which runs ALL configured providers per request and returns
  `IReadOnlyList<Evidence>`; the analyzer interprets each. Failures/timeouts are
  isolated (failed `Evidence`), never stopping others. Findings keep
  `EvidenceProvider`; the merger does not merge across providers. Sequential now
  (one-method switch to parallel). No voting/consensus.
- **Evidence Provider layer** (ADR-005): analyzers reason over `Evidence`, not
  raw LLM output; providers return evidence, never findings. Any source
  (LLM / static analyzer / SCM / docs / tool) fits `IEvidenceProvider`.
  **Microsoft Agent Framework lives only inside `ClaudeEvidenceProvider`** —
  `EngineeringCouncil.Agent` no longer references it.
- **Specialized analyzers, not one generic reviewer** (ADR-003): 7 discipline
  agents on a shared `EvidenceBasedAnalyzer` base; `AnalysisOrchestrator`
  discovers them via `IEnumerable<IAnalyzerAgent>` and aggregates (assigning
  `RAW-NNN` ids). Analyzers return only findings; reporting is in dedicated exporters.
- The `FindingsNormalizer` **drops file references not in the snapshot** — the
  code guardrail enforcing "do not invent files".
- Mock fallback whenever the LLM provider has no API key, so nothing external is
  required to run. Provider chosen by `Evidence:DefaultProvider` / `--provider`.

## Package/version pins (central — `Directory.Packages.props`)

- `Microsoft.Agents.AI` 1.13.0 · `Microsoft.Extensions.AI(.OpenAI)` 10.7.0
- `Microsoft.Extensions.*` 10.0.9 · `Aspire.Hosting.AppHost` 13.4.6
- Correct API: `IChatClient.AsAIAgent(...)` → `AIAgent`; `AIAgent.RunAsync(string)`
  returns `AgentResponse` (`.Text`). (`CreateAIAgent`/`AgentRunResponse` do **not**
  exist in this version — a common trap.)

## Roadmap (next milestones)

All future capabilities **enrich the `EngineeringReviewPackage`** (ADR-007) and
consume the normalized `EngineeringObservation` language (ADR-008):

1. **Real interpreters + providers** — implement the skeleton interpreters (SARIF,
   Sonar, Roslyn, Semgrep, NDepend, Git, coverage) and their repository-scoped
   providers; wire a real Claude client.
2. **Provider comparison / voting / consensus** — reason across per-provider,
   per-scope observations (repository-wide vs discipline) — the Engineering Council.
3. **Recommendation engine / ticket generation** — enrich the package backlog.
4. **More exporters** — HTML / PDF / Dashboard projections of the package.
5. **LLM-based reconciler** — semantic merger behind `IFindingMerger`.
6. **Parallel execution** — orchestrator, executor, and interpretation pipeline are
   all shaped for a `Task.WhenAll` drop-in.

## How to run (quick)

```
dotnet build EngineeringCouncil.slnx
dotnet run --project src/EngineeringCouncil.Cli -- analyze --path <target> --provider Mock
```
