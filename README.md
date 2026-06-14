# tia-portal-mcp

MCP server for Siemens SIMATIC TIA Portal V21. It lets MCP clients and AI agents inspect a running TIA Portal project through the Siemens Openness API.

The current implementation covers project discovery and lifecycle operations, PLC block export/import, tag table reads and guarded tag mutations, hardware/network discovery, cross-reference diagnostics, hardware catalog search, guarded network-device provisioning, and compile/check diagnostics.

The server currently exposes 42 tools:

- `browse_project_tree` - recursively enumerates TIA devices, PLC software, Software Units, program blocks, PLC tags, and PLC data types, returning a JSON project tree with callable `Path` details.
- `get_block_content` - exports a PLC block to its SIMATIC SD document representation.
- `preview_update_block_logic` / `update_block_logic` - preview and then import SIMATIC SD document content to update or create a PLC block. Writes require `confirm=true` and `safetyToken`.
- `list_tag_tables` - retrieves PLC tag tables, tags, and user constants.
- `preview_create_tag_table` / `preview_delete_tag_table` / `create_tag_table` / `delete_tag_table` - preview and then create or delete PLC tag tables. Writes require `confirm=true` and `safetyToken`.
- `preview_create_tag` / `preview_update_tag` / `preview_delete_tag` / `create_tag` / `update_tag` / `delete_tag` - preview and then create, modify, or delete PLC tags. Writes require `confirm=true` and `safetyToken`.
- `preview_create_user_constant` / `preview_update_user_constant` / `preview_delete_user_constant` / `create_user_constant` / `update_user_constant` / `delete_user_constant` - preview and then create, modify, or delete user constants. Writes require `confirm=true` and `safetyToken`.
- `read_hardware_config` - exports device hardware, rack/module items, network interfaces, node addressing, subnets, and IO systems as JSON.
- `read_cross_references` - exports PLC cross-reference diagnostics, including source objects, referenced objects, usage locations, access types, and reference types.
- `search_equipment_catalog` - search the local TIA Portal hardware catalog, including installed GSD/HSP packages.
- `preview_add_network_device` / `add_network_device` - preview and then insert an exact catalog `typeIdentifier` into the project.
- `preview_configure_network_device` / `configure_network_device` - preview and then configure IP address, subnet mask, PROFINET device name, subnet, and IO-system settings when supported by Openness.
- `compile_check` - run compile/check operations and return diagnostics.
- `get_project_status` - read active project metadata.
- `preview_open_project` / `preview_create_project` / `preview_save_project` / `preview_save_project_as` / `preview_archive_project` / `preview_close_project` plus matching write tools - project lifecycle operations. Writes require `confirm=true` and `safetyToken`.

## Write safety

Every MCP write operation uses a preview-then-apply workflow. Call the matching `preview_*` tool first, review its summary, `currentStateHash`, `requestedInputHash`, and any diff, then pass the returned `safetyToken` to the write tool with `confirm=true`.

Safety tokens are short-lived, single-use, and bound to the exact tool name, normalized project path, target, requested input, and current project state. The server rejects missing, expired, reused, mismatched, or stale-state tokens. Successful write attempts append audit JSONL records under `%LOCALAPPDATA%\TiaMcpServer\audit`.

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

Once a server process is bound to a project path, later tool calls with a different `projectPath` are rejected. Start a new MCP session for a different customer project.

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
- Click `List Tools` and verify the 42 tools appear.
- Start with read-only tools: `browse_project_tree`, `list_tag_tables`, `read_hardware_config`, `read_cross_references`, and `compile_check`.
- Use `search_equipment_catalog` before hardware insertion so you can copy an exact `typeIdentifier`.
- Use `get_block_content` on a block path returned by `browse_project_tree`.
- Use `get_project_status` before lifecycle changes.
- Avoid write tools unless the project is disposable or backed up. Every write requires a matching `preview_*` call, then `confirm=true` and the returned `safetyToken`.

Recommended smoke-test inputs:

```json
{}
```

for `browse_project_tree`, `read_hardware_config`, and `compile_check`, or:

```json
{
  "filter": "UnusedObjects"
}
```

for `read_cross_references`. Large projects can return large JSON from cross-reference diagnostics; use `plcName` and `filter` to narrow the response:

```json
{
  "plcName": "PLC_1",
  "filter": "ObjectsWithReferences"
}
```

Use:

```json
{
  "projectPath": "C:\\Projects\\Sandbox\\Line.ap21"
}
```

when testing explicit project binding.

For catalog/device provisioning, start with a read-only catalog search:

```json
{
  "query": "1516"
}
```

Then preview the exact returned `typeIdentifier` with a disposable project:

```json
{
  "typeIdentifier": "OrderNumber:6ES7 510-1DJ01-0AB0/V2.0",
  "deviceName": "PLC_1",
  "deviceItemName": "PLC_1"
}
```

After reviewing the preview, apply with the returned token:

```json
{
  "typeIdentifier": "OrderNumber:6ES7 510-1DJ01-0AB0/V2.0",
  "deviceName": "PLC_1",
  "deviceItemName": "PLC_1",
  "confirm": true,
  "safetyToken": "<token from preview_add_network_device>"
}
```

Network configuration uses the same preview-then-apply flow:

```json
{
  "deviceName": "PLC_1",
  "ipAddress": "192.168.0.10",
  "subnetMask": "255.255.255.0",
  "pnDeviceName": "plc-1",
  "subnetName": "PN/IE_1",
  "confirm": true,
  "safetyToken": "<token from preview_configure_network_device>"
}
```

Tag writes are atomic and use the same preview-then-apply flow:

```json
{
  "plcName": "PLC_1",
  "tableName": "StandardTags",
  "name": "StartButton",
  "dataType": "Bool",
  "logicalAddress": "%I0.0",
  "confirm": true,
  "safetyToken": "<token from preview_create_tag>"
}
```

Project lifecycle writes also use the same preview-then-apply flow:

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

Once a server process is bound to a project path, later tool calls with a different `projectPath` are rejected. Start a new MCP session for a different customer project.

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
