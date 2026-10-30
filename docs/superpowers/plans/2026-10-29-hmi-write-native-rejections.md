# hmi_write Native Rejections and Shared Trusted Verification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Status:** draft 2026-10-29, written after the [checkpoint probes](../acceptance/reports/2026-10-29-hmi-write-checkpoint-probes.md) and the [timing baseline](../acceptance/reports/inputs/2026-10-29-hmi-write-timing-before.md). Awaiting maintainer review.

**Goal:** Leave Portal's rules to Portal in `hmi_write`. Siemens rejections become typed `engineering_rejected`/`worker_operation_failed` items with `engineeringError`, and `portal_lost` takes precedence. The server-side collision, existence and bound-tag checks go away. Planning reads only targets and delete consequences, and completed items are verified `trusted`/`superseded` by the shared verifier under write contract `2.0`.

**Architecture:** The guarded single-call pipeline is unchanged. The worker's one wrap point, `HmiWriteRun.Failure`, classifies through `WorkerExecution`. Write-path lookups opt into `engineeringError` so the disposal re-check can fire. The host planner keeps device resolution, target reads, delete-consequence reads and the read-free working-state simulation, and declares no replan. `HmiWriteVerifier` is replaced by `HmiVerificationPolicy` on `TrustedWriteVerifier<TItem>`, following Network.

**Tech Stack:** C#/.NET 10 host, netstandard2.0 contracts, net48 worker, xUnit, FakeWorker, PowerShell for `dotnet … /p:` commands and live steps. No new dependencies.

**Spec:** `docs/superpowers/specs/2026-10-29-hmi-write-native-rejections-design.md`. Section numbers (§6.1 …) refer to it. The checkpoint report settles §6.7's gate.

**Analysis baseline:** `main` at `0b2640a`; branch `refactor/hmi-write-native-rejections`. Line numbers are navigation aids; revalidate them before editing.

## Global Constraints

- Preserve the 8/19/19 tool surface, `hmi_write(operations, dryRun=false)`, 1–50 unique-ID items, caller order, stop-at-first-failure, no batch rollback, no server elicitation, binding lease, access-mode gates, audit v2, and one `CanonicalJson.Serialize` call for text and `structuredContent`.
- HMI worker method names, `HmiWriteMethods.All`, and the `WithEngineeringRoot` handler path are unchanged; local-session roots keep working. `OperationPolicyCatalog` and `ProjectCapabilityCatalog.OperationFor` are unchanged.
- `hmi_read` stays contract `1.1`, and its output is byte-for-byte unchanged, including messages from the shared `HmiReadLog.Guard`/`HmiTagLookup` read path. `plc_write`/`network_write` schema rows are unchanged.
- New conditional members go through `ConditionalMemberRegisterTests`; payload null policy through `WorkerPayloadNullPolicyRegisterTests`; `output-schemas.json` changes only in its `hmi_write` key, if at all.
- `TiaMcpServer.Tests.csproj` links host files explicitly outside `Safety/Pipeline`: a new or deleted `TiaMcpServer/Hmi/` file updates it. Worker `Openness/Hmi/*.cs` is a glob; test files are globbed.
- Build with `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true` from PowerShell (Git Bash mangles `/p:`). Run `dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"` at the end of every code task. No product `.csproj` changes, so packaging tests do not run.
- Commit after each task with a short one-line conventional message, no body or trailers; no push until the maintainer asks.
- No TIA project names or paths in repository files or commits; check with `git grep -n -i -E "DisposableProjects|_Copy|\.ap21|\.als21"` on the branch diff before every docs commit, plus a search for the project's name taken from local provenance (never typed into a tracked file).
- No server-side check that re-implements a Portal rule Portal enforces (O1) or reports (O2). An O3 finding in Task 6 gets a guard or an adjacent worker read-back by maintainer ruling, never a silent success.
- A failed create's cleanup never deletes an object it did not create. A Siemens exception in a host planning read never becomes `engineering_rejected`.
- Documentation (Task 7) is written last, from Task 6 evidence. The acceptance-report inputs and this plan's execution record are the exception.

## Review Focus

1. A duplicate create rejected at `Create` (P1/P4/P8): item `engineering_rejected` with `engineeringError`, `hmiWriteOutcome.cleanup:"notNeeded"`, earlier items applied, later items skipped, and no delete dispatched. Owner: Task 4 `DuplicateCreate_ReachesPortalAndIsTypedWithoutCleanup`.
2. Portal disposed while `update_tag`/`delete_alarm` resolves its target before any mutation, or mid-create: `portal_lost`, outcome kept, binding invalidated, not `worker_operation_failed`. Owners: Task 1 `WriteLookupFailure_AfterPortalDisposal_MapsToPortalLost` (worker) and `PortalLostWithOutcome_KeepsOutcomeAndInvalidatesBinding` (host).
3. `create_tag` into a table the planner never read, followed in the same call by `update_tag` of that tag: both resolve, and no `target_not_found` comes from the host. Owner: Task 3 `CreateIntoUnreadTableThenUpdate_ResolvesFromSimulation`.
4. `delete_tag_table` after an earlier item in the call created a tag in that table: the removal lists the in-call tag, and that earlier create is `superseded`. Owners: Task 3 `DeleteTable_ListsTagsCreatedEarlierInCall`; Task 4 `DeleteTable_SupersedesTagsItRemoves`.
5. Case-only renames (P12, all accepted): no self-collision, keys compare case-insensitively, earlier items on the object are `superseded` and the rename is `trusted`. Owner: Task 4 `CaseOnlyRename_KeysMatchCaseInsensitively`.

## Decisions taken in this plan

- **D1 Checkpoint satisfied ahead of the code.** The design's gate ran natively before any refactor: 21 O1, three O2, five controls, two worker refusals, **no O3**. Default outcome applies: every §6.2 retirement goes ahead, and P17 retires (Portal throws "Tag table not found").
- **D2 Catalog rules.** P15-create (`connection`+`plcTag`+`dataType`) and P16-create (`plcTag` without `connection`) are stopped by `HmiWriteCatalog.ValidateWrite` before they reach Portal, and Portal rejects both natively with cleanup. Retire `'dataType' cannot accompany 'plcTag'`. Relax the create rule to "Create requires plcTag or dataType." Keep `'connection' requires 'plcTag'`: a connection-only assignment was never probed (checkpoint, P18 note).
- **D3 `effect.dependsOn` stays informational.** Effects keep their shape and schema. The planner stops copying it onto `ItemPlan.DependsOn`, so the pipeline never replans.
- **D4 Supersession scope.** A later tag rename does not supersede an earlier alarm that triggers on it, and a table rename does not supersede its tags: keys are flat per object, matching how today's verifier only rewrote expectations. `delete_tag_table` offers the keys of every tag its effect removes.
- **D5 Timing baseline.** The baseline was measured on installed `75c0568`, not `0b2640a` (recorded deviation). Task 6 replays its frozen client and workloads unchanged on the candidate.

