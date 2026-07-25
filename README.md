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

The server currently exposes 10 tools.

### Batch operations

- `execute_read_batch` - run up to 50 read operations in one call. Each item carries an `operationId`, an `operation` name (e.g. `get_block_content`, `list_tag_tables`), and that operation's parameters. Reads run independently, so a failing item does not stop the others. Bound large reads with `depth`/`startPath` (`browse_project_tree`) and `maxResults` (`search_equipment_catalog` defaults to 50, and `read_cross_references`); oversized batch responses are truncated or omitted server-side with explicit markers.
- `preview_write_batch` / `apply_write_batch` - preview up to 50 write operations and receive one batch-level `safetyToken` bound to the exact ordered operation list and the combined current state, then apply them. Apply runs sequentially, stops on the first failure, and marks later items `skipped` (no transaction or rollback). Requires `confirm=true` and the `safetyToken`. Batches cover data writes (block, tag table, tag, user constant, network device); project-lifecycle operations stay single-tool only.

The batch tools are the only path for data operations. Each `operation` name (e.g. `get_block_content`, `list_tag_tables`, `create_tag`, `update_block_logic`, `add_network_device`) carries that operation's parameters as one item; a single operation is just a one-item batch.

Every operation result may carry a `warnings` array — non-fatal degradation notes captured from the TIA Openness worker (e.g. members skipped while reading a protected device). A populated `warnings` array means the payload may be partial. `read_hardware_config` additionally reports unreadable members in a payload-level `messages` array; device/module name and type-identifier fields omit values that could not be read instead of returning `0`/empty-string placeholders (a few secondary name fields still fall back to an empty string, with the failure noted in `messages`).

Available read operations (for `execute_read_batch`): `browse_project_tree`, `get_block_content`, `list_tag_tables`, `read_hardware_config`, `read_cross_references`, `search_equipment_catalog`, `compile_check`, `get_project_status`.

Available write operations (for `preview_write_batch` / `apply_write_batch`): `update_block_logic`, `create_block` / `delete_block`, `create_block_group` / `delete_block_group`, `create_tag_table` / `delete_tag_table`, `create_tag` / `update_tag` / `delete_tag`, `create_user_constant` / `update_user_constant` / `delete_user_constant`, `add_network_device`, `configure_network_device`, `start_plc` / `stop_plc`.

### Project tools

- `get_project_status` - read active project metadata.
- `open_project` / `create_project` / `save_project` / `save_project_as` / `archive_project` / `close_project` - project lifecycle writes. These stay single-tool only (not batchable) and are self-previewing: call the tool WITHOUT `safetyToken` to get a preview plus a single-use token, then call it again with `confirm=true` and the token to apply.

## Write safety

Every MCP write operation uses a preview-then-apply workflow. Batch data writes preview with `preview_write_batch` and apply with `apply_write_batch`. Project lifecycle writes are self-previewing: call the write tool WITHOUT `safetyToken` to get the preview (summary, `currentStateHash`, `requestedInputHash`, a fresh single-use `safetyToken`, and `instructions`), review it, then call the same tool again with the same arguments plus `confirm=true` and the `safetyToken`.

Safety tokens are single-use, expire 10 minutes after preview, and are bound to the exact tool name, normalized project path, target, requested input, and current project state. The server rejects missing, expired, reused, mismatched, or stale-state tokens. Successful write attempts append audit JSONL records under `%LOCALAPPDATA%\TiaMcpServer\audit`.

`preview_write_batch` issues one token for the whole batch, bound to the exact ordered operation list and the combined current state. Reordering items, changing any item's input, retargeting the project path, or a change in project state all invalidate the token. `apply_write_batch` re-reads the combined current state once before consuming the token, then applies items sequentially and stops on the first failure.

Every failed write reports a categorized `failureCategory` field alongside its human-readable `error` message, so a caller can branch on the exact failure without parsing text: `validation_error`, `binding_conflict`, `state_changed`, `worker_operation_failed`, `worker_timeout`, `worker_crashed`, or `postcondition_failed`. `save_project_as` requires `rebind:true`; calling it with `rebind:false` is rejected up front with `validation_error` before any preview, safety-token issuance, Siemens `SaveAs` call, or audit write, so it has no side effects. Warnings are always reported in a separate `warnings` array from the primary success/failure outcome — a populated `warnings` array never turns a failure into a success, and a categorized failure is never masked by an accompanying warning.

