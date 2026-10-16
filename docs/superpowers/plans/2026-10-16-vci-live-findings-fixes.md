# VCI live findings fixes implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the demonstrated HMI mapping-path defect and investigate/fix reproducible export-cost and crash-recovery defects offline, then perform the subsequently authorized live follow-up.

**Architecture:** Retain the existing host/worker split and guarded operations. Resolve HMI mapping paths through verified project ownership when the native parent chain omits software. Investigate export cost with counting doubles and recovery with FakeWorker/session doubles before changing either behavior.

**Tech Stack:** C# net10.0 host/tests, net48 worker, netstandard2.0 contracts, Siemens V21 references/stubs, xunit and FakeWorker.

**Spec:** [VCI design](../specs/2026-10-15-vci-tools-design.md), its [implementation decisions](2026-10-15-vci-tools.md), and findings D1/O1/O2 in the [frozen-candidate live report](../acceptance/reports/2026-10-16-vci-tools-live-acceptance.md). The original instruction required the current checkout on `feat/vci-tools`, a commit per fix, and a stop before live testing. On 2026-10-16 the maintainer resumed work and explicitly authorized proceeding directly to live testing after implementation. Tasks 1–3 and their offline gates retain the boundaries below; live checks follow them and preserve the disposable fixture.

## Global Constraints

- During implementation and offline gates: no TIA/Openness attachment or live calls, installed-server launches, fixture mutations or tool reinstall. No push, PR or merge is authorized. Real-reference compilation and static API metadata/documentation inspection are offline.
- Stay on `feat/vci-tools` in the current checkout. Preserve unrelated work and the earlier plan's private acceptance/recovery material.
- Both roots use `contractVersion:"1.0"` (`VciContractVersion.Current`), are canonical JSON, and have no nested JSON strings.
- Counts remain **8/19/19**; no new public tool, operation, argument, status bucket, confirmation flow, timeout policy, or automatic retry.
- `list_mappings` preserves plain offset paging, raw optional status, excluded unit/system mappings, and null paths for gone/library objects. Ambiguous or unreadable live ownership must not become a fabricated selector.
- Compare live Openness identities with `object.Equals`; retain reference-based cycle guards. No persistent cache of native object handles or guessed HMI software names.
- `export`, `sync_to_workspace`, `delete_mapping`, `delete_workspace`, `create_workspace` run plain. Do not add export transactions or exclusive UI locks to improve timing.
- An export target file already exists on disk, or another mapping in the workspace uses the key: block/refuse without overwrite. Preserve current-state checks, case-insensitive relative file keys, path policy, and races tested by existing late-collision regressions.
- Planning never reads status. Keep leaf order, per-leaf breadcrumb before attempts, typed failed counts, fatal propagation, stop-at-first-failed-item and no replay/rollback across items.
- Recovery must prove the current attachment is empty before opening a destination after context loss; unknown state, another owner, or drift must still deny. A projectless identity must never be promoted to a verified project binding. Keep explicit target/force-rebind policy and pinned identity/revision checks.
- Use systematic debugging and TDD for behavior changes: record root-cause evidence, relevant RED, minimal fix, GREEN. Commit each fix with a conventional message after validation.
- Serialize builds/tests. Test doubles and FakeWorker are offline evidence only; retain the original live report's candidate-specific verdict and limits.

## Review Focus

- Missing HMI software parent: emitted mapping path must be a usable selector and match a valid scoped inventory.
- Foreign, duplicate, disposed or unreadable HMI ownership: no guessed path or partial-success concealment.
- Mapping/file collision introduced after planning or during directory creation: no stale cache may permit overwrite.
- Large exports: distinguish native-call/enumeration counts from wall-clock claims; preserve safety when reducing repeated work.
- Portal restart or owner change between preparation and execution: recovery may open only after fresh empty-state proof and must reject drift.

## Task 1: Normalize HMI mapping paths using verified project ownership

**Ownership/files:** worker `Openness/Vci/VciReader.cs`, `VciScopeResolver.cs`; tests `Vci/VciReaderTests.cs`, `VciScopeResolverTests.cs`; the smallest necessary topology support in `TestUtilities/HmiUnifiedSiemensDoubles.cs` or `VciSiemensDoubles.cs`. New files/project wiring only when existing seams cannot support the regression.

