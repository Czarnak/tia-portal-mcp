# `hmi_write` — design

*2026-10-13. Builds on [the `hmi_read` design](2026-10-07-hmi-read-design.md), its
[completion track](2026-10-11-hmi-read-completion-design.md) (Appendix A is this track's handoff) and the
[live write spike](../acceptance/reports/2026-10-13-hmi-write-spike.md), whose parameter table and Q12
(maintainer answers) are the factual basis for every Openness behavior stated here.*

## 1. Intent

Give an agent PLC→HMI data plumbing on WinCC Unified: in one guarded call it can create a tag table,
HMI tags bound to PLC tags, and discrete and analog alarms on those tags, with a dry run that shows
exactly what will change and a read-back that proves what did.

**Success:** one `hmi_write` call creates a table, bound tags (global tag, DB member, UDT instance), an
internal tag and both alarm kinds; its dry run reports the same effects; verification reads every object
back and runs `Validate()`; the editor shows the written texts correctly.

**Decided during brainstorming (2026-10-13):**

| Decision | Reason |
| --- | --- |
| Spike first, then this spec | Spike answers shaped the schema (binding order, text forms, name scopes) |
| Typed operations on the `plc_write` pipeline (approach A) | Openness properties are order- and kind-dependent; a property bag would hide that; macros would blur the dry run |
| No HMI connection creation | The spike showed Openness never creates one when a PLC tag is bound; a bound tag needs an existing connection |
| Callers send plain text only; the server formats `eventText` | `EventText` must be formatted while `InfoText` and `Comment` must be plain (editor-verified) |
| Deleting a still-used tag is an info guard | Matches `plc_deletes_block`; v1 cannot edit screens or scripts, so a block would make such tags undeletable |
| Renaming needs no guard | References follow a rename (maintainer, spike Q12) |
| HMI locate cache (follow-up O1) is part of this track | About 2 s per locate × 50 items per call; live acceptance measures its memory cost |
| UDT binding is ordinary binding; acknowledgment tags deferred | Maintainer scope call |
| `hmi_read` tag and alarm rows gain the values verification needs | `displayName`, `plcName`, `triggerBitAddress` and `triggerAddress` are written or derived here but no `hmi_read` row returns them (§6.1) |

**Out of scope:** HMI connection create/edit, tag-table group create/delete/rename, moving a tag between
tables (Openness cannot), unbinding a tag, `accessMode`, `acquisitionMode`, `EventText1`–`EventText9`,
alarm-text parameter fields (`<field…>`), acknowledgment tags, logging, screens, Classic HMIs.

## 2. Contract at a glance

- New tool `hmi_write(operations, dryRun=false)`, registered in read-write and full only. Tool counts
  move from 7/16/16 to **7/17/17**. Read-only mode does not register it and denies every new worker method.
- Root `contractVersion:"1.0"` on the shared canonical JSON contract (`StructuredToolResult`,
  `StructuredOperationBatch`); text and structured content come from one `CanonicalJson.Serialize`.
- Safety model identical to `plc_write`: one guarded single-call pipeline (validate, verified binding,
  resolve, guards, `dryRun`, mutate, verify, audit); up to 50 items in caller order; stop at the first
  failure; no rollback across items; `ConfirmsEveryCall=false`; no acknowledge guards; no elicitation;
  audit v2 confirmation `none` in read-write and `policy` in full.
- Works on standalone projects and on `.als21` local sessions through `WithEngineeringRoot`, as PR6 did
  for PLC writes. Persisting changes stays with `save_project`.
- `hmi_read` changes additively (§6.1): its contract version is unchanged.

## 3. Request schema

Every item carries `operationId` (unique, at most 256 characters), `operation`, and the optional
`hmiName` and `projectPath` with `hmi_read`'s rules (`hmiName` may be omitted only when the project has
exactly one Unified HMI; all items target one project). Fields outside an operation's lists are rejected
before any worker call.