## Architecture

TIA Portal V21 ships its Openness API as .NET Framework 4.8 assemblies. Those assemblies use .NET Framework remoting APIs that cannot run correctly inside a .NET 8 process.

This project therefore uses two processes:

- `TiaMcpServer` - the .NET 8 MCP stdio server and .NET global tool host.
- `TiaMcpServer.OpennessWorker` - a .NET Framework 4.8 worker process that loads `Siemens.Engineering.*` and talks to TIA Portal.

The MCP host keeps one persistent .NET Framework 4.8 worker process attached to TIA Portal and exchanges newline-delimited JSON over stdin/stdout. Requests are serialized, and the worker restarts automatically after a crash or timeout. Siemens DLLs are never copied into this repository or the NuGet package; the worker resolves them from the local TIA Portal V21 installation.

## Requirements

- Windows
- Siemens TIA Portal V21 installed
- TIA Portal Openness installed and enabled
- Current Windows user is a member of the `Siemens TIA Openness` user group
- .NET SDK 8.0 or newer for `dotnet tool install`
- .NET Framework 4.8 Runtime for the Openness worker

Source builds additionally need:

- .NET SDK 8.0.4xx or newer 8.0 feature band. The repo includes `global.json` to prefer .NET SDK 8 for builds.
- .NET Framework 4.8 Developer Pack or targeting pack

By default, source builds expect Openness DLLs here:

```text
C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48
```

Local developer builds prefer real TIA Portal V21 assemblies from `TiaPortalV21Dir`. You can override that path with the `TiaPortalV21Dir` MSBuild property or environment variable. It must point to the folder containing `Siemens.Engineering.Base.dll` and `Siemens.Engineering.Step7.dll`.

The repo also contains compile-time reference stubs in `ref/` so CI can build and package the MCP server without installing TIA Portal. Those stubs are fallback-only when a local TIA install is not found. To force stub references for CI/package builds:

```powershell
dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
```

To force local TIA references:

```powershell
dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=false /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"
```

During build, the worker prints the selected reference directory:

```text
TIA Openness compile references: C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48 (UseTiaPortalReferenceStubs=false)
```

## Install

```powershell
dotnet tool install -g TiaMcpServer
```

Run the installed server:

```powershell
tia-mcp
```

To bind an MCP server process to a specific project, pass `--project` or set `TIA_MCP_PROJECT_PATH`:

```powershell
tia-mcp --project C:\Projects\Line.ap21
$env:TIA_MCP_PROJECT_PATH = 'C:\Projects\Line.ap21'
tia-mcp
```

After the first successful call, the session binds to the worker-reported active project. A later
project-scoped call that names a different `projectPath` is rejected; call `open_project` with `forceRebind=true` to
rebind the session, or start a new MCP session for a different customer project. Project-scoped read
operations also refuse to switch projects: `TIA Portal currently has project 'A' open, but this
request targets 'B'. Read operations never switch projects. Omit projectPath to use the open project,
or call open_project to switch.` `get_project_status(projectPath)` is read-only and non-binding: it never opens or switches projects, even
when `projectPath` names a project that is not the one currently open. It is the human-approved Round 5
deferral from the read-side switching policy because it shares a lifecycle RPC with guarded write-state probes; do not use it to switch
projects. Use `open_project` for deliberate session switching.

### Version flag

Run `tia-mcp --version` (or `tia-mcp -v`) to print the host version and exit without starting the MCP server.

### Doctor command

Run `tia-mcp doctor` to validate the runtime environment before using the MCP server. It checks the operating system, .NET runtimes, TIA Portal installation, Openness assemblies, user group membership, worker executable, host/worker version compatibility, running TIA Portal processes, and project binding.

```powershell
tia-mcp doctor
tia-mcp doctor --json
tia-mcp doctor --verbose
tia-mcp doctor --project C:\Projects\Line.ap21
```

Options:

- `--json` - emit a single JSON document to stdout.
- `--verbose` - include diagnostic evidence for each check.
- `--project` - informational project binding (does not start the MCP host).

Exit codes: `0` (no blocking failures), `1` (one or more checks failed), `2` (invalid arguments).

The package includes the `openness-worker` folder and required non-Siemens dependencies. It intentionally excludes `Siemens.Engineering*.dll`; those are loaded from the local TIA Portal installation at runtime.

## Build From Source

