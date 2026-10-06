# TIA Portal MCP server

[![Build Status](https://img.shields.io/github/actions/workflow/status/Czarnak/tia-portal-mcp/ci.yml?branch=main&style=flat-square)](https://github.com/Czarnak/tia-portal-mcp/actions)
[![Codecov](https://img.shields.io/codecov/c/github/Czarnak/tia-portal-mcp?style=flat-square)](https://codecov.io/gh/Czarnak/tia-portal-mcp)
[![GitHub Release](https://img.shields.io/github/v/release/Czarnak/tia-portal-mcp?style=flat-square)](https://github.com/Czarnak/tia-portal-mcp/releases)
[![.NET SDK](https://img.shields.io/badge/.NET-10.0-512BD4.svg?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![.NET Framework](https://img.shields.io/badge/.NET_Framework-4.8-512BD4.svg?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![TIA Portal](https://img.shields.io/badge/TIA_Portal-V21-009999.svg?style=flat-square&logo=siemens)](https://new.siemens.com/global/en/products/automation/industry-software/automation-software/tia-portal.html)
[![MCP](https://img.shields.io/badge/MCP-Ready-000000.svg?style=flat-square)](https://modelcontextprotocol.io/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg?style=flat-square)](https://github.com/Czarnak/tia-portal-mcp/blob/main/LICENSE)
![NuGet Downloads](https://img.shields.io/nuget/dt/TiaMcpServer)

MCP server for Siemens SIMATIC TIA Portal V21. It lets MCP clients and AI agents inspect a running TIA Portal project through the Siemens Openness API.

The current implementation covers project discovery and lifecycle operations, PLC block export/import, tag table reads and guarded tag mutations, hardware/network discovery, cross-reference diagnostics, hardware catalog search, guarded network-device provisioning, and compile/check diagnostics.

## Tools

The server exposes 6 tools in `read-only`, 16 in `read-write` (the startup default), and 16 in `full`.
`read-write` permits in-project edits, compilation and project lifecycle calls with user confirmation.
`full` adds PLC runtime control and runs lifecycle calls directly. Lifecycle uses guarded single-call
writes; Network uses the same guarded pipeline without server elicitation, while legacy batch writes retain tokens.

### PLC reads and cross-references

- `plc_read` - run up to 50 PLC read operations in one call: `get_block_content`, `get_type_content` and `list_tag_tables`. Each item carries a unique `operationId`, an `operation` name, and that operation's parameters. Items run independently, so a failing item does not stop the others. Results are structured JSON with `contractVersion` `1.0`; a value over 60,000 characters is omitted whole with narrowing guidance, never cut.
- `read_cross_references` - read the cross-references of one project-tree target (a block, type, tag, constant, or a PLC, software unit or folder swept over every owner beneath it). Arguments: `target`, `filter`, `maxResults`, `projectPath`. Both reads are available in every access mode and never open or switch a project.

### Batch operations

The generic batch write tools remain on their legacy token flow until the planned `plc_write` replaces them; `execute_read_batch` was retired in favor of `plc_read` and `read_cross_references`.

- `preview_write_batch` / `apply_write_batch` - preview up to 50 retained generic data writes and receive one batch-level `safetyToken` bound to the exact ordered operation list and the combined current state, then apply them. Apply runs sequentially, stops on the first failure, and marks later items `skipped` (no transaction or rollback). Requires `confirm=true` and the `safetyToken`. Project-lifecycle and network writes stay dedicated.

The generic batch write tools are the path for retained block, PLC type, tag-table, tag, and user-constant writes. Each `operation` name carries that operation's parameters as one item; a single operation is just a one-item batch.

Every operation result may carry a `warnings` array — non-fatal degradation notes captured from the TIA Openness worker. A populated `warnings` array means the payload may be partial.

Available `plc_read` operations: `get_block_content`, `get_type_content`, and `list_tag_tables`.

Available write operations (for `preview_write_batch` / `apply_write_batch`): `update_block_logic`, `update_type_content`, `create_block` / `delete_block`, `create_block_group` / `delete_block_group`, `create_tag_table` / `delete_tag_table`, `create_tag` / `update_tag` / `delete_tag`, `create_user_constant` / `update_user_constant` / `delete_user_constant`.

`get_block_content` / `update_block_logic` and `get_type_content` / `update_type_content` accept a `format` field. `format=source` is available for global data blocks, PLC data types, and SCL-language FB/FC/OB. Every other block language stays on `format=xml`.

`withDependencies` (`plc_read` only, default `false`) asks TIA Portal to include the object's dependency closure. The resulting document declares several objects and is **context only** — a write refuses any source declaring more than one object, and the read carries a warning saying so. Omit the field to get a document you can edit and submit back.

`get_block_content` and `get_type_content` reads also return a `contentHash` (`xml:sha256:<hex>` or `source:sha256:<hex>`) computed over the exact text served in `result`, tagged with the served format. It lets a later guarded write detect that the document changed since it was read. It is omitted for `withDependencies` reads, failed reads, and results truncated or omitted for size.

### Network operations

`network_read` and `network_write` both declare an MCP output schema and return one canonical JSON document identically as the `content` text block and as `structuredContent` — never a nested JSON string inside an outer envelope. Both use contract version `1.0`, root warnings arrays and explicit nulls; see [docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md](https://github.com/Czarnak/tia-portal-mcp/blob/main/docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md) for the exact envelopes.

- `network_read` - run up to 50 dedicated network reads: `read_hardware_config`, `search_equipment_catalog`, `list_network_objects`, and `inspect_network_object`. Reads run independently. Bound catalog searches with `query` and `maxResults`; `read_hardware_config` accepts an optional `deviceName` filter, an optional `plcName` for tag matching, and opt-in structured I/O extraction (`includeIoDetails` for addresses and channels, `includeTagMatches` for exact PLC tag matches per channel). Detailed I/O output can be large, so filter by `deviceName` or split into separate calls.
- `network_write` - preview or apply up to 50 dedicated network writes: `add_network_device` (flat `typeIdentifier`/`deviceName`, since it names something that does not exist yet), `configure_network_device` (nested `target: { deviceName, nodeId }` plus `changes: { ipAddress?, subnetMask?, pnDeviceNameAutoGeneration?, pnDeviceName?, subnet?: { subnetId }, ioSystem?: { subnetId, number } }` — a null `changes` member means leave that setting unchanged), `create_subnet` (`subnet: { name, networkType }`, plus PROFIBUS-only `highestAddress`/`transmissionSpeed`), `update_subnet` (`target: { kind: "subnet", subnetId }` plus `subnetChanges` with at least one member), and `delete_subnet` (`target: { kind: "subnet", subnetId }` — connected or not). Set `dryRun:true` to preview. `dryRun:false` or omitting `dryRun` executes, with no server Network elicitation in read-write/full. Legacy `confirm`, `safetyToken`, `acknowledge`, unknown roots and nonboolean `dryRun` are rejected before tool entry. Writes require a verified binding to the exact already-open project. Apply is sequential, stops on the first failure, marks later items skipped, and does not roll back completed writes: `network_write` attaches an explicit warning to the failed item that this operation and any earlier operation in the same call may already have changed TIA state, and that you should re-read with `network_read` before retrying rather than blindly re-running the batch.

`configure_network_device` selects one exact device/node and exact subnet/IO tuple, with no first-match fallback. Applied settings receive immediate and final effective-prefix checks; unreadable evidence cannot pass. Requested skips fail the item while retaining sparse typed results. Connected subnet deletion is informational; incomplete consequence inventory blocks every mode. Results use `phase:applied` even when execution or verification fails (`error:null`, MCP `isError:false`). Inspect current state before retrying; output omission may report delivery `success:false` while actual execution summaries remain successful. See the [Network contract](https://github.com/Czarnak/tia-portal-mcp/blob/main/docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md#network_write-envelope) for guards, sparse results, and whole-value omission recovery.

`read_hardware_config` additionally reports unreadable members in a payload-level `messages` array; device/module name and type-identifier fields omit values that could not be read instead of returning `0`/empty-string placeholders (a few secondary name fields still fall back to an empty string, with the failure noted in `messages`). Hardware configuration data is engineering evidence, not certification that a physical installation has been commissioned.

`read_hardware_config` supports opt-in structured I/O extraction. With `includeIoDetails: true`, each device item carries an `ioDetails` object with `addresses[]` (Openness `Address` evidence: `ioType`, byte-based `startAddress`, `length`, dynamic `context`, and ordinal `controllerNames`) and `channels[]` (`number`, `ioType`, `type`, bit-based `channelAddressBits`, `channelWidthBits`, and a formatted `logicalAddress` such as `%I4.0`/`%IW64` emitted only when the evidence is present and aligned). With `includeTagMatches: true` (which requires `includeIoDetails`), each channel carries `tagMatches[]` resolved deterministically against one selected PLC's tag tables — `plcName` selects the PLC by exact name, otherwise tag matching applies only when exactly one PLC exists. A tag matches a channel only when its normalized absolute I/O interval is identical to the channel's, and tags are never matched across controllers. A default read (no flags) returns no `ioDetails`; the current envelope and additive evidence follow the declared Network contract. See [docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md](https://github.com/Czarnak/tia-portal-mcp/blob/main/docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md) for the exact request/response shapes and unit semantics.

Large hardware reads can opt into cursor pagination with `pageSize` (`1..200`) or `cursor`. Pages count devices first and then subnets in one stable sequence while keeping the two public arrays separate; canonical size projection may return fewer complete entities than requested. Follow `pagination.nextCursor` until it is absent/null and keep the project, filters, and detail flags unchanged. Requests with neither field use the unpaged path under the current declared contract. Cursors are process-local and cannot survive a host restart. See the [Network operations reference](https://github.com/Czarnak/tia-portal-mcp/blob/main/docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md#hardware-configuration-pagination) for recovery and omission semantics.

### Project tools

- `bind_project` — default `bind` adopts an already-open project in any mode; omit the path to adopt the sole open project or list candidates. Use `forceRebind:true` to switch. Explicit `list_portals` discovers without adoption; five Project Server inventory actions inspect the persistent attached instance without changing project binding. Reattachment may show TIA's human-answerable Openness access dialog. See [Multiuser inventory and acceptance limits](https://github.com/Czarnak/tia-portal-mcp/blob/main/docs/SupportedOperations/MULTIUSER_OPERATIONS_SUMMARY.md).
- `get_project_status` — inspect active project metadata without opening, binding, or switching projects; a supplied path is an assertion.
- `browse_project_tree` — browse a canonical, paged v3 point-in-time project-tree snapshot with optional typed PLC block header author, version, family, and header-name metadata in block-node `details` (default-on string fields: `HeaderAuthor`, `HeaderVersion`, `HeaderFamily`, and `HeaderName`), `projectPath`, typed `startSelector`, `depth`, and `pageSize`; continue with the returned opaque `cursor`.
- `compile_check` — compile a PLC or selected block and return compiler messages; available in read-write and full modes.
- `open_project` / `create_project` / `save_project` / `save_project_as` / `archive_project` / `close_project` - guarded single-call lifecycle writes in read-write and full. Read-write asks once per actual call; full proceeds without server elicitation. Set `dryRun:true` to inspect effects and guards without mutation or elicitation. These tools advertise structured outputs and accept no public confirmation list or safety token; they remain single-tool only.

`get_project_status` and `compile_check` advertise structured output schemas with contract version `1.0`. Their text and `structuredContent` contain the same canonical document; read the typed payload at `result.value`. Compiler errors set `success:false` while retaining diagnostics, with MCP `isError:false`. Oversized values are omitted whole with retry guidance. See the [standalone response contract](https://github.com/Czarnak/tia-portal-mcp/blob/main/docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md#standalone-status-and-compilation-contract) for migration details.

Project-tree callers must use `v3.0.0` or newer: the v2 `startPath` input and bare nested-array response were removed rather than retained as aliases. See the [project operations reference](https://github.com/Czarnak/tia-portal-mcp/blob/main/docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md#browse_project_tree-v3) for the migration request, complete response envelope, selector reconstruction, continuation, limits, and recovery behavior.

## Write safety

Lifecycle tools validate input, prepare the exact binding, resolve targets, evaluate guards, mutate,
verify, and audit in one call. `dryRun:true` returns `phase:preview` without mutation or a confirmation
prompt and creates no token. In read-write every actual lifecycle call requires one form elicitation
with explicit acceptance and boolean `confirm:true`, including calls with only info guards or no
guards. Unsupported clients, decline, cancel, timeout, or transport failure deny with `access_denied`.
Full mode satisfies acknowledge guards by policy without server elicitation. Block guards stop the
call in every mode. Confirmation follows the access mode; the old startup switch was removed.
See the [lifecycle reference](https://github.com/Czarnak/tia-portal-mcp/blob/main/docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md#lifecycle-operations)
for guards, requests, responses, and recovery.

**Consent depends on the MCP client.** Elicitation proves that the client returned an accepted
answer, not that a person saw it. Project mutation and lifecycle tools retain conservative destructive hints; keep them
out of client auto-approve lists if you want permission prompts on every call. Generic
batch writes retain tokens as consistency checks, not user consent. Generic writes use
`preview_write_batch` then `apply_write_batch`. Network uses `dryRun:true` for preview and executes
by default; no Network token or server elicitation remains. Client permission prompts remain independent.

Those tokens are single-use, expire after ten minutes, and bind input, state, tool, and the verified
project identity/revision. Lifecycle uses pinned binding and transition checks without tokens;
open/create can start unbound, save-as binds the resulting copy, and close clears the binding.
Lifecycle audits every call, including dry runs and refusals, in `writes-yyyy-MM-dd.jsonl` under
`%LOCALAPPDATA%\TiaMcpServer\audit`; audit v2 records confirmation by `user`, `policy`, or `none`.
Network also records one audit v2 per entered call, including previews and denials; SDK argument rejection before entry has no write audit. Generic batches retain their audit files.

The worker never attaches to the first enumerated TIA Portal or selects the first open project. It
requires an exact path match or a genuinely sole candidate; multiple possible targets fail with
`target_ambiguous` before Attach or mutation. A configured `--project` path becomes write-ready only
after a matching worker identity is observed. Later identity drift returns `binding_conflict` and
invalidates the session until an explicit rebind.

There are no implicit opens: only `open_project` and `create_project` open a project. Use
`bind_project` for an already-open project. Worker ownership is lost across a detach; a reattached
project is treated as UI-owned. Read-only never opens, creates, saves or closes a project, while
explicit binding may switch the selected session. Project-tree cursors reject binding changes,
including a switch away and back to the same path.

`preview_write_batch` issues one token for the whole batch, bound to the exact ordered operation list and the combined current state. Reordering items, changing any item's input, retargeting the project path, or a change in project state all invalidate the token. `apply_write_batch` re-reads the combined current state once before consuming the token, then applies items sequentially and stops on the first failure.

Apply-time state validation, token consumption, mutation, post-verification, and audit capture run under one pinned project-binding lease. A concurrent rebind cannot redirect a token-validated operation, and two tokens previewed from the same state cannot both write: after the first mutation, the second apply fails with `state_changed`.

`network_write` plans and re-plans exact current targets under one pinned verified project binding, then verifies applied settings and the effective attempted prefix. It has no token or server elicitation; every entered call records one audit v2 document.

Lifecycle rejection has top-level `{category,message}` `error` and MCP `isError:true`. An attempted
write or verification failure instead has `success:false`, `error:null`, and `isError:false`, with
typed failure evidence in `result` or `verification`. A mutation may have succeeded despite failed
verification; inspect state before retrying. Legacy writes retain their categorized failure fields.
`save_project_as` still requires `rebind:true`, and warnings stay separate from success/failure.

Lifecycle's removed inputs and structured outputs are breaking changes staged for the redesign's
final major release. Network migration is implemented; generic-batch migration and final token retirement remain future phases. This
step does not publish a package or complete the whole redesign.

## Architecture

TIA Portal V21 ships its Openness API as .NET Framework 4.8 assemblies. Those assemblies use .NET Framework remoting APIs that cannot run correctly inside a .NET 10 process.

This project therefore uses two processes:

- `TiaMcpServer` - the .NET 10 MCP stdio server and .NET global tool host.
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
| Framework-dependent global tool: stable .NET 10 SDK 10.0.400 or newer | provides `dotnet tool install` and the supported .NET 10 runtime |
| .NET Framework 4.8 runtime | required by the Openness worker process |

The framework-dependent `tia-mcp` global tool requires a supported .NET 10 runtime. The separate
self-contained `win-x64` archive includes the host runtime. Neither installation method requires
users to install the `ModelContextProtocol` NuGet package.

Custom .NET integrations that compile directly against the C# MCP SDK must review the
ModelContextProtocol 2.2 migration notes and retest protocol negotiation, tool schemas, and
structured results. This custom-integration requirement is separate from the `browse_project_tree`
v3 migration described above.

Supported clients for `tia-mcp install`: Claude Code, Codex, OpenCode, MiMoCode. Servers register in
**read-only** mode by default (six tools); add `--access-mode read-write` for edits, compilation,
and lifecycle with one prompt per actual call (sixteen tools). Select `--access-mode full` for
lifecycle without server elicitation and PLC runtime control (sixteen tools).

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
- [JSON contract roadmap](https://github.com/Czarnak/tia-portal-mcp/blob/main/docs/roadmap/json-contract.md) — one structured output contract across the tools
- [Improvement log](https://github.com/Czarnak/tia-portal-mcp/blob/main/docs/IMPROVEMENT_LOG.md) — open follow-ups and completed engineering work

## Contributing

Contributions are welcome. See [CONTRIBUTING.md](https://github.com/Czarnak/tia-portal-mcp/blob/main/CONTRIBUTING.md) for the development workflow and how to set up your environment. For architecture and build reference, see [AGENTS.md](https://github.com/Czarnak/tia-portal-mcp/blob/main/AGENTS.md).

## Security

For how to report security vulnerabilities, see [SECURITY.md](https://github.com/Czarnak/tia-portal-mcp/blob/main/SECURITY.md).

## Check other tools

- [TIA Portal V21 Git Add-In](https://github.com/Czarnak/tia-git-addin)
- [Claude Code / Codex / Gemini plugin for TIA Portal development](https://github.com/Czarnak/totally-integrated-claude)
