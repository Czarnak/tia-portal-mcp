# hmi_write native rejections and shared trusted verification — design

**Status:** implemented and qualified within recorded R1–R3 scope, 2026-10-30. The
[acceptance report](../acceptance/reports/2026-10-30-hmi-write-native-rejections-live-verification.md)
retains version boundaries, compile waivers and broader native-fault limits.
The [checkpoint probes](../acceptance/reports/2026-10-29-hmi-write-checkpoint-probes.md)
and [implementation plan](../plans/2026-10-29-hmi-write-native-rejections.md) preserve the execution record.
At the pre-refactor analysis baseline, `hmi_name_collision`, bound-tag refusals and existence checks
stopped P1–P6, P8, P9, P11, P15–P22 before Portal was reached. P7, P10, P12, P13 and P14 were
reachable; P13 was incorrectly listed as blocked and P21/P22 as reachable in the draft header.
The native checkpoint used a direct probe to bypass those host checks. Branch
`refactor/hmi-write-native-rejections`, cut from `main` at `0b2640a` (merge of the Network
native-rejections PR 202).

**Predecessors:** the [PLC plan](../plans/2026-10-25-plc-write-native-rejections.md) and
[Network design](2026-10-26-network-write-native-rejections-design.md), with their acceptance
reports ([PLC](../acceptance/reports/2026-10-26-plc-write-native-rejections-live-verification.md),
[Network](../acceptance/reports/2026-10-29-network-write-native-rejections-live-verification.md)).
This is the HMI follow-up both left open (`docs/IMPROVEMENT_LOG.md`: "HMI and VCI adopt the same
error contract", "HMI and VCI onto the shared verifier").

## 1. Intent and why

`hmi_write` re-implements Portal. Before every call the planner pages the whole tag and alarm
inventory of the HMI (2,000 rows per page), the connection, alarm-class, culture and table lists,
and member details of trigger tags, so it can refuse name collisions, missing references and
bound-tag rules itself. Before every dependent item it re-reads its targets. After the call it
re-reads every written object and runs `Validate()` on each tag and alarm.

At the same time Portal's own answer is lost. `HmiWriteRun.Failure` rewraps every Siemens exception
as `worker_operation_failed` with the message text only, so `hmi_write` never reports
`engineering_rejected` or `engineeringError`, and because the wrap carries no `EngineeringError`,
`WorkerExecution` never re-checks the Portal disposal revision: a Portal lost mid-write surfaces as
`worker_operation_failed` and the binding stays verified.

Maintainer decisions taken in the design discussion on 2026-10-29:

1. **Guard rule.** The server guards only against writes Portal neither rejects nor reports
   afterwards. Anything Portal rejects (O1) or reports after the fact (O2) is left to Portal. The
   precedent is the Network subnet rename (L3): Portal silently dropped a rename to a taken name and
   the tool returned success.
2. **The after-the-fact check for HMI is `hmi_read validate`.** `compile_check` compiles PLC
   software only; Openness documents no compile target for Unified `HmiSoftware`. `Validate()`
   returned "The object "X" does not exist. Please update or delete the reference." for dangling
   connection, alarm class and trigger tag in the write spike. Every live probe also records the
   maintainer's HMI device compile in the TIA Portal UI, so a case silent in `validate` but reported
   by compile, or silent in both, is known.
3. **Removed as well:** the host refusals of `dataType` on a bound tag and `plcTag` without a bound
   tag or connection (Portal's setters reject both), and the connection, alarm-class and trigger-tag
   existence checks (Portal accepts; `Validate()` reports).
4. **Verification moves to the shared `TrustedWriteVerifier`**, like PLC and Network. The re-read
   and per-object `Validate()` after a write go away.
5. **Approach A** (§5): one PR in the Network task order, with an early live probe checkpoint
   after the guard removal and before the planning and verifier changes.
6. Live testing must be thorough: Network showed that some Portal failures are not reported like
   others. Every probe records Portal's actual state independently of the tool's response.

## 2. Goals, non-goals, anti-goals

**Goals**

- Every Siemens exception during an `hmi_write` mutation is a typed item failure: `engineering_rejected`
  for `EngineeringTargetInvocationException`, `worker_operation_failed` otherwise, both with the
  verbatim `engineeringError`, and the existing `hmiWriteOutcome` (`appliedProperties`,
  `failedProperty`, `cleanup`) kept.
- `portal_lost` wins over the HMI wrap, and keeps the `hmiWriteOutcome` as evidence.
- Guards and pre-checks keep only what Portal cannot provide: delete consequences
  (`hmi_deletes_table_contents`, `hmi_deletes_referenced_tag`), the unreadable-consequence block
  (`hmi_state_unverifiable`, narrowed), update/delete target resolution, and the server's own
  request contract (name rules, text rules, enum and range shape).
- Planning reads only what those need. No full tag or alarm inventory for creates and updates. No
  replan reads. No post-write reads.
- Completed items are `trusted`; items a later item changed again are `superseded`; through the
  shared verifier.
- `hmi_write` moves to write contract `2.0`; `hmi_read` stays `1.1`.
- Before/after timing on one disposable project, a live probe matrix that captures every native
  rejection verbatim and Portal's real state, and documentation written last from that evidence.

**Non-goals**

- `vci_write` (its own follow-up).
- Changing `hmi_read`, its cursors, snapshot store, `validate` shape or the read contract.
- The `HmiSoftwareLocator` cache (O1 stays open by maintainer decision).
- An HMI compile tool. `compile_check` keeps its PLC-only contract.
- New worker methods or public operations; `OperationPolicyCatalog` and
  `ProjectCapabilityCatalog.OperationFor` are unchanged.

**Anti-goals**

- A server-side check that re-implements a Portal rule Portal enforces (O1) or reports (O2).
- A host check that is silently replaced by nothing when the probe shows O3. An O3 finding gets a
  guard or an adjacent worker read-back by maintainer decision (§6.8); it is never left as a silent
  success.
- A failed create whose cleanup deletes an object it did not create.
- A Siemens exception in planning that surfaces as `engineering_rejected` (reads keep read
  categories).

## 3. Constraints

- Preserve the 8/19/19 tool surface, `hmi_write(operations, dryRun=false)`, 1–50 unique-ID items,
  caller order, stop-at-first-failure, no batch rollback, no server elicitation, binding lease,
  access-mode gates, audit v2, and one `CanonicalJson.Serialize` call for text and
  `structuredContent`.
- HMI worker method names, `HmiWriteMethods.All`, and the `WithEngineeringRoot` handler path are
  unchanged; local-session roots keep working.
- New conditional members go through `ConditionalMemberRegisterTests`; payload null policy through
  `WorkerPayloadNullPolicyRegisterTests`; `output-schemas.json` is regenerated with PLC and Network
  rows unchanged.
- `TiaMcpServer.Tests.csproj` links host files explicitly outside `Safety/Pipeline`: a new or
  deleted `Hmi/` file updates it.
- Build with `-m:1`; run `dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"` after each
  task; packaging tests only if a product `.csproj` changes.
- Commit after each task with a short one-line message; no push until the maintainer asks.
- Timing baseline is measured on `0b2640a` before any code change and repeated unchanged on the
  candidate.
- No TIA project names or paths in repository files or commits.
- Documentation is the last task, written from the live evidence.

## 4. What exists today (analysis baseline `0b2640a`)

| Layer | Where | What it does | Verdict |
| --- | --- | --- | --- |
| Host planning read | `HmiWritePlanner.Load`/`LoadCommon` | `list_hmi_devices`; per HMI: `list_tag_tables`, `list_project_languages`, `list_connections`, `list_alarm_classes`, all tags and all alarms paged at 2,000; `get_tag` member details for dotted trigger tags; one cross-reference query per deleted pre-existing tag (cap 200) | keep device resolution, target reads for update/delete, and the delete-consequence reads; drop the rest (§6.3) |
| Host resolution | `HmiWorkingState.ResolveEffect` | `hmi_name_collision`; existence of group, table (create_tag), connection, alarm class, trigger tag and member, culture; bound-tag rules for `plcTag`/`dataType`; unreadable-connection block; target existence for update/delete | keep target resolution and delete consequences; drop the rest (§6.2) |
| Host replan | `HmiWritePlanner.ReplanAsync`, `HmiWriteDomain.ReplanAsync` | before each dependent item: re-reads common lists, target and new name, table tags, all alarms, cross references | drop (§6.3) |
| Host verification | `HmiWriteVerifier` (277 lines) | re-reads every written table/tag/alarm, compares requested and derived fields, checks absence after deletes and failed creates, runs name-scoped `validate` per tag and alarm (errors fail `postcondition_failed`) | replace with the shared verifier (§6.4) |
| Worker wrap | `HmiWriteRun.Failure` and its five catch sites | rewrap every `EngineeringException` as `worker_operation_failed`, message only | classify by type, attach `engineeringError` (§6.1) |
| Worker pre-mutation lookups | `HmiTagLookup`, `HmiTagTableWriter.Find`/`Tables`, `HmiAlarmWriter.Find`, `HmiTextMapper` culture | resolve the handle the mutation needs; `target_not_found`/`target_ambiguous` | keep; they obtain the object, they do not re-prove Portal |
| Worker cleanup | `HmiWriteRun.Create` | after a failed tag/alarm create: identity-checked (`object.Equals`) delete of the created object | keep |
| Snapshot invalidation | `HmiWriteDispatch` `finally` | drops that HMI's read snapshots | keep |

Guards today (`HmiGuardDefinitions`): `hmi_name_collision` (block), `hmi_state_unverifiable`
(block), `hmi_deletes_table_contents` (info), `hmi_deletes_referenced_tag` (info).

Known native behavior before this work (spike `2026-10-13-hmi-write-spike.md`, live
`2026-10-14-hmi-write-live-acceptance.md`): tag and table renames into a taken name are rejected;
a direct case-only tag rename fails at the `Name` setter; the default table delete is rejected;
`PlcTag` for a missing PLC tag or without connection is rejected; `DataType` on a bound tag is
rejected ("Set is not allowed for disabled fields"); dangling connection, alarm class and trigger
tag, plain-struct binding and Real trigger tags are accepted and reported by `Validate()`; deletes
cascade from table to tags and leave alarm triggers dangling. **Never observed:** duplicate
creates of any kind, alarm renames into a taken name, and any exception type except
`EngineeringNotSupportedException` for `PlcName`.

## 5. Approaches considered

**A. One PR in the Network task order, with an early live probe checkpoint (picked).** Baseline
timing; error surfacing; guard and pre-check removal; **live probe checkpoint**; planning shrink;
shared verifier and contract 2.0; offline qualification; full live run and timing; docs. The
checkpoint moves the Portal-behavior discovery that cost Network two late reworks (L3 silent drop,
L11 accepted PN name) to before the planning and verifier code is built. Trade-off: two live
sessions.

**B. Two PRs: error surfacing first, guards and verifier second.** One release-notes line each.
Trade-off: two reviews and two live runs; PR 1 alone changes visible behavior only on rejection or
Portal loss, so its live evidence is thin.

**C. Throwaway spike with guards disabled first, then design and build.** Cleanest evidence before
design. Trade-off: the same probes as A's checkpoint plus a disposable build and an extra round.

Picked **A** by maintainer decision: the merged precedent twice, with C's benefit through the
checkpoint.

## 6. Decided design

### 6.1 Error surfacing in the worker

- `HmiWriteRun.Failure(ex, outcome)`: when `ex` is an `EngineeringException`, the category is
  `WorkerExecution.Classify(ex, mutates: true)`, the message `WorkerExecution.FailureMessage(ex,
  category)`, and `engineeringError: WorkerExecution.Describe(ex)`; the `hmiWriteOutcome` is kept.
  A `WorkerOperationException` (worker refusals such as `target_not_found`) keeps its category,
  message and warnings as today. All nine methods route through it, so this is the one change.
- `WorkerExecution.Execute`'s existing `WorkerOperationException when EngineeringError is not null`
  catch also copies `HmiWriteOutcome` onto the `portal_lost` response (today it copies only
  `ConfigureNetworkDeviceOutcome`). `WorkerExecutionTests` pins both branches for HMI.
- Host: `HmiWritePayloadContract.Project` keeps the outcome on a failed item; the shared
  `WriteRun.WithEngineeringError` already copies `engineeringError`. A `portal_lost` item keeps its
  outcome for the caller's inspect step; the builder confirms the host projection accepts it.
- `portal_lost` wins on every write path, not only at `HmiWriteRun.Failure` (design call). The
  disposal re-check in `WorkerExecution.Execute` runs only for a raw `EngineeringException` or a
  `WorkerOperationException` that carries `EngineeringError`. Checked at `0b2640a`, every HMI
  write site routes Siemens exceptions through `Failure` (creates, setters, cleanup, the table
  writer's `Run` including its group walk, text writes) except one: `HmiReadLog.Guard`, used by the
  target lookups of `update_tag`/`delete_tag` (`HmiTagLookup.FindUnique`) and
  `update_alarm`/`delete_alarm` (`HmiAlarmWriter.Find`). It replaces the Siemens exception with a
  `worker_operation_failed` message and drops it. The write lookups get their own opt-in that
  attaches `Describe(ex)`; the category stays `worker_operation_failed`. `HmiReadLog.Guard` and
  `HmiTagLookup` are shared with `hmi_read` (tag reads, `validate`), whose output stays unchanged:
  the read path does not opt in. The same gap on the `hmi_read` path is recorded as an
  improvement-log follow-up, not fixed here. The worker's own refusals (missing group, table or
  culture: `target_not_found`) wrap no Siemens exception and are unchanged.
- Unchanged: non-Siemens exceptions from `Enum.Parse` and checked casts (host shape validation keeps
  them unreachable).

### 6.2 Retired and kept checks

| Check | Layer | Decision | Proving live case |
| --- | --- | --- | --- |
| `hmi_name_collision` (tables, tags, alarms; creates and renames) | host guard | retire; guard catalog drops to three | P1–P13 |
| `dataType` refused on a bound tag | host catalog/state | retire | P15 |
| `plcTag` refused without bound tag or connection | host catalog/state | retire | P16 |
| Unreadable connection blocks a `plcTag`/`dataType` update | host state | retire (it fed the bound-tag rule) | — |
| Connection exists | host state | retire | P18 |
| Alarm class exists | host state | retire | P19 |
| Trigger tag and trigger member exist (incl. member reads and the "rebind then second call" limit) | host planner/state | retire | P20 |
| Table exists for `create_tag` | host state | retire if Portal rejects or reports; otherwise keep | P17 |
| Group exists for `create_tag_table` | host state | retire; the worker's group walk already refuses `target_not_found` before `Create` | P21 |
| Culture exists | host state | retire; the worker's `HmiTextMapper` refusal stays | P22 |
| Update/delete target exists, unique | host state and worker lookups | keep (`target_not_found`/`target_ambiguous` pre-flight, PLC and Network precedent) | — |
| Name rules (`.`, `"`, control, blank/padded), text rules, enum and range shape, `conditionValue` type | host catalog | keep: the server's request contract, not Portal rules | — |
| `hmi_deletes_table_contents`, `hmi_deletes_referenced_tag` | host info guards | keep: Portal cascades and dangles without reporting | — |
| `hmi_state_unverifiable` | host block guard | narrow to: unreadable or incomplete delete-consequence evidence (table tags, cross references incl. the 200-query cap, alarm triggers, uncertain retargeted references) and an unreadable update/delete target | — |
| Failed-create identity-checked cleanup, `appliedProperties`/`failedProperty` | worker | keep | P4, P15, P16 |

An O3 finding in any probe reverses the decision for that case only, by maintainer ruling (§6.8).

### 6.3 Planning

- One `list_hmi_devices` per call to resolve the HMI.
- Per update/delete item, the target read it resolves against: `list_tag_tables` (tables),
  `get_tag` (tags), `get_alarm` (alarms). Creates read nothing.
- Delete consequences only: `delete_tag_table` lists that table's tags; a call containing
  `delete_tag` or `delete_tag_table` pages the HMI's alarms once (their `triggerTag` is how today's
  working state finds alarm triggers, which `UsedBy` cross references do not distinguish) and runs
  one cross-reference query per deleted pre-existing tag, 200-query cap unchanged (design call:
  the alarm read stays because it is consequence evidence for a kept info guard; a call without a
  delete never reads alarms). The final live run records whether cross references alone list alarm
  triggers; if they do, dropping the alarm read is an improvement-log follow-up, not this branch.
- The working-state simulation stays, without reads, so a later item can target an object created
  or renamed earlier in the call, and delete consequences include in-call creations.
- Replan is dropped: no item declares `DependsOn`; `HmiWriteDomain.ReplanAsync` fails loudly, as
  Network's does. Documented limitation: delete consequences come from planning evidence plus the
  simulation, so a reference Portal creates as a side effect of an earlier item in the call (for
  example a rename propagation it performs) is not re-read.
- Effects keep their shape: target identity, requested changes with current values where the
  target read provides them, removals for deletes.

### 6.4 Shared trusted verification

`HmiVerificationPolicy : IWriteVerificationPolicy<HmiWriteOperationRequest>` with `ReadTool =
"hmi_read"`, no read-back (`HasReadBack` false), and object keys:

- `hmi\n{HMI}\ntable\n{NAME}`, `hmi\n{HMI}\ntag\n{NAME}`,
  `hmi\n{HMI}\nalarm\n{kind}\n{NAME}`; names upper-cased (Portal names are case-insensitive).
- A rename owns and offers both the old and the new name.
- `delete_tag_table` offers the table key and the keys of the tags its effect removes, so earlier
  items on those tags become `superseded`.

`HmiWriteVerifier.cs` is deleted. `HmiWriteDomain`'s verification type is the shared
`WriteVerification`. A failed item has no verification entry; `verification.success:true` with
root `success:false` is by design, as in PLC and Network.

Accepted consequence: an input Portal accepts and only `Validate()` reports (dangling reference,
plain-struct binding, Real trigger) now returns `applied`/`trusted`. The tool description says to
run `hmi_read validate` (and compile the HMI device in TIA Portal) after writes.

### 6.5 Contract version and registers

- `HmiWriteContractVersion.Current = "2.0"`; `hmi_read` stays `1.1`.
- Root envelope unchanged in member names; `verification` becomes the shared
  `{success, operations[{operationId, success, check, expected, actual, message}], omission}` with
  checks `trusted`/`superseded` only. HMI-specific verification types and checks leave.
- Guard catalog: three guards; `HmiGuardDefinitionsTests` updated.
- Registers: `ConditionalMemberRegisterTests` (outcome on `portal_lost` if a member changes),
  `ToolOutputContractConformanceTests` probes for 2.0, `output-schemas.json`, FakeWorker scenarios
  (a rejected create with outcome and `engineeringError`; a rejected update with applied
  properties).
- Tool description: drop collision and existence claims; describe `engineering_rejected`,
  stop-at-first-failure, cleanup, `trusted`/`superseded`, and the `hmi_read validate` follow-up.

### 6.6 Timing protocol

Same protocol as PLC and Network: `full` mode, one disposable project copy (record the HMI's tag,
alarm and table counts), warm worker, first call after server start discarded, one warm-up, three
runs, medians of audit v2 root `durationMs`, item `durationMs` sum and remainder, each median
computed independently. Restoration between actual runs: close without saving and reopen, with an
untimed `hmi_read` warm-up after each reopen.

| Case | Call | Why |
| --- | --- | --- |
| H1 | one `create_tag` (internal) | smallest write; fixed planning cost of the inventory read |
| H2 | ten items: create table, three creates of tags, update a tag (rename), create discrete and analog alarm, update an alarm, delete a tag, delete the created table | typical multi-item call; inventory, replan and verification cost |
| H3 | one `delete_tag_table` of a table whose tags are referenced by alarms | consequence path whose reads stay |
| H4 | H2 with `dryRun:true` | planning alone |
| H5 | one `update_alarm` rename | update path with target read |

### 6.7 Live probing protocol

Every case runs in `full` mode on the disposable copy and records:

1. the canonical `hmi_write` document (category, `engineeringError` type and message, outcome);
2. Portal's real state through an independent `hmi_read` (`get_tag`, `get_alarm`,
   `list_tag_tables` on every object the case could have touched, including the pre-existing
   colliding object) — this is what catches a quiet failure the tool's own report cannot show;
