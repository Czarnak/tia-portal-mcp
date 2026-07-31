# TIA Portal PLC Operations

## Scope

This file maps the TIA Portal PLC Openness area to the PLC capabilities exposed by `tia-portal-mcp`. The MCP covers a focused software-engineering subset; it is not a general PLC Openness proxy.

## Exposed read operations

| Operation | Inputs | Result or limitation |
|---|---|---|
| `browse_project_tree` | Optional `depth`, `startPath` | Locates PLC software, blocks, groups, types, and other project objects. |
| `get_block_content` | Required `blockPath`; optional `format`, `withDependencies` | Reads an existing PLC block as XML/SimaticML or eligible external source. See the import/export summary. |
| `get_type_content` | Required `typePath`; optional `format`, `withDependencies` | Reads an existing PLC type as `.udt` source or XML/SimaticML. |
| `list_tag_tables` | Optional `plcName` | Lists PLC tag tables and their exposed tag/constant information. |
| `read_cross_references` | Optional `plcName`, `filter`, `maxResults` | Reads cross-reference information. Filters are `AllObjects`, `ObjectsWithReferences`, `ObjectsWithoutReferences`, and `UnusedObjects`. |
| `compile_check` | Optional `plcName`, `blockPath` | Compiles the PLC or selected block scope and returns compiler messages. |

## Exposed write operations

All rows below use `preview_write_batch` followed by `apply_write_batch`.

| Operation | Required inputs | Optional inputs |
|---|---|---|
| `update_block_logic` | `blockPath`, `yamlContent` | `format` (`xml` by default; `source` where eligible) |
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
| `start_plc` / `stop_plc` | None | `plcName` |

`create_block` accepts `FB`, `FC`, `OB`, or `GlobalDB`. The language field supports `LAD`, `FBD`, `STL`, `SCL`, and `GRAPH` for the applicable block types; OB creation also accepts event classes such as `ProgramCycle`, `Startup`, `TimeDelay`, `CyclicInterrupt`, `HardwareInterrupt`, `Diagnostic`, and `TimeOfDay`.

## Guardrails and verification

- Block updates use the current block documents as the source for a validated import. The worker performs preflight, import, compile verification, and postcondition/re-export checks.
- Type updates likewise require an existing addressed type and an object declaration matching the target.
- Import/update operations do not create, rename, delete, or upsert the addressed object.
- Delete and PLC start/stop operations require the normal confirmed write flow.
- A batch applies sequentially and stops on the first failure; completed mutations are not rolled back.

## Not exposed

No public MCP operation was found for:

- Generic online/offline status and connection configuration.
- Compare-to-online, program upload/download, or `UpdateProgram` workflows.
- Software units, safety units, safety administration, safety signatures, or safety validation.
- PLC alarms, alarm classes, alarm text lists, ProDiag supervision, or supervision import/export.
- Technology objects, motion control objects, watch tables, or force tables.
- OPC UA server configuration, communication groups, access control, or role mapping.
- System blocks, know-how protection workflows, webserver pages, block fingerprints, or loadable files.

## Static evidence

- `TiaMcpServer/Batch/BatchOperationCatalog.cs`
- `TiaMcpServer/Batch/BatchOperationRequest.cs`
- `TiaMcpServer.OpennessWorker/Program.cs`
- `TiaMcpServer.OpennessWorker/Openness/BlockExporter.cs`
- `TiaMcpServer.OpennessWorker/Openness/BlockImporter.cs`
- `TiaMcpServer.OpennessWorker/Openness/PlcTypeExporter.cs`
- `TiaMcpServer.OpennessWorker/Openness/PlcTypeImporter.cs`
- `TiaMcpServer.OpennessWorker/Openness/TagTableReader.cs`
- `TiaMcpServer.OpennessWorker/Openness/TagMutationService.cs`
- `TiaMcpServer.OpennessWorker/Openness/PlcOnlineService.cs`
