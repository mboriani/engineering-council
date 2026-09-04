# MILESTONE-015.3C — Repository Snapshot Reproducibility

Date: 2026-08-19 · Decision: `D-107` · No ADR (identity metadata + read-only VCS probe; no architectural boundary change)

## Context

M15.2 exposed a reproducibility defect: Council finding **CQL-7fe95e8d97** could not be
reproduced against RedirectToService because the target working tree had changed after
the Council run — the run analyzed one repository state, later verification inspected
another. A real Engineering Review must identify the repository state it analyzed.

M15.3C makes every `AnalysisRun` carry a **deterministic description of the analyzed
source state** so "what exact source state produced this finding?" is always answerable —
without silently claiming a commit SHA represents the analyzed source when the working
tree differs from that commit. This milestone is about **identification and
reproducibility metadata**; it deliberately does **not** build a backup/version-control
system, immutable copies, or transactional snapshot isolation.

## What changed

### Repository snapshot identity (the core of the milestone)

New provider-neutral model `RepositorySnapshotIdentity` (`Core.Domain`):

| Field | Semantics |
|-------|-----------|
| `VersionControl` | `"git"` when positively identified (git work tree, or a `.git` directory when the executable is unavailable); null otherwise. |
| `CommitSha` | Full HEAD SHA (Git); null for non-Git / git unavailable / unborn HEAD. |
| `Branch` | Current branch; null for detached HEAD or non-Git. |
| `IsDirty` | Tracked working-tree/index differs from HEAD; `false` when verified clean; null when unavailable. |
| `HasUntrackedFiles` | Untracked files exist in the analyzed selection; `false` when verified absent; null when unavailable. |
| `SnapshotFingerprint` | Deterministic identity of the analyzed working state (see below). |
| `RepositoryChangedDuringRun` | True when the single end-of-acquisition fingerprint check differs from the start capture. |

It is **additive** to `AnalysisRun` (`RepositoryIdentity`) and to the external package
(`repositorySnapshot`), keeping `schemaVersion` **1.1** per the established additive-field
policy.

### SnapshotFingerprint

`SnapshotFingerprint.Compute(root, scannedFiles)` (`Core.Analysis`) is a deterministic
identity of the **actual analyzed working state**, not merely HEAD:

- Derived from the **SAME repository file selection the scanner already produces** — the
  post-ignore-rule set. `ScanOptions.IgnoredDirectories` (bin/obj/.git/node_modules/
  packages/artifacts/.vs) already excludes build/vendor/IDE state, so there is **no second
  independent definition** of "repository files" for snapshotting.
- For every selected file: relative path + SHA-256 of the file's raw bytes, accumulated in
  **Ordinal order**, then SHA-256 of the aggregate → `SNAP-<12 hex>`.
- Relative paths only (never absolute) — identical source trees on different machines
  produce the same value. No timestamps, no randomness, no source contents (only hashes).
- Dirty tracked changes **and** relevant untracked files change the fingerprint — a commit
  SHA alone is never sufficient identity for a dirty tree.
- An unreadable file contributes a stable `unreadable` marker (never content).

`ContextFingerprint` is deliberately **not** repurposed: it identifies the effective
context sent to a provider (its documented semantic). `SnapshotFingerprint` is a distinct,
repository-snapshot identity.

### Git metadata (read-only)

`GitSnapshotMetadataProvider` (`Infrastructure.Scanning`) invokes git via
`ProcessStartInfo.ArgumentList` (argv, `UseShellExecute = false` — the same safety rules as
the evidence process runners; **no shell command strings**). Only the milestone-prescribed
commands are used: `rev-parse --is-inside-work-tree`, `rev-parse HEAD`,
`symbolic-ref --short HEAD`, `status --porcelain`. It never runs
checkout/reset/stash/clean/add/commit. When git is unavailable or the target is non-Git,
the run **continues** — metadata becomes null and `SnapshotFingerprint` still works from
the analyzed files.

`IsDirty` = any porcelain line where the index or worktree column is non-blank (tracked
change); `HasUntrackedFiles` = any `??` entry. Ignored files are never reported (no
`--ignored`).

### Capture timing + mid-run mutation detection

- **Start capture** happens in `AnalysisPipeline.RunAsync` immediately after the scan and
  **before acquisition begins** — the run never calculates the fingerprint after providers
  finish, so a long run cannot silently change the analyzed identity.
