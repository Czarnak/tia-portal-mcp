# Supported TIA Openness Operations

This directory maps TIA Portal Openness operation areas to the functionality actually exposed by `tia-portal-mcp`.

## How to read these documents

The wider TIA Portal Openness V21 API is substantially broader than the MCP server's guarded surface. Each area summary therefore separates:

- **Exposed** — implemented as a public MCP tool or a batch operation dispatched to the .NET Framework Openness worker.
- **Not exposed** — no corresponding public MCP operation was found in the repository.
- **Static status** — conclusions are based on source and contract inspection. They are not live TIA Portal or hardware certification.

## Public MCP tools

The server exposes ten public tools:

| Tool | Role |
|---|---|
| `execute_read_batch` | Up to 50 independent read operations. |
| `preview_write_batch` | Preview up to 50 data-write operations and issue one safety token. |
| `apply_write_batch` | Apply the unchanged previewed write batch sequentially. |
| `get_project_status` | Read project status and metadata. |
| `open_project` | Open and bind a project. |
| `create_project` | Create and bind a project. |
| `save_project` | Save the active project. |
| `save_project_as` | Save a copy and rebind to the copy. |
| `archive_project` | Archive the active project. |
| `close_project` | Close the active project and clear the binding. |

The six lifecycle writes use an internal preview/apply flow. Data writes use `preview_write_batch` followed by `apply_write_batch` with the exact same ordered operations and a single-use token. Batch execution is not transactional: apply stops on the first failure and does not roll back completed items.

## Area summaries

| Area | Summary |
|---|---|
| Project and portal lifecycle | [PROJECT_OPERATIONS_SUMMARY.md](PROJECT_OPERATIONS_SUMMARY.md) |
| Devices | [DEVICES_OPERATIONS_SUMMARY.md](DEVICES_OPERATIONS_SUMMARY.md) |
| PLC software | [PLC_OPERATIONS_SUMMARY.md](PLC_OPERATIONS_SUMMARY.md) |
| HMI | [HMI_OPERATIONS_SUMMARY.md](HMI_OPERATIONS_SUMMARY.md) |
| Networks and topology | [NETWORK_OPERATIONS_SUMMARY.md](NETWORK_OPERATIONS_SUMMARY.md) |
| SIMATIC drives / Startdrive | [SIMATIC_DRIVES_OPERATIONS_SUMMARY.md](SIMATIC_DRIVES_OPERATIONS_SUMMARY.md) |
| Import/export | [IMPORT_EXPORT_OPTIONS_SUMMARY.md](IMPORT_EXPORT_OPTIONS_SUMMARY.md) |
| Multiuser | [MULTIUSER_OPERATIONS_SUMMARY.md](MULTIUSER_OPERATIONS_SUMMARY.md) |
| Teamcenter | [TEAMCENTER_OPERATIONS_SUMMARY.md](TEAMCENTER_OPERATIONS_SUMMARY.md) |
| TestSuite | [TESTSUITE_OPERATIONS_SUMMARY.md](TESTSUITE_OPERATIONS_SUMMARY.md) |

## Evidence boundaries

The primary implementation paths are:

- `TiaMcpServer/Batch/BatchOperationCatalog.cs` — registered batch operation names and required/optional fields.
- `TiaMcpServer/Batch/ReadBatchTools.cs` and `WriteBatchTools.cs` — public batch tools and safety boundary.
- `TiaMcpServer/Tools/ProjectLifecycleTools.cs` — public lifecycle tools.
- `TiaMcpServer.OpennessWorker/Program.cs` — worker dispatch and Openness handlers.
- `TiaMcpServer.Contracts/WorkerRequest.cs` — shared request fields and operation forwarding.

The worker is the only process that loads Siemens Openness assemblies. A successful static build or unit test does not prove runtime compatibility with a locally installed TIA Portal V21 project or hardware.
