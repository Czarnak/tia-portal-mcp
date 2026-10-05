# PLC Read/Write Tools and Standalone Cross-References Design

**Date:** 2026-10-05

**Status:** Spec approved on 2026-10-05. PR A plan in progress; PR B is planned after PR A merges.

**Source baseline:** `main` at `430a454` (after Network PR #107).

**Delivery:** Two serial PRs from `main`, shipped one after another: PR A (reads) then PR B (writes). Each gets its own implementation plan and live TIA Portal V21 acceptance.

## 1. Purpose and agreed decisions

Retire the three legacy generic-batch tools and replace them with PLC domain tools built on the
structured JSON contract and the guarded single-call write pipeline that `network_read` and
`network_write` already use. Extract cross-reference reading into its own tool that can target
far more than a whole PLC.

This is the "batch-split redesign" that the
[write-safety redesign](2026-09-29-write-safety-redesign-design.md) §4.9 and Phase 4 delegate to a
separate spec. It inherits §4.1 (pipeline) and §4.9 (requirements) and owns tool names, schemas and
delivery order.

Agreed decisions (2026-10-05):

- **One PLC domain pair, not per-family tools.** `plc_read` and `plc_write` replace the
  `block_read`/`block_write`/`tag_read`/`tag_write` names proposed in the write-safety redesign
  §3, §4.9 and Phase 4 and in the [JSON contract roadmap](../../roadmap/json-contract.md).
- **`read_cross_references` becomes its own read tool** with a typed object selector.
- **`start_plc`/`stop_plc` are dropped**, not migrated. They are broken
  ([#74](https://github.com/Czarnak/tia-portal-mcp/issues/74)), have a physical effect outside the
  project, and do not fit project-data verification. A separate full-only online tool may follow
  once #74 is fixed; it is outside this design.
- **Network confirmation model for `plc_write`.** `ConfirmsEveryCall=false`, only `info` and
  `block` guards, zero server elicitation in read-write and full. `dryRun` never prompts.
- **Breaking field cleanup.** The content field of `update_block_logic` (today `yamlContent`,
  which carries XML or source) and of `update_type_content` (today `sourceContent`) becomes
  `content`.
- **Cross-reference targets** use the project-tree selector plus a `member` leaf. A live spike
  decides which object kinds beyond the already verified ones are shipped.
- **Two serial PRs**: PR A ships the reads and retires `execute_read_batch`; PR B ships the writes
  and retires `preview_write_batch`/`apply_write_batch` and the machinery only they use.

## 2. Scope

| Today | After |
| --- | --- |
| `execute_read_batch` (`read_cross_references`, `get_block_content`, `get_type_content`, `list_tag_tables`) | `plc_read` (`get_block_content`, `get_type_content`, `list_tag_tables`) and `read_cross_references` |
| `preview_write_batch` + `apply_write_batch` (16 operations) | `plc_write` (14 operations: the 16 minus `start_plc`/`stop_plc`) |

The 14 `plc_write` operations keep their names: `update_block_logic`, `update_type_content`,
`create_tag_table`, `delete_tag_table`, `create_tag`, `update_tag`, `delete_tag`,
`create_user_constant`, `update_user_constant`, `delete_user_constant`, `create_block`,
`delete_block`, `create_block_group`, `delete_block_group`.

Out of scope: every other tool (`network_*`, lifecycle, `browse_project_tree`, `compile_check`,
binding/status tools), new PLC operations, PLC run/stop, the major version bump and release notes
(write-safety redesign Phase 5), and the workspace/VCI work.

## 3. Tool surface and contracts

| Tool | Modes | Arguments | Annotations |
| --- | --- | --- | --- |
| `plc_read` | read-only, read-write, full | `operations[]` | `ReadOnly`, structured, `contractVersion "1.0"` |
| `read_cross_references` | read-only, read-write, full | `target`, `filter?`, `maxResults?`, `projectPath?` | `ReadOnly`, structured, `contractVersion "1.0"` |
| `plc_write` | read-write, full | `operations[]`, `dryRun=false` | `Destructive`, structured, `contractVersion "1.0"` |

Tool counts move from 5/15/15 to **6/15/15** (read-only gains one; read-write and full lose three
and gain three). Between PR A and PR B the counts are 6/16/16, because the batch write pair is
still registered. Read-write and full keep identical tool lists. The `OnlineControl` capability
stays in `OperationPolicyCatalog` with no operations until an online tool exists; `full` then
differs from read-write only in lifecycle confirmation.

### 3.1 Shared rules

- Batches hold at most 50 operations with unique, non-blank `operationId`s of at most 256
  characters, kept in caller order. Request DTOs are strict (`JsonUnmappedMemberHandling.Disallow`)
  and implement `IOperationBatchItem`.
- Responses follow the Network envelope: `tool`, `contractVersion`, `success`, `error`,
  `warnings` (always an array), explicit nulls, and `batch` for multi-item tools. Writes add
  `phase` (`preview`/`applied`/`blocked`/`error`), `guards`, `effects` and `verification`.
  `isError` is `true` only for a rejection before anything ran; a write that ran and failed
  returns `success:false`, `error:null`, `isError:false`.
- Text and `structuredContent` come from one `CanonicalJson.Serialize` call through
  `StructuredToolResult`. Each operation decodes exactly one CLR result type through the
  worker-payload reader; a mismatch is `protocol_error` and the payload is never echoed.
- Budgets are 60,000 characters per value and 180,000 per document. Oversized values are
  omitted whole with an omission record and narrowing guidance; text, XML and JSON are never cut.
- `projectPath` is an optional per-item member, as in Network. Reads never bind, switch or open a
  project; a read item whose `projectPath` differs from the bound project fails on its own with
  `binding_conflict` and the other items continue. Writes require a verified binding to the
  already-open project: differing `projectPath`s within one `plc_write` call are
  `validation_error`, and a path that differs from the binding is rejected with
  `binding_conflict` before any worker activity, as is a binding identity or revision change
  before dispatch.

### 3.2 `plc_read`

- Items run independently; a failed item does not stop the others.
- `get_block_content` and `get_type_content` keep `blockPath`/`typePath`, `format`
  (`xml`/`source`; blocks default to xml, types to source) and `withDependencies`. Their result is
  `{ format, content, contentHash, warnings }`.
- `contentHash` is `xml:sha256:<hex>` or `source:sha256:<hex>` over the exact served text, as
  today. It is an explicit `null` on `withDependencies` reads (today it is absent). When the
  content exceeds the budget, the whole item `result` is omitted with an omission record, so there
  is no hash to report; `contentHash` is therefore not a conditional member.
- `list_tag_tables` keeps `plcName` and returns an object root `{ isComplete, plcs[] }`, each PLC
  carrying its software and device name and its `tables[]`. The read becomes the planner's tag
  inventory (§4.1), so PR A extends it:
  - an omitted `plcName` reads every PLC, including PLCs in device groups (enumerated as
    `browse_project_tree` does); today it silently reads the first PLC and never sees grouped
    devices;
  - tags carry their external-access flags (`externalAccessible`, `externalVisible`,
    `externalWritable`), `null` when unreadable;
  - user constants carry a readability marker, so an unreadable value no longer looks like `""`;
  - skipped tables, groups, tags or constants mark `isComplete:false` with a message instead of
    vanishing silently;
  - optional `tableName` and `folderPath` narrow the read (amended after the final review): a
    mid-size PLC exceeds the 60,000-character value (§7), so `plcName` alone is not enough.
    `tableName` keeps only the table of that name and `folderPath` only the tables directly in
    that folder, written as the inventory emits it (`/`, `/Line/Cell`); both match
    case-insensitively, as the tree does, and combine with `plcName`. A narrowing that matches no
    table fails the item with `target_not_found`, unless part of the tree was unreadable: then the
    incomplete inventory is returned, because the target may be in the unreadable part. Omission
    guidance for `list_tag_tables` names `plcName`, `folderPath` and `tableName`;
  - `messages` is capped: the first 50 are kept and one final `... and N more messages.` entry
    summarizes the rest (`isComplete` stays `false`).
- `CrossReferenceReport` and `TagTableInfo` (and their nested types) lose
  `[LegacyNullOmission(BatchRedesign)]` and decode through the worker-payload reader; the
  null-policy and conditional-member registers are updated.

### 3.3 `plc_write` inputs

- `update_block_logic` and `update_type_content` take `content` and require
  `expectedContentHash`. A missing hash, a malformed hash, or a hash whose format tag differs from
  the write's `format` is `validation_error` before any worker call.
- Value-setting operations (`update_tag`, `update_user_constant`, …) take no hash: write-time
  exact-target resolution already refuses a renamed or deleted target.
- A content operation on a block or type created earlier in the same call is `validation_error`:
  no hash of its content can have been read.
- The SDK-entry wrapper rejects unknown root members, legacy `confirm`, `safetyToken` and
  `acknowledge`, and a non-boolean `dryRun`, with a normal MCP error and no audit record. It is a
  new parameterized strict-arguments wrapper in `TiaMcpServer/Tools/`;
  `NetworkWriteArgumentValidatingTool` stays untouched (folding it onto the shared wrapper is a
  logged follow-up).

### 3.4 Policy

Operation entries keep their capabilities: `list_tag_tables` and `read_cross_references` are
`Observe`; `get_block_content` and `get_type_content` are `TemporaryExport`; the 14 writes are
`ProjectMutation`. PR B removes the 11 `SafetyRead` entries (and the capability if it is left
empty) and the `start_plc`/`stop_plc` entries.

## 4. `plc_write` execution

`plc_write` is one `WriteExecution.RunAsync` call over a new `PlcWriteDomain : IWriteDomain`,
reusing the generic pipeline unchanged: validate → verified binding → lease → plan → guards →
`dryRun` stop → mutate in order → verify → compose → one audit v2 record.

### 4.1 Plan

Planning runs under the pinned binding lease, before any mutation. Like Network's
`read_hardware_config`, the planner uses **public read operations only** (no new snapshot reader,
no new `SafetyRead` entry). Missing evidence is added to those typed reads instead:

- one tag inventory read (`list_tag_tables`, extended in PR A per §3.2);
- one project-tree snapshot of the PLC's blocks, groups and software units, through the worker's
  existing `ProjectTreeSnapshotWalker`. PR B adds an internal skipped-node signal to the walker,
  because today it drops unreadable blocks and groups silently. `browse_project_tree`'s public
  output does not change;
- for the two content operations, one export in the write's own format, feeding the hash check and
  bounded diff evidence (`BatchPreviewDiff` is kept and moves into `Plc/`).

Resolution fails closed, as in Network and the write-safety redesign §4.1. A failure stops the
whole call before anything runs (`phase: error`, `isError: true`):

- The PLC selector must match exactly one PLC among every PLC in the project, including grouped
  devices (not today's first match). Zero matches
  is `target_not_found`; several is `target_ambiguous`.
- Object targets resolve exactly: zero matches is `target_not_found`; several is
  `target_ambiguous`.
- Block paths must be deterministic. **Deliberate fix:** a block directly under the PLC root is a
  valid path (today the snapshot reader rejects `MCP_Station/NewFC`,
  [#82](https://github.com/Czarnak/tia-portal-mcp/issues/82)).
- **Stale content:** a plan-time export whose hash differs from `expectedContentHash` rejects the
  call with `state_changed`. This keeps the redesign §4.5 category; it is a call-level rejection
  instead of an item failure only because the two-pass plan detects it before anything runs.
- Incomplete or unreadable evidence for a target does not fail resolution. It fires the
  `plc_state_unverifiable` block guard (§4.2).

**In-call overlay (closes [#79](https://github.com/Czarnak/tia-portal-mcp/issues/79)).** The planner
applies every planned item's effect to its working inventory: creations, deletions, renames and
value changes. A later item gets `DependsOn = <operationId>` of the latest earlier item that
creates, deletes or renames its target or the container it needs. This covers:

- a tag, constant or block created inside a table or group created earlier in the call;
- create then update or delete of the same tag or constant;
- a rename followed by use of the new name;
- delete then re-create of the same name.

Two creates of the same name in one call are a `plc_name_collision`. In a dry run a dependent item
reports `dependsOn` inside its PLC effect; the plan carries both the effect and `DependsOn`,
because the pipeline drops plans with a null effect. Immediately before its own mutation a
dependent item is re-planned against real state. If that late re-plan finds no target, or fires a
`block` guard, the item fails (`target_not_found` or `guard_blocked`) and the call stops. Earlier
items stay applied, with `partial_write_no_rollback` on the failed item.

### 4.2 Guards

Only `block` and `info` severities are registered, so `ConfirmsEveryCall=false` cannot be
undermined by an `acknowledge` guard. Guard definitions are concatenated into the
`WriteGuardCatalog` in `Program.cs` next to the lifecycle and Network definitions; the same
expression in the test harnesses (`McpProtocolTestHarness`, `NetworkGuardedWriteFixture`) is
updated with it.

| Severity | Guard id | Fires when |
| --- | --- | --- |
| block | `plc_state_unverifiable` | The tag inventory, project-tree snapshot or descendant read relevant to a target is incomplete or unreadable. Unreadable evidence never becomes an empty inventory. |
| block | `plc_name_collision` | A new or renamed tag, user constant, block, group or table collides with an existing or in-call object. Tags, user constants and blocks (including system blocks) share the CPU namespace, case-insensitive. Tag-table names are unique across all folders of the PLC. |
| block | `plc_block_exists` | `create_block` targets an existing block. Closes today's silent overwrite through `ImportOptions.Override`. |
| block | `plc_default_tag_table` | The call deletes the default tag table. |
| block | `plc_attribute_unreadable` | Requested external-access flags are `null` (unreadable) on the current tag in the inventory (today's host-side refusal). |
| info | `plc_deletes_block` | Always on `delete_block`; names path, type and language. |
| info | `plc_deletes_group_contents` | `delete_block_group` on a non-empty group; lists descendant blocks and groups. |
| info | `plc_deletes_table_contents` | `delete_tag_table` on a non-empty table; counts tags and user constants. |
| info | `plc_address_overlap` | The requested logical address is already used by another tag. TIA allows overlap, so this does not block. |
| info | `partial_write_no_rollback` | The pipeline's existing guard on the failed item. |

Software-unit-aware namespace collisions remain a documented limitation, as today.

### 4.3 Worker preconditions

The worker write methods are the authority at mutation time. The fail-closed rules held only by the
token snapshot readers move into them, each with its test before the reader is deleted:

- unique-PLC resolution for writes (replacing first-match `PlcSoftwareLocator.Find` on write paths);
- cross-kind name collision and tag-table uniqueness across folders;
- `create_block` refuses an existing target;
- deterministic block paths, with no fallback to the PLC root group;
- refusal of unreadable descendant blocks and unreadable user-constant values;
- `expectedContentHash` re-check: export in the write's format, hash, compare, and fail the item
  with `state_changed` before importing on mismatch.

The pure rule functions (collision keys, content-hash computation) move to
`TiaMcpServer.Contracts` so the host planner and the worker run one implementation. Contracts
targets netstandard2.0, so the hash code is rewritten without .NET 5+ APIs
(`SHA256.Create().ComputeHash` with manual hex). The host's audit hashing (`WriteRun`) keeps using
the same function. The same rule runs twice on purpose: the plan-time check makes "rejected" or
`blocked` mean "nothing ran", and the worker precondition closes the window for an edit made in
the TIA Portal UI between plan and mutation (an item failure with `state_changed` or a collision
failure, earlier items applied). Appendix A lists every rule the snapshot readers hold today and
where it ends up.

### 4.4 Effects, mutation and verification

- **Effects** per item (closes [#78](https://github.com/Czarnak/tia-portal-mcp/issues/78)): the
  resolved target identity; the change in current → requested terms for tag and user-constant
  fields and renames; where a created object will be placed; what a delete removes; bounded content
  diff evidence; and `dependsOn` where it applies.
- **Mutation** calls the existing worker write methods through a new `PlcWorkerInvoker`.
  `BlockImportOutcome` rides on the worker response envelope, mostly on failures where no success
  payload exists; a content item carries it in its typed result on success and in its typed
  failure detail on failure, keeping its conditional-member register entry. The write payload
  roots `TagMutationResultInfo`, `BlockMutationResultInfo` and `PlcTypeImportResult` lose
  `[LegacyNullOmission(BatchRedesign)]` in PR B so they decode through the worker-payload reader.
- **Verification** uses ordinary reads after the write: presence, absence and values of tags,
  user constants, tables, blocks and groups. Content operations report the import and compile
  outcome and the new `contentHash`; they do not compare against the submitted text, which TIA
  normalizes. A verification failure adds a warning and makes `success:false`.
- **Audit**: one audit v2 record per entered call in `writes-yyyy-MM-dd.jsonl`, including dry runs,
  blocked calls and validation failures; confirmation `none` in read-write and `policy` in full,
  as for Network. These tools no longer write the legacy audit stream.
- **Budget**: compact diagnostics first, then omit whole effects, evidence and checks
  (Network-style).

## 5. `read_cross_references`

### 5.1 Arguments

- `target` (required): `{ path: ProjectTreeSelectorSegment[], member?: { kind, name } }`.
  - `path` is the segment list `browse_project_tree` already emits, validated by the existing
    `ProjectTreeNodeTypes` rules. `browse_project_tree` and its node vocabulary are unchanged.
  - `member` addresses leaves the project tree does not model: `Tag` and `SystemConstant` under a
    `TagTable` path, plus any kinds the spike (§5.4) verifies.
- `filter`: `AllObjects`, `ObjectsWithReferences` (default), `ObjectsWithoutReferences`,
  `UnusedObjects`; validated on the host before any worker call.
- `maxResults` (optional, ≥ 1): caps top-level sources.
- `projectPath` (optional): target constraint only.

One target per call. A container fan-out covers breadth; batching several targets is deferred
until real use needs it.

### 5.2 Resolution

- A path ending at a leaf object (a block kind or `Type`), or any `member`, is one query on that
  object.
- A path ending at a container (`Device`, `PlcSoftware`, `SoftwareUnit`, a block, tag-table or
  type folder, `TagTable`) fans out over the cross-reference owners beneath it. `Device` or
  `PlcSoftware` reproduces today's whole-PLC sweep, with its completeness accounting kept:
  `ownerQueryCount`, `successfulOwnerQueryCount`, `isComplete`, messages. A container with no
  owners (an empty folder or table) is an empty, complete success. If owners exist and none of
  their queries succeeds, the call fails with `worker_operation_failed`, not an empty success.
- Exact selectors only: zero matches is `target_not_found`, several is `target_ambiguous`.
- A leaf without `CrossReferenceService` fails with the existing category
  `target_kind_unsupported`, distinct from "no references". Inside a sweep, an owner without the
  service is counted, marks the result incomplete and adds a message.

### 5.3 Result

- One typed document: the canonical target echo, `filter`, completeness fields, totals, and
  recursive `sources[] → references[] → locations[]`.
- `referencedAs` becomes `{ name, typeName }` of the referenced engineering object instead of its
  `ToString()`.
- `access` and `referenceType` are closed string enums using the V21 member names; the reference
  stubs gain those enum members.
- An incomplete `UnusedObjects` result keeps its documented warning that it is not proof that
  objects can be deleted.
- Budget: sources are dropped whole from the tail until the report value fits 60,000 characters
  and the document 180,000. The report carries `isComplete:false`, `omittedSourceCount` and a
  narrowing warning (`maxResults`, a narrower path, or a `member`) instead of a separate omission
  record, so the report itself is never withheld.
- Deferred: mapping `SourceObject.UnderlyingObject` back to canonical selectors (useful for
  chaining tools; needs stub work and has no consumer yet).

### 5.4 Ownership spike

PR A starts with a live V21 probe. For each candidate kind it records whether
`GetService<CrossReferenceService>()` returns a service and whether a query succeeds:
UserConstant, TagTable, PlcSoftware, SoftwareUnit, system blocks, technology objects and TO
instance DBs, Device, DeviceItem, Subnet, Node, WinCC Classic HMI tag, WinCC Unified `HmiTag`.

Already verified by repository code: OB, FB, FC, GlobalDB, InstanceDB, ArrayDB, PlcTag,
SystemConstant, PlcType. Only verified kinds ship as `member` kinds or fan-out endpoints; the rest
are listed as unverified in the PLC operations summary. The probe is throwaway evidence, not a
registered operation. Results are appended to this spec before the tool is built.

## 6. Delivery

### 6.1 PR A — reads

1. Cross-reference ownership spike (§5.4); results recorded here.
2. Read side of `TiaMcpServer/Plc/`: request DTO, catalog, payload contract, invoker, `plc_read`;
   `BatchContentHashes` moves in; `list_tag_tables` extended per §3.2.
3. `read_cross_references`: worker reader accepts the target selector through a new `WorkerRequest`
   member; payload types rewritten without legacy null omission; stubs extended.
4. Retire `execute_read_batch`: `ReadBatchTools`, the read half of `BatchTools`, the read specs in
   `BatchOperationCatalog`, and the legacy read paths of `OperationBatchExecutionEngine`,
   `OperationBatchResultFormatter` and `OperationBatchPayloadBudget`. Replace the hidden
   `ReadCrossReferencesAsync` phase probe in `ProjectTreeSafetyDedupTests` and the FakeWorker.
5. Tests: conformance register drops `execute_read_batch` and gains success and rejection probes
   for both tools; counts 5/15/15 → 6/16/16; discovery, schema and annotation lists; csproj links
   for the new `Plc/` and cross-reference files; the null-policy and conditional-member
   registers; per-item `binding_conflict` on a mismatched read `projectPath`.
6. Live V21 acceptance: every `plc_read` operation; cross-references for a leaf, a member, a
   container sweep, an unsupported target and an incomplete `UnusedObjects` result; all in
   read-only mode.
7. Documentation after live acceptance.

Between PR A and PR B, `preview_write_batch`/`apply_write_batch` keep working unchanged on their
token flow.

### 6.2 PR B — writes

1. Worker precondition port (§4.3), rule by rule with tests first; the `content` field rename; the
   shared rule functions in Contracts; legacy null omission removed from the three write payload
   roots (§4.4); the walker's skipped-node signal (§4.1).
2. Write side of `Plc/`: `PlcWriteDomain`, planner (overlay and `DependsOn`), verifier, guard
   definitions registered in `Program.cs`, budget, `plc_write` behind the strict-arguments wrapper.
3. Retire the batch write path and everything that only serves it:
   - host: `WriteBatchTools`, `BatchTools`, `BatchOperationCatalog`, `BatchOperationRequest`,
     `BatchWorkerInvoker`, `BatchSafetySnapshot`, `TagOperationSafetySelector`,
     `TagOperationSafetySnapshotContract`, `ProjectTreeSafetyPayloadContract`, the dead
     `TagUpdateSafetyCurrentState`, `OperationBatchStateComposer`, and the legacy
     `OperationBatchResult`/`OperationBatchExecutionEngine`/`OperationBatchPayloadBudget`/
     `OperationBatchResultFormatter`;
   - worker: `TagOperationSafetySnapshotReader`, `TagOperationSafetySnapshotBuilder`,
     `TagUpdateSafetySnapshotReader`, `ProjectTreeSafetySnapshotReader` and the 11
     `read_*_safety_snapshot` dispatch entries;
   - contracts: the snapshot info types, the `SafetyRead` entries, the snapshot roots marked
     `LegacyNullOmission(BatchRedesign)`, and the `BatchRedesign` reason once no root uses it;
   - `start_plc`/`stop_plc`: catalog and policy entries, `PlcOnlineService`, worker dispatch,
     client methods, the result type and FakeWorker scenarios. #74 stays open for the future
     online tool.
4. Token core: delete the token half of `WriteSafetyService`, the legacy audit writer
   (`AppendAudit` and its record shape; existing `yyyy-MM-dd.jsonl` files on disk are left alone),
   the dead `WriteSafetyTooling` and the remaining token tests, where the plan confirms no consumer
   is left. This moves those deletions from write-safety redesign Phase 5 into this PR; the version
   bump, release notes and migration note stay in Phase 5.
5. Tests: conformance register drops the last two legacy entries and the `BatchRedesign` reason and
   gains `plc_write` probes; counts 6/16/16 → 6/15/15; csproj links for the `Plc/` write files and
   the new strict-arguments wrapper; SDK-entry rejection through the real MCP binding path;
   `binding_conflict` on a binding identity or revision change before dispatch; read-only hides
   `plc_write` and denies its operations at host and worker dispatch; pipeline behaviour through
   the FakeWorker, including the late re-plan failure of a dependent item and the worker-side
   `state_changed` window; access-tier tests updated for a `full` mode without extra operations;
   the 80% coverage gate.
6. Live V21 acceptance per family:
   - tags, user constants and tables: `create_tag_table` then `create_tag` in one call, a blocking
     collision, a dry run;
   - blocks and groups: `create_block` over an existing block blocks; a delete reports its
     contents;
   - content: a missing hash is rejected with `validation_error`; an edit in the TIA Portal UI made
     after the read rejects the call with `state_changed` and writes nothing;
   - no prompts in read-write or full.
7. Documentation after live acceptance: `README.md`, `AGENTS.md`, `docs/ARCHITECTURE.md`, the PLC
   and import/export operation summaries, `docs/SupportedOperations/README.md`, the guides,
   `docs/development/local-mcp-testing.md`, the bug-report issue template, the JSON contract
   roadmap Scope row, and `docs/IMPROVEMENT_LOG.md` (including the follow-up to fold
   `NetworkWriteArgumentValidatingTool` onto the shared wrapper). An amendment note on the
   write-safety redesign records: the `plc_*` naming; the §4.3 `acknowledge` delete guards becoming
   `info` under the Network model; stale content as a call-level `state_changed` rejection at plan
   time plus the worker item failure (§4.5); and the token-core deletions moving from Phase 5 into
   this PR.

## 7. Known ceilings and risks

| Item | Handling |
| --- | --- |
| Content over 60,000 characters is omitted, so it has no hash and cannot be updated through `plc_write`. | Same ceiling as today's truncation. Recorded in the PLC operations summary. |
| A tag inventory serializes at roughly 150 characters per tag, so one PLC of about 400 tags exceeds the 60,000-character value and `list_tag_tables` is omitted whole. | Narrow with `plcName`, `folderPath` or `tableName` (§3.2); the guidance names all three. A single table larger than the value remains unreadable through `list_tag_tables`. |
| `get_block_content(format="source")` can return stale symbol names until recompiled ([#82](https://github.com/Czarnak/tia-portal-mcp/issues/82)). | Unchanged; the hash protects against concurrent edits, not against stale export. |
| A concurrent TIA Portal UI edit to a value-setting target between plan and mutation. | Exact-target resolution at mutation; residual risk as in the write-safety redesign §8. |
| Snapshot-reader rules are lost when the readers are deleted. | §4.3 ports rule by rule with tests before deletion; Appendix A is the checklist. |
| Breaking change for agents and skills using the batch tools, `yamlContent` or `sourceContent`. | Released once with write-safety redesign Phase 5 as one major version, with a migration note. |
| Merge interaction with the workspace branch. | None expected: PRs ship one after another, not in parallel. |

## 8. Non-goals

- PLC run/stop, downloads, online operations.
- New PLC operations or changes to non-PLC tools.
- Batching several cross-reference targets in one call; `UnderlyingObject` selector mapping.
- Transactions or rollback.
- Any claim that the server guarantees a human approved a write.

## Appendix A. Snapshot-reader rules and their destination

Taken from `main` at `430a454`. "Detect-only" means the rule only feeds the token's state hash
today and refuses nothing.

| # | Rule today | Where today | Kind today | Write method today | Destination |
| --- | --- | --- | --- | --- | --- |
| 1 | Exactly one PLC matches; ambiguity or incomplete discovery fails (tag readers only; the project-tree reader takes the first match) | `TagOperationSafetySnapshotBuilder`, `TagOperationSafetySnapshotReader` | refusal | takes first match (`PlcSoftwareLocator.Find`) | `target_ambiguous` / `target_not_found` at plan for every operation + worker precondition |
| 2 | Tag-table folder resolves segment by segment | `TagOperationSafetySnapshotReader` | refusal | equivalent | keep in worker; planner resolution |
| 3 | Table name unique across all folders | `TagOperationSafetySnapshotReader` | detect-only | checks target folder only | `plc_name_collision` + worker precondition |
| 4 | New or renamed tag collides with any tag, user constant or block (CPU namespace, case-insensitive) | `TagOperationSafetySnapshotReader`, `Builder` | detect-only | same table only | `plc_name_collision` + worker precondition |
| 5 | Logical-address collision across all tags | `TagOperationSafetySnapshotReader`, `Builder` | detect-only | unchecked | `plc_address_overlap` (info) |
| 6 | User-constant name collides with tag, constant or block | `TagOperationSafetySnapshotReader` | detect-only | same table only | `plc_name_collision` + worker precondition |
| 7 | Target tag or constant must exist | `TagOperationSafetySnapshotReader` | refusal | yes | keep in worker; `target_not_found` at plan |
| 8 | Unreadable user-constant value refused | `TagOperationSafetySnapshotReader` | refusal | no equivalent | `plc_state_unverifiable` + worker precondition |
| 9 | Requested external-access flags must be readable on the current tag | reader + host `ValidateRequestedTagState` | refusal | assigns directly | `plc_attribute_unreadable` + worker precondition |
| 10 | Delete tag table: full export must be non-empty | `TagOperationSafetySnapshotReader`, `Builder` | refusal | refuses default table only | `plc_state_unverifiable`, `plc_default_tag_table`, `plc_deletes_table_contents` |
| 11 | Project-tree ops need a deterministic `blockPath` | `ProjectTreeSafetySnapshotReader` | refusal | falls back to PLC root group | worker precondition; root-level blocks accepted (#82) |
| 12 | PLC first match; parent group and software unit resolved strictly | `ProjectTreeSafetySnapshotReader` | refusal | similar | keep in worker; planner resolution with unique PLC (row 1) |
| 13 | `create_block`: name occupied by a block or group in the same parent group | `ProjectTreeSafetySnapshotReader` | detect-only | none; imports with `Override` | `plc_block_exists`, plus CPU-namespace `plc_name_collision` (wider than today) + worker precondition |
| 14 | `create_block_group`: name occupied in the same parent group | `ProjectTreeSafetySnapshotReader` | detect-only | relies on Siemens throwing | `plc_name_collision` + worker precondition |
| 15 | `delete_block_group`: group exists; every descendant block exportable | `ProjectTreeSafetySnapshotReader` | refusal + hash | refuses only when not found | `plc_state_unverifiable`, `plc_deletes_group_contents` + worker precondition |
| 16 | Any block export empty → refuse | `ProjectTreeSafetySnapshotReader` | refusal | n/a | `plc_state_unverifiable` |
| 17 | Software-unit namespaces not guessed for tag collisions | `TagOperationSafetySnapshotReader` comment | limitation | n/a | documented limitation (§4.2) |

## Appendix B. Ownership spike results

Run 2026-10-05 against TIA Portal V21, project `SimpleProject_copy` (read-only: attach, read, query,
detach; nothing opened, saved or modified). Probe: `GetService<CrossReferenceService>()` on one
instance per kind, then `GetCrossReferences(AllObjects)`. A kind ships only if the service is
available and the query succeeds.

| Kind | Instance | Service | Query | Sources | Verdict |
| --- | --- | --- | --- | --- | --- |
| Block (control) | `Main` | yes | ok | 1 | verified |
| PlcTag (control) | `System_Byte` | yes | ok | 1 | verified |
| UserConstant | `heaterStages` | yes | ok | 1 | verified |
| System block | `System blocks/G7_RT_Plus_1_V6` | yes | ok | 1 | verified |
| Technology object | `PID_Compact_1` | yes | ok | 1 | verified |
| WinCC Unified `HmiTag` | `PLC_LAD_Simulation_DB_TankLevel` | yes | ok | 1 | verified (HMI scope) |
| TagTable | `Default tag table` | no | n/a | n/a | unverified |
| PlcSoftware | `ET 200SP station_1` | no | n/a | n/a | unverified |
| SoftwareUnit | `Test_SU` | no | n/a | n/a | unverified |
| Device | `ET 200SP station_1` | no | n/a | n/a | unverified |
| DeviceItem | `PLC_LAD` | no | n/a | n/a | unverified |
| Subnet | `PN/IE_1` | no | n/a | n/a | unverified |
| Node | `X1` | no | n/a | n/a | unverified |
| TO instance DB | none found under system blocks | n/a | n/a | n/a | notPresent |
| WinCC Classic HMI tag | none found via `TagFolder.Tags` | n/a | n/a | n/a | notPresent |

### Decisions

- **Member kinds** (leaves addressed by `member` under a `TagTable` path):
  `CrossReferenceMemberKinds.All = { Tag, SystemConstant, UserConstant }`. `Tag` and
  `SystemConstant` are verified by code and stay regardless; `UserConstant` is newly verified.
- **Leaf endpoints** (path-addressed, one query): the block kinds and `Type` (code-verified) and
  system blocks, which the tree already emits as `Block` under `SystemBlockFolder`.
- **Container endpoints** (path-addressed, fan out over verified owners): `Device`, `PlcSoftware`,
  `SoftwareUnit`, the block, system-block, tag-table and type folders, and `TagTable`. None of
  these is itself a cross-reference owner (service unavailable for `Device`, `PlcSoftware`,
  `SoftwareUnit`, `TagTable`); they only enumerate owners beneath them, and the owner set is the
  member kinds plus the leaf endpoints above.
- **Not shipped in PR A**: technology objects and WinCC Unified `HmiTag` (verified, but the project
  tree has no node type for them and the tool is PLC-scoped; follow-up needs new node types),
  `DeviceItem`, `Subnet`, `Node` (unverified), TO instance DBs and Classic HMI tags (not present
  in this project, so unverified). Listed as unverified in the PLC operations summary.