| Operation | Required | Optional |
| --- | --- | --- |
| `create_tag_table` | `tableName` | `groupPath` (existing group, `hmi_read` syntax) |
| `update_tag_table` | `tableName`, `newName` | – |
| `delete_tag_table` | `tableName` | – |
| `create_tag` | `tableName`, `tagName`, and either `connection` + `plcTag` or `dataType` | `comment`, `displayName`, `acquisitionCycle` |
| `update_tag` | `tagName` | `newName`, `plcTag`, `connection` (only with `plcTag`), `dataType` (internal tags only), `comment`, `displayName`, `acquisitionCycle` |
| `delete_tag` | `tagName` | – |
| `create_alarm` | `alarmKind`, `alarmName`, `triggerTag`; analog also `condition`, `conditionValue` | `alarmClass` (default `Alarm`), `priority`, `eventText`, `infoText`; discrete only `bitNumber` (default 0), `triggerMode` |
| `update_alarm` | `alarmKind`, `alarmName` | `newName` and any `create_alarm` field valid for the kind |
| `delete_alarm` | `alarmKind`, `alarmName` | – |

**Field rules**

- **Bound or internal.** A tag is bound (`connection` and `plcTag` together; `dataType` rejected, because
  Openness derives it and refuses a set) or internal (`dataType` required on create). The planner tells
  them apart from `list_tags`: an internal tag's connection reads `<Internal tag>`. `update_tag` with
  `plcTag` alone requires a bound tag; `dataType` on update requires an internal tag.
- **`plcTag`** accepts quoted (`"DB".Member`) or unquoted (`DB.Member`) spellings; results report TIA's
  unquoted dotted form.