## File structure

| Path | After this plan |
| --- | --- |
| `TiaMcpServer.OpennessWorker/Openness/Hmi/HmiWriteRun.cs` | `Failure` classifies Siemens exceptions; forwards a wrapped `EngineeringError` |
| `TiaMcpServer.OpennessWorker/Openness/Hmi/HmiReadLog.cs`, `HmiTagLookup.cs` | `describe` opt-in for write-path lookups |
| `TiaMcpServer.OpennessWorker/Openness/Hmi/HmiTagWriter.cs`, `HmiAlarmWriter.cs` | lookups pass `describe: true` |
| `TiaMcpServer.OpennessWorker/Worker/WorkerExecution.cs` | wrapped `portal_lost` also keeps `HmiWriteOutcome` |
| `TiaMcpServer/Hmi/HmiGuardDefinitions.cs` | three guards |
| `TiaMcpServer/Hmi/HmiWriteCatalog.cs` | D2 |
| `TiaMcpServer/Hmi/HmiWorkingState.cs` | simulation, target resolution, delete consequences only |
| `TiaMcpServer/Hmi/HmiWritePlanner.cs` | device, target and delete-consequence reads; no replan |
| `TiaMcpServer/Hmi/HmiWriteModels.cs` | slimmer `HmiEvidence`; `Current = "2.0"`; HMI verification records gone |
| `TiaMcpServer/Hmi/HmiVerificationPolicy.cs` (new) | object keys; no read-back |
| `TiaMcpServer/Hmi/HmiWriteVerifier.cs` | deleted |
| `TiaMcpServer/Hmi/HmiWriteDomain.cs`, `HmiWritePayloadBudget.cs`, `HmiWriteTools.cs` | shared `WriteVerification`; `ReplanAsync` throws; description |
| `TiaMcpServer.FakeWorker/Hmi/HmiWriteRoundtripScenario.cs` | duplicate and setter rejections with `engineeringError` |

---

## After the tasks (orchestrator and maintainer gates — not part of any task brief)

- Tasks 0–5 run offline in order. The full default suite (about 8.5 min) runs serially at the end of each code task; do not run two suites concurrently.
- After Task 5 the orchestrator stops. Task 6 needs the maintainer's machine, TIA Portal V21 and explicit authorization for live mutation of the disposable copy.
- An O3 finding in Task 6 stops execution. The maintainer rules, and the ruling adds one focused task with its own test before Task 6 resumes. Rulings are recorded in the execution record below.

---

### Task 0: Redact project identity from the checkpoint evidence

**Files:**
- Modify: `docs/superpowers/acceptance/reports/2026-10-29-hmi-write-checkpoint-probes.md:21`
- Modify: `docs/superpowers/acceptance/reports/inputs/2026-10-29-hmi-native-probe/{Program.cs,README.md}`, `inputs/2026-10-29-hmi-ui-compile-probe/{Program.cs,README.md}`
- Modify: `inputs/2026-10-29-hmi-ui-P10-cleanup-audit.json`, `hmi-ui-final-status.json`, `hmi-write-baseline-preflight.json`, `hmi-write-final-restoration.json`, `hmi-write-installed-P7.json`, `hmi-write-native-summary.json`
- Modify, if they contain it: every `inputs/2026-10-29-hmi-*.jsonl.gz` and `timing-before/*.jsonl.gz`

Scope is this branch's 2026-10-29 HMI evidence only; older reports on `main` stay as they are (maintainer rule). Take the project name and path from local provenance and keep the search script in the scratchpad: neither may be typed into a tracked file, this plan included.

- [ ] **Step 1:** List every occurrence, including inside the gzip archives (decompress to the scratchpad and search).
  Run: `git grep -n -i -E "DisposableProjects|<name>" -- "docs/superpowers/acceptance/reports/*2026-10-29-hmi*" "docs/superpowers/acceptance/reports/inputs/2026-10-29-hmi*"` plus a `gzip -dc | grep -c` loop over those `.gz` files.
  Expected: the files above; record the per-archive counts.
- [ ] **Step 2:** Replace paths with `<disposable-copy>` and project names with `<disposable-project>`. In the two probe `Program.cs` files, replace only the string literal and leave the logic unchanged. Add one sentence to the report and both probe READMEs: the archived source was edited after the run only to remove the path, so the recorded executable SHA-256 identifies the pre-redaction build. Re-compress the edited traces. Record each new decompressed SHA-256 in place of the old one, with one sentence saying hashes were renewed after redaction.
- [ ] **Step 3:** Run: the Step 1 search again. Expected: zero matches in that scope. The JSON files still parse (`python -c "import json,sys;[json.load(open(p)) for p in sys.argv[1:]]" <files>` exits 0).
- [ ] **Step 4:** Record D1–D5 in this plan's execution record, then commit: `docs: redact hmi checkpoint project identity`.

**Task test command:** the Step 1 search returns no match in its scope.

---

### Task 1: Typed worker rejections and `portal_lost` precedence (§6.1)

**Files:**
- Modify: `TiaMcpServer.OpennessWorker/Openness/Hmi/HmiWriteRun.cs:52-54`
- Modify: `TiaMcpServer.OpennessWorker/Openness/Hmi/HmiReadLog.cs:31-42`, `HmiTagLookup.cs:12-36`, `HmiTagWriter.cs:17,30`, `HmiAlarmWriter.cs:49-62`
- Modify: `TiaMcpServer.OpennessWorker/Worker/WorkerExecution.cs:29-37`
- Modify: `TiaMcpServer.Tests/Json/ConditionalMemberRegisterTests.cs:38` (comment only)
- Modify: `TiaMcpServer.FakeWorker/Hmi/HmiWriteRoundtripScenario.cs` (a portal_lost variant)
- Test: `TiaMcpServer.Tests/Worker/WorkerExecutionTests.cs`, `TiaMcpServer.Tests/Hmi/{HmiTagWriterTests,HmiAlarmWriterTests,HmiTagTableWriterTests,HmiGuardedWriteMcpTests}.cs`

