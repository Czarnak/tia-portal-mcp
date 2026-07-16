# tia-portal-mcp

MCP server for Siemens SIMATIC TIA Portal V21. It lets MCP clients and AI agents inspect a running TIA Portal project through the Siemens Openness API.

The current implementation covers project discovery and lifecycle operations, PLC block export/import, tag table reads and guarded tag mutations, hardware/network discovery, cross-reference diagnostics, hardware catalog search, guarded network-device provisioning, and compile/check diagnostics.

## Tools

The server currently exposes 16 tools.

### Batch operations

- `execute_read_batch` - run up to 50 read operations in one call. Each item carries an `operationId`, an `operation` name (e.g. `get_block_content`, `list_tag_tables`), and that operation's parameters. Reads run independently, so a failing item does not stop the others.
- `preview_write_batch` / `apply_write_batch` - preview up to 50 write operations and receive one batch-level `safetyToken` bound to the exact ordered operation list and the combined current state, then apply them. Apply runs sequentially, stops on the first failure, and marks later items `skipped` (no transaction or rollback). Requires `confirm=true` and the `safetyToken`. Batches cover data writes (block, tag table, tag, user constant, network device); project-lifecycle operations stay single-tool only.

The batch tools are the only path for data operations. Each `operation` name (e.g. `get_block_content`, `list_tag_tables`, `create_tag`, `update_block_logic`, `add_network_device`) carries that operation's parameters as one item; a single operation is just a one-item batch.

Every operation result may carry a `warnings` array — non-fatal degradation notes captured from the TIA Openness worker (e.g. members skipped while reading a protected device). A populated `warnings` array means the payload may be partial. `read_hardware_config` additionally reports unreadable members in a payload-level `messages` array, and omits values it could not read instead of returning `0`/empty-string placeholders.

Available read operations (for `execute_read_batch`): `browse_project_tree`, `get_block_content`, `list_tag_tables`, `read_hardware_config`, `read_cross_references`, `search_equipment_catalog`, `compile_check`, `get_project_status`.

Available write operations (for `preview_write_batch` / `apply_write_batch`): `update_block_logic`, `create_block` / `delete_block`, `create_block_group` / `delete_block_group`, `create_tag_table` / `delete_tag_table`, `create_tag` / `update_tag` / `delete_tag`, `create_user_constant` / `update_user_constant` / `delete_user_constant`, `add_network_device`, `configure_network_device`, `start_plc` / `stop_plc`.

### Project tools

- `get_project_status` - read active project metadata.
- `preview_open_project` / `preview_create_project` / `preview_save_project` / `preview_save_project_as` / `preview_archive_project` / `preview_close_project` plus matching write tools - project lifecycle operations. These stay single-tool only (not batchable). Writes require `confirm=true` and `safetyToken`.

## Write safety

Every MCP write operation uses a preview-then-apply workflow. Call the matching `preview_*` tool first, review its summary, `currentStateHash`, `requestedInputHash`, and any diff, then pass the returned `safetyToken` to the write tool with `confirm=true`.

Safety tokens are single-use, expire 10 minutes after preview, and are bound to the exact tool name, normalized project path, target, requested input, and current project state. The server rejects missing, expired, reused, mismatched, or stale-state tokens. Successful write attempts append audit JSONL records under `%LOCALAPPDATA%\TiaMcpServer\audit`.

`preview_write_batch` issues one token for the whole batch, bound to the exact ordered operation list and the combined current state. Reordering items, changing any item's input, retargeting the project path, or a change in project state all invalidate the token. `apply_write_batch` re-reads the combined current state once before consuming the token, then applies items sequentially and stops on the first failure.

## Architecture

TIA Portal V21 ships its Openness API as .NET Framework 4.8 assemblies. Those assemblies use .NET Framework remoting APIs that cannot run correctly inside a .NET 8 process.

This project therefore uses two processes:

- `TiaMcpServer` - the .NET 8 MCP stdio server and .NET global tool host.
- `TiaMcpServer.OpennessWorker` - a .NET Framework 4.8 worker process that loads `Siemens.Engineering.*` and talks to TIA Portal.

The MCP host starts the worker on demand and exchanges newline-delimited JSON over stdin/stdout. Siemens DLLs are never copied into this repository or the NuGet package; the worker resolves them from the local TIA Portal V21 installation.

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

Once a server process is bound to a project path, later tool calls with a different `projectPath` are rejected. Call `open_project` with `forceRebind=true` to rebind the session, or start a new MCP session for a different customer project.

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
- Click `List Tools` and verify the 16 tools appear.
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

Project lifecycle writes remain single-tool and use their own preview-then-apply flow:

```json
{
  "projectPath": "C:\\Projects\\Sandbox\\Line.ap21",
  "confirm": true,
  "safetyToken": "<token from preview_open_project>"
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

Once a server process is bound to a project path, later tool calls with a different `projectPath` are rejected. Call `open_project` with `forceRebind=true` to rebind the session, or start a new MCP session for a different customer project.

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

## Check other tools

- [TIA Portal V21 Git Add-In](https://github.com/Czarnak/tia-git-addin)
- [Claude Code / Codex / Gemini plugin for TIA Portal development](https://github.com/Czarnak/totally-integrated-claude)
