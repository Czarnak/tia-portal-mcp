# TIA Portal PLC Operations

## Supported read operations

| Entry point | Operation | Inputs | Behavior |
|---|---|---|---|
| `browse_project_tree` | `browse_project_tree` | Optional `projectPath`, `depth`, `startPath` | Locates PLC software, blocks, groups, types, and other project objects. |
| `execute_read_batch` | `get_block_content` | Required `blockPath`; optional `format` | Reads an existing block as XML/SimaticML or an eligible external source. See [IMPORT_EXPORT_OPTIONS_SUMMARY.md](IMPORT_EXPORT_OPTIONS_SUMMARY.md). |
| `execute_read_batch` | `get_type_content` | Required `typePath`; optional `format` | Reads an existing PLC type as `.udt` source or XML/SimaticML. |
| `execute_read_batch` | `list_tag_tables` | Optional `plcName` | Lists PLC tag tables and the exposed tag and constant information. |
| `execute_read_batch` | `read_cross_references` | Optional `plcName`, `filter`, `maxResults` | Reads cross-references. Filters are `AllObjects`, `ObjectsWithReferences`, `ObjectsWithoutReferences`, and `UnusedObjects`. |
| `compile_check` | `compile_check` | Optional `projectPath`, `plcName`, `blockPath` | Compiles the PLC or selected block scope and returns compiler messages. |

Tree browsing and compilation are standalone tools. `compile_check` is a read-write-mode engineering operation and does not use a safety token.

## Supported write operations

The operations below run through `preview_write_batch` and `apply_write_batch`.

| Operation | Required inputs | Optional inputs |
|---|---|---|
| `update_block_logic` | `blockPath`, `yamlContent` | `format` (`xml` by default; `source` for global DBs) |
| `create_block` | `blockPath`, `blockType` | `language`, `obEventClass` |
| `delete_block` | `blockPath` | — |
| `create_block_group` / `delete_block_group` | `blockPath` | — |
| `update_type_content` | `typePath`, `sourceContent` | `format` (`source` by default) |
| `create_tag_table` / `delete_tag_table` | `tableName` | `plcName`, `folderPath` |
| `create_tag` | `tableName`, `name`, `dataType` | `plcName`, `folderPath`, `logicalAddress` |
| `update_tag` | `tableName`, `name` | `plcName`, `folderPath`, `newName`, `dataType`, `logicalAddress`, `externalAccessible`, `externalVisible`, `externalWritable`, `isSafety` |
| `delete_tag` | `tableName`, `name` | `plcName`, `folderPath` |
| `create_user_constant` | `tableName`, `name`, `dataType`, `value` | `plcName`, `folderPath` |
| `update_user_constant` | `tableName`, `name` | `plcName`, `folderPath`, `dataType`, `value` |
| `delete_user_constant` | `tableName`, `name` | `plcName`, `folderPath` |
| `start_plc` / `stop_plc` | — | `plcName` |

`create_block` accepts `FB`, `FC`, `OB`, or `GlobalDB`. Applicable block types support `LAD`, `FBD`, `STL`, `SCL`, and `GRAPH`. OB creation also accepts event classes such as `ProgramCycle`, `Startup`, `TimeDelay`, `CyclicInterrupt`, `HardwareInterrupt`, `Diagnostic`, and `TimeOfDay`.

## Update and write behavior

- Block updates validate the supplied document, import it into an existing block, compile the affected PLC scope, and verify the resulting block state through a postcondition/re-export check.
- Type updates require an existing addressed type and a declaration whose name matches that target.
- Block/type replacement previews can include bounded structured evidence that is current-versus-requested only, not predicted post-write state.
- Import/update operations modify existing objects only. They do not create, rename, delete, or upsert the addressed object.
- Deletes and PLC start/stop operations use the same confirmed write flow as other data writes.
- A write batch is applied in order and stops on the first failure. Completed mutations are not rolled back.
- Tag-related writes bind exact targets plus scoped name/address collision probes, replacing
  the full table-list safety payload. Table deletion binds the exact table's normalized Simatic ML
  export and digest; tag deletion binds exact tag state; constant deletion binds exact constant
  state. Create operations bind the parent/table identity and relevant collisions. Updates also
  bind the exact object's current state. See the [eight selector shapes](../ARCHITECTURE.md#8-write-safety)
  for the complete contract. Identical selectors share reads only within one phase and expand
  back into the original operation order; apply reads fresh state with no cross-phase cache.