**Interfaces:**
- Consumes: `WorkerExecution.Classify(EngineeringException, bool mutates)`, `FailureMessage(EngineeringException, string category)`, `Describe(Exception)`; `WorkerOperationException(…, HmiWriteOutcomeInfo? hmiWriteOutcome, …, EngineeringErrorInfo? engineeringError, …)`.
- Produces:
  - `HmiWriteRun.Failure(Exception ex, HmiWriteOutcomeInfo outcome)` (signature unchanged). For an `EngineeringException`: category `Classify(e, true)`, message `FailureMessage(e, category)`, `engineeringError: Describe(e)`. A `WorkerOperationException` keeps its category, message and warnings, and forwards its own `EngineeringError`. The outcome is always attached.
  - `HmiReadLog.Guard<T>(Func<T> read, string what, bool describe = false)`. With `describe:true` the thrown `WorkerOperationException` also carries `engineeringError: WorkerExecution.Describe(ex)`. Category and message are unchanged in both modes.
  - `HmiTagLookup.FindUnique(HmiSoftware software, string tagName, bool describe = false)`. Only `HmiTagWriter.Update/Delete` pass `true`; `HmiAlarmWriter.Find` passes `describe: true` to its `Guard` calls.
  - `WorkerExecution.Execute`'s wrapped catch sets `HmiWriteOutcome = ex.HmiWriteOutcome` next to `ConfigureNetworkDeviceOutcome` on the `portal_lost` response.

- [ ] **Step 1: Write the failing tests**
  - `WorkerExecutionTests`: `WrappedHmiFailure_AfterPortalDisposal_MapsToPortalLostAndKeepsOutcome` (`Assert.Same` on `HmiWriteOutcome`, `EngineeringError` null, category `portal_lost`); `WrappedHmiFailure_WithoutDisposal_IsRethrown` (same instance).
  - `HmiTagWriterTests`: `CreateTag_TargetInvocationRejection_IsEngineeringRejectedWithEvidence`. A setter throws `EngineeringTargetInvocationException("m")`; assert category `engineering_rejected`, message `"TIA Portal rejected the operation: m"`, `EngineeringError.Type == "EngineeringTargetInvocationException"`, `FailedProperty`, and cleanup `deleted`.
  - `HmiAlarmWriterTests`: `UpdateAlarm_TargetInvocationRejection_KeepsAppliedProperties` (rename applied, later setter rejected).
  - `HmiTagWriterTests`/`HmiAlarmWriterTests`: `WriteLookupFailure_CarriesEngineeringError`. The lookup enumeration throws `EngineeringException`; category `worker_operation_failed`, message unchanged, `EngineeringError` present.
  - `WorkerExecutionTests`: `WriteLookupFailure_AfterPortalDisposal_MapsToPortalLost` (lookup failure with revision bump inside `Execute`).
  - Read path: `ReadPathGuard_OmitsEngineeringError` (`Guard` without `describe`, and `FindUnique` without `describe`, throw with `EngineeringError` null).
  - Reshape `HmiTagTableWriterTests.DefaultTableDelete_ReportsOpennessMessage`: message `"TIA Portal operation failed: Hmi tag table could not be deleted"`, `EngineeringError` present.
  - `HmiGuardedWriteMcpTests.PortalLostWithOutcome_KeepsOutcomeAndInvalidatesBinding`: a FakeWorker `hmi-write-roundtrip` variant answers the second of three items with `failureCategory:"portal_lost"` and an `hmiWriteOutcome` (`appliedProperties:["Create"]`). Assert that item 2 is `portal_lost` with its outcome, not `protocol_error`; item 3 is skipped; the next `get_project_status` shows the binding invalidated.
- [ ] **Step 2:** Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~WorkerExecutionTests|FullyQualifiedName~HmiTagWriterTests|FullyQualifiedName~HmiAlarmWriterTests|FullyQualifiedName~HmiTagTableWriterTests|FullyQualifiedName~HmiGuardedWriteMcpTests"`. Expected: build errors for the `describe` parameter, then assertion failures. The host portal_lost test may already pass, because the host copies the outcome on every failure category. Keep it as the pin.
- [ ] **Step 3:** Implement per Interfaces. Refresh the register comment on `WorkerResponse.HmiWriteOutcome` to: present on a worker failure after a mutation attempt, including `portal_lost`. Leave the `HmiSoftwareLocator.FindUnique` path in `HmiWriteDispatch` as it is: a raw Siemens exception there already reaches `Execute`'s shared catch, the same as PLC and Network.
- [ ] **Step 4:** Run the Step 2 filter plus `ConditionalMemberRegisterTests`. Expected: all pass.
- [ ] **Step 5:** Build with stubs and run the full default suite. Expected: 0 errors; all pass.
- [ ] **Step 6:** Commit: `refactor: hmi write task 1 typed worker rejections`.

**Task test command:** `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~WorkerExecutionTests|FullyQualifiedName~Hmi"`

---

### Task 2: Retire host checks Portal already makes (§6.2, D2)

**Files:**
- Modify: `TiaMcpServer/Hmi/HmiGuardDefinitions.cs`, `HmiWorkingState.cs:36-39,78-181`, `HmiWritePlanner.cs:108`, `HmiWriteCatalog.cs:89-95`
- Test: `TiaMcpServer.Tests/Hmi/{HmiGuardDefinitionsTests,HmiWorkingStateTests,HmiWritePlannerTests,HmiWriteCatalogTests,HmiGuardedWriteMcpTests}.cs`

**Interfaces:**
- Produces: `HmiGuardDefinitions.Definitions` contains exactly `hmi_state_unverifiable` (block), `hmi_deletes_table_contents` (info) and `hmi_deletes_referenced_tag` (info); the `NameCollision` constant is removed. The `StateUnverifiable` description reads: "Delete-consequence evidence or an update/delete target could not be read completely."
- Produces: `HmiWorkingState.ResolveEffect` no longer contains `Collision`, the group, connection, alarm-class, trigger-member and culture `Reference` calls, the unreadable-connection guard, or the two bound-tag `ResolutionException`s. The derived `dataType`/`plcName` change flags keep using `bound`.
- Trigger tags: an alarm's trigger adds a dependency only when the simulation knows the parent tag. A missing trigger tag or member is not an error.
- Unknown tables are tolerated in both `ResolveEffect` and `Apply`. `create_tag` into a table absent from the simulation resolves with `table: r.TableName, group: null`, and `Apply` adds that table to the simulation. A tag whose `Table` is unknown to the simulation resolves with that table name and `group: null`. Portal rejects a truly missing table (P17: "Tag table not found").
- Duplicate names: a create whose name already exists in its scope (case-insensitive; tags HMI-wide, tables HMI-wide, alarms per kind) resolves and does not add a second row in `Apply`. Later items on that name resolve to the one existing row, never `target_ambiguous`. Portal rejects the create itself (P1/P4/P8).
- Kept for Task 3: `AddProvisionalMemberPath`, `_members` and the planner's provisional plumbing remain, unused by any check, so the planner compiles.
- Produces, in `HmiWriteCatalog.ValidateWrite` (the `create_tag or update_tag` block, `:89-95`):
  - delete `'dataType' cannot accompany 'plcTag'` for both operations (P15 create and update are native-rejected with cleanup);
  - keep `'connection' requires 'plcTag'` (a connection-only assignment was never probed);
  - replace the create rule with `r.PlcTag is null && r.DataType is null` → `"Create requires plcTag or dataType."` (P16 create, plcTag without connection, is native-rejected with cleanup).
