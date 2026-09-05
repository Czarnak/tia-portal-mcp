# TIA Portal MCP server

[![Build Status](https://img.shields.io/github/actions/workflow/status/Czarnak/tia-portal-mcp/ci.yml?branch=main&style=flat-square)](https://github.com/Czarnak/tia-portal-mcp/actions)
[![Codecov](https://img.shields.io/codecov/c/github/Czarnak/tia-portal-mcp?style=flat-square)](https://codecov.io/gh/Czarnak/tia-portal-mcp)
[![GitHub Release](https://img.shields.io/github/v/release/Czarnak/tia-portal-mcp?style=flat-square)](https://github.com/Czarnak/tia-portal-mcp/releases)
[![.NET SDK](https://img.shields.io/badge/.NET-8.0-512BD4.svg?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![.NET Framework](https://img.shields.io/badge/.NET_Framework-4.8-512BD4.svg?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![TIA Portal](https://img.shields.io/badge/TIA_Portal-V21-009999.svg?style=flat-square&logo=siemens)](https://new.siemens.com/global/en/products/automation/industry-software/automation-software/tia-portal.html)
[![MCP](https://img.shields.io/badge/MCP-Ready-000000.svg?style=flat-square)](https://modelcontextprotocol.io/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg?style=flat-square)](https://github.com/Czarnak/tia-portal-mcp/blob/main/LICENSE)
![NuGet Downloads](https://img.shields.io/nuget/dt/TiaMcpServer)

MCP server for Siemens SIMATIC TIA Portal V21. It lets MCP clients and AI agents inspect a running TIA Portal project through the Siemens Openness API.

The current implementation covers project discovery and lifecycle operations, PLC block export/import, tag table reads and guarded tag mutations, hardware/network discovery, cross-reference diagnostics, hardware catalog search, guarded network-device provisioning, and compile/check diagnostics.

## Tools

The server currently exposes 14 tools in read-write mode and 4 tools in read-only mode.

### Batch operations

- `execute_read_batch` - run up to 50 retained generic read operations in one call. Each item carries an `operationId`, an `operation` name (e.g. `get_block_content`, `list_tag_tables`), and that operation's parameters. Reads run independently, so a failing item does not stop the others. Bound `read_cross_references` with `maxResults`; oversized batch responses are truncated or omitted server-side with explicit markers.
- `preview_write_batch` / `apply_write_batch` - preview up to 50 retained generic data writes and receive one batch-level `safetyToken` bound to the exact ordered operation list and the combined current state, then apply them. Apply runs sequentially, stops on the first failure, and marks later items `skipped` (no transaction or rollback). Requires `confirm=true` and the `safetyToken`. Project-lifecycle and network writes stay dedicated.

The generic batch tools are the path for retained block, PLC type, tag-table, tag, and user-constant operations. Each `operation` name carries that operation's parameters as one item; a single operation is just a one-item batch.

Every operation result may carry a `warnings` array — non-fatal degradation notes captured from the TIA Openness worker. A populated `warnings` array means the payload may be partial.

Available read operations for `execute_read_batch`: `read_cross_references`, `get_block_content`, `list_tag_tables`, and `get_type_content`.

Available write operations (for `preview_write_batch` / `apply_write_batch`): `update_block_logic`, `update_type_content`, `create_block` / `delete_block`, `create_block_group` / `delete_block_group`, `create_tag_table` / `delete_tag_table`, `create_tag` / `update_tag` / `delete_tag`, `create_user_constant` / `update_user_constant` / `delete_user_constant`.

`get_block_content` / `update_block_logic` and `get_type_content` / `update_type_content` accept a `format` field. `format=source` is available for global data blocks, PLC data types, and SCL-language FB/FC/OB. Every other block language stays on `format=xml`.

`withDependencies` (reads only, default `false`) asks TIA Portal to include the object's dependency closure. The resulting document declares several objects and is **context only** — a write refuses any source declaring more than one object, and the read carries a warning saying so. Omit the field to get a document you can edit and submit back.

### Network operations

`network_read` and `network_write` both declare an MCP output schema and return one canonical JSON document identically as the `content` text block and as `structuredContent` — never a nested JSON string inside an outer envelope. This is the Phase 2 JSON contract; see [docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md](https://github.com/Czarnak/tia-portal-mcp/blob/main/docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md) for the exact envelopes.

- `network_read` - run up to 50 dedicated network reads: `read_hardware_config`, `search_equipment_catalog`, `list_network_objects`, and `inspect_network_object`. Reads run independently. Bound catalog searches with `query` and `maxResults`; `read_hardware_config` accepts an optional `deviceName` filter, an optional `plcName` for tag matching, and opt-in structured I/O extraction (`includeIoDetails` for addresses and channels, `includeTagMatches` for exact PLC tag matches per channel). Detailed I/O output can be large, so filter by `deviceName` or split into separate calls.
- `network_write` - preview or apply up to 50 dedicated network writes: `add_network_device` (flat `typeIdentifier`/`deviceName`, since it names something that does not exist yet), `configure_network_device` (nested `target: { deviceName, nodeId }` plus `changes: { ipAddress?, subnetMask?, pnDeviceName?, subnet?: { subnetId }, ioSystem?: { subnetId, number } }` — a null `changes` member means leave that setting unchanged), `create_subnet` (`subnet: { name, networkType }`, plus PROFIBUS-only `highestAddress`/`transmissionSpeed`), `update_subnet` (`target: { subnetId }` plus `subnetChanges` with at least one member), and `delete_subnet` (`target: { subnetId }` — connected or not). Call with `confirm:false` and no token for a preview, then call the same tool with `confirm:true`, the unchanged ordered list, and the returned `safetyToken`. Apply is sequential, stops on the first failure, marks later items skipped, and does not roll back completed writes: `network_write` attaches an explicit warning to the failed item that this operation and any earlier operation in the same call may already have changed TIA state, and that you should re-read with `network_read` before retrying rather than blindly re-running the batch.

`configure_network_device` targets one exact existing node: `target.deviceName` (case-insensitive) plus the exact `target.nodeId` reported by a prior `network_read` — never the first interface or first node on a device. `changes.subnet.subnetId` and `changes.ioSystem.subnetId`/`changes.ioSystem.number` are similarly exact, subnet-scoped selectors. Selector resolution is fail-closed: a selector that matches zero, more than one, or an unreadable candidate always fails with `postcondition_failed` rather than falling back to a guess. This is what makes it safe to target one port on a multi-homed device (a PC station with several network interfaces, for example) — configuring one node's exact `nodeId` changes only that node; every other node on the device is left byte-for-byte unchanged. Always follow a `network_write` apply with a `network_read` (`read_hardware_config`) post-read to confirm the outcome — the response never echoes back a re-read of the written value.

`read_hardware_config` additionally reports unreadable members in a payload-level `messages` array; device/module name and type-identifier fields omit values that could not be read instead of returning `0`/empty-string placeholders (a few secondary name fields still fall back to an empty string, with the failure noted in `messages`). Hardware configuration data is engineering evidence, not certification that a physical installation has been commissioned.

`read_hardware_config` supports opt-in structured I/O extraction. With `includeIoDetails: true`, each device item carries an `ioDetails` object with `addresses[]` (Openness `Address` evidence: `ioType`, byte-based `startAddress`, `length`, dynamic `context`, and ordinal `controllerNames`) and `channels[]` (`number`, `ioType`, `type`, bit-based `channelAddressBits`, `channelWidthBits`, and a formatted `logicalAddress` such as `%I4.0`/`%IW64` emitted only when the evidence is present and aligned). With `includeTagMatches: true` (which requires `includeIoDetails`), each channel carries `tagMatches[]` resolved deterministically against one selected PLC's tag tables — `plcName` selects the PLC by exact name, otherwise tag matching applies only when exactly one PLC exists. A tag matches a channel only when its normalized absolute I/O interval is identical to the channel's, and tags are never matched across controllers. A default read (no flags) returns no `ioDetails` and is byte-identical to earlier versions. See [docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md](https://github.com/Czarnak/tia-portal-mcp/blob/main/docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md) for the exact request/response shapes and unit semantics.

Large hardware reads can opt into cursor pagination with `pageSize` (`1..200`) or `cursor`. Pages count devices first and then subnets in one stable sequence while keeping the two public arrays separate; canonical size projection may return fewer complete entities than requested. Follow `pagination.nextCursor` until it is absent/null and keep the project, filters, and detail flags unchanged. Requests with neither field retain the byte-for-byte unpaged contract. Cursors are process-local and cannot survive a host restart. See the [Network operations reference](https://github.com/Czarnak/tia-portal-mcp/blob/main/docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md#hardware-configuration-pagination) for recovery and omission semantics.

Available write operations (for `preview_write_batch` / `apply_write_batch`): `update_block_logic`, `update_type_content`, `create_block` / `delete_block`, `create_block_group` / `delete_block_group`, `create_tag_table` / `delete_tag_table`, `create_tag` / `update_tag` / `delete_tag`, `create_user_constant` / `update_user_constant` / `delete_user_constant`, `add_network_device`, `configure_network_device`, `start_plc` / `stop_plc`.

### Project tools

- `get_project_status` — read active project metadata without opening or switching projects.
- `browse_project_tree` — browse a bounded project subtree with optional `depth` and `startPath`.
- `compile_check` — compile a PLC or selected block and return compiler messages; available only in read-write mode.
- `open_project` / `create_project` / `save_project` / `save_project_as` / `archive_project` / `close_project` - project lifecycle writes. These stay single-tool only (not batchable) and are self-previewing: call the tool WITHOUT `safetyToken` to get a preview plus a single-use token, then call it again with `confirm=true` and the token to apply.

## Write safety

Every MCP write operation uses a preview-then-apply workflow. Generic batch data writes preview with `preview_write_batch` and apply with `apply_write_batch`. Network and project lifecycle writes are self-previewing: call the same write tool WITHOUT `safetyToken` (with `confirm:false` for `network_write`) to get the preview (summary, `currentStateHash`, `requestedInputHash`, a fresh single-use `safetyToken`, and `instructions`), review it, then call the same tool again with the same arguments plus `confirm=true` and the `safetyToken`.

Safety tokens are single-use, expire 10 minutes after preview, and are bound to the exact tool name, target, requested input, current project state, and host binding revision. Project-scoped tokens also retain the complete verified project identity (worker id, TIA Portal PID, project generation, and canonical path). `open_project` and `create_project` may start from an unbound/configured revision, but they bind only to the successful worker response after lifecycle continuity checks. The server rejects missing, expired, reused, mismatched, stale-state, restarted-worker, or reopened-project tokens. Successful write attempts append audit JSONL records under `%LOCALAPPDATA%\TiaMcpServer\audit`.

The worker never attaches to the first enumerated TIA Portal or selects the first open project. It
requires an exact path match or a genuinely sole candidate; multiple possible targets fail with
`target_ambiguous` before Attach or mutation. A configured `--project` path becomes write-ready only
after a matching worker identity is observed. Later identity drift returns `binding_conflict` and
invalidates the session until an explicit rebind.

`preview_write_batch` issues one token for the whole batch, bound to the exact ordered operation list and the combined current state. Reordering items, changing any item's input, retargeting the project path, or a change in project state all invalidate the token. `apply_write_batch` re-reads the combined current state once before consuming the token, then applies items sequentially and stops on the first failure.

Apply-time state validation, token consumption, mutation, post-verification, and audit capture run under one pinned project-binding lease. A concurrent rebind cannot redirect an authorized operation, and two tokens previewed from the same state cannot both write: after the first mutation, the second apply fails with `state_changed`.

`network_write` snapshots topology once for preview and once for apply-time token validation. Its token is bound to the exact ordered network operation list and project state; successful apply attempts append an audit record.

Every failed write reports a categorized `failureCategory` field alongside its human-readable `error` message, so a caller can branch on the exact failure without parsing text. Common categories include `validation_error`, `binding_conflict`, `state_changed`, `target_ambiguous`, `worker_operation_failed`, `worker_timeout`, `worker_crashed`, `protocol_error`, and `postcondition_failed`. `save_project_as` requires `rebind:true`; calling it with `rebind:false` is rejected up front with `validation_error` before any preview, safety-token issuance, Siemens `SaveAs` call, or audit write, so it has no side effects. Warnings are always reported in a separate `warnings` array from the primary success/failure outcome — a populated `warnings` array never turns a failure into a success, and a categorized failure is never masked by an accompanying warning.

## Architecture

TIA Portal V21 ships its Openness API as .NET Framework 4.8 assemblies. Those assemblies use .NET Framework remoting APIs that cannot run correctly inside a .NET 8 process.

This project therefore uses two processes:

- `TiaMcpServer` - the .NET 8 MCP stdio server and .NET global tool host.
- `TiaMcpServer.OpennessWorker` - a .NET Framework 4.8 worker process that loads `Siemens.Engineering.*` and talks to TIA Portal.

The MCP host keeps one persistent .NET Framework 4.8 worker process attached to TIA Portal and exchanges newline-delimited JSON over stdin/stdout. Requests are serialized, and the worker restarts automatically after a crash or timeout. Siemens DLLs are never copied into this repository or the NuGet package; the worker resolves them from the local TIA Portal V21 installation.


## Quick start

Install the server as a .NET global tool, check your environment, and register it with an MCP client:

```powershell
dotnet tool install -g TiaMcpServer
tia-mcp doctor
tia-mcp install claude-code
```

`tia-mcp doctor` validates Windows version, .NET runtimes, the TIA Portal installation, Openness
assemblies, user-group membership, and host/worker compatibility before you connect anything. Run it
first — it reports exactly which prerequisite is missing.

| Prerequisite | Notes |
| --- | --- |
| Windows | Windows-only; the Openness API has no other host |
| Siemens TIA Portal V21 | with Openness installed and enabled |
| `Siemens TIA Openness` group | the current Windows user must be a member |
| .NET SDK 8.0 or newer | required by `dotnet tool install` |
| .NET Framework 4.8 runtime | required by the Openness worker process |

Supported clients for `tia-mcp install`: Claude Code, Codex, OpenCode, MiMoCode. Servers register in
**read-only** mode by default; add `--access-mode read-write` to expose the write tools.

Binding to a specific project, every install option, and the full access-mode reference are in the
[installation guide](https://github.com/Czarnak/tia-portal-mcp/blob/main/docs/guides/installation.md). To build from source instead of installing
the published tool, see [building from source](https://github.com/Czarnak/tia-portal-mcp/blob/main/docs/development/building.md).

## Documentation

**Using the server**

- [Installation](https://github.com/Czarnak/tia-portal-mcp/blob/main/docs/guides/installation.md) — install, verify with `doctor`, register with a client, access modes
- [MCP client configuration](https://github.com/Czarnak/tia-portal-mcp/blob/main/docs/guides/mcp-client-configuration.md) — client config reference and block path addressing
- [Troubleshooting](https://github.com/Czarnak/tia-portal-mcp/blob/main/docs/guides/troubleshooting.md) — common failures and verified TIA Portal V21 behavior
- [Supported operations](https://github.com/Czarnak/tia-portal-mcp/blob/main/docs/SupportedOperations/README.md) — every operation by area, with parameters

**Understanding the design**

- [Architecture](https://github.com/Czarnak/tia-portal-mcp/blob/main/docs/ARCHITECTURE.md) — two-process topology, access enforcement, write safety, the canonical JSON seam

**Building and contributing**

- [Contributing](https://github.com/Czarnak/tia-portal-mcp/blob/main/CONTRIBUTING.md) — workflow, branch and commit conventions
- [Building from source](https://github.com/Czarnak/tia-portal-mcp/blob/main/docs/development/building.md) — build, test, coverage, run locally
- [Local MCP sandbox testing](https://github.com/Czarnak/tia-portal-mcp/blob/main/docs/development/local-mcp-testing.md) — the MCP Inspector test loop
- [Packaging](https://github.com/Czarnak/tia-portal-mcp/blob/main/docs/development/packaging.md) — build the package, install a local build as `tia-mcp`

**Direction**

- [Roadmap](https://github.com/Czarnak/tia-portal-mcp/blob/main/ROADMAP.md) — directional priorities
- [Network operations roadmap](https://github.com/Czarnak/tia-portal-mcp/blob/main/docs/roadmap/network-operations.md) — phased network tool delivery
- [Export/import format roadmap](https://github.com/Czarnak/tia-portal-mcp/blob/main/docs/roadmap/export-import-format.md) — source-format exchange
- [Improvement log](https://github.com/Czarnak/tia-portal-mcp/blob/main/docs/IMPROVEMENT_LOG.md) — open follow-ups and completed engineering work

## Contributing

Contributions are welcome. See [CONTRIBUTING.md](https://github.com/Czarnak/tia-portal-mcp/blob/main/CONTRIBUTING.md) for the development workflow and how to set up your environment. For architecture and build reference, see [AGENTS.md](https://github.com/Czarnak/tia-portal-mcp/blob/main/AGENTS.md).

## Security

For how to report security vulnerabilities, see [SECURITY.md](https://github.com/Czarnak/tia-portal-mcp/blob/main/SECURITY.md).

## Check other tools

- [TIA Portal V21 Git Add-In](https://github.com/Czarnak/tia-git-addin)
- [Claude Code / Codex / Gemini plugin for TIA Portal development](https://github.com/Czarnak/totally-integrated-claude)