```powershell
dotnet restore TiaMcpServer.sln
dotnet build TiaMcpServer.sln -m:1
```

The `-m:1` option serializes solution builds. The MCP host project also builds and copies the net48 Openness worker, so serialized builds avoid duplicate parallel worker builds during local development.

The source build creates the .NET 8 host and copies the .NET Framework worker into:

```text
TiaMcpServer\bin\Debug\net8.0\openness-worker
```

### Coverage

CI collects coverage, then enforces an 80% scoped line-coverage threshold locally (before the Codecov upload, which stays reporting-only). Run the same scoped collection and threshold check locally:

```powershell
$results = Join-Path 'TestResults' ('local-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --collect:"XPlat Code Coverage" --settings TiaMcpServer.Tests/coverage.runsettings --results-directory $results
$report = Get-ChildItem -LiteralPath $results -Recurse -Filter coverage.cobertura.xml | Select-Object -First 1
./scripts/verify-coverage-threshold.ps1 -CoveragePath $report.FullName -MinimumLineRate 0.80
```

`coverage.runsettings` scopes the Cobertura report to `TiaMcpServer` and `TiaMcpServer.Contracts`; test assemblies, `TiaMcpServer.FakeWorker`, and `TiaMcpServer.OpennessWorker` are excluded. `verify-coverage-threshold.ps1` exits non-zero below the threshold.

## Run Locally

Start TIA Portal V21 first and open a project, then run:

```powershell
dotnet run --project TiaMcpServer
```

The server uses MCP over stdio, so it is normally launched by an MCP client rather than used interactively in a terminal.

You can test the Openness worker directly:

```powershell
'{ "method": "browse_project_tree", "projectPath": null }' | .\TiaMcpServer.OpennessWorker\bin\Debug\net48\TiaMcpServer.OpennessWorker.exe
'{ "method": "read_hardware_config", "projectPath": null }' | .\TiaMcpServer.OpennessWorker\bin\Debug\net48\TiaMcpServer.OpennessWorker.exe
'{ "method": "read_cross_references", "projectPath": null, "crossReferenceFilter": "ObjectsWithReferences" }' | .\TiaMcpServer.OpennessWorker\bin\Debug\net48\TiaMcpServer.OpennessWorker.exe
'{ "method": "search_equipment_catalog", "query": "1516", "projectPath": null }' | .\TiaMcpServer.OpennessWorker\bin\Debug\net48\TiaMcpServer.OpennessWorker.exe
```

Expected successful response shape:

```json
{"success":true,"payload":"[...]"}
```

Expected error response shape:

```json
{"success":false,"error":"No running TIA Portal V21 instance found. Please start TIA Portal before using the MCP server."}
```

## Local MCP Sandbox Testing

For the safest local MCP test loop, use the official MCP Inspector against a disposable copy of a TIA project. The Inspector runs your server as a child stdio process and lets you list/call tools without adding the server to a daily-use AI client.

1. Start TIA Portal V21.
2. Open a test project, preferably a copied `.ap21` project, not a production project.
3. Build the repo:

    ```powershell
    dotnet restore TiaMcpServer.sln
    dotnet build TiaMcpServer.sln -m:1
    ```

4. Launch MCP Inspector against the built server:

```powershell
npx -y @modelcontextprotocol/inspector dotnet .\TiaMcpServer\bin\Debug\net8.0\TiaMcpServer.dll
```

To bind the inspector session to a specific project path instead of the currently open TIA project:

```powershell
npx -y @modelcontextprotocol/inspector dotnet .\TiaMcpServer\bin\Debug\net8.0\TiaMcpServer.dll --project C:\Projects\Sandbox\Line.ap21
```

In the Inspector UI:

- Open the Tools tab.
- Click `List Tools` and verify the 10 tools appear.
- Start with reads: call `execute_read_batch` with an `operations` array (each item has an `operationId`, an `operation` name such as `browse_project_tree` / `list_tag_tables` / `read_hardware_config` / `read_cross_references` / `compile_check`, and that operation's parameters).
- Use a `search_equipment_catalog` read item before hardware insertion so you can copy an exact `typeIdentifier`.
- Use a `get_block_content` read item on a block path returned by `browse_project_tree`.
- Use `get_project_status` before lifecycle changes.
- Avoid writes unless the project is disposable or backed up. Writes go through `preview_write_batch`, then `apply_write_batch` with `confirm=true` and the returned batch `safetyToken`.

Recommended read smoke-test for `execute_read_batch` (independent items; a failing item does not stop the others):

```json
{
  "operations": [
    { "operationId": "tree", "operation": "browse_project_tree" },
    { "operationId": "hw", "operation": "read_hardware_config" },
    { "operationId": "compile", "operation": "compile_check" },
    { "operationId": "xref", "operation": "read_cross_references", "filter": "ObjectsWithReferences", "plcName": "PLC_1" },
    { "operationId": "catalog", "operation": "search_equipment_catalog", "query": "1516" }
  ]
}
```

Large projects can return large JSON from cross-reference diagnostics; narrow each read item with `plcName` and `filter`. To test explicit project binding, set `projectPath` on each operation item (all write items in a batch must target the same project path):

```json
{
  "operations": [
    { "operationId": "tree", "operation": "browse_project_tree", "projectPath": "C:\\Projects\\Sandbox\\Line.ap21" }
  ]
}
```

For writes, preview first with `preview_write_batch` to obtain one batch-level `safetyToken`, for example a device add plus its network configuration in order:

```json
{
  "operations": [
    {
      "operationId": "add",
      "operation": "add_network_device",
      "typeIdentifier": "OrderNumber:6ES7 510-1DJ01-0AB0/V2.0",
      "deviceName": "PLC_1",
      "deviceItemName": "PLC_1"
    },
    {
      "operationId": "configure",
      "operation": "configure_network_device",
      "deviceName": "PLC_1",
      "ipAddress": "192.168.0.10",
      "subnetMask": "255.255.255.0",
      "pnDeviceName": "plc-1",
      "subnetName": "PN/IE_1"
    }
  ]
}
```

Then apply with `apply_write_batch`, passing the same `operations` array unchanged plus the returned token (apply runs sequentially and stops on the first failure):

```json
{
  "operations": [
    {
      "operationId": "add",
      "operation": "add_network_device",
      "typeIdentifier": "OrderNumber:6ES7 510-1DJ01-0AB0/V2.0",
      "deviceName": "PLC_1",
      "deviceItemName": "PLC_1"
    },
    {
      "operationId": "configure",
      "operation": "configure_network_device",
      "deviceName": "PLC_1",
      "ipAddress": "192.168.0.10",
      "subnetMask": "255.255.255.0",
      "pnDeviceName": "plc-1",
      "subnetName": "PN/IE_1"
    }
  ],
  "confirm": true,
  "safetyToken": "<token from preview_write_batch>"
}
```

A tag write is the same flow with a one-item batch, e.g. `preview_write_batch` then `apply_write_batch` over:

```json
{
  "operations": [
    {
      "operationId": "tag",
      "operation": "create_tag",
      "plcName": "PLC_1",
      "tableName": "StandardTags",
      "name": "StartButton",
      "dataType": "Bool",
      "logicalAddress": "%I0.0"
    }
  ]
}
```

Project lifecycle writes remain single-tool and self-previewing. First call `open_project` with only the project path to receive the preview and token:

```json
{
  "projectPath": "C:\\Projects\\Sandbox\\Line.ap21"
}
```

Then call `open_project` again with the same arguments plus `confirm=true` and the returned token:

```json
{
  "projectPath": "C:\\Projects\\Sandbox\\Line.ap21",
  "confirm": true,
  "safetyToken": "<token from the preview call>"
}
```

Use archive mode values `None`, `DiscardRestorableData`, `Compressed`, or `DiscardRestorableDataAndCompressed`.

## Local Package Build

The package is already published on NuGet. Use this section only when testing package changes locally before publishing a new version.

Create a local tool package:

```powershell
dotnet pack TiaMcpServer\TiaMcpServer.csproj -c Release
```

Install from the generated package source:

```powershell
dotnet tool install -g TiaMcpServer --add-source .\TiaMcpServer\bin\Release
```

Run the installed server:

```powershell
tia-mcp
```

To bind an MCP server process to a specific project, pass `--project` or set `TIA_MCP_PROJECT_PATH`:

```powershell
tia-mcp --project C:\Projects\Line.ap21
$env:TIA_MCP_PROJECT_PATH = 'C:\Projects\Line.ap21'
tia-mcp
```

After the first successful call, the session binds to the worker-reported active project. A later
project-scoped call that names a different `projectPath` is rejected; call `open_project` with `forceRebind=true` to
rebind the session, or start a new MCP session for a different customer project. Project-scoped read
operations also refuse to switch projects: `TIA Portal currently has project 'A' open, but this
request targets 'B'. Read operations never switch projects. Omit projectPath to use the open project,
or call open_project to switch.` `get_project_status(projectPath)` is read-only and non-binding: it never opens or switches projects, even
when `projectPath` names a project that is not the one currently open. It is the human-approved Round 5
deferral from the read-side switching policy because it shares a lifecycle RPC with guarded write-state probes; do not use it to switch
projects. Use `open_project` for deliberate session switching.

## Block Paths

Prefer block paths returned in `browse_project_tree` node `Details.Path` values. Supported block path forms are:

```text
BlockName
PLC_1/BlockName
PLC_1/Blocks/Folder/SubFolder/BlockName
PLC_1/Units/UnitName/Blocks/Folder/SubFolder/BlockName
```

Legacy `BlockName` and `PLC_1/BlockName` paths are accepted only when the block name is unambiguous. If more than one block has the same name, use the deterministic `Path` returned by `browse_project_tree`.

## MCP Client Configuration

Configure your MCP client to launch the tool command:

```json
{
  "mcpServers": {
    "tia-portal": {
      "command": "tia-mcp"
    }
  }
}
```

For local development without installing the tool, point the client at `dotnet`:

```json
{
  "mcpServers": {
    "tia-portal-dev": {
      "command": "dotnet",
      "args": ["run", "--project", "{REPO PATH}\\TiaMcpServer"]
    }
  }
}
```

With an explicit project binding:

```json
{
  "mcpServers": {
    "tia-portal-dev": {
      "command": "dotnet",
      "args": ["run", "--project", "{REPO PATH}\\TiaMcpServer", "--", "--project", "C:\\Projects\\Sandbox\\Line.ap21"]
    }
  }
}
```

## Troubleshooting

- `System.Runtime.Remoting.RemotingException` / `TypeLoadException` in .NET 8: Siemens Openness must run in the net48 worker. Rebuild the solution and make sure the host output contains `openness-worker\TiaMcpServer.OpennessWorker.exe`.
- Openness DLL not found: verify TIA Portal V21 is installed and set `TiaPortalV21Dir` to the `PublicAPI\V21\net48` folder if your install path is non-standard.
- Build uses `ref/` on a developer machine: verify `TiaPortalV21Dir` points to the local V21 `PublicAPI\V21\net48` folder, or force local references with `/p:UseTiaPortalReferenceStubs=false`.
- No running TIA Portal instance: start TIA Portal V21 before calling tools that attach to the current project.
- Access denied or attach failure: confirm the Windows user belongs to the `Siemens TIA Openness` user group, then sign out and back in.
- `dotnet` selects the wrong SDK: install .NET SDK 8.0.4xx or update `global.json` to a locally installed .NET 8 SDK feature band.

## Verified TIA Portal V21 behavior

The Phase 5 acceptance record documents the verified recovery guidance for these previously
problematic paths. Multi-document `update_block_logic` round trips are verified: submit the
exported SIMATIC ML document bundle through a guarded batch, expect one import followed by compile
and re-export verification, and treat a structural/unsafe-document rejection as a no-change result.
An edited bundle is likewise compiled and re-exported. Do not automatically retry a write with an
uncertain worker outcome; inspect the current block instead.

SCL `create_block` calls are verified: the generated SCL source contains a non-empty compile unit,
the requested block resolves at its requested path, and `compile_check` confirms it compiles. The
same guarded preview/token/apply flow applies to SCL and GlobalDB block creation.

`save_project_as` with `rebind: false` is resolved: it is rejected up front with a
`validation_error` response, before any preview, safety-token issuance, Siemens `SaveAs` call, or
audit write, so it has no side effects. `rebind: true` is required; see
[Write safety](#write-safety). A supported SaveAs rebinds both host and worker to the
worker-reported copied project path; verify it with a subsequent status or read call.

## Contributing

Contributions are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for the development workflow and how to set up your environment. For architecture and build reference, see [AGENTS.md](AGENTS.md).

## Security

For how to report security vulnerabilities, see [SECURITY.md](SECURITY.md).

## Check other tools

- [TIA Portal V21 Git Add-In](https://github.com/Czarnak/tia-git-addin)
- [Claude Code / Codex / Gemini plugin for TIA Portal development](https://github.com/Czarnak/totally-integrated-claude)