- `HmiEvidence` members are left in place; Task 3 removes the dead ones.

- [ ] **Step 1: Write the absence tests first**
  - `HmiGuardDefinitionsTests.ProductionCatalog_RegistersExactlyThreeHmiGuards` (`hmi_name_collision` absent).
  - `HmiWorkingStateTests` (Theory per operation): `NameInUse_ResolvesWithoutGuard` covers create and rename into a taken name for tables, tags and alarms. `MissingReferences_ResolveWithoutError` covers connection, alarm class, trigger tag, trigger member, group and culture. `BoundTagDataTypeAndUnboundPlcTag_ResolveWithoutError`. `UnreadableConnection_DoesNotBlockUpdate`. `CreateIntoUnknownTable_ResolvesWithRequestedTable` covers `Resolve` plus `Apply`, then `update_tag` of that tag resolves. `DuplicateCreateThenUpdate_ResolvesToOneRow`.
  - `HmiWriteCatalogTests.FieldRules`: rows `create_tag connection+plcTag+dataType`, `create_tag plcTag only` and `update_tag plcTag+dataType` become valid; `create_tag` with neither `plcTag` nor `dataType` fails with the new message; `connection only` stays invalid.
  - `HmiGuardedWriteMcpTests.NameCollision_ReachesWorker`: a second identical `create_tag_table` reaches the FakeWorker; assert the worker request log contains `hmi_create_tag_table` twice and that no guard fired. Task 4 adds the typed rejection.
- [ ] **Step 2:** Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Hmi"`. Expected: the new tests fail.
- [ ] **Step 3:** Delete the tests that pin retired behavior:
  - `HmiWorkingStateTests`: `Collision_IsCaseInsensitiveAndScoped`, `AlarmNames_AreScopedByKind`, `CaseOnlyRename_DoesNotCollideWithItself`, `DeleteThenCreateCaseVariant_DoesNotCollide`, `MissingReference_ReturnsTargetNotFound`, `InternalTag_PlcTagOnlyUpdateFails`, `UnknownConnection_BlocksInternalDataTypeDecision`, `BoundDataTypeUpdate_IsRejectedAndMissingGroupCultureAndMemberAreRejected`, `ProvisionalMemberAfterRecreationDoesNotBecomeObservedEvidence`;
  - `HmiWritePlannerTests`: `ReferencedNamesMustExist`, `MemberTriggerChecksFullParentMemberPath`, `NestedMemberEvidenceCannotBeReparentedOrDeduplicated`, `BoundCreatedMemberIsProvisionalUntilFreshProof` (this retires the "rebind then second call" limit);
  - `HmiGuardDefinitionsTests.ProductionCatalog_RegistersExactlyFourHmiGuardsWithFixedSeverities`, replaced by the Step 1 test.

  Reshape:
  - `HmiWritePlannerTests.ReplanMissIsTargetNotFoundAndCollisionIsGuard`: keep only its `target_not_found` half until Task 3.
  - `HmiWorkingStateTests.AmbiguousAndBlockedOperations_DoNotMutateState`: no longer relies on the collision guard stopping `Apply`.
  - `HmiGuardedWriteMcpTests.MalformedFailureAfterCreate…`: drop the collision-guard assertion at `:167`.
  - `HmiGuardedWriteMcpTests.FailedCreateCarriesCleanupStopsLaterItemsAndKeepsEarlierWrite`: the second call is no longer `blocked`; Task 4 pins its typed result.
- [ ] **Step 4:** Implement per Interfaces. Remove the `NameCollision` uses in `HmiWorkingState.Apply:181` and `HmiWritePlanner:108`.
- [ ] **Step 5:** Run the Step 2 filter, then build with stubs and run the full default suite. Expected: all pass. `HmiWriteVerifier` still runs `validate` in this task, so a dangling-reference create can fail `postcondition_failed` until Task 4 replaces it. Tests that send such inputs belong to Task 4.
- [ ] **Step 6:** Commit: `refactor: hmi write task 2 retire host portal checks`.

**Task test command:** `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Hmi"`

---

### Task 3: Plan from targets and delete consequences only; no replan (§6.3, D3)

**Files:**
- Modify: `TiaMcpServer/Hmi/HmiWritePlanner.cs` (`PlanAsync`, `Load`, `LoadCommon`; delete `ReplanAsync`, `LoadItem`, `MemberPaths`, provisional/`Bound` tracking, `_deleteTags`/`_createdDeleteTags`)
- Modify: `TiaMcpServer/Hmi/HmiWriteModels.cs:24-38` (`HmiEvidence`), `HmiWorkingState.cs` (delete `AddProvisionalMemberPath` and `_members`, left unused by Task 2), `HmiWriteDomain.cs:52-62`
- Test: `TiaMcpServer.Tests/Hmi/{HmiWritePlannerTests,HmiWorkingStateTests,HmiGuardedWriteDomainTests,HmiGuardedWriteMcpTests}.cs`

**Interfaces:**
- Produces, in `PlanAsync` reads per call:
  - `list_hmi_devices` once.
  - Per HMI: `list_tag_tables` once only when the call has an update or delete of a table or tag.
  - `get_tag` per `update_tag`/`delete_tag` target; `get_alarm` per `update_alarm`/`delete_alarm` target.
  - Per `delete_tag_table`: table-scoped `list_tags`, all pages.
  - Only when the call contains `delete_tag` or `delete_tag_table`: `list_alarms` once per HMI, all pages, and one `read_cross_references` per deleted pre-existing tag, 200-query cap unchanged.
  - Creates read nothing. Paging limit 2000 is unchanged.
- Rules:
  - All reads for an HMI finish before the resolve pass (two passes kept).
  - Target reads use `Read<T>(…, optional: true)` as `LoadItem` does. A `target_not_found` answer means "absent at planning". If the simulation creates or renames that name earlier in the call, the item resolves from the simulation. Otherwise the call fails at plan level with `target_not_found` naming the item, through the existing `HmiResolution.Error` path; the pipeline has no per-item plan failure.
  - An incomplete or truncated payload (`IsComplete:false`, bad paging, the 200-query cap) marks evidence incomplete, so `hmi_state_unverifiable` fires for that item.
  - Decode failures stay `protocol_error`. `portal_lost`, timeouts and every other worker failure still fail the call with their own category (`ReadFailure`), as today.
  - Cross-reference origins come from target reads and deleted-table contents.
- Produces: `HmiEvidence` stays an init-property record. Remove only the `Devices`, `Cultures`, `Groups`, `MemberPaths`, `Connections` and `AlarmClasses` properties. Delete only the `HmiProjectLanguagesInfo`, `HmiConnectionListInfo` and `HmiAlarmClassesInfo` cases of `ValidateRows`; the device-list and table-tree validation stays.
- Produces: every `ItemPlan.DependsOn` is null; `HmiWriteEffect.DependsOn` keeps its informational value (D3). `HmiWriteDomain.ReplanAsync` throws `InvalidOperationException("hmi_write plans declare no dependencies; ReplanAsync must never be called.")`.

