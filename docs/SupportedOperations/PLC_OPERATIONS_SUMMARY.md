# TIA Portal PLC Operations

## Supported read operations

| Entry point | Operation | Inputs | Behavior |
|---|---|---|---|
| `browse_project_tree` | `browse_project_tree` | Optional `projectPath`, `depth`, `startSelector`, `pageSize`; continuation `cursor` | Locates PLC software, blocks, groups, types, and other project objects; cursors reject binding changes. |
| `plc_read` | `get_block_content` | Required `blockPath`; optional `format`, `withDependencies` | Reads an existing block as XML/SimaticML (default) or an eligible external source. See [IMPORT_EXPORT_OPTIONS_SUMMARY.md](IMPORT_EXPORT_OPTIONS_SUMMARY.md). |
| `plc_read` | `get_type_content` | Required `typePath`; optional `format`, `withDependencies` | Reads an existing PLC type as `.udt` source (default) or XML/SimaticML. |
| `plc_read` | `list_tag_tables` | Optional `plcName`, `folderPath`, `tableName` | Lists PLC tag tables and the exposed tag and constant information. |
| `read_cross_references` | `read_cross_references` | Required `target`; optional `filter`, `maxResults`, `projectPath` | Reads the cross-references of one project-tree target. Filters are `AllObjects`, `ObjectsWithReferences` (default), `ObjectsWithoutReferences`, and `UnusedObjects`. |
| `compile_check` | `compile_check` | Optional `projectPath`, `plcName`, `blockPath` | Compiles the PLC or selected block scope and returns compiler messages. |

`plc_read` and `read_cross_references` are registered in every access mode. `execute_read_batch` was
retired; use these two tools instead. Tree browsing and compilation are standalone tools. `compile_check`
and `plc_write` are available in read-write and full modes, and behave the same in both. PLC run/stop
(`start_plc`/`stop_plc`) was removed; no mode controls a PLC.

Use `bind_project` in any mode to adopt or switch to an already-open project; a different binding
requires `forceRebind:true`. Ordinary PLC reads never bind, switch or open. Only `open_project` and
`create_project` open projects. Lifecycle runs in read-write with one confirmation form per actual
call, or in full without server elicitation; block guards stop the call in every mode. The complete
tool counts are 7/16/16. Read-only never opens, creates, saves or closes a project.

## PLC reads (`plc_read`)

`plc_read(operations)` takes at most 50 items. Each has a unique, non-blank `operationId` of at most
256 characters, an `operation`, an optional `projectPath`, and that operation's parameters. Items run
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
- A `projectPath` that differs from the bound project fails only that item with `binding_conflict`.
  Reads never bind, switch or open a project.

Budget: a value over 60,000 characters or a document over 180,000 characters is never cut. The
affected item `result` is omitted whole with an omission record and narrowing guidance, so it has no
hash. A single block or type export over 60,000 characters therefore cannot be read through
`plc_read`, and cannot be updated through `plc_write`, which needs the hash. One PLC of
roughly 400 tags already exceeds the value: narrow `list_tag_tables` with `plcName`, `folderPath` or
`tableName`.

## Compiler diagnostics (`compile_check`)

Compilation can change TIA Portal's in-memory project state. A returned report is not evidence of
saving, downloading, or PLC/plant acceptance. `compile_check` cannot yet reach a PLC inside a device
group (see the improvement log).

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

`read_cross_references(target, filter?, maxResults?, projectPath?)` reads one target per call.
`target` is `{ path: [{ nodeType, name }, ...], member?: { kind, name } }`: copy the `path` segments
from `browse_project_tree` output (matching is case-insensitive as in the tree; siblings that differ
only in case are `target_ambiguous`). `member` addresses leaves the tree does not model, under a
`TagTable` path, with `kind` one of `Tag`, `SystemConstant` or `UserConstant` (exact case). `filter`
and `maxResults` (1 or greater) are validated before any worker call; `projectPath` is a target
constraint only.

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

Only kinds whose owners were verified live (2026-10-05, V21) ship as members or endpoints. Not
shipped, and unverified for this tool: technology objects and WinCC Unified `HmiTag` (the
cross-reference service works, but the project tree has no node type for them), `DeviceItem`,
`Subnet` and `Node` (no service observed), technology-object instance DBs and WinCC Classic HMI tags
(not present in the spike project). `Device`, `PlcSoftware`, `SoftwareUnit` and `TagTable` are not
owners themselves; they only enumerate the owners beneath them. See Appendix B of the
[design spec](../superpowers/specs/2026-10-05-plc-read-write-and-cross-references-design.md).

## PLC writes (`plc_write`)

`plc_write(operations, dryRun=false)` runs up to 50 PLC software writes in caller order against one
verified, already-open project. Omitted `dryRun` or `dryRun:false` executes; `dryRun:true` returns
`phase:preview` with effects, `dependsOn` and guards, without mutation. No confirmation prompt is sent
in any mode. Each item has a unique, non-blank `operationId` of at most 256 characters; an optional
`projectPath` must normalize to the same project on every item that names one (otherwise
`validation_error`). The tool never
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
| `update_tag` | `tableName`, `name` | `plcName`, `folderPath`, `newName`, `dataType`, `logicalAddress`, `externalAccessible`, `externalVisible`, `externalWritable`, `isSafety` |
| `delete_tag` | `tableName`, `name` | `plcName`, `folderPath` |
| `create_user_constant` | `tableName`, `name`, `dataType`, `value` | `plcName`, `folderPath` |
| `update_user_constant` | `tableName`, `name` | `plcName`, `folderPath`, `dataType`, `value` |
| `delete_user_constant` | `tableName`, `name` | `plcName`, `folderPath` |

