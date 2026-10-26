# TIA Portal PLC Operations

## Supported read operations

| Entry point | Operation | Inputs | Behavior |
|---|---|---|---|
| `browse_project_tree` | `browse_project_tree` | Optional `depth`, `startSelector`, `pageSize`; continuation `cursor` | Locates PLC software, blocks, groups, types, and other project objects; cursors reject binding changes. |
| `plc_read` | `get_block_content` | Required `blockPath`; optional `format`, `withDependencies` | Reads an existing block as XML/SimaticML (default) or an eligible external source. See [IMPORT_EXPORT_OPTIONS_SUMMARY.md](IMPORT_EXPORT_OPTIONS_SUMMARY.md). |
| `plc_read` | `get_type_content` | Required `typePath`; optional `format`, `withDependencies` | Reads an existing PLC type as `.udt` source (default) or XML/SimaticML. |
| `plc_read` | `list_tag_tables` | Optional `plcName`, `folderPath`, `tableName` | Lists PLC tag tables and the exposed tag and constant information. |
| `read_cross_references` | `read_cross_references` | Required `target`; optional `filter`, `maxResults` | Reads the cross-references of one project-tree target. Filters are `AllObjects`, `ObjectsWithReferences` (default), `ObjectsWithoutReferences`, and `UnusedObjects`. |
| `compile_check` | `compile_check` | Optional `plcName`, `blockPath` | Compiles the PLC or selected block scope and returns compiler messages. |

`plc_read` and `read_cross_references` are registered in every access mode. `execute_read_batch` was
retired; use these two tools instead. Tree browsing and compilation are standalone tools. `compile_check`
and `plc_write` are available in read-write and full modes, and behave the same in both. PLC run/stop
(`start_plc`/`stop_plc`) was removed; no mode controls a PLC.

Use `bind_project` in any mode to adopt or switch to an already-open project; a different binding
requires `forceRebind:true`. Ordinary PLC reads never bind, switch or open. Only `open_project` and
`create_project` open projects. Lifecycle runs in read-write with one confirmation form per actual
call, or in full without server elicitation; block guards stop the call in every mode. The complete
tool counts are 8/19/19. Read-only never opens, creates, saves or closes a project.

## PLC reads (`plc_read`)

`plc_read(operations)` takes at most 50 items. Each has a unique, non-blank `operationId` of at most
256 characters, an `operation`, and that operation's parameters. Items run
in caller order and independently: a failed item does not stop the others. Unknown fields are
rejected. The response is one structured document (`contractVersion` `1.0`, root `warnings` array,
explicit nulls), delivered identically as text and `structuredContent`. `isError` is true only for a
rejection before anything ran.

- `get_block_content` and `get_type_content` return `{ format, content, contentHash, warnings }`.
  `format` is `xml` or `source`; blocks default to `xml` and types to `source`. `withDependencies`
  adds the dependency closure to a `source` export; that document is context only and cannot be
  written back.
- `contentHash` is `xml:sha256:<hex>` or `source:sha256:<hex>` over the exact served text. It is an
  explicit `null` when `withDependencies` is true.
- `list_tag_tables` returns `{ isComplete, plcs[] }`, each PLC naming its software and device and
  carrying its `tables[]`. An omitted `plcName` reads every PLC, including PLCs in device groups.
  Tags carry `externalAccessible`, `externalVisible` and `externalWritable` (`null` when unreadable);
  user constants carry a readability marker; anything that could not be read sets `isComplete:false`
  with a message (the first 50 messages are kept, then one summary entry). `tableName` keeps one table
  and `folderPath` the tables directly in one folder (written as the inventory emits it, `/` or
  `/Line/Cell`); both are case-insensitive and combine with `plcName`. A narrowing that matches no
  table fails the item with `target_not_found`, unless part of the tree was unreadable.