- [ ] **Step 1: Write the failing tests** (the planner `Fixture`'s `Requests` array proves absence; it holds worker method names, and includes the transport's `hello`, so compare after excluding `hello`):
  - `CreateOnlyCall_ReadsDevicesOnly`: one each of `create_tag_table`, `create_tag` and discrete/analog `create_alarm` with a dotted trigger; requests excluding `hello` equal `["hmi_list_hmi_devices"]`.
  - `UpdateCall_ReadsTablesOnceAndEachTargetOnly`: no `hmi_list_tags`, `hmi_list_alarms`, connection, class or language read.
  - `CallWithoutDelete_NeverReadsAlarms`, and `DeleteTagCall_PagesAlarmsOnceAndQueriesOriginalName`.
  - `CreateIntoUnreadTableThenUpdate_ResolvesFromSimulation`, `UpdateOfTagCreatedEarlierInCall_ResolvesFromSimulation`, `DeleteTable_ListsTagsCreatedEarlierInCall`.
  - `PlanDeclaresNoReplan`: all `ItemPlan.DependsOn` null; `effects[i].DependsOn` still `"table"` where today's `PreviewThenApply…` expects it.
  - `HmiGuardedWriteDomainTests.ReplanAsync_Throws`, with the exact message.
  - `HmiGuardedWriteMcpTests.DryRun_ReadsOnlyPlanningEvidence`: the FakeWorker request log has no mutation, `hmi_validate` or post-plan read.
- [ ] **Step 2:** Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~HmiWritePlannerTests|FullyQualifiedName~HmiGuardedWrite"`. Expected: the new tests fail.
- [ ] **Step 3:** In `HmiWritePlannerTests` (every `planner.ReplanAsync` call and every `Plan.Items[i].DependsOn` assertion must go):
  - Delete: `LocalMemberNamesResolveFromObservedContainment`, `IncompleteOrTruncatedMembersStillBlockInitialAndFreshPlans`, `FreshDeleteReplanPagesMemberTriggerAlarmsOfBothKinds`, and the `target_not_found` remainder of `ReplanMissIsTargetNotFoundAndCollisionIsGuard`.
  - Keep the plan-time half, drop the replan half: `RenamedExistingTagQueriesOriginalThenCurrentAtReplan`, `ReplanTableDeletePagesOnlyItsOwnTagsAndRejectsUncoveredTags`, `Exactly200PlanQueriesRemainCoveredDuringDependentReplan`.
  - Move `DependsOn` assertions from `Plan.Items[i]` to `Effect.DependsOn`: `CreatedDeletedAndRecreatedLineagesDoNotQueryNewTags`, `LaterDeletionEvidenceAlsoInformsEarlierAlarmDependency`, `RenamedParentAndTableKeepOriginalMemberProofAndDependencies` (dropping its member-proof part), and the kept half of `RenamedExistingTagQueriesOriginalThenCurrentAtReplan`.
  - Reshape onto the new read set: `EvidenceIsLoadedOnceForAliasesAndPagesToTheEnd` (paging now on scoped `list_tags`/`list_alarms`), `InvalidEvidenceFailsClosed` and `NullRequiredEvidenceIsProtocolError` (still `protocol_error` on a surviving read), and the 200-cap and incomplete-cross-reference tests.

  In `HmiGuardedWriteMcpTests`, `LateChangedInfoMessagesAndBlockerRemainVisibleBeforePartialWriteGuard` becomes planning-time guards only; delete it if nothing late remains. Drop the removed `HmiEvidence` members from working-state fixtures.
- [ ] **Step 4:** Implement per Interfaces, using `LoadItem`'s per-item read shape for the initial target reads.
- [ ] **Step 5:** Run the Step 2 filter, then build with stubs and run the full default suite. Expected: all pass.
- [ ] **Step 6:** Commit: `refactor: hmi write task 3 target-only planning`.

**Task test command:** `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Hmi"`

---

### Task 4: Shared trusted verifier, contract 2.0, description, rejection fixtures (§6.4, §6.5, D4)

**Files:**
- Create: `TiaMcpServer/Hmi/HmiVerificationPolicy.cs`; add its `<Compile Include … Link="Host\Hmi\HmiVerificationPolicy.cs" />` row
- Delete: `TiaMcpServer/Hmi/HmiWriteVerifier.cs` (and csproj row `:182`), `TiaMcpServer.Tests/Hmi/HmiWriteVerifierTests.cs`
- Modify: `TiaMcpServer/Hmi/HmiWriteModels.cs:7,14-19`, `HmiWriteDomain.cs`, `HmiWritePayloadBudget.cs`, `HmiWriteTools.cs:14`
- Modify: `TiaMcpServer.FakeWorker/Hmi/HmiWriteRoundtripScenario.cs`
- Modify: `TiaMcpServer.Tests/Tools/ToolOutputContractConformanceTests.cs` (contract-version block), `TiaMcpServer.Tests/Tools/Snapshots/output-schemas.json` (`hmi_write` key only, if it changes)
- Test: new `TiaMcpServer.Tests/Hmi/HmiVerificationPolicyTests.cs`; `HmiGuardedWriteMcpTests`, `HmiWriteBudgetTests`, `HmiGuardedWriteDomainTests`

**Interfaces:**
- Consumes: `IWriteVerificationPolicy<TItem>` (`ReadTool`, `ObjectKeys(TItem, bool asLater)`, `HasReadBack(TItem)`, `ReadBackAsync(TItem, StructuredOperationFailure?)`), `TrustedWriteVerifier<TItem>(items, policy)`, `WriteVerification`, `WriteVerificationChecks`.
- Produces: `public sealed class HmiVerificationPolicy(IReadOnlyDictionary<string, HmiWriteEffect> effects) : IWriteVerificationPolicy<HmiWriteOperationRequest>`.
  - `ReadTool = "hmi_read"`; `HasReadBack` is false; `ReadBackAsync` throws as `NetworkVerificationPolicy` does.
  - Keys are built from the effect target, HMI name and object name upper-cased invariantly: `hmi\n{HMI}\ntable\n{NAME}`, `hmi\n{HMI}\ntag\n{NAME}`, `hmi\n{HMI}\nalarm\n{alarmKind}\n{NAME}` (`alarmKind` is `discrete`/`analog`, as is).
  - A rename owns and offers the old and new keys. `delete_tag_table` also offers the key of every tag in `effect.Removes.Tags`. Nothing else crosses kinds (D4).
