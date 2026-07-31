# TIA Portal Project and Portal Operations

## Scope

This file covers the MCP project-lifecycle surface. It records the project operations exposed through `tia-portal-mcp`, not the complete TIA Portal Openness project API.

## Exposed operations

### Read operations

| Public entry point | Operation | Inputs and behavior |
|---|---|---|
| `get_project_status` | `get_project_status` | Optional `projectPath`; reads status and metadata without opening or switching projects. |
| `execute_read_batch` | `get_project_status` | Same status read inside a read batch. |
| `execute_read_batch` | `browse_project_tree` | Optional `depth` and `startPath`; returns the project tree, with bounded traversal for large projects. |
| `execute_read_batch` | `compile_check` | Optional `plcName` and `blockPath`; compiles the selected scope and returns compile messages. The batch catalog classifies this as a read operation, although compilation is an engineering action inside TIA Portal. |

### Lifecycle operations

| Public tool | Openness action | Main inputs |
|---|---|---|
| `open_project` | Open and bind | Absolute `.ap21` `projectPath`; `forceRebind` can explicitly allow rebinding. |
| `create_project` | Create and bind | Absolute `projectDirectory`, `projectName`; optional `author` and `comment`. |
| `save_project` | Save | Optional project path; otherwise the active project. |
| `save_project_as` | Save a copy and rebind | `targetDirectory`, `targetName`; optional source `projectPath`; `rebind` must remain `true`. |
| `archive_project` | Archive | `archiveDirectory`, `archiveName`; optional `mode`, `saveBeforeArchive`, and `projectPath`. Supported mode names are `None`, `DiscardRestorableData`, `Compressed`, and `DiscardRestorableDataAndCompressed`. |
| `close_project` | Close and clear binding | Optional `projectPath` and `saveBeforeClose`. |

Lifecycle calls are deliberately non-batchable. The worker also has an internal `probe_project_status_for_lifecycle` method used for guarded lifecycle handling; it is not a public MCP tool.

## Safety and binding behavior

- Lifecycle writes preview when called without a safety token, then require `confirm=true` plus the returned single-use token.
- Tokens expire after ten minutes and are bound to the exact tool input, project path, and current project state.
- `save_project_as` requires rebinding because Siemens `SaveAs` switches the active project to the copy.
- Archive output is rejected when the archive directory is inside the project folder.
- If a write times out or the worker crashes, the client must inspect current state rather than automatically retrying.

## Not exposed

No public MCP operation was found for:

- `OpenWithUpgrade`, `Retrieve`, `RetrieveWithUpgrade`, or project deletion.
- Project history, used-products metadata, simulation/virtual-PLC properties, or full project attribute editing.
- Project language settings and multilingual text import/export.
- UMAC delegates, authentication events, or explicit primary/secondary `ProjectOpenMode` selection.
- Portal settings, diagnostics settings, or search-index administration.
- VCI workspace, version-control, compare, synchronize, or mapped-object operations.
- Multiuser server projects and local sessions; see [MULTIUSER_OPERATIONS_SUMMARY.md](MULTIUSER_OPERATIONS_SUMMARY.md).

## Static evidence

- `TiaMcpServer/Tools/ProjectLifecycleTools.cs`
- `TiaMcpServer/Tools/ProjectReadTools.cs`
- `TiaMcpServer/Tools/ProjectWriteTools.cs`
- `TiaMcpServer.OpennessWorker/Openness/ProjectLifecycleService.cs`
- `TiaMcpServer.Contracts/WorkerRequest.cs`