`create_block` accepts `FB`, `FC`, `OB`, or `GlobalDB`. Applicable block types support `LAD`, `FBD`, `STL`, `SCL`, and `GRAPH`. OB creation also accepts event classes such as `ProgramCycle`, `Startup`, `TimeDelay`, `CyclicInterrupt`, `HardwareInterrupt`, `Diagnostic`, and `TimeOfDay`.
`create_block`, `create_block_group` and `delete_block_group` take a deterministic group path or the
two-segment `PLC/Name` (the PLC root group); a one-segment path is `validation_error`.
`update_block_logic` and `delete_block` resolve the block uniquely across the PLC, so a missing block
is `target_not_found`. The PLC itself is resolved uniquely across every device, including grouped
devices; several matches are `target_ambiguous`.

### Content writes

`update_block_logic` and `update_type_content` take the whole document as `content`, together with
`expectedContentHash`: the `contentHash` from a `plc_read` of the same object in the same format.
If the object changed since that read, the call fails with `state_changed` before anything runs; a
change between planning and import fails that item with `state_changed`. A document over the
60,000-character value is omitted from `plc_read`, has no hash, and cannot be updated through
`plc_write`. A source declaring more than one object (a `withDependencies` read) is refused.
Block updates import into the existing block, compile the affected PLC scope, and verify the result
by re-export; type updates require a declaration whose name matches the target. Import/update never
creates, renames, deletes, or upserts the addressed object. A preview can carry a bounded
`contentDiff` of current versus requested text (see the
[import/export options](IMPORT_EXPORT_OPTIONS_SUMMARY.md)). A failed `update_block_logic` import
reports `blockImportOutcome` on the item's failure; that member is conditional on every structured tool's
failure schema.

### Guards

Guards are evaluated for every item before the first mutation. Block guards stop the call in every
mode (a dry run lists them and still returns `preview`); info guards become warnings.

| Guard | Severity | Fires when |
|---|---|---|
| `plc_state_unverifiable` | block | Evidence relevant to the target is incomplete or unreadable. An incomplete root inventory fires it for every item, and so does updating or deleting a constant whose value is unreadable. Unreadable evidence is never treated as empty. |
| `plc_name_collision` | block | A new or renamed tag, constant, block, group or table collides with an existing or in-call object. Tag and constant names are compared case-insensitively across tags, user constants and blocks in the PLC's CPU namespace; table names across the PLC's whole tag-table hierarchy. |
| `plc_block_exists` | block | `create_block` targets an existing block; existing blocks are never overwritten. |
| `plc_default_tag_table` | block | `delete_tag_table` targets the default tag table. |
| `plc_attribute_unreadable` | block | `update_tag` requests an external-access flag that is unreadable on the current tag. |
| `plc_deletes_block` | info | `delete_block` removes the block and its content. |
| `plc_deletes_group_contents` | info | `delete_block_group` removes every block and group inside it; the effect lists them. |
| `plc_deletes_table_contents` | info | `delete_tag_table` removes its tags and user constants. |
| `plc_address_overlap` | info | The requested logical address is already used by another tag; TIA Portal allows the overlap. |

### Execution, verification and audit

Items run sequentially. Later items may use objects created earlier in the same call; they carry
`dependsOn` and are re-planned just before their own mutation. The first failure stops the call and
later items are `skipped`. There is no rollback or automatic replay: re-read affected objects with
`plc_read` before retrying. Phases are `preview`, `applied`, `blocked` and `error`; `isError` is true
only for a rejection before anything ran, and a call that ran and failed returns `success:false` with
`error:null`. Verification re-reads every attempted item; an item whose object a later item changed
again is reported `superseded`. Budgets are 60,000 characters per value and 180,000 per document;
whole values are omitted, never cut. Every entered call appends one audit v2 record to
`writes-yyyy-MM-dd.jsonl`, with confirmation `none` in read-write and `policy` in full.

### Known limits

- The address-overlap check is an exact string match (`%MW0` and `%M0.0` are not detected as
  overlapping), at info severity.
- Two content updates of the same block in one call cannot both plan.
- Tags created earlier in the same call are assumed to have all external-access flags true for
  planning.
- With an incomplete inventory, a selector miss is `plc_state_unverifiable` (block), not
  `target_not_found`.
- Blocks inside software units are counted in the CPU namespace, so a tag named like an unpublished
  unit block is blocked as `plc_name_collision`. This fails closed; see the
  [improvement log](../IMPROVEMENT_LOG.md).
- `isSafety` is not verified after the write.

## Tag safety acceptance boundary

`plc_write` replaced the token-bound tag safety snapshots. Tag, table and constant writes now plan
against one tag inventory per call and fire the guards above: name collisions, the default tag
table and unreadable flags or constants block; table deletion and address overlaps are info.
Collisions and stale state found at mutation time fail the item with `state_changed`. The
2026-10-07 [validation report](../superpowers/acceptance/reports/2026-10-07-plc-write-validation.md)
records the offline and live acceptance of this path.

Historical: PR 5 bound typed tag snapshots to preview/apply tokens. Its
[live acceptance report](../superpowers/acceptance/reports/2026-09-01-pr5-tag-operation-safety-scopes-live.md)
covers that token flow only and does not qualify `plc_write`. Multilingual per-tag comment binding
and public `list_tag_tables` completeness changes remain deferred.

## Project-tree safety acceptance boundary

`plc_write` replaced the token-bound project-tree safety snapshots. Block and group writes plan
against one project-tree snapshot per involved PLC: an existing block on create blocks, deletes are
info, and an unreadable node makes the snapshot incomplete, which blocks as `plc_state_unverifiable`
instead of hiding a collision. The
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