- Produces: `HmiWriteContractVersion.Current = "2.0"`. `HmiGuardedWriteResponse.Verification` is `WriteVerification?`. `HmiOperationVerification`/`HmiWriteVerification` are deleted. `HmiWriteDomain : IWriteDomain<HmiWriteOperationRequest, HmiWriteEffect, WriteVerification, HmiGuardedWriteResponse>`, and `VerifyAsync` uses `new TrustedWriteVerifier<HmiWriteOperationRequest>(attemptedItems, new HmiVerificationPolicy(_effects))`.
- Produces, in the `hmi_write` description: drop "Later items may depend on earlier writes." Add:
  - "Later items may target objects created or renamed earlier in the same call."
  - "TIA Portal enforces names, references and binding rules: a rejection fails the item as engineering_rejected (EngineeringTargetInvocationException) or worker_operation_failed (other Siemens exceptions) with engineeringError; portal_lost takes precedence."
  - "Completed items are verified trusted or superseded without read-back; run hmi_read validate on written tags and alarms, and compile the HMI device in TIA Portal, to find dangling references."
- Produces, in FakeWorker `hmi-write-roundtrip`:
  - A create whose name already exists in its scope returns `failureCategory:"engineering_rejected"`, `error:"TIA Portal rejected the operation: " + NativeDuplicateTagMessage`, `engineeringError {type:"EngineeringTargetInvocationException", message, inner:[]}` and an outcome with `cleanup:"notNeeded"`.
  - A `dataType` on a bound tag returns the same shape with `NativeBoundDataTypeMessage` and `appliedProperties` up to the failing setter.
  - Copy both messages verbatim from P4 and P15-update in `inputs/2026-10-29-hmi-write-native-summary.json`.