3. `hmi_read validate` on the touched objects;
4. the maintainer's HMI device compile in the TIA Portal UI (Info > Compile messages for the
   touched objects);
5. the Portal UI language.

Classification:

- **O1** Openness throws: typed failure with verbatim `engineeringError`; becomes a FakeWorker
  fixture and a documentation example.
- **O2** Openness accepts; `validate` or the HMI compile reports it: nothing added; documented.
- **O3** Openness accepts; neither reports it, or the write is silently dropped or lands on the
  wrong object: maintainer ruling per case; default is a guard or an adjacent worker read-back
  (`postcondition_failed`), as PLC `update_tag` and Network `update_subnet` did.

Restore after each mutating case by closing without saving and reopening.

**The checkpoint is a gate** (design call). Execution stops after P1–P22 and the results go to
the maintainer. The tasks after the checkpoint are planned for the default outcome: every P case
is O1 or O2, and P17 retires. Each O3 ruling adds one focused task (a guard or an adjacent worker
read-back, with its test), and the remaining tasks are amended in the plan's execution record
before they start, as the Network plan recorded R-rulings.

**Checkpoint matrix** (after error surfacing and guard removal, before planning and verifier work):

| Case | Operation | Probes |
| --- | --- | --- |
| P1 | `create_tag_table` with an existing name, same group | duplicate table create |
| P2 | `create_tag_table` with an existing name in another group, and a case variant | cross-group, case-insensitive uniqueness |
| P3 | `update_tag_table` rename to an existing name | rename collision; landed or dropped |
| P4 | `create_tag` with an existing name, same table | duplicate tag create; does `Create` throw or return the existing object; cleanup must not delete the original |
| P5 | `create_tag` with an existing name in another table, and a case variant | HMI-wide uniqueness |
| P6 | `update_tag` rename to an existing name | rename collision |
| P7 | `update_tag` case-only rename | known native failure; capture type |
| P8 | `create_alarm` discrete with an existing discrete name, and a case variant | duplicate alarm create |
| P9 | `create_alarm` analog with an existing analog name | duplicate alarm create |
| P10 | `create_alarm` analog with an existing discrete name | control: separate scopes, expected success |
| P11 | `update_alarm` rename to an existing same-kind name | rename collision; silently ignored? |
| P12 | `update_alarm` and `update_tag_table` case-only renames | control: expected success |
| P13 | `delete_tag` then `create_tag` of a case variant in one call | in-call reuse of a freed name |
| P14 | `delete_tag_table` of the default table | known native rejection; capture type |
| P15 | `dataType` on a bound tag (update, and create with connection) | retired refusal; create cleanup |
| P16 | `plcTag` without connection (create and update) | retired refusal |
| P17 | `create_tag` into a missing table | retired existence check |
| P18 | `create_tag` with a missing connection | retired existence check; expected O2 |
| P19 | `create_alarm` with a missing alarm class | retired existence check; expected O2 |
| P20 | `create_alarm` with a missing trigger tag, and with a missing member of an existing struct tag | retired existence checks; expected O2 |
| P21 | `create_tag_table` into a missing group | worker refusal, no `Create` reached |
| P22 | `create_tag` with a comment in a missing culture | worker refusal after `Create`; cleanup |