**Interfaces:** consumes `VciReader.ListMappings(VersionControlInterface, Project, VciQueryInfo)` and `VciScopeResolver.PathOf(IEngineeringObject)`/`Expand(ProjectBase, scope)`. Preserve public wire DTOs and existing non-HMI path behavior. Any project-aware internal helper must have explicit ownership and be reused by the reader rather than duplicate traversal.

- [ ] Inspect every caller of changed helpers and the test double's artificial parent assignment; identify why it masked the observed topology.
- [ ] Add an offline HMI mapping whose parent chain skips `HmiSoftware` while project/device/software discovery still owns the module. Assert unscoped output is `Device -> HmiSoftware -> ScriptModule`, a scoped query finds it, and the emitted selector resolves back to the same native identity.
- [ ] Cover same module names on different HMIs, equality across distinct native wrappers where feasible, ambiguous/unreadable ownership, gone/library paths and preserved excluded PLC mappings. Use existing focused coverage where it already answers a case.
- [ ] Observe the relevant failure, implement the smallest owner-proven path repair, and run focused reader/resolver tests followed by the full offline suite once before the fix commit.
- [ ] Self-review and commit `fix: normalize HMI VCI mapping paths`. Report exact commands/results, changed files, root-cause evidence and remaining live limits.

## Task 2: Investigate and reduce export enumeration cost without weakening collisions

**Ownership/files:** worker `Openness/Vci/VciWriter.cs` and only necessary planner/shared VCI helpers; tests `Vci/VciWriterTests.cs`, `VciPlannerTests.cs`, `TestUtilities/VciSiemensDoubles.cs`. Accommodate Task 1's changes in shared doubles.

**Interfaces:** consumes `VciWriter.Write`, relative `VciFileKey`, `Workspace.MappedObjects.Find` and current `ExportDirectory` checks. Preserve outcome/guard contracts and exact native behavior boundaries.

- [ ] Establish costs using counting offline doubles: object lookup, mapping enumerations/property reads, file/directory scans and native export calls as the workspace grows. Inspect actual installed API signatures or primary Siemens docs before assuming a native key lookup exists.
- [ ] Confirm a reproducible avoidable cost before proposing a fix. Add a deterministic regression on operation/enumeration growth; avoid timing thresholds or a benchmark app.
- [ ] Choose the smallest safe reduction supported by the API. Do not replace fresh collision checks with an unvalidated snapshot, count-only freshness check, or persistent object cache. Preserve the checks before and after directory creation, or demonstrate an equally strong replacement with late-state regressions.
- [ ] Retain/extend late mapping/file collision, duplicate key, already-mapped, refused leaf, fatal exception, breadcrumb and typed-count tests. Show RED/GREEN for any behavior/performance change and compare measured offline native-operation counts before/after.
- [ ] If the API forces a remaining scan for safety, record that evidence and the remaining complexity instead of claiming linear scaling or a live speedup. If no safe code change is demonstrated, report the investigation with supporting evidence rather than invent a fix.
- [ ] Run focused tests and the full offline suite once before each fix commit. Commit each proven fix separately, for example `fix: reduce VCI export mapping scans`.

## Task 3: Investigate and repair same-session recovery after Portal restart

**Ownership/files:** host `Worker/OpennessWorkerClient.cs`, `ProjectLifecycle/LifecycleBindingStrategy.cs`, relevant lifecycle binding/preparation code; contracts `Project/ProjectSessionBinding.cs` only if necessary; worker session/identity logic only with an evidenced root cause; FakeWorker `Program.cs`/scenarios and existing integration/session tests. Prefer `OpennessWorkerClientIntegrationTests`, `ProjectLifecyclePreviewSafetyTests`, `LifecycleIdentityContinuityTests`, `LifecycleSecondIdentityValidationContractTests`, and binding tests over a parallel harness.

**Interfaces:** consumes `RegroundInvalidatedSourceForOpenAsync`, existing empty-Portal recovery state, invalidated binding snapshots and lifecycle leases. Opening remains explicit and guarded; inspection never adopts or opens.