- **`acquisitionCycle`** is one of `T100ms`, `T250ms`, `T500ms`, `T1s`, `T2s`, `T5s`, `T10s`.
- **Tags are addressed by `tagName` alone** (names are HMI-wide); `tableName` only says where a new tag goes.
- **`triggerTag`** names an HMI tag, or a member of a UDT-bound tag as `Tag.Member` (spike Q12).
- **`alarmKind`** is `discrete` or `analog`. `triggerMode` is `OnRisingEdge` or `OnFallingEdge`.
  `condition` is one of `LowerLimit`, `UpperLimit`, `Equal`, `NotEqual`, `LowerLimitOrEqual`,
  `UpperLimitOrEqual`. `priority` is 0–255. `bitNumber` is 0 or greater (Openness checks the upper bound
  against the trigger's type at set time).
- **`conditionValue`** is a JSON number: an integral value in `Int32` range (including `50.0`) is sent as
  `Int32`, any other as `Double` (Openness accepts only those two types).
- **Texts.** `comment`, `displayName`, `eventText` and `infoText` are arrays of `{culture, text}` (the shape
  `hmi_read` returns). Only listed cultures are written. A repeated culture is `validation_error`; a
  culture that is not a project language is `target_not_found`, as in `hmi_read`. Callers always send
  **plain text**: a value starting with `<body` is rejected ("send plain text; the server adds markup"),
  which catches pasting a raw `hmi_read` text back. The server XML-escapes `eventText` and wraps it as
  `<body><p>…</p></body>`; a line break in `eventText` is rejected. `infoText`, `comment` and
  `displayName` are written as given.
- **New names** (`tableName`, `tagName` and `alarmName` on creates, and every `newName`) are rejected when
  blank, padded with whitespace, or containing control characters, `.` or `"`. Openness accepts these, but
  a dot collides with the `Tag.Member` notation used by `hmi_read` and the `{hmi}` cross-reference
  selector. Names that select existing objects are not checked against these rules and match
  case-insensitively, as Openness `Find` does.

## 4. Planning

`HmiWritePlanner` mirrors `PlcWritePlanner`: public reads only, a working state that applies each item so
later items see earlier creates, renames and deletes.

**Evidence, once per HMI per call:** `list_hmi_devices`, `list_project_languages`, `list_tag_tables`,
`list_tags` read to the end at `limit` 2000 (names, tables, connection, `plcTag`), `list_connections`,
`list_alarm_classes`, and `list_alarms` for both kinds read to the end. A `list_hmi_devices` row whose
kind is not `unified` stops the call with `target_kind_unsupported` at plan time. When `hmiName` is
omitted, the planner resolves the single Unified HMI from that evidence and sends its explicit name to
every worker method. The planner's paged reads take snapshot slots like any reader and can evict a
caller's older `hmi_read` cursors (the store holds 4); a mutation drops that HMI's snapshots anyway.

**Delete evidence.** For every pre-existing top-level tag a call removes, directly or inside a deleted
table, the planner runs the `{hmi}` cross-reference read used by `read_cross_references` for its `UsedBy`
references (screens, scripts, alarms). Alarm references also come from the `list_alarms` evidence (a
trigger equal to the tag or starting with `Tag.`), which covers member triggers and alarms created
earlier in the same call. Each such tag costs one query; a call needing more than 200 blocks with
`hmi_state_unverifiable`. A table holding more than 200 tags therefore cannot be deleted in v1; the
caller deletes its tags over several calls first.

**References.** A `triggerTag` of the form `Tag.Member` is checked with one `get_tag` on `Tag`, whose
`members` must contain the path.

**Plan-time failures** stop the call before any mutation: `validation_error` for schema and field rules;
`target_not_found` / `target_ambiguous` for the item's target (table, tag, alarm, group) and for every name
it refers to (`connection`, `alarmClass`, `triggerTag`). Openness itself accepts missing `connection`,
`alarmClass` and `triggerTag` names and reports them only in `Validate()`, so the planner is the first line.

**Dependent items.** An item depends on an earlier item of the same call when its target or a name it
refers to (`tableName`, `tagName`, `triggerTag`, `alarmName`) exists only because that item creates or
renames it. Such items carry `dependsOn` and are re-planned just before their own mutation, as
`plc_write` does. `ReplanAsync` re-reads only what the item uses: `list_tag_tables`, and `get_tag` or
`get_alarm` on the target, on the new name (collision) and on each referenced name. A miss is
`target_not_found`; late guards go through `GuardDecisions.DecideLate`; a failed re-plan stops the call.
A dependent delete re-queries only its own tags, which the plan-time count already covers, so re-plans
do not add to the 200-query cap.

**Guards** (`HmiGuardDefinitions`):

| Guard | Level | Fires when |
| --- | --- | --- |
| `hmi_name_collision` | block | A create or rename would collide case-insensitively: tags HMI-wide, tables across all groups, alarms within one kind. A rename never collides with its own object, so a case-only rename is allowed |
| `hmi_state_unverifiable` | block | An evidence read was incomplete, or a call's deletes need more than 200 cross-reference queries |
| `hmi_deletes_table_contents` | info | `delete_tag_table` also deletes its tags; the effect lists them |
| `hmi_deletes_referenced_tag` | info | A deleted tag is used by other objects (alarms, screens, scripts); the effect lists them, since Openness leaves them dangling |

**What a dry run cannot promise, and how it is reported:**

- **PLC tag existence.** No public read covers DB members; the `PlcTag` setter rejects a missing PLC tag
  at mutation time, and the create cleanup (§5) leaves nothing behind. The effect makes no claim about it.
- **The default tag table.** No Openness member identifies it and it can be renamed; its delete fails at
  mutation time ("Hmi tag table could not be deleted"), mapped to a clear item error. No name-based guard.
- **Derived values** (`dataType` of a bound tag, `plcName`, `triggerBitAddress`, `triggerAddress`) are
  marked derived in the effect; verification reports the real values.

The effect of each item names the target identity, the action, the normalized requested values (with the
wrapped `eventText`), the derived markers, and for deletes the removed tags and their references.

## 5. Execution

**Worker methods.** Nine methods, `hmi_create_tag_table` … `hmi_delete_alarm`, mapped through
`HmiOperationCatalog.WorkerMethod`, each with an `OperationPolicyCatalog` `ProjectMutation` row.
`ProjectCapabilityCatalog.OperationFor` maps these nine names to a new `hmi_write` content operation and
every other `hmi_*` name to `hmi_read` (closing the Appendix A hazard), with matching
`ProjectContentOperations` entries; `LocalSessionContextContractTests` covers both. Parameters travel in a
new nested `WorkerRequest.HmiWrite` DTO in `TiaMcpServer.Contracts`; the host counterpart is the
`hmi_write` request type. Every handler runs inside `WithEngineeringRoot`.

**Setter order.** Typed setters, never `SetAttributes` (the spike showed it all-or-nothing with no
per-attribute errors). Texts are written per culture through `MultilingualTextItem.Text`.

| Object | Create | Update |
| --- | --- | --- |
| Table | `Create(name)` in the root or group composition | `Name` |
| Tag | `Create(name, table)` → `Connection` → `PlcTag` → `DataType` (internal) → `AcquisitionCycle` → `Comment` → `DisplayName` | `Name`, then the create order for supplied fields |
| Discrete alarm | `Create(name)` → `RaisedStateTag` → `RaisedStateTagBitNumber` → `TriggerMode` → `AlarmClass` → `Priority` → `EventText` → `InfoText` | `Name`, then the create order for supplied fields |
| Analog alarm | `Create(name)` → `RaisedStateTag` → `Condition` → `ConditionValue` → `AlarmClass` → `Priority` → `EventText` → `InfoText` | as discrete |

The trigger precedes the condition value because an analog alarm without a trigger rejects it (spike Q6).

**Failure.** An item stops at the first failing setter. An Openness rejection maps to
`worker_operation_failed` with Openness' message, as in `plc_write`. The typed item result carries
`appliedProperties` (in order), `failedProperty` and, for creates, `cleanup`:

- **A failed create leaves nothing behind.** The worker looks the object up again with `Find` and deletes
  it if it exists; `cleanup` reports `notNeeded`, `deleted` or `failed` with the message. Verification
  checks the object is absent.
- **A failed update** keeps the properties already applied; verification reads the object back.

**After a mutation** the worker drops that HMI's `HmiSnapshotStore` entries, so an `hmi_read` cursor never
pages over changed data.

**HMI locate cache (O1).** `HmiSoftwareLocator` keeps the located `HmiSoftware` per engineering root and
explicit `hmiName`. A hit is used only after a liveness check: the cached software's name and its
device's name are read again and must still match the selector; an exception or a mismatch evicts the
entry and locates afresh. A null selector ("the only Unified HMI") is never cached, because adding an HMI
would change its answer unseen; `hmi_write` always sends an explicit name (§4). The cache keeps the root
it was filled for and empties when the current root differs (`object.Equals`): binding change, lifecycle
call, worker restart. Reads and writes both use it.

## 6. Verification and response

`HmiWriteVerifier` runs after the last attempted item, through public reads only:

- **Tables** are verified by `list_tag_tables` read-back alone (name, group, tag count); `validate` has no
  tag-table category.
- **Tags and alarms** created or updated are read back with `get_tag` and `get_alarm` without `language`
  and compared with the normalized request: exact-case names (`Find` is case-insensitive), unquoted
  `plcTag`, each written culture's text, wrapped `eventText`, `conditionValue` type and value. Derived
  values are reported, not compared.
- **`Validate()`.** Each such tag or alarm goes through `hmi_read validate` (`category` `tags` or `alarms`,
  with its `name`). Discrete and analog alarms can share a name, so findings are filtered by `objectKind`:
  `HmiDiscreteAlarm` or `HmiAnalogAlarm`, the CLR type names `HmiValidationReader.KindOf` emits. Any error fails
  verification with `postcondition_failed` and Openness' message (plain-struct binding, Real trigger on a
  discrete alarm); warnings are reported without failing.
