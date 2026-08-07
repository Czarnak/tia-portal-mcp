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

For a bounded tree read, call standalone `browse_project_tree` with inputs such as:

```json
{ "projectPath": null, "depth": 2, "startPath": "PLC_1" }
```

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