- Tag and user-constant create/update name probes include case-insensitive matches against PLC
  tags, user constants, and blocks in the selected PLC's unqualified CPU namespace. Matching blocks
  include nested user and system-block groups; each probe retains its symbol kind and exact path.
  Logical-address probes remain tag-only. Table creation retains the exact destination folder but
  probes matching table names across the PLC's entire tag-table hierarchy, including sibling and
  nested folders. Unrelated names remain outside the snapshot; incomplete traversal fails closed.
  These scopes follow Siemens V21's [tag-name rules](https://docs.tia.siemens.cloud/r/en-us/v21/declaring-plc-tags/rules-for-plc-tags/valid-names-of-plc-tags),
  [constant-name rules](https://docs.tia.siemens.cloud/r/en-us/v21/declaring-plc-tags/declaring-global-constants/rules-for-global-user-constants),
  and [table-creation rules](https://docs.tia.siemens.cloud/r/en-us/v21/declaring-plc-tags/creating-and-managing-plc-tag-tables/creating-plc-tag-tables).
  Software Unit namespace-aware block collision coverage remains a design/live qualification
  follow-up; these operations do not treat unit-local names as unqualified CPU-global names.
- If an `update_tag` requests `externalAccessible`, `externalVisible`, or
  `externalWritable` and that selected flag cannot be read for the exact tag, preview fails before
  token issuance. This safety condition does not change the public `list_tag_tables` contract:
  that read remains best-effort and may retain its existing skipped-read behavior.

## Tag safety acceptance boundary

PR 5 has offline/FakeWorker evidence and a statically checked
[guarded live harness](../../scripts/live-test-tag-operation-safety-scopes.ps1). Mandatory live
TIA Portal V21 acceptance remains pending; the harness has not been executed in any mode.
Public previews expose hashes and ordered targets, so successful previews alone do not prove
internal typed snapshot contents, worker read counts, or apply behavior.

The harness requires PowerShell 7.2 or later, the built net8 host, an already-open exact
disposable project copy, and explicit PLC/table/tag/user-constant fixture names and values.
`PreviewOnly` is the non-mutating default. `DriftAndRestore` covers same-object drift, relevant
name/address collision drift, and unrelated sibling tolerance. `ApplyAndRestore` performs one
authorized feature apply. Both mutation modes require `-AllowMutation`, `-ConfirmDisposableCopy`, an
`-AuthorizedProjectPath` equal to `-ProjectPath`, and `-CleanupStrategy Discard`, plus a pre-saved
unmodified copy. The implemented cleanup is guarded `close_project` with `saveBeforeClose=false`;
the mode names do not imply an implemented inverse-restore strategy. No project files are deleted
and no save is issued. Scenario mutations remain until that final discard; no inverse fixture
writes restore constants or delete collision tags between checks. Acceptance must verify the
on-disk copy remains clean. A failed or
unconfirmed discard fails the run and requires manual no-save cleanup of the isolated copy.
Each run retains redacted MCP and failure/cleanup JSON in a dedicated artifact directory; it does
not automatically create a live acceptance report. Ordinary tests only inspect the script as text.

Explicitly deferred: multilingual per-tag comment binding; public `list_tag_tables` completeness
changes; broader snapshot narrowing; and PLC `start_plc` / `stop_plc` safety work. Existing PLC
start/stop operations are not changed or qualified by PR 5.

## Current limits

The current surface does not provide:

- Generic online/offline status or connection configuration.
- Compare-to-online, program upload/download, or `UpdateProgram` workflows.
- Software units, safety units, safety administration, safety signatures, or safety validation.
- PLC alarms, alarm classes, alarm text lists, ProDiag supervision, or supervision import/export.
- Technology objects, motion-control objects, watch tables, or force tables.
- OPC UA server configuration, communication groups, access control, or role mapping.
- System blocks, know-how protection workflows, webserver pages, block fingerprints, or loadable files.