- **Deleted objects**, the tags of a deleted table, and objects of a failed create are verified absent.
- An object changed again by a later item is marked `superseded`.

### 6.1 `hmi_read` additions

Additive members, written as explicit nulls when unreadable, decoded through the worker-payload reader:

| Row | New member | Source |
| --- | --- | --- |
| `HmiTagRowInfo` (`list_tags`, `get_tag`, members) | `displayName` (`[{culture, text}]`), `plcName` | `DisplayName`, `PlcName` |
| `HmiAlarmRowInfo` (`list_alarms`, `get_alarm`) | `triggerBitAddress` (discrete), `triggerAddress` (analog) | `TriggerBitAddress`, `TriggerAddress` |

Members that apply to one alarm kind are null on the other, like `triggerMode` and `condition` today. W0
read all four without error.

### 6.2 Response

The response follows `plc_write`: phases `preview`, `applied`, `blocked`, `error`; per item status, effect,
fired guards, a typed result per operation and `dependsOn`; root `verification` with typed checks and
explicit nulls; attempted mutation or verification failure is `success:false`, `error:null`,
`isError:false`. Worker payloads are typed per operation through the worker-payload reader; new
conditional members join `ConditionalMemberRegisterTests`. Budgets: 180,000 characters per response,
60,000 per value. One audit v2 record per call, including dry runs and blocked calls.

## 7. Testing and live acceptance

### 7.1 Offline (TDD per task)

