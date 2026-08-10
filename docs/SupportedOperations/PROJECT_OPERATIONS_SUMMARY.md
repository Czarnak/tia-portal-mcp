# TIA Portal Project and Portal Operations

## Read operations

| Entry point | Operation | Inputs and behavior |
|---|---|---|
| `get_project_status` | `get_project_status` | Optional `projectPath`; reads status and metadata without opening or switching projects. |
| `browse_project_tree` | `browse_project_tree` | Optional `projectPath`, `depth`, and `startPath`; returns bounded project-tree data. |
| `compile_check` | `compile_check` | Optional `projectPath`, `plcName`, and `blockPath`; compiles the selected scope and returns compiler messages. Available only in read-write mode. |

`compile_check` is a standalone engineering operation. It is not marked read-only, does not use a safety token, and is exposed only in read-write mode.

### `get_project_status` metadata surface

When a project is open, `get_project_status` reports the status fields plus a nested `metadata`
object carrying the extended read-only project metadata:

| Field | Description |
| --- | --- |
| `copyright` | Project copyright text, verbatim from Openness. |
| `family` | Project family, verbatim from Openness. |
| `comment` | Multilingual project comment: `translations` list of `{ culture, text }` in the order Openness reports them, preserving every translation. `culture` is the language culture name (for example `en-US`). |
| `languageSettings` | `languages` and `activeLanguages` as culture-name lists; `editingLanguage` and `referenceLanguage` culture names (null when unset). |
| `historyEntries` | Text and date-time of each history entry, in Openness order, verbatim and not deduplicated. Capped at `1000` entries (oldest first); when Openness reports more, `historyTruncated` is `true`. When history could not be read, both `historyEntries` and `historyTruncated` are `null` (omitted) — `historyTruncated` is `false` only when history was read completely. |
| `usedProducts` | `{ name, version }` for every product Openness records, no inference and no deduplication. |
| `compilationSettings` | V21 block-compilation toggles read through `PlcSimulationSettingsProvider` and `VirtualPlcSettingsProvider`: `isSimulationDuringBlockCompilationEnabled` and `isVirtualPlcDuringBlockCompilationEnabled`. A value is `null` (omitted) when its provider or value is unavailable, reported as a response warning — never a fabricated `false`. |

All metadata is readable in both access modes; nothing here opens, closes, switches, saves, or
confirms anything. Unavailable sections degrade to a warning and `null` output rather than a
fabricated default; unrelated errors still fail the call normally.

The successful `get_project_status` response is subject to the standalone response budget
(60000 characters, like every other standalone read): an oversized status is truncated with an
explicit `TRUNCATED` marker naming the limit. Lifecycle post-write verification (after
`open_project`, `create_project`, `save_project`, `save_project_as`, and `archive_project`) reads
the plain project status only — it never enumerates history or the extended metadata surface.

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
- Project language settings and multilingual text import/export (reading is exposed through `get_project_status` metadata; editing is not).
- Full project attribute editing (copyright, family, comment, and language settings are read-only today).
- UMAC delegates, authentication events, or explicit primary/secondary `ProjectOpenMode` selection.
- Portal settings, diagnostics settings, or search-index administration.
- VCI workspace, version-control, compare, synchronize, or mapped-object operations.
- Multiuser server projects and local sessions; see [MULTIUSER_OPERATIONS_SUMMARY.md](MULTIUSER_OPERATIONS_SUMMARY.md).
