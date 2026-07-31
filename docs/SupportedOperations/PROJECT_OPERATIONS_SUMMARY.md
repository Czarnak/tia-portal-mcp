# TIA Portal Project and Portal Operations

## Read operations

| Entry point | Operation | Inputs and behavior |
|---|---|---|
| `get_project_status` | `get_project_status` | Optional `projectPath`; reads status and metadata without opening or switching projects. |
| `execute_read_batch` | `get_project_status` | The same status read inside a read batch. |
| `execute_read_batch` | `browse_project_tree` | Optional `depth` and `startPath`; returns bounded project-tree data. |
| `execute_read_batch` | `compile_check` | Optional `plcName` and `blockPath`; compiles the selected scope and returns compiler messages. |

`compile_check` is a read-batch operation for safety classification, although compiling is an engineering action in TIA Portal.

## Lifecycle operations

| Tool | Behavior | Main inputs |
|---|---|---|
| `open_project` | Opens a project and binds the session to it. | Absolute `.ap21` `projectPath`; optional `forceRebind`. |
| `create_project` | Creates a project and binds the session to it. | Absolute `projectDirectory`, `projectName`; optional `author`, `comment`. |
| `save_project` | Saves the active project. | Optional `projectPath`. |
| `save_project_as` | Saves a copy and rebinds the session to the copy. | `targetDirectory`, `targetName`; optional source `projectPath`; `rebind` must remain `true`. |
| `archive_project` | Archives a project. | `archiveDirectory`, `archiveName`; optional `mode`, `saveBeforeArchive`, `projectPath`. |
| `close_project` | Closes the project and clears the session binding. | Optional `projectPath`, `saveBeforeClose`. |

Supported archive modes are `None`, `DiscardRestorableData`, `Compressed`, and `DiscardRestorableDataAndCompressed`. Lifecycle tools are single-tool operations and cannot be included in a batch.

## Safety and session binding

- A lifecycle call without a safety token returns a preview and a single-use token. Applying the change requires the same tool input, `confirm=true`, and that token.
- Tokens expire after ten minutes and bind the exact tool input, project path, and current project state.
- `save_project_as` requires rebinding because Siemens `SaveAs` switches the active project to the copy.
- Archive output is rejected when the archive directory is inside the project folder.
- After a timeout or worker crash, inspect the current project state before deciding whether another call is safe; the server does not automatically retry lifecycle writes.
- `get_project_status(projectPath)` is non-binding and never switches the active project. Use `open_project` for an intentional project switch.

## Current limits

The current project surface does not provide:

- `OpenWithUpgrade`, `Retrieve`, `RetrieveWithUpgrade`, or project deletion.
- Project history, used-products metadata, simulation/virtual-PLC properties, or full project attribute editing.
- Project language settings and multilingual text import/export.
- UMAC delegates, authentication events, or explicit primary/secondary `ProjectOpenMode` selection.
- Portal settings, diagnostics settings, or search-index administration.
- VCI workspace, version-control, compare, synchronize, or mapped-object operations.
- Multiuser server projects and local sessions; see [MULTIUSER_OPERATIONS_SUMMARY.md](MULTIUSER_OPERATIONS_SUMMARY.md).