- [ ] Trace the observed old-session failure through lifecycle preparation and worker identity promotion. Reproduce an invalidated old Portal/project, a restarted new-PID empty Portal, and explicit `open_project(..., forceRebind:true)` in the same host with offline FakeWorker/session doubles.
- [ ] Add RED coverage showing recovery reaches the guarded opener only after a fresh empty observation. Test same-PID empty recovery, healthy source continuity, unknown/failed inventory, a foreign or local-session owner, concurrent owner/PID/generation/revision drift, and refusal without required force-rebind.
- [ ] Implement the smallest repair to the existing recovery seam; never clear a binding merely because status has `projectPath:null`, or weaken complete-project identity validation globally. Preserve read-only denial, no implicit adoption, lifecycle confirmation, audit and pinned checks.
- [ ] Demonstrate GREEN on recovery and adjacent identity/lifecycle tests, then the full offline suite once before the fix commit. Commit the confirmed fix, for example `fix: recover explicit opens after Portal restart`.
- [ ] Document proved causes and offline evidence, and distinguish investigation limits if the exact native behavior cannot be modeled. Do not replay the crash or open a real project.

## Final offline gate and handoff

- [ ] Run serial reference-stub and installed-real-reference solution builds; run the final full suite and meaningful changed-logic coverage. Do not repeat a suite already run on the identical final code without a new concern.
- [ ] Task reviewers receive the task brief, implementer report and BASE..HEAD review package; one spec+quality gate per task. Final whole-branch review uses the most capable available model and the branch merge-base package. Handle findings through the skill's bounded fix loops.
- [ ] Add a concise dated offline follow-up to the existing live report and update its index/history text. Preserve the original candidate's failed live verdict; record new fix commits, offline evidence and required live rechecks. No broad Task 14 user-documentation migration yet.
- [ ] Verify diff/privacy/whitespace and clean Git state, record all rulings and evidence, then perform the separately authorized live follow-up through the final real-reference built candidate. Revalidate the current disposable target, save/archive before mutations or controlled restart, and verify restoration. Do not repeat the known native exporter crash without fresh explicit approval at that moment. Keep the installed tool unchanged.

## Execution checkpoint — 2026-10-16

Tasks 1–3 and the final synchronization fix wave are implemented and independently reviewed. Candidate `a26b45f` passed 6,906 tests, corrected linked-source coverage, stub/real builds and the affected live checks. HMI selectors work in both writable modes; normal sync decisions/import/compile and same-host new-PID recovery passed. Temporary objects were removed, baseline workspace identities/counts restored, the fixture saved/reopened unmodified, and the installed connector rebound without replacing it. The public live report records candidate provenance, exact scope, audit evidence, the original failed verdict and retained bulk-export/native-crash limits. No broad live-acceptance claim is made from the bounded follow-up.

## Task 4: Complete original VCI documentation task after the affected live gate

This carries forward original plan Task 14 and design §9; it adds no application behavior or live scope.

**Ownership/files:** create `docs/SupportedOperations/VCI_OPERATIONS_SUMMARY.md`; modify `docs/README.md`, `README.md`, `AGENTS.md`, `docs/ARCHITECTURE.md`, `docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md`, `docs/IMPROVEMENT_LOG.md`.

- [ ] Document the three read/six write operations, every current limit and E1–E14 as confirmed or changed, current counts 8/19/19, native crash-report location, sync ownership/current-status safeguards and retained evidence limits. Preserve historical candidate counts/verdicts.
- [ ] Record follow-ups for library scopes, connect, workspace groups/settings, MCP progress, local sessions, software units/system blocks, portal_lost in other domains and path-policy consolidation. State remaining export scan growth rather than imply a performance fix.
- [ ] Follow the existing documentation layout and index the new reference. README cross-document links must be absolute GitHub URLs; other docs use relative links. Keep all fixture identities/paths private.
- [ ] Run documentation-only whitespace/link/count/privacy validation; do not rerun unchanged application suites/builds. Commit `docs: document vci tools`, then obtain independent spec/quality review of this documentation delta.
