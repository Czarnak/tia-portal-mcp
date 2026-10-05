# Supported TIA Portal Operations

This directory is the reference for the public operation surface of `tia-portal-mcp`. It describes the MCP tools, batch operation names, supported data formats, safety rules, and current capability boundaries.

The MCP surface is intentionally narrower than the complete TIA Portal Openness V21 API. A capability listed as a current limit is outside the server contract; it is not a statement that the underlying Openness API cannot perform that action.

## Operation model

### PLC read tools and batch write tools

PLC reads run through `plc_read` (up to 50 independent operations; a failed item does not stop the
remaining items) and the standalone `read_cross_references`; see the
[PLC operations summary](PLC_OPERATIONS_SUMMARY.md). Data writes run through the two legacy batch
tools until `plc_write` replaces them:

| Tool | Purpose |
|---|---|
| `preview_write_batch` | Validates and previews up to 50 data-write operations, then returns one single-use `safetyToken`. |
| `apply_write_batch` | Applies the exact previewed operation list in order. Requires `confirm=true` and the preview's `safetyToken`; both are set by the caller and are not a user approval. |

Every batch item contains an `operationId`, an `operation` name, and the fields for that operation. Read and write operation names are separate; project-lifecycle operations are not valid batch items.

In `apply_write_batch` responses, each `operations[]` item includes `failureCategory`. A failed item retains its approved worker failure category; succeeded, skipped, and omitted items have `failureCategory: null`. The existing `result` text and item status remain available.

#### Read operations

`plc_read` supports `get_block_content`, `get_type_content`, and `list_tag_tables`;
`read_cross_references` is a standalone tool. `execute_read_batch` was retired.
Hardware/catalog reads use `network_read`.

Project binding, status, project-tree browsing, and compilation are separate tools: `bind_project`, `get_project_status`, `browse_project_tree`, and `compile_check`. The first three are available in every mode; compile and lifecycle are available in read-write and full. OnlineControl (PLC run/stop) requires full. See [Installation](../guides/installation.md#access-modes) for the 6/16/16 surfaces and confirmation policy.

#### Write operations

`preview_write_batch` and `apply_write_batch` support:

`update_block_logic`, `update_type_content`, `create_block`, `delete_block`, `create_block_group`, `delete_block_group`, `create_tag_table`, `delete_tag_table`, `create_tag`, `update_tag`, `delete_tag`, `create_user_constant`, `update_user_constant`, `delete_user_constant`, `start_plc`, and `stop_plc`. PLC run/stop requires full. Network provisioning/configuration uses `network_write`.

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

Lifecycle and Network use guarded single-call writes. Only legacy generic batches retain
preview-then-apply consistency tokens; those tokens do not establish user consent.

- Data writes receive a batch-level token from `preview_write_batch` and require the unchanged operation list, `confirm=true`, and that token in `apply_write_batch`.
- Lifecycle uses `dryRun` with operation inputs and no public confirmation array or token. Every actual read-write call asks once through form elicitation; full runs under policy without server elicitation. Block guards refuse in every mode; dry runs do not mutate or elicit. See the [lifecycle reference](PROJECT_OPERATIONS_SUMMARY.md#lifecycle-operations).
- Network previews with `dryRun:true`; `dryRun:false` or omitted dryRun executes by default, with zero server elicitation. Exact already-open verified binding, complete guards and typed postchecks apply; stop on failure, no batch rollback or automatic replay. See the [Network contract](NETWORK_OPERATIONS_SUMMARY.md#network_write-envelope) for pre-entry SDK rejection versus canonical entered denials.
- Generic-batch tokens are single-use, expire after ten minutes, and bind the exact tool, normalized project path, requested input, and current project state.
- A write batch is sequential rather than transactional. Application stops at the first failure; completed items remain applied and later items are marked `skipped`.
- Audit JSONL lives under `%LOCALAPPDATA%\TiaMcpServer\audit`. Lifecycle and Network audit v2 record every entered call, including previews and refusals, with confirmation by `user`, `policy`, or `none`; Network SDK rejection before entry has no write audit. Generic batches retain their audit behavior.

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

The Openness worker is the only process that loads Siemens assemblies. Using the server requires Windows, TIA Portal V21 with Openness enabled, membership in the Siemens TIA Openness user group, and the supported .NET runtimes. The worker communicates with the .NET 10 host over newline-delimited JSON and is supervised across timeouts and crashes. The framework-dependent global tool requires a supported .NET 10 runtime; the separate self-contained `win-x64` archive includes the host runtime. Both layouts retain the .NET Framework 4.8 Openness worker.

This reference describes the software contract. A successful build or automated test does not replace validation against the target TIA Portal V21 installation, project, device configuration, or hardware.