- **Stubs.** The WinCC Unified stubs in `reference-stubs/Siemens.Engineering.WinCCUnified/*.cs` are
  hand-written: add every member the worker uses (setters, `Create`, `Delete`, `Find`,
  `MultilingualTextItem.Text`, the new read members), refresh the hash with
  `scripts/verify-reference-stubs.ps1`, and mirror the changes in
  `TiaMcpServer.Tests/TestUtilities/Hmi*SiemensDoubles.cs`.
- **Catalog.** Field sets per operation; bound versus internal; text rules (plain only, `<body` rejected,
  project cultures, duplicates, line breaks); new-name rules versus selectors; `acquisitionCycle` values;
  `conditionValue` typing (`50`, `50.0`, `50.5`, out of `Int32` range).
- **Planner** with faked reads: case-insensitive collisions per scope, case-only rename, `dependsOn` and
  re-plan misses, each guard, the 200-query cap, incomplete evidence, missing references, member
  triggers, Classic rejection, omitted `hmiName` resolution.
- **Worker** with stubs: setter order per object, create cleanup outcomes, snapshot drop, cache hit,
  liveness miss after an HMI rename, no caching of a null selector, invalidation on root change.
- **Verifier:** normalization, `Validate` errors versus warnings, `objectKind` filtering, `superseded`,
  deletes, failed-create absence.
- **`hmi_read` rows:** the new members in payload decode tests and the `hmi_read` conformance probes.
- **Registration:** read-only refuses the tool and every method; `OperationFor` maps the nine writes to
  `hmi_write` and other `hmi_*` names to `hmi_read`; `ToolOutputContractConformanceTests` success and
  rejection probes; FakeWorker IPC; budget tests. New host files are added to `TiaMcpServer.Tests.csproj`.
  At least 80% coverage of changed logic.

### 7.2 Fixtures (disposable copies, prepared by hand)

A copy with an S7-1500 class PLC, a Unified Comfort HMI with an integrated connection and existing alarms
and screens (the spike's fixture qualifies), plus one `.als21` local session from the Multiuser fixtures.

### 7.3 Live cases (installed tool; read-write and full)

| # | Case |
| --- | --- |
| L01 | One call: table, bound tags (global, DB member, UDT), internal tag, discrete alarm on a UDT member, analog alarm; dry-run effects equal applied effects; verification passes |
| L02 | Collisions (tag, table, alarm; case variants) block in dry run and actual; a case-only rename succeeds |
| L03 | Delete a tag used by an alarm: info guard lists it; the alarm is left dangling |
| L04 | Delete a table: info guard lists its tags; they are gone afterwards |
| L05 | Missing PLC tag: item fails, `cleanup` reports, nothing created |
| L06 | Plain-struct binding: verification fails with Openness' `Validate` message |
| L07 | Default tag table delete: clear item error, nothing else changed |
| L08 | Texts written by the tool, `displayName` included, render correctly in the editor (maintainer check) |
| L09 | One write on an `.als21` local session, then `save_project` |
| L10 | Read-only mode: tool absent, worker methods denied |
| L11 | Rename a tag used by an alarm and a screen: references follow; rebind a tag to another PLC tag on the same connection |
| L12 | Timing (below) |
| L13 | Memory (below) |

**L12 timing.** Baseline is the currently installed release, which has no locate cache; candidate is the
new build. On the same fixture: three `hmi_read get_tag` calls and one 20-item `hmi_write` dry run, each
timed per call. The candidate's first call after binding is the cold case; later calls are warm.

**L13 memory.** `Get-Process` working set and private bytes of the worker and the host, baseline and
candidate, at four points: after `bind_project`; after ten cached `hmi_read` calls; after a 50-item
`hmi_write`; after a binding change that empties the cache. Three repetitions, median reported.
Proposed acceptance: the candidate's worker private bytes exceed the baseline's by at most 20 MB at
points 2 and 3, and point 4 returns within 10% of point 1. The maintainer decides whether the cache stays.

### 7.4 Documentation (last task, after live acceptance)

`HMI_OPERATIONS_SUMMARY.md` (write operations, guards, limits, the new read members), README (tool list,
7/17/17), AGENTS.md (counts and the "`hmi_read` is read-only" line), `ARCHITECTURE.md`,
`IMPROVEMENT_LOG.md` (close O1, add follow-ups) and the `docs/README.md` index.