- **End verification** runs **once** after acquisition: re-scan with the existing scanner
  (`LoadContent = false` — identical selection rules, no content bodies loaded), recompute
  the fingerprint, compare. `RepositoryChangedDuringRun = start != end`.
- A detected mutation **never fails the run** and **never replaces the start fingerprint** —
  the review describes the START state providers were intended to analyze, and the run
  warns that mutation occurred. No file watcher, no continuous monitoring.

### Artifacts

- `run.json` persists the full identity (`repositoryIdentity`) — operational run provenance.
- `engineering-review-package.json` gains an additive `repositorySnapshot` (CommitSha,
  Branch, IsDirty, HasUntrackedFiles, SnapshotFingerprint, RepositoryChangedDuringRun) so
  downstream consumers can answer "what source state does this review describe?". **No
  absolute local paths** are exposed (not part of the package contract). `schemaVersion`
  stays **1.1**; the consumer DTO gate (`PackageContract` + `RepositorySnapshotContract`)
  was updated and is green.
- `engineering-review.md` renders a compact **Repository State** block with a useful commit
  prefix; JSON preserves the full values.

## Files changed

| File | Change |
|------|--------|
| `src/EngineeringCouncil.Core/Domain/RepositorySnapshotIdentity.cs` | New provider-neutral identity model |
| `src/EngineeringCouncil.Core/Analysis/SnapshotFingerprint.cs` | Deterministic fingerprint (relative paths + content hashes, Ordinal order) |
| `src/EngineeringCouncil.Core/Abstractions/IRepositorySnapshotIdentityProvider.cs` | Capture + verify-once interface |
| `src/EngineeringCouncil.Infrastructure/Scanning/GitSnapshotMetadataProvider.cs` | Read-only argv git probe (rev-parse/symbolic-ref/status --porcelain) |
| `src/EngineeringCouncil.Infrastructure/Scanning/RepositorySnapshotIdentityProvider.cs` | Combines git metadata + fingerprint; end re-scan reuses the scanner |
| `src/EngineeringCouncil.Core/Domain/ScanOptions.cs` | `LoadContent` (default true) for the content-free identity re-scan |
| `src/EngineeringCouncil.Infrastructure/Scanning/FileSystemRepositoryScanner.cs` | Honors `LoadContent` (same selection either way) |
| `src/EngineeringCouncil.Core/Domain/AnalysisRun.cs` | `RepositoryIdentity` (additive) |
| `src/EngineeringCouncil.Core/Application/AnalysisPipeline.cs` | Start capture before acquisition + single end verification |
| `src/EngineeringCouncil.Core/Domain/EngineeringReviewPackage.cs` | `RepositorySnapshot` (additive) |
| `src/EngineeringCouncil.Core/Application/EngineeringReviewPackageBuilder.cs` | Maps the run identity into the package |
| `src/EngineeringCouncil.Infrastructure/Reporting/EngineeringReviewMarkdownExporter.cs` | Compact Repository State block |
| `src/EngineeringCouncil.Infrastructure/DependencyInjection/CouncilServiceCollectionExtensions.cs` | Registers `IRepositorySnapshotIdentityProvider` |
| `src/EngineeringCouncil.Tests/SnapshotFingerprintTests.cs` | **8 new offline tests** |
| `src/EngineeringCouncil.Tests/RepositorySnapshotIdentityTests.cs` | **16 new offline/git tests** |
| `src/EngineeringCouncil.Tests/PackageContractTests.cs` + `Contracts/EngineeringReviewPackageContract.cs` | Consumer DTO gate + serialization/privacy tests |

## Tests

Build 0/0 warnings/errors; full suite **566/566 tests** (541 existing + 25 new). Coverage
maps 1:1 to the milestone's required list:

deterministic fingerprint for identical files; file ordering invariance; absolute-location
invariance; content change → different; file addition → different; file deletion →
different; excluded bin/obj/etc does not affect the selection; same HEAD + dirty tracked
file → different fingerprint + `IsDirty=true`; same HEAD + relevant untracked file →
different fingerprint + `HasUntrackedFiles=true`; clean git metadata captured correctly;
dirty detected; untracked detected; detached HEAD → null branch + commit; non-Git repo
still gets a fingerprint; git unavailable does not fail analysis (bogus executable); git
unavailable with `.git` present still reports `versionControl: "git"`; start=end →
`RepositoryChangedDuringRun=false`; start≠end → `true` and the start fingerprint is
retained; `LoadContent=false` re-scan returns the same selection; package/consumer
serialization; Markdown provenance rendering (clean / dirty+changed / non-Git / absent).

