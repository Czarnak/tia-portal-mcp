# TIA Portal PLC Operations

## Supported read operations

| Operation | Inputs | Behavior |
|---|---|---|
| `browse_project_tree` | Optional `depth`, `startPath` | Locates PLC software, blocks, groups, types, and other project objects. |
| `get_block_content` | Required `blockPath`; optional `format` | Reads an existing block as XML/SimaticML or an eligible external source. See [IMPORT_EXPORT_OPTIONS_SUMMARY.md](IMPORT_EXPORT_OPTIONS_SUMMARY.md). |
| `get_type_content` | Required `typePath`; optional `format` | Reads an existing PLC type as `.udt` source or XML/SimaticML. |
| `list_tag_tables` | Optional `plcName` | Lists PLC tag tables and the exposed tag and constant information. |
| `read_cross_references` | Optional `plcName`, `filter`, `maxResults` | Reads cross-references. Filters are `AllObjects`, `ObjectsWithReferences`, `ObjectsWithoutReferences`, and `UnusedObjects`. |
| `compile_check` | Optional `plcName`, `blockPath` | Compiles the PLC or selected block scope and returns compiler messages. |

`compile_check` is classified as a read operation for batch and safety purposes, but it performs a compile action in TIA Portal.

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
- Import/update operations modify existing objects only. They do not create, rename, delete, or upsert the addressed object.
- Deletes and PLC start/stop operations use the same confirmed write flow as other data writes.
- A write batch is applied in order and stops on the first failure. Completed mutations are not rolled back.

## Current limits

The current surface does not provide:

- Generic online/offline status or connection configuration.
- Compare-to-online, program upload/download, or `UpdateProgram` workflows.
- Software units, safety units, safety administration, safety signatures, or safety validation.
- PLC alarms, alarm classes, alarm text lists, ProDiag supervision, or supervision import/export.
- Technology objects, motion-control objects, watch tables, or force tables.
- OPC UA server configuration, communication groups, access control, or role mapping.
- System blocks, know-how protection workflows, webserver pages, block fingerprints, or loadable files.
