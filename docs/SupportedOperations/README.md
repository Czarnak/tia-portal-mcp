# Supported TIA Portal Operations

This directory is the reference for the public operation surface of `tia-portal-mcp`. It describes the MCP tools, batch operation names, supported data formats, safety rules, and current capability boundaries.

The MCP surface is intentionally narrower than the complete TIA Portal Openness V21 API. A capability listed as a current limit is outside the server contract; it is not a statement that the underlying Openness API cannot perform that action.

## Operation model

### Batch tools

Data operations run through one of three batch tools:

| Tool | Purpose |
|---|---|
| `execute_read_batch` | Executes up to 50 independent read operations. A failed item does not stop the remaining items. |
| `preview_write_batch` | Validates and previews up to 50 data-write operations, then returns one single-use `safetyToken`. |
| `apply_write_batch` | Applies the exact previewed operation list in order after confirmation. |

Every batch item contains an `operationId`, an `operation` name, and the fields for that operation. Read and write operation names are separate; project-lifecycle operations are not valid batch items.

#### Read operations

`execute_read_batch` supports:

`read_hardware_config`, `search_equipment_catalog`, `read_cross_references`, `get_block_content`, `list_tag_tables`, and `get_type_content`.

Project status, project-tree browsing, and compilation are separate tools: `get_project_status`, `browse_project_tree`, and `compile_check`. The first two are available in both access modes; `compile_check` is available only in read-write mode.

#### Write operations

`preview_write_batch` and `apply_write_batch` support:

`update_block_logic`, `update_type_content`, `create_block`, `delete_block`, `create_block_group`, `delete_block_group`, `create_tag_table`, `delete_tag_table`, `create_tag`, `update_tag`, `delete_tag`, `create_user_constant`, `update_user_constant`, `delete_user_constant`, `add_network_device`, `configure_network_device`, `start_plc`, and `stop_plc`.

### Project lifecycle tools

The server also provides six single-purpose lifecycle tools:

| Tool | Behavior |
|---|---|
| `open_project` | Opens a project and binds the MCP session to it. |
| `create_project` | Creates a project and binds the session to it. |
| `save_project` | Saves the active project. |
| `save_project_as` | Saves a copy and rebinds the session to the copy. |
| `archive_project` | Archives a project to the requested location. |
| `close_project` | Closes the active project and clears the session binding. |

`get_project_status`, `browse_project_tree`, and `compile_check` are standalone project tools rather than batch operations.

## Write safety

All writes use preview-then-apply confirmation.

- Data writes receive a batch-level token from `preview_write_batch` and require the unchanged operation list, `confirm=true`, and that token in `apply_write_batch`.
- Lifecycle tools preview themselves when called without a token. The same tool is called again with `confirm=true` and the returned token to apply the change.
- Tokens are single-use, expire after ten minutes, and bind the exact tool, normalized project path, requested input, and current project state.
- A write batch is sequential rather than transactional. Application stops at the first failure; completed items remain applied and later items are marked `skipped`.
- Successful write attempts produce audit JSONL records under `%LOCALAPPDATA%\TiaMcpServer\audit`.

Read responses may include `warnings` for partial or degraded data. Hardware reads also provide payload-level `messages` for unreadable members. Callers should treat these fields as part of the result contract rather than filling missing values locally.

## Area reference

| Area | Reference |
|---|---|
| Project and portal lifecycle | [PROJECT_OPERATIONS_SUMMARY.md](PROJECT_OPERATIONS_SUMMARY.md) |
| Devices | [DEVICES_OPERATIONS_SUMMARY.md](DEVICES_OPERATIONS_SUMMARY.md) |
| PLC software | [PLC_OPERATIONS_SUMMARY.md](PLC_OPERATIONS_SUMMARY.md) |
| HMI | [HMI_OPERATIONS_SUMMARY.md](HMI_OPERATIONS_SUMMARY.md) |
| Networks and topology | [NETWORK_OPERATIONS_SUMMARY.md](NETWORK_OPERATIONS_SUMMARY.md) |
| SIMATIC drives / Startdrive | [SIMATIC_DRIVES_OPERATIONS_SUMMARY.md](SIMATIC_DRIVES_OPERATIONS_SUMMARY.md) |
| PLC import/export formats | [IMPORT_EXPORT_OPTIONS_SUMMARY.md](IMPORT_EXPORT_OPTIONS_SUMMARY.md) |
| Multiuser | [MULTIUSER_OPERATIONS_SUMMARY.md](MULTIUSER_OPERATIONS_SUMMARY.md) |
| Teamcenter | [TEAMCENTER_OPERATIONS_SUMMARY.md](TEAMCENTER_OPERATIONS_SUMMARY.md) |
| TestSuite | [TESTSUITE_OPERATIONS_SUMMARY.md](TESTSUITE_OPERATIONS_SUMMARY.md) |

## Runtime requirements

The Openness worker is the only process that loads Siemens assemblies. Using the server requires Windows, TIA Portal V21 with Openness enabled, membership in the Siemens TIA Openness user group, and the supported .NET runtimes. The worker communicates with the .NET 8 host over newline-delimited JSON and is supervised across timeouts and crashes.

This reference describes the software contract. A successful build or automated test does not replace validation against the target TIA Portal V21 installation, project, device configuration, or hardware.