- Content tools act on the bound project only; with no verified binding (a configured `--project` is grounded by status first) they fail closed with `binding_conflict`; call `bind_project` first.
  An unbound call returns one root error, never per-item failures. Reads never bind, switch or open a project.

Budget: a value over the value budget or a document over the response budget (60,000 and 180,000
characters by default) is never cut. The affected item `result` is omitted whole with an omission
record and narrowing guidance, so it has no hash. A single block or type export over the value budget
therefore cannot be read through `plc_read`, and cannot be updated through `plc_write`, which needs
the hash, until the server is restarted with `--max-item-chars` at or above the omission's
`originalChars` and `--max-document-chars` above that with headroom; read such a block in its own
call. See [response size limits](../guides/mcp-client-configuration.md#response-size-limits). One PLC
of roughly 400 tags already exceeds the default value budget: narrow `list_tag_tables` with
`plcName`, `folderPath` or `tableName`.

## Compiler diagnostics (`compile_check`)

Compilation can change TIA Portal's in-memory project state. A returned report is not evidence of
saving, downloading, or PLC/plant acceptance. `compile_check` finds PLCs at the project root and in
device groups. Without `plcName` it compiles every PLC; it compiles nothing if a device item cannot be
read, because that item could hide a PLC. A `blockPath` without a PLC qualifier requires a single PLC
in the project and otherwise fails with `target_ambiguous`.

Each PLC result uses `plcName` for the actual PLC software name and `deviceName` for the containing
hardware device. The input `plcName` selector continues accepting either name. The same identities
are returned for PLC and selected-block compilation, including expected compiler failures.

Nested compiler messages are flattened in deterministic parent-before-child order. Each row uses
that message's own description, path, and severity. Siemens-reported `state`, `errorCount`, and
`warningCount` are preserved whenever a compiler result is available; they are not recomputed from
the displayed rows and remain available on handled root-diagnostic collection access failures.

One projection budget is shared across all selected PLCs:

- Maximum message depth: 16, counting roots as depth 1.
- Maximum projected message count: 200.
- Maximum length of each description and path: 1,024 UTF-16 code units.
- Maximum combined description/path text: 32,000 UTF-16 code units.

The serialized compile report is additionally kept below 60,000 characters, including JSON
escaping. Trailing message rows may be removed to meet that bound without changing reported totals
or identities. Each affected PLC receives one sanitized omission note in `diagnosticNotes` when
diagnostics are shortened, omitted, or recoverably unreadable. For nested diagnostic access,
recoverable failures are expected exact `InvalidOperationException` access failures, not derived
exceptions. Infrastructure or interruption failures—including session loss, I/O, cancellation,
format errors, derived invalid-operation exceptions, and other unexpected faults—fail the operation
rather than return typed partial success. If metadata alone exceeds the response limit, the
operation fails with bounded guidance: compilation may have run, so inspect the current in-memory
project state before deciding whether to retry.

When an expected compiler invocation failure leaves no compiler result, the typed PLC result keeps
both identities, reports `state: "Error"`, and includes a sanitized unavailable-details note.
Its zero counts are fallback values, not evidence of an error-free compilation.

## Cross-reference coverage (`read_cross_references`)

`read_cross_references(target, filter?, maxResults?)` reads one target per call.
`target` is `{ path: [{ nodeType, name }, ...], member?: { kind, name } }`: copy the `path` segments
from `browse_project_tree` output (matching is case-insensitive as in the tree; siblings that differ
only in case are `target_ambiguous`). `member` addresses leaves the tree does not model, under a
`TagTable` path, with `kind` one of `Tag`, `SystemConstant` or `UserConstant` (exact case). `filter`
and `maxResults` (1 or greater) are validated before any worker call.

Provisionally, `target` may instead be `{ hmi: { hmiName, tagName } }` for one top-level WinCC
Unified HMI tag; it may be replaced by a unified HMI selector once alarms or screens need
cross-references. `hmiName` is required (software or device name, case-insensitive) and `tagName`
matches case-insensitively. A dotted UDT member name (`Motor_1.Speed`) fails `target_not_found` with
the guidance to name only the top-level tag; an ambiguous HMI is `target_ambiguous` and a Classic
HMI `target_kind_unsupported`. A target carries either a non-empty `path` (with optional `member`) or
`hmi`: both, neither (including an empty `path` without `hmi`) or a blank `hmiName`/`tagName` is
`validation_error`. Filters, `maxResults`, budgets and the result shape are unchanged, and the
target echo repeats the `hmi` selector.

- A path ending at a leaf (an OB, FB, FC, GlobalDB, InstanceDB or ArrayDB block, a system block, or a
  PLC type) or any `member` is one query on that object.
- A path ending at a container (`Device`, `PlcSoftware`, `SoftwareUnit`, a block, system-block,
  tag-table or type folder, or `TagTable`) sweeps every owner beneath it. A container with no owners
  is an empty, complete success; if owners exist and none of their queries succeeds, the call fails
  with `worker_operation_failed`.
- Zero matches is `target_not_found`; a leaf without the cross-reference service is
  `target_kind_unsupported`, which is distinct from "no references".

The result is one typed document: the target echo, `filter`, completeness fields (`isComplete`,
`ownerQueryCount`, `successfulOwnerQueryCount`), totals and the recursive
`sources[] -> references[] -> locations[]` hierarchy. `referencedAs` is `{ name, typeName }` of the
referenced object, and `access` and `referenceType` are V21 member names. Overlapping results from
different owners are not deduplicated. A complete successful sweep can have zero sources.

Unavailable owner services, traversal skips and a `maxResults` cut retain the available data and set
`isComplete:false` with messages. An incomplete `UnusedObjects` result is not an authoritative
unused-object audit and must not be used as proof that objects can safely be deleted.

Budget: sources are dropped whole from the tail until the report value fits 60,000 characters and
the document 180,000. The report then carries `isComplete:false`, `omittedSourceCount` and narrowing
guidance (`filter`, `maxResults`, a narrower `path`, or a `member`). When not even one whole source
fits, the first source is kept and trimmed inside (child sources, then references, then locations),
with one message counting what was omitted; the omitted tail cannot be paged.

### Unverified owner kinds

Only kinds whose owners were verified live (2026-10-05, V21) ship as members or endpoints. WinCC
Unified `HmiTag` ships through the provisional `hmi` selector above (live 2026-10-12, see the
[completion report](../superpowers/acceptance/reports/2026-10-12-hmi-read-completion-live-acceptance.md)).
Not shipped, and unverified for this tool: technology objects (the cross-reference service works,
but the project tree has no node type for them), `DeviceItem`,
`Subnet` and `Node` (no service observed), technology-object instance DBs and WinCC Classic HMI tags
(not present in the spike project). `Device`, `PlcSoftware`, `SoftwareUnit` and `TagTable` are not
owners themselves; they only enumerate the owners beneath them. See Appendix B of the
[design spec](../superpowers/specs/2026-10-05-plc-read-write-and-cross-references-design.md).

## PLC writes (`plc_write`)

`plc_write(operations, dryRun=false)` runs up to 50 PLC software writes in caller order against one
verified, already-open project. Omitted `dryRun` or `dryRun:false` executes; `dryRun:true` returns
`phase:preview` with effects, `dependsOn` and guards, without mutation. No confirmation prompt is sent
in any mode. Each item has a unique, non-blank `operationId` of at most 256 characters; the tool acts on the
bound project only (an item `projectPath` is rejected as `validation_error`). The tool never
saves, downloads, or controls a PLC.

| Operation | Required inputs | Optional inputs |
|---|---|---|
| `update_block_logic` | `blockPath`, `content`, `expectedContentHash` | `format` (`xml` by default; `source` for global DBs and SCL blocks) |
| `update_type_content` | `typePath`, `content`, `expectedContentHash` | `format` (`source` by default) |
| `create_block` | `blockPath`, `blockType` | `language`, `obEventClass` |
| `delete_block` | `blockPath` | — |
| `create_block_group` / `delete_block_group` | `blockPath` | — |
| `create_tag_table` / `delete_tag_table` | `tableName` | `plcName`, `folderPath` |
| `create_tag` | `tableName`, `name`, `dataType` | `plcName`, `folderPath`, `logicalAddress` |
| `update_tag` | `tableName`, `name` | `plcName`, `folderPath`, `newName`, `dataType`, `logicalAddress`, `externalAccessible`, `externalVisible`, `externalWritable` |
| `delete_tag` | `tableName`, `name` | `plcName`, `folderPath` |
| `create_user_constant` | `tableName`, `name`, `dataType`, `value` | `plcName`, `folderPath` |
| `update_user_constant` | `tableName`, `name` | `plcName`, `folderPath`, `dataType`, `value` |
| `delete_user_constant` | `tableName`, `name` | `plcName`, `folderPath` |

`create_block` accepts `FB`, `FC`, `OB`, or `GlobalDB`. Applicable block types support `LAD`, `FBD`, `STL`, `SCL`, and `GRAPH`. `GRAPH` is not supported for `OB`. `STL` FB, FC and OB creation works.

OB creation takes `obEventClass`, a closed set of 15 names, omitted meaning `ProgramCycle`:
`ProgramCycle`, `Startup`, `TimeOfDay`, `TimeDelayInterrupt`, `CyclicInterrupt`,
`HardwareInterrupt`, `Status`, `Update`, `Profile`, `TimeErrorInterrupt`, `DiagnosticErrorInterrupt`,
`PullOrPlugOfModules`, `RackOrStationFailure`, `ProgrammingError` and `IOAccessError`. The server
picks the OB number: a singleton class gets its base number; a multi-instance class gets its base
number if no OB in the PLC holds it, otherwise the lowest free number from 123 to 32767 (only OB
numbers count, including OBs in software units and system groups). `dryRun` shows the planned
`number` and `obEventClass` in the effect. Each applied `create_block` result carries `number` (the
created block's number; null for other block operations) and `obEventClass` (null for non-OB), both
always written. An unknown `obEventClass` is `validation_error` listing the valid names; so is
`obEventClass` with a non-OB `blockType`. A class the CPU does not support fails the item with TIA
Portal's own message (for example "Cannot create an organization block of type
'ProgrammingError'." on an S7-1200). The 2026-10-09
[live acceptance report](../superpowers/acceptance/reports/2026-10-09-ob-creation-live-acceptance.md)
records the V21 run.

`create_block`, `create_block_group` and `delete_block_group` take a deterministic group path or the
two-segment `PLC/Name` (the PLC root group); a one-segment path is `validation_error`.
`update_block_logic` and `delete_block` resolve the block uniquely across the PLC, so a missing block
is `target_not_found`. The PLC itself is resolved uniquely across every device, including grouped
devices; several matches are `target_ambiguous`.

### Content writes

`update_block_logic` and `update_type_content` take the whole document as `content`, together with
`expectedContentHash`: the `contentHash` from a `plc_read` of the same object in the same format.
If the object changed since that read, the call fails with `state_changed` before anything runs; a
change between planning and import fails that item with `state_changed`. A document over the value
budget (60,000 characters by default) is omitted from `plc_read`, has no hash, and cannot be updated through
`plc_write`. A source declaring more than one object (a `withDependencies` read) is refused.
Block updates import into the existing block, compile the affected PLC scope, and verify the result
by re-export (the verification `contentHash`, below); type updates require a declaration whose name matches the target. Import/update never
creates, renames, deletes, or upserts the addressed object. A preview can carry a bounded
`contentDiff` of current versus requested text (see the
[import/export options](IMPORT_EXPORT_OPTIONS_SUMMARY.md)). A failed `update_block_logic` import
reports `blockImportOutcome` on the item's failure; that member is conditional on every structured tool's
failure schema.

### Guards

Guards are evaluated for every item before the first mutation. Block guards stop the call in every
mode (a dry run lists them and still returns `preview`); info guards become warnings. Guards cover
only what TIA Portal cannot tell the caller. Name collisions, existing blocks, existing groups and
tables, and the default tag table are not guarded: planning does not check creates, and Portal
rejects them at mutation (see [Failures from TIA Portal](#failures-from-tia-portal)).

| Guard | Severity | Fires when |
|---|---|---|
| `plc_state_unverifiable` | block | The PLC inventory is incomplete, the update or delete target or what a delete removes is unreadable, the user-constant value an update replaces is unreadable, or the content cannot be exported. Unreadable evidence is never treated as empty. |
| `plc_ob_singleton_exists` | block | `create_block` creates an OB of a singleton event class and the PLC already holds that class's OB number, or an earlier item in the same call creates it. The message names the class, the number and the existing block. |
| `plc_deletes_block` | info | `delete_block` removes the block and its content. |
| `plc_deletes_group_contents` | info | `delete_block_group` removes every block and group inside it; the effect lists them. |
| `plc_deletes_table_contents` | info | `delete_tag_table` removes its tags and user constants. |
| `plc_address_overlap` | info | The requested logical address is already used by another tag; TIA Portal allows the overlap. |

`plc_ob_singleton_exists` stayed because Portal's own rejection of a duplicate singleton OB was never
observed: `create_block` picks the OB number itself, and an `update_block_logic` that declares a
duplicate number or event class is refused before any import, so no `plc_write` input reaches Portal
with one. The second `TimeErrorInterrupt` OB was blocked by the guard, also within one call.

The guards `plc_name_collision`, `plc_block_exists`, `plc_default_tag_table` and
`plc_attribute_unreadable` were removed with write contract `2.0`. Deleting the default tag table
is now Portal's rejection: `engineering_rejected` with "The operation is not valid because of the
current status of object" (the message does not say "default"). An unreadable external-access flag
on `update_tag` is no longer a guard; the request reaches Portal.

### Failures from TIA Portal

When Portal itself rejects a mutation, the item fails with a typed category and the Openness
exception, never a message parsed by the server:

- `engineering_rejected`: the worker method was a mutation and Openness threw
  `EngineeringTargetInvocationException` (a duplicate tag, table, block or group; a delete Portal
  refuses). The failure carries `engineeringError`.
- `worker_operation_failed`: any other Siemens `EngineeringException`. It also carries
  `engineeringError` when the exception reached the worker's shared catch.
- Reads never produce `engineering_rejected`. A Siemens exception during a planning read keeps its
  read category (`worker_operation_failed`, `target_not_found`, and so on) at the call level.
- `portal_lost` wins over both: if the Portal attachment was disposed during the item, the category
  is `portal_lost` with no `engineeringError`, and the binding and cursors are invalidated.

`engineeringError` is `{ "type", "message", "inner": [ { "type", "message" } ] }`: the exception type
name, its message verbatim, and the inner-exception chain outermost first (`inner` is always an
array, empty when there is no inner exception). The contract bounds a message at 4,000 characters
and the chain at 8 entries; longer text is cut without a marker. The member is conditional: absent
on failures that did not come from Siemens and on host-side rejections. Messages are in the Portal
UI language, so callers must not parse them; the live run used an English UI. Captured example,
`create_tag` with a name the table already holds (item 1 of 1; the call is `phase:"applied"`,
`success:false`, `error:null`, `isError:false`):

```json
{
  "operationId": "l1-01",
  "operation": "create_tag",
  "status": "failed",
  "failure": {
    "category": "engineering_rejected",
    "message": "TIA Portal rejected the operation: Error when calling method 'Create' of type 'Siemens.Engineering.SW.Tags.PlcTagComposition'.\r\n\r\nAn attempt was made to create an object that already exists.",
    "engineeringError": {
      "inner": [],
      "message": "Error when calling method 'Create' of type 'Siemens.Engineering.SW.Tags.PlcTagComposition'.\r\n\r\nAn attempt was made to create an object that already exists.",
      "type": "EngineeringTargetInvocationException"
    }
  }
}
```

Other live captures have the same shape and `inner:[]`: a duplicate tag table
(`PlcTagTableComposition`), a duplicate block group (`PlcBlockUserGroupComposition`), a duplicate
block (`create_block` is an XML import, so the message mentions `Import` of
`PlcBlockComposition`, a line and a position) and deleting the default tag table. When item N of M
is rejected, items before N are `applied`, item N is `engineering_rejected`, later items are
`skipped` with `earlierOperationFailed`, and nothing is rolled back. See the
[live cases](../superpowers/acceptance/reports/inputs/2026-10-26-plc-write-native-rejections-live-cases.md).

Portal does not throw for every collision. A rename through `update_tag` to a name another tag in
the table holds is silently ignored. The worker applies the rename first, reads `Name` back and, if
Portal kept the old name, fails the item `postcondition_failed` before any other property is
applied ("No other property was changed"). The unsupported-flag case is different: an
`update_tag` that requests an external-access flag the tag cannot take fails mid-update, after a
rename or data-type change in the same item has already applied; there is no rollback.

### Execution, verification and audit

Binding-gate failures preserve their original category, including `worker_timeout`, `worker_crashed`,
`portal_lost` and `protocol_error`, in the top-level error and audit. A gate denial stops before planning
or mutation; a missing, mismatched or changed binding is `binding_conflict`.

Items run sequentially. Later items may use objects created earlier in the same call; they carry
`dependsOn` and are re-planned just before their own mutation. Creates are not pre-checked, so a
create into a missing parent block group or tag-table folder fails at mutation (Portal or the
worker rejects the item) instead of in pre-flight, and a later delete of an object that was never
placed fails the whole call as `target_not_found`. Update and delete targets are still resolved
before the first mutation, so a mistyped path fails the whole call `target_not_found` before
anything runs. The first failure stops the call and later items are `skipped`. There is no
rollback or automatic replay: re-read affected objects with `plc_read` before retrying. Phases are
`preview`, `applied`, `blocked` and `error`; `isError` is true only for a rejection before anything
ran, and a call that ran and failed returns `success:false` with `error:null`. Budgets are 60,000
characters per value and 180,000 per document by default; whole values are omitted, never cut. Every
entered call appends one audit v2 record to `writes-yyyy-MM-dd.jsonl`, with the actual item
statuses and confirmation `none` in read-write and `policy` in full.

Verification (write contract `2.0`; `plc_read` stays `1.0`) reads back only content:

- `trusted`: a completed non-content item. The Openness call returned without rejection and nothing
  was read back. The `exists`, `absent`, `values` and `observed` checks of contract `1.0` are gone.
- `contentHash`: a completed `update_block_logic`/`update_type_content` item is re-exported and its
  hash reported (`contentHash` and `actual`). A failed content item also reports the current hash,
  since a failed import may have committed.
- `superseded`: an item whose object a later item changed again; the later item asserts the final
  state. A short-path (`PLC/Name`) `update_block_logic` target is not resolved through the tree, so
  conservatively any later block item of the same name, or any `delete_block_group` on that PLC,
  supersedes it, and it gets no content check.
- A failed non-content item has no verification entry (nothing to verify). Inspect the object with
  `plc_read` before retrying.

The verification `contentHash` equals the hash the next `plc_read` of that object returns and can be
used as the next `expectedContentHash`. It cannot be computed from the submitted content: the export
carries a generated id, and four identical `update_block_logic` writes gave four different hashes.
Take it from the verification or a fresh read.

### Known limits

- The address-overlap check is an exact string match (`%MW0` and `%M0.0` are not detected as
  overlapping), at info severity.
- Two content updates of the same block in one call cannot both plan.
- Tags created earlier in the same call are assumed to have all external-access flags true for
  planning.
- With an incomplete inventory, a selector miss is `plc_state_unverifiable` (block), not
  `target_not_found`.
- Duplicate tag-table names were tested only within the same folder: `plc_write` cannot create
  tag-table folders, so a cross-folder duplicate was not reachable.
- An `update_tag` that requests an unsupported external-access flag can fail after a rename or
  data-type change in the same item has applied (no rollback).
- Portal's own rejection of a duplicate singleton OB was never observed (see the guard note above).
- Content importers (`update_block_logic`, `update_type_content`) still flatten import failures to
  `worker_operation_failed` without `engineeringError`; see the
  [improvement log](../IMPROVEMENT_LOG.md).
- Created OBs use TIA Portal defaults: no event parameters (cyclic time, phase offset, priority,
  time-of-day schedule, hardware-interrupt triggers) and no renumbering of existing blocks. See the
  [improvement log](../IMPROVEMENT_LOG.md).
- `SynchronousCycle` is not offered: it does not compile until an isochronous IO system is assigned,
  which no tool here configures. Motion Control, redundancy and ProDiag OBs are created by TIA Portal.
- A multi-instance OB number can differ from the plan if another OB takes it in between; the result
  `number` is authoritative. A singleton number taken in between fails the item with `state_changed`.

## Tag safety acceptance boundary

`plc_write` replaced the token-bound tag safety snapshots. Tag, table and constant writes plan
against the tag inventory their guards need: an unreadable constant value blocks, table deletion
and address overlaps are info. Name collisions and the default tag table are no longer guarded;
Portal rejects them as `engineering_rejected`. The 2026-10-07
[validation report](../superpowers/acceptance/reports/2026-10-07-plc-write-validation.md)
records the original offline and live acceptance of this path, and the 2026-10-26
[native rejections report](../superpowers/acceptance/reports/2026-10-26-plc-write-native-rejections-live-verification.md)
records the contract `2.0` change.

Historical: PR 5 bound typed tag snapshots to preview/apply tokens. Its
[live acceptance report](../superpowers/acceptance/reports/2026-09-01-pr5-tag-operation-safety-scopes-live.md)
covers that token flow only and does not qualify `plc_write`. Multilingual per-tag comment binding
and public `list_tag_tables` completeness changes remain deferred.

## Project-tree safety acceptance boundary

`plc_write` replaced the token-bound project-tree safety snapshots. Block and group writes plan
against a project-tree snapshot only where a guard needs it (deletes and OB creates): deletes are
info, an existing block on create is Portal's `engineering_rejected`, and an unreadable node makes
the snapshot incomplete, which blocks as `plc_state_unverifiable` instead of hiding what a delete
removes. The
[validation report](../superpowers/acceptance/reports/2026-10-07-plc-write-validation.md) records
the offline and live acceptance of this path.

Historical: PR 6 bound typed project-tree snapshots to preview/apply tokens. Its
[live acceptance report](../superpowers/acceptance/reports/2026-09-01-pr6-project-tree-safety-scopes-live.md)
covers that token flow only and makes no save, persistence, plant, or hardware claim.

## Current limits

The current surface does not provide:

- Generic online/offline status or connection configuration.
- Compare-to-online, program upload/download, or `UpdateProgram` workflows.
- Software Unit lifecycle or administration operations; block operations within existing Software Units are supported. Safety units, safety administration, safety signatures, and safety validation are not provided.
- PLC alarms, alarm classes, alarm text lists, ProDiag supervision, or supervision import/export.
- Technology objects, motion-control objects, watch tables, or force tables.
- OPC UA server configuration, communication groups, access control, or role mapping.
- System blocks, know-how protection workflows, webserver pages, block fingerprints, or loadable files.
