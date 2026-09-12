# Local MCP sandbox testing

The safest local test loop: run the server under the official MCP Inspector against a
disposable copy of a TIA project, without registering it with a daily-use AI client.

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
- Click `List Tools` and verify the 14 tools appear in read-write mode (or the four observation tools in read-only mode).
- Start with the standalone `get_project_status` and `browse_project_tree` tools.
- In read-write mode, call standalone `compile_check` for PLC or block compilation.
- Then call `execute_read_batch` with an `operations` array whose items use retained operations such as `list_tag_tables`, `read_cross_references`, or `get_block_content`.
- Use `network_read` with `search_equipment_catalog` before hardware insertion so you can copy an exact `typeIdentifier`.
- Use a `get_block_content` read item on a block path returned by `browse_project_tree`.
- Use `get_project_status` before lifecycle changes.
- Avoid writes unless the project is disposable or backed up. Generic writes go through `preview_write_batch`, then `apply_write_batch`; network writes use self-previewing `network_write` with `confirm:false`, then the unchanged list, `confirm:true`, and the returned token.

For a bounded project-tree read, migrate the v2 request:

```json
{ "projectPath": null, "depth": 2, "startPath": "PLC_1" }
```

to the v3 request:

```json
{
  "projectPath": null,
  "depth": 2,
  "startSelector": [
    { "nodeType": "Device", "name": "PLC_1" }
  ]
}
```

The v2 `startPath` and project-tree `deviceName` inputs are removed in `v3.0.0`; the old bare nested array is not retained. A successful v3 call returns a `contractVersion: "3.0"` envelope whose flat nodes are parent-first. When `pagination.nextCursor` is non-null, continue with only:

```json
{ "cursor": "<pagination.nextCursor>" }
```

To target a returned descendant, index the complete snapshot by `nodeId`, follow its `parentNodeId` chain to null, reverse the chain, and project each returned node to its exact `{ nodeType, name }`. A snapshot selected below `Device` does not repeat the missing ancestors: prepend `result.query.startSelector` excluding its final segment, then append the reconstructed returned chain and submit the combined array as `startSelector`. Use an empty prefix when the original `startSelector` was omitted. See the [project operations reference](../SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md#browse_project_tree-v3) for the full envelope, failure categories, and cache/limit semantics.

In read-write mode, call standalone `compile_check` with inputs such as:

```json
{ "projectPath": null, "plcName": "PLC_1", "blockPath": "PLC_1/Blocks/Main" }
```

Then use this read smoke-test for `execute_read_batch` (independent items; a failing item does not stop the others):

```json
{
  "operations": [
    { "operationId": "xref", "operation": "read_cross_references", "filter": "ObjectsWithReferences", "plcName": "PLC_1" },
    { "operationId": "tables", "operation": "list_tag_tables", "plcName": "PLC_1" }
  ]
}
```

Large projects can return large JSON from cross-reference diagnostics; narrow each read item with `plcName` and `filter`. For the dedicated network surface, use `network_read`:

```json
{
  "operations": [
    { "operationId": "hardware", "operation": "read_hardware_config", "projectPath": "C:\\Projects\\Sandbox\\Line.ap21" },
    { "operationId": "catalog", "operation": "search_equipment_catalog", "query": "1516", "maxResults": 5 }
  ]
}
```

For network writes, preview first with self-previewing `network_write`. Do not provide a token during preview; use `confirm:false`. Target resolution (device, node, subnet, IO system) is always resolved against a single hardware snapshot taken before any operation in the batch runs, so a `configure_network_device` cannot target a node created earlier in the *same* batch — add a device first, `network_read` to discover its exact `nodeId`, then configure it in a separate `network_write` call:

```json
{
  "confirm": false,
  "operations": [
    {
      "operationId": "add",
      "operation": "add_network_device",
      "typeIdentifier": "OrderNumber:6ES7 510-1DJ01-0AB0/V2.0",
      "deviceName": "PLC_1",
      "deviceItemName": "PLC_1"
    }
  ]
}
```

Call `network_write` again with the same `operations` array unchanged, `confirm:true`, and the returned token to create the device, then call `network_read` (`read_hardware_config`) to read back its exact `nodeId`:

```json
{
  "operations": [
    { "operationId": "hardware", "operation": "read_hardware_config", "projectPath": "C:\\Projects\\Sandbox\\Line.ap21" }
  ]
}
```

With that `nodeId` in hand, preview a `configure_network_device` write against the exact `target`, then apply it the same way:

```json
{
  "confirm": false,
  "operations": [
    {
      "operationId": "configure",
      "operation": "configure_network_device",
      "projectPath": "C:\\Projects\\Sandbox\\Line.ap21",
      "target": { "deviceName": "PLC_1", "nodeId": "<nodeId from read_hardware_config>" },
      "changes": {
        "ipAddress": "192.168.0.10",
        "subnetMask": "255.255.255.0",
        "pnDeviceName": "plc-1",
        "subnet": { "subnetId": "<subnetId from read_hardware_config>" }
      }
    }
  ]
}
```

Then call `network_write` again with the same `operations` array unchanged, `confirm:true`, and the returned token to apply it:

```json
{
  "operations": [
    {
      "operationId": "configure",
      "operation": "configure_network_device",
      "projectPath": "C:\\Projects\\Sandbox\\Line.ap21",
      "target": { "deviceName": "PLC_1", "nodeId": "<nodeId from read_hardware_config>" },
      "changes": {
        "ipAddress": "192.168.0.10",
        "subnetMask": "255.255.255.0",
        "pnDeviceName": "plc-1",
        "subnet": { "subnetId": "<subnetId from read_hardware_config>" }
      }
    }
  ],
  "confirm": true,
  "safetyToken": "<token from network_write preview>"
}
```

A `changes` member left out (for example, omitting `ioSystem`) means "leave that setting unchanged" — there is no flat legacy alias and no compatibility converter. Always follow the apply with another `network_read` (`read_hardware_config`) to confirm the outcome; the write response does not echo back a re-read of the written value.

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

## Project-tree v3 read-only live acceptance

The maintained [project-tree v3 harness](../../scripts/live-test-project-tree-v3.ps1) is a separately authorized live check, not part of the offline test suite. It never opens, switches, saves, compiles, confirms, or mutates a project. Before running it, build the Release server and manually open an approved read-only fixture in TIA Portal V21. Then provide that exact path explicitly or through `TIA_MCP_LIVE_PROJECT_PATH`:

```powershell
pwsh -NoProfile -File .\scripts\live-test-project-tree-v3.ps1 -ProjectPath C:\Projects\Sandbox\Line.ap21
```

The harness starts the Release executable with `--read-only --project <exact path>`, verifies the four-tool observation surface, and writes JSON/local protocol evidence under `artifacts/issue-32-live/`. It times three cursor-free calls for each full-project, depth-limited, and selected-device mode; walks the final snapshot for every mode; verifies canonical text/structured equality, sequence and parent ordering, limits, selector reconstruction, and categorized missing/ambiguous/invalid selector behavior; and always stops the host in `finally`.

An ambiguous-selector pass requires a naturally ambiguous direct-child `(nodeType, name)` pair in the observed project. If the fixture has none, the harness fails that check explicitly. Do not alter a project to manufacture the case; obtain authorization for a different read-only fixture. A parser pass and the static `ProjectTreeLiveHarnessContractTests` prove only the harness source boundary, not live TIA behavior. Create a live acceptance report only after an authorized run has produced and reviewed the evidence.
