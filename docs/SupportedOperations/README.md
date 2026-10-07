# Supported TIA Portal Operations

This directory is the reference for the public operation surface of `tia-portal-mcp`. It describes the MCP tools, operation names, supported data formats, safety rules, and current capability boundaries.

The MCP surface is intentionally narrower than the complete TIA Portal Openness V21 API. A capability listed as a current limit is outside the server contract; it is not a statement that the underlying Openness API cannot perform that action.

## Operation model

### PLC read and write tools

PLC reads run through `plc_read` (up to 50 independent operations; a failed item does not stop the
remaining items) and the standalone `read_cross_references`. PLC writes run through `plc_write`
(up to 50 ordered operations against one project; the first failure stops the call, with no
rollback). See the [PLC operations summary](PLC_OPERATIONS_SUMMARY.md#plc-writes-plc_write).
The generic batch tools `preview_write_batch`, `apply_write_batch` and `execute_read_batch` were
retired.

Every item contains an `operationId`, an `operation` name, and the fields for that operation. Read and write operation names are separate; project-lifecycle operations are not valid items.

#### Read operations

`plc_read` supports `get_block_content`, `get_type_content`, and `list_tag_tables`;
`read_cross_references` is a standalone tool. `execute_read_batch` was retired.
Hardware/catalog reads use `network_read`. WinCC Unified HMI reads use `hmi_read`; see [HMI_OPERATIONS_SUMMARY.md](HMI_OPERATIONS_SUMMARY.md).

Project binding, status, project-tree browsing, and compilation are separate tools: `bind_project`, `get_project_status`, `browse_project_tree`, and `compile_check`. The first three are available in every mode; compile and lifecycle are available in read-write and full. Full adds no tools or operations; PLC run/stop was removed. See [Installation](../guides/installation.md#access-modes) for the 7/16/16 surfaces and confirmation policy.

#### Write operations

`plc_write` supports:

`update_block_logic`, `update_type_content`, `create_block`, `delete_block`, `create_block_group`, `delete_block_group`, `create_tag_table`, `delete_tag_table`, `create_tag`, `update_tag`, `delete_tag`, `create_user_constant`, `update_user_constant`, and `delete_user_constant`. Content updates take `content` together with `expectedContentHash` from a `plc_read`. Network provisioning/configuration uses `network_write`.

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

Lifecycle, Network and PLC writes use guarded single-call writes. No tool uses safety tokens.

- PLC writes preview with `dryRun:true`; omitted `dryRun` executes, with zero server elicitation. Collisions, existing blocks on create, the default tag table and unreadable evidence block the call in every mode; deletes and address overlaps are info. Content writes need `expectedContentHash`; stale content fails with `state_changed`. See the [PLC reference](PLC_OPERATIONS_SUMMARY.md#plc-writes-plc_write).
- Lifecycle uses `dryRun` with operation inputs and no public confirmation array or token. Every actual read-write call asks once through form elicitation; full runs under policy without server elicitation. Block guards refuse in every mode; dry runs do not mutate or elicit. See the [lifecycle reference](PROJECT_OPERATIONS_SUMMARY.md#lifecycle-operations).
- Network previews with `dryRun:true`; `dryRun:false` or omitted dryRun executes by default, with zero server elicitation. Exact already-open verified binding, complete guards and typed postchecks apply; stop on failure, no batch rollback or automatic replay. See the [Network contract](NETWORK_OPERATIONS_SUMMARY.md#network_write-envelope) for pre-entry SDK rejection versus canonical entered denials.
- A multi-item write is sequential rather than transactional. Application stops at the first failure; completed items remain applied and later items are marked `skipped`.
- Audit JSONL lives under `%LOCALAPPDATA%\TiaMcpServer\audit`. Lifecycle, Network and PLC audit v2 record every entered call, including previews and refusals, with confirmation by `user`, `policy`, or `none`; SDK rejection before entry has no write audit. The legacy write audit stream was retired.

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
| Multiuser inventory through `bind_project`, passive context and acceptance boundary | [MULTIUSER_OPERATIONS_SUMMARY.md](MULTIUSER_OPERATIONS_SUMMARY.md) |
| Teamcenter | [TEAMCENTER_OPERATIONS_SUMMARY.md](TEAMCENTER_OPERATIONS_SUMMARY.md) |
| TestSuite | [TESTSUITE_OPERATIONS_SUMMARY.md](TESTSUITE_OPERATIONS_SUMMARY.md) |

## Runtime requirements

The Openness worker is the only process that loads Siemens assemblies. Using the server requires Windows, TIA Portal V21 with Openness enabled, membership in the Siemens TIA Openness user group, and the supported .NET runtimes. The worker communicates with the .NET 10 host over newline-delimited JSON and is supervised across timeouts and crashes. The framework-dependent global tool requires a supported .NET 10 runtime; the separate self-contained `win-x64` archive includes the host runtime. Both layouts retain the .NET Framework 4.8 Openness worker.

This reference describes the software contract. A successful build or automated test does not replace validation against the target TIA Portal V21 installation, project, device configuration, or hardware.