## Live validation (2026-08-19)

### RedirectToService (read-only, offline Mock run, no LLM invoked)

Offline `review --providers Mock` run against
`C:\Repositories\Paramo\RedirectTo\redirect-to-service` (target untouched):

| Field | Value |
|-------|-------|
| `versionControl` | `git` |
| `commitSha` | `4a7d279aaa99898ea76128af12575b70431c401a` |
| `branch` | `master` |
| `isDirty` | `false` |
| `hasUntrackedFiles` | `false` |
| `snapshotFingerprint` | `SNAP-23d782a35393` |
| `repositoryChangedDuringRun` | `false` |

**Comparison with M15.3B:** the M15.3B identity was `master @ 4a7d279aaa99` (short). The
full SHA `4a7d279aaa99898ea76128af12575b70431c401a` matches that prefix and the working
tree is clean — **the analyzed state is unchanged since M15.3B**.

### Dirty/untracked fixture proof (deterministic local mutation, no LLM)

Temp fixture repo, same HEAD, offline Mock runs:

| State | HEAD | `isDirty` | `hasUntrackedFiles` | fingerprint |
|-------|------|:---:|:---:|-------------|
| clean | `7658a530…ca74ea` | false | false | `SNAP-2fa72dca3011` |
| tracked file modified | `7658a530…ca74ea` | **true** | false | **`SNAP-da40a9a59af4`** |
| + relevant untracked file | `7658a530…ca74ea` | true | **true** | **`SNAP-4b4acd4b18ca`** |

Same HEAD → content changed → fingerprint changed (both dirty-tracked and untracked paths).
The repository was only ever read by the pipeline; mutations were to the throwaway fixture.

## Exit criteria

| Criterion | Result |
|-----------|--------|
| Every run can identify the analyzed state with a deterministic `SnapshotFingerprint` | ✅ + regression-tested |
| Git HEAD alone is never presented as sufficient for dirty/untracked states | ✅ (`IsDirty`/`HasUntrackedFiles` + fingerprint change, regression-tested) |
| Dirty tracked changes affect the fingerprint | ✅ + regression-tested + live fixture |
| Relevant untracked files affect the fingerprint | ✅ + regression-tested + live fixture |
| Non-Git repos still work (fingerprint, null VCS fields) | ✅ + regression-tested |
| Start state captured BEFORE acquisition | ✅ (pipeline step 1b) |
| End state checked ONCE | ✅ (single post-acquisition verification) |
| Mid-run mutation surfaced without failing the run / without replacing start identity | ✅ + regression-tested |
| No transactional-isolation claim | ✅ documented as a scope boundary |
| `run.json` persists provenance | ✅ (`repositoryIdentity`) |
| External package decision explicit | ✅ additive `repositorySnapshot`, no paths, `schemaVersion` 1.1 |
| Markdown exposes compact provenance | ✅ Repository State block |
| No secrets / source contents persisted | ✅ hashes + identity only; git remote URLs out of scope |
| No provider invocation required for identity | ✅ git probe + file hashing only |
| Focused tests pass, build 0/0, full suite once | ✅ 25 new; 0/0; **566/566** |
| Docs updated | ✅ milestone, PROJECT_MEMORY (v34), DECISION_LOG (D-107), integration contract, RUNBOOK |
| No ADR (identity metadata, no architectural boundary change) | ✅ |

## Known limitations (documented, not fixed)

- Start/end fingerprint equality is **not** a transactional filesystem snapshot: if the
  repository changes while providers are actively reading it, different providers could
  theoretically observe different states. Start/end equality detects many mutations but is
  not proof of per-provider byte equality. A future milestone may introduce immutable
  analysis copies if real evidence shows they are required.
- The fingerprint hashes every selected file's bytes (plus the scan's own reads) — for very
  large repositories this doubles I/O; acceptable for identity and bounded by the existing
  ignore rules. Content bodies are not re-loaded at end verification.
- `git` executable unavailable ⇒ `CommitSha`/`Branch`/`IsDirty`/`HasUntrackedFiles` null
  (git remote URLs are never read/persisted; they may contain credentials and are out of
  scope). The fingerprint still works.
- Pre-M15.3C runs have no identity (`repositorySnapshot`/`repositoryIdentity` absent).

## Next step

M15.3D (not started per instruction).