- [ ] **Step 1: Write the failing tests**
  - `HmiVerificationPolicyTests`, modelled on `NetworkMutationVerificationTests.QualifiedKey_…`:
    - `DistinctObjects_AreTrusted`
    - `DiscreteAndAnalogSameName_AreIndependent`
    - `Rename_OwnsOldAndNewName`
    - `CaseOnlyRename_KeysMatchCaseInsensitively`
    - `DeleteTable_SupersedesTagsItRemoves`
    - `DeleteThenRecreate_SupersedesEarlier`
    - `LaterTagRename_DoesNotSupersedeEarlierAlarm`
    - `PreconditionRefusal_DoesNotSupersede`
  - `HmiGuardedWriteMcpTests`:
    - `DuplicateCreate_ReachesPortalAndIsTypedWithoutCleanup`: three items, the second a duplicate. Assert root `success:false`, `error:null`, `isError:false`; item 2 is `engineering_rejected` with the exact `engineeringError` and `cleanup:"notNeeded"`; item 3 is skipped; verification has an entry for item 1 only and `success:true`; no `hmi_delete_*` is in the request log; one audit line.
    - `BoundDataTypeRejection_KeepsAppliedProperties`.
    - `AppliedCall_IssuesNoPostWriteRead`: no `hmi_validate`, `hmi_get_*` or `hmi_list_*` after the first mutation.
    - `DanglingReferenceCreate_IsTrusted` (Task 2's deferred inputs).
  - `ToolOutputContractConformanceTests`: assert `hmi_write` `contractVersion == "2.0"` and `hmi_read` still `"1.1"`.
- [ ] **Step 2:** Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~HmiVerificationPolicyTests|FullyQualifiedName~HmiGuardedWrite|FullyQualifiedName~ToolOutputContractConformanceTests"`. Expected: build errors, then failures.
- [ ] **Step 3:** Implement per Interfaces. Reshape `FailedCreateReadbackDistinguishesCleanupOutcome`: cleanup state is asserted on the item result, and `verification.success` is true in both cases. In `AnalogCreateAndUpdateRoundtrip_IsTrusted`, checks become `trusted`. In `HmiWriteBudgetTests`, `"1.0"` becomes the constant, and the late-guard shape goes if Task 3 removed it. Finish `FailedCreateCarriesCleanup…`'s second call as a typed duplicate rejection.
- [ ] **Step 4:** Run `McpToolSchemaTests.OutputSchemasMatchMain`. Expected: pass unchanged; the shared verification schema already equals Network's. If it fails, replace only the `hmi_write` key with the dumped schema, following the Network Task 3 precedent, and re-run.
- [ ] **Step 5:** Build with stubs and run the full default suite. Expected: 0 errors; all pass.
- [ ] **Step 6:** Commit: `refactor: hmi write task 4 shared verifier contract 2.0`.

**Task test command:** `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Hmi|FullyQualifiedName~ToolOutputContractConformanceTests|FullyQualifiedName~McpToolSchemaTests"`

---

### Task 5: Offline qualification

**Files:**
- Modify: this plan's execution record.

- [ ] **Step 1:** Run: the stub build in Release. Expected: 0 errors; record the warning count against `main`.
- [ ] **Step 2:** Run: the full default suite in Release with coverage collected. Expected: zero failures and skips; CI host/contracts line coverage ≥ 80%; record the counts.
- [ ] **Step 3:** Adversarially review `git diff 0b2640a..HEAD -- TiaMcpServer TiaMcpServer.OpennessWorker TiaMcpServer.Contracts TiaMcpServer.FakeWorker` against the Global Constraints, and check each of these explicitly:
  - no Siemens call before the binding gate;
  - a planning read failure never yields `engineering_rejected`;
  - `portal_lost` wins on the `Failure`, lookup and shared paths;
  - `hmi_read` messages are unchanged;
  - `output-schemas.json` changes, if any, are in the `hmi_write` key only;
  - no new conditional member is outside the register;
  - no `[LegacyNullOmission]` change.
- [ ] **Step 4:** Record `git diff --numstat 0b2640a..HEAD` for production files, added and removed. Commit: `docs: hmi write task 5 offline qualification`.

**Task test command:** `dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"`

---

### Task 6: Live verification, timing after, captured fixtures (maintainer's machine, TIA Portal V21)

**Files:**
- Create: `docs/superpowers/acceptance/reports/inputs/<date>-hmi-write-native-rejections-live-cases.md`, `inputs/<date>-hmi-write-timing-after.md`
- Modify: `TiaMcpServer.FakeWorker/Hmi/HmiWriteRoundtripScenario.cs`, `TiaMcpServer.Tests/Hmi/HmiGuardedWriteMcpTests.cs`

Protocol is §6.7, in `full` mode on the disposable copy. Use a direct JSON-RPC client with a 900 s deadline and `--worker-timeout-minutes 15`: the desktop client's 300 s timeout cut off fixture setup and the P10 cleanup at the checkpoint. Record each case as (1) canonical document, (2) independent `hmi_read` of every object it could touch, (3) `hmi_read validate`, (4) maintainer UI compile for state-changing variants, (5) UI language. Close without saving and reopen after each mutating case. Classify every case O1, O2 or O3.

- [ ] **Step 1:** Install the Task 5 candidate with `scripts/install-local-tool.ps1`; record the version, binary hashes and UI language.
- [ ] **Step 2:** Through the installed tool, run P1–P22 (all 31 variants). For each one, compare category, `engineeringError.type` and Portal state with the native checkpoint row. P21/P22 are worker refusals (`target_not_found`); P22 shows cleanup `deleted`.
- [ ] **Step 3:** Run L1–L5 (§6.7 final run). L5 runs on a local-session root. During L4, record whether `read_cross_references` alone lists the alarm triggers (input for the alarm-read follow-up).
- [ ] **Step 4:** Report any O3 to the maintainer and stop. The ruling (a guard or an adjacent worker read-back, `postcondition_failed`) becomes one focused task with its own test, and it is recorded in the execution record before this task resumes.
- [ ] **Step 5:** Run timing after. Replay `inputs/2026-10-29-hmi-write-timing-before/workloads.json` unchanged, with the same server settings (`full`, both budgets 1,000,000, worker timeout 15 min) and the same protocol: round 0 discarded, three measured rounds, and close/reopen plus untimed full `hmi_read` before every workload.
  - Copy `client.py` with exactly two recorded changes: `EXPECTED_VERSION` becomes the Step 1 candidate version, and the `contractVersion` assertion becomes `'2.0'`. Put the diff and both hashes in the timing-after input.
  - Before measuring, record the target HMI's counts and confirm they equal the baseline: 101 tags, 9 tables, 4 alarms (2 discrete, 2 analog).
  - Write root, item-sum and remainder medians, each computed independently, against the baseline measured on installed `75c0568` rather than `0b2640a` (recorded deviation).
- [ ] **Step 6:** Pin the captured documents offline. Replace the two FakeWorker constants with the tool-captured `engineeringError.message` values. Add `LiveP4_DuplicateTagCreate_IsEngineeringRejected` and `LiveL2_MiddleRejection_EarlierAppliedRejectedTypedRestSkipped`, modelled on `NetworkGuardedWriteMcpTests.LiveL13_…`. Assert category, message prefix, `engineeringError` members, outcome, root `success:false`/`error:null`/`isError:false`, verification entries for earlier items only, and one audit line.
- [ ] **Step 7:** Run the full default suite. Run the project-name grep. Commit: `test: hmi write task 6 live fixtures`.

**Task test command:** `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~HmiGuardedWriteMcpTests"`

---

### Task 7: Documentation (last, from Task 6 evidence; §6.8)

**Files:**
- Modify: `README.md:33` (the `hmi_write` bullet: 2.0, `engineering_rejected`, validate and compile after writes; drop "Binding requires an existing connection" and the member-dependency limit) and `README.md:131` (HMI no longer re-plans). README links stay absolute `https://github.com/Czarnak/tia-portal-mcp/blob/main/...` URLs.
- Modify: `docs/SupportedOperations/HMI_OPERATIONS_SUMMARY.md` (write sections), `AGENTS.md` (HMI writes bullet; the "HMI/VCI wraps … still flatten" sentence), `docs/ARCHITECTURE.md` §7a and the verifier paragraph, `docs/IMPROVEMENT_LOG.md`, `docs/README.md`, `docs/roadmap/json-contract.md`, the HMI roadmap
- Create: `docs/superpowers/acceptance/reports/<date>-hmi-write-native-rejections-live-verification.md`
- Modify: the design's status line and its header reachability notes (P13 reachable on current source; P21/P22 blocked by host checks before this work); this plan's status line

- [ ] **Step 1:** HMI reference for 2.0: three guards, removed refusals, the remaining request rules (`'connection' requires 'plcTag'`; a create needs `plcTag` or `dataType`), `engineering_rejected` with a captured example, cleanup, `trusted`/`superseded`, the outcome table from Task 6, and `validate` plus UI compile after writes.
- [ ] **Step 2:** Update `README.md`, `AGENTS.md` and `ARCHITECTURE.md` per Files.
- [ ] **Step 3:** Improvement log:
  - a completed entry with Task 5 line counts and the H1–H5 timing table;
  - close the HMI halves of "adopt the same error contract" and "onto the shared verifier", and the "rebind then second call" limit;
  - add three follow-ups: the `hmi_read` `HmiReadLog.Guard` evidence gap, dropping the delete-call alarm read if Task 6 showed cross references list alarm triggers, and the unprobed connection-only assignment;
  - record O3 rulings, if any.
- [ ] **Step 4:** Write the acceptance report: matrix, O-classes, timing, retained limits, `docs/README.md` rows and roadmap notes.
- [ ] **Step 5:** Run: `git diff 0b2640a..HEAD | grep -i -E "^\+.*(DisposableProjects|_Copy|\.ap21|\.als21)"`, plus the provenance-name search from Task 0. Expected: no match except generic `.ap21` mentions in the timing client, which name no project. Commit: `docs: hmi write task 7 documentation`.

**Task test command:** the Step 5 grep returns no new match.

---

## Execution record

### Checkpoint record — 2026-10-29 (pre-plan)

The native checkpoint ([report](../acceptance/reports/2026-10-29-hmi-write-checkpoint-probes.md)) and the installed timing baseline ([input](../acceptance/reports/inputs/2026-10-29-hmi-write-timing-before.md)) ran before this plan. Rulings D1–D5 above apply. Baseline medians (root / item sum / remainder, ms): H1 119,492 / 12,775 / 106,717; H2 574,416 / 363,268 / 170,527; H3 160,490 / 16,995 / 143,325; H4 95,049 / 0 / 95,049; H5 142,877 / 16,124 / 126,753.

### Task 0 — project identity redacted

The disposable project path and name were removed from the 2026-10-29 HMI checkpoint evidence, and the re-compressed traces' decompressed SHA-256 values were renewed. Rulings D1–D5 above apply.

### Task 5 — offline qualification

Candidate `c2a20f8` against merge base `main` (`0b2640a`); no code changed.

- **Build:** Release stub build, 0 errors, 7 warnings on the branch and 7 on `main` (`0b2640a`) (same set, none in changed files).
- **Tests:** default suite in Release with the CI coverage command: 7,630 passed, 0 failed, 0 skipped (7 m 47 s).
- **Coverage:** CI `coverage.runsettings` plus `scripts/verify-coverage-threshold.ps1`: line rate 0.9504 (12,303 / 12,944), threshold 0.80 met.
- **Checks:**
  - No Siemens call before the binding gate: pass; `TiaMcpServer/Safety/Pipeline/WriteRun.cs:121` gates before `Validate` (184) and `PlanAsync` (213), and the diff adds no worker call ahead of it.
  - Planning read failure never `engineering_rejected`: pass; `HmiWritePlanner.Read` forwards the read result's category, read methods classify with `mutates=false`, so only `worker_operation_failed`.
  - `portal_lost` wins on the `Failure`, lookup and shared paths: pass; `WorkerExecution.Execute` checks the disposal revision in the `EngineeringException` and `WorkerOperationException` catches, now including `HmiWriteOutcome` (outcome copied onto the response).
  - Deviation found by the final review: the wrapped catch filter in `TiaMcpServer.OpennessWorker/Worker/WorkerExecution.cs` was widened to `EngineeringError is not null || HmiWriteOutcome is not null` (the plan only asked to copy `HmiWriteOutcome` inside the existing `EngineeringError` catch). Effect: a worker refusal carrying an HMI outcome also becomes `portal_lost` when Portal disposal was observed during that execution, consistent with "portal_lost wins on every write path"; only HMI sets that outcome. Task 7 docs (AGENTS.md / ARCHITECTURE.md) must describe the filter accordingly.
  - `hmi_read` messages unchanged: pass; `HmiReadLog.Guard` and `HmiTagLookup.FindUnique` take `describe=false` by default and only write paths pass `true`.
  - `output-schemas.json`: unchanged (not in the diff), so no change outside the `hmi_write` key; `OutputSchemasMatchMain` passed.
  - No new conditional member outside the register: pass; only a comment edit in `ConditionalMemberRegisterTests`, no new member.
  - No `[LegacyNullOmission]` change: pass; none in the diff.
- **Numstat, production (`TiaMcpServer`, `.OpennessWorker`, `.Contracts`, `.FakeWorker`):** +215 / -564. Largest: `HmiWriteVerifier.cs` 0/277 (deleted), `HmiWritePlanner.cs` 66/185, `HmiWorkingState.cs` 39/42, `HmiVerificationPolicy.cs` 40/0 (new), `HmiWriteRoundtripScenario.cs` 30/6, `HmiWriteDomain.cs` 9/18, `HmiWriteModels.cs` 3/11, `HmiWriteRun.cs` 8/2; the remaining files change at most 8 lines each (`HmiAlarmWriter.cs` 4/4 and `HmiTagLookup.cs` 4/4, 8 total each; `HmiWritePayloadBudget.cs` 2/3, 5 total). Whole branch incl. tests and docs: +7,367 / -1,386 over 71 files.
- **Findings:** none.

### Task 6 — resumed 2026-10-30 (in progress)

Installed candidate `96a28b7`, version `4.2.0-local.112.g96a28b7`, includes the post-Task-5 review fixes. Installation was already complete when this phase resumed. Installed SHA-256: host `c01713aa2394e6159e0401e3200ac7d2de51c6295916f655af6700188e191fa8`, worker `50f24a4fa9f84da2c10b0485adbab66e6dbf1b71c80f45de4ed21335415dc736`. The direct installed-tool status probe established a clean, open standalone owner whose saved-file hash matches the checkpoint before the serial live matrix began. Persisted Portal GUI language is `en-US`; maintainer UI observation and compilation remain pending.

**R1 — outcome-bearing refusals can supersede.** Independent branch review found that HMI text mapping can fail `target_not_found` after a rename, other setters or partial text assignment. The shared verifier previously treated that category alone as proof of no mutation and left earlier writes incorrectly `trusted`. It now also considers a non-null item result as evidence of possible mutation, preserving the public error and partial outcome while marking earlier writes `superseded`. A refusal without a result keeps the existing behavior. This is conservative when an outcome-bearing refusal happened not to change anything; it adds no Portal rule or read-back. Two new regressions failed with `trusted` instead of `superseded` before the fix; the 25-test focused verification passes after it. The serial stub build passes with 0 errors and the existing 7 warnings. The full default suite passes: 7,642 passed, 0 failed, 0 skipped (6 m 40 s). Independent review of the focused fix found no further issues. At this checkpoint the live matrix still used installed `96a28b7`; R1 had not yet been installed or live-qualified.

**Standalone matrix complete on installed `96a28b7`.** All 31 P variants match the checkpoint: 21 O1 native rejections (exact exception type and message), three O2 accepted-but-invalid alarms reported by public validation, five accepted controls and two `target_not_found` worker refusals. P22 reports cleanup `deleted`. L1 completes all 11 items covering all nine operations; L2 completes five items, rejects the duplicate and skips six; L3 is a non-mutating preview; L4 completes both referenced deletions with informational guards. Its four successful cross-reference reports alone list all four expected alarm trigger users. All 35 cases have matching audit evidence and either verified unchanged state or close-without-save/reopen restoration. The [case input](../acceptance/reports/inputs/2026-10-30-hmi-write-native-rejections-live-cases.md) links the privacy-redacted archive. A separate post-matrix public validation of the 15 children from the table-only cases finds no issues on the restored baseline; it is not a native table validator. Maintainer UI compiles remain pending, so this is not a complete Task 6 acceptance ruling.

**R2 — qualify the R1 delta separately.** Keep the full matrix attributed to build 112 and qualify R1 with a focused live replay on build 113. Independent review compared all 35 canonical case documents and all 72 earlier-success/later-attempted pairs: zero differences in the old/new supersession admission predicate for those captured batches. The outcome-bearing P22 refusal is a singleton; the L2 rejection already qualifies under either predicate. The source delta changes no object keys, schema, worker logic or native call. This supports scoped reuse, not a full build-113 matrix rerun or proof about arbitrary workloads, races or native faults.

**Fixture qualification before timing.** The two required P4/L2 regression tests are pinned after their captures arrive, while L3/L4 finish; the full suite runs before the benchmark so it cannot contend with measured rounds. Both FakeWorker message constants already match the tool captures byte-for-byte, so their values need no change. The focused HMI MCP suite passes 33 tests; the full default Release suite passes 7,644 tests, 0 failures, 0 skips (6 m 37 s). CI coverage is 0.9505 (12,311 / 12,952), above 0.80. Independent fixture review finds no issues.

**Fixed candidate installed.** The real-assembly package and package verifier pass for `4.2.0-local.113.g31d1a44` (R1 source `31d1a44`). Installation through the existing script requires restarting six idle Codex tool hosts; their parentage, executable and absence of Openness workers are checked first. Installed SHA-256: host `369695059fa9da0b0f149d79c0beae3359d4afa74c28b78af03761f8536749b0`, worker `52c8e7de27ef6eb9d93832667baf6033737643610118cf29cf265e36015a797c`. Focused R1 live verification, timing, L5 and UI compile evidence are still pending at this record.

**Updated-worker connection unresolved.** The focused R1 client times out after 900 seconds on its first public `get_project_status` call. It has not bound or dispatched any write. The worker started, but the client received no status response; the cause is unconfirmed. The maintainer is asked to check Portal for an Openness access prompt or another modal dialog. No replay is attempted. The prepared timing replay and L5 remain unexecuted, and Task 7 and the requested push/PR are deferred until the remaining Task 6 evidence can be collected. Local commit `9954ea6` preserves the verified fixtures and standalone evidence; only its input links are indexed now, without rewriting current operation documentation ahead of Task 7.