**Final run** (on the candidate): the P cases whose behavior the later tasks could affect, plus:

| Case | Operation | Probes |
| --- | --- | --- |
| L1 | happy path mixing all nine operations | `applied`; `trusted` except superseded items |
| L2 | L1 with a P-case rejection in the middle | `success:false`, `error:null`, earlier items applied, rejected item typed with outcome, rest skipped |
| L3 | `dryRun:true` of L2 | preview; effects; no mutation; no `engineering_rejected` |
| L4 | delete of a referenced tag and of a table with referenced tags | info guards unchanged |
| L5 | on a local session root (Multiuser or Exclusive) | the PR6 path still works |

### 6.8 Documentation (last, from evidence)

- `docs/SupportedOperations/HMI_OPERATIONS_SUMMARY.md`: write sections for 2.0 (guards, removed
  refusals, `engineering_rejected` with a captured example, cleanup, `trusted`/`superseded`, the
  outcome table from §6.7, `validate` after writes).
- `AGENTS.md`: the HMI writes bullet and the failure-categories paragraph ("HMI/VCI wraps … still
  flatten").
- `docs/ARCHITECTURE.md` §7a: HMI on the shared verifier and error surfacing.
- `docs/IMPROVEMENT_LOG.md`: completed entry with line counts and timing; close the HMI halves of
  the two open follow-ups and the "rebind then second call" limit; record O3 rulings.
- `docs/README.md`: rows for this design, its plan and the acceptance report.
- `docs/roadmap/json-contract.md` and the HMI roadmap: version note.

## 7. What is left to the builder

- Exact placement and names of `HmiVerificationPolicy` and the contract-version constant.
- Which `HmiEvidence` members and `HmiWorkingState` paths survive once the retired checks go, and
  whether the working-state simulation is simplified.
- Whether an in-call alarm whose trigger tag is renamed later in the call is keyed as superseded
  (Portal propagates renames to references); decide from today's verifier's owner chaining and pin
  it with a test.
- Test reshaping in `HmiWorkingStateTests`, `HmiWritePlannerTests`, `HmiWriteVerifierTests`,
  `HmiGuardedWriteMcpTests`, `HmiWriteBudgetTests` and the worker writer tests, with the rule that
  every retired behavior gets a test proving its absence (no inventory read for a create, no replan
  read, no post-write read, a collision reaches Portal).
- Exact H2 items and selectors on the disposable project.

## 8. Risks

| Risk | Handling |
| --- | --- |
| Portal silently drops a rename or accepts a duplicate (Network L3, PLC L7 pattern) | every probe reads real state independently; O3 gets a guard or read-back by ruling |
| A duplicate `Create` returns the existing object instead of throwing; later setters would modify it, and a later setter failure would make cleanup **delete the original** (the `object.Equals` check cannot tell them apart, because `created` *is* the original) | P4/P8/P9 read the pre-existing object before and after, with a property that makes a setter fail after `Create`, on the disposable copy only. If `Create` throws, nothing is added. If it returns the existing object, that is O3 by definition and the ruling adds the protection (for example, the worker records whether the name resolved before `Create` and never cleans up an object it did not create) before any task after the checkpoint (design call: no pre-create lookup until the evidence shows it is needed) |
| Cleanup's case-insensitive `Find` matches a different pre-existing object | identity check (`object.Equals`) refuses cleanup when `find()` returns an object other than the created one; P4 confirms the original survives |
| Removing the bound-tag refusal leaves a half-applied update | stop-at-first and `appliedProperties` report it; P15/P16 capture it |
| Write success no longer means `Validate()` is clean | tool description and docs direct callers to `hmi_read validate`; accepted by maintainer decision |
| Exception types differ from PLC (`EngineeringNotSupportedException` was seen) | classification is by type; non-target-invocation types stay `worker_operation_failed` with `engineeringError` |
| Portal loss during a probe | `portal_lost` with outcome; binding invalidated; recorded |
