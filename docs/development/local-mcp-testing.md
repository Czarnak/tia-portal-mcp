# Local MCP sandbox testing

The safest local test loop: run the server under the official MCP Inspector against a
disposable copy of a TIA project, without registering it with a daily-use AI client.

## Local MCP Sandbox Testing

For the safest local MCP test loop, use the official MCP Inspector against a disposable copy of a TIA project. The Inspector runs your server as a child stdio process and lets you list/call tools without adding the server to a daily-use AI client.

1. Start TIA Portal V21.
2. Open a test project, preferably a copied `.ap21` project, not a production project.
3. Build the repo:

    ```powershell
    dotnet restore TiaMcpServer.slnx
    dotnet build TiaMcpServer.slnx -m:1
    ```

4. Launch MCP Inspector against the built server:

```powershell
npx -y @modelcontextprotocol/inspector dotnet .\TiaMcpServer\bin\Debug\net10.0\TiaMcpServer.dll
```

To bind the inspector session to a specific project path instead of the currently open TIA project:

```powershell
npx -y @modelcontextprotocol/inspector dotnet .\TiaMcpServer\bin\Debug\net10.0\TiaMcpServer.dll --project C:\Projects\Sandbox\Line.ap21
```

In the Inspector UI:

- Open the Tools tab.
- Click `List Tools` and verify 17 tools in read-write/full or 7 in read-only. Full adds no tools; lifecycle works in both writable modes.
- Call `bind_project` to select the already-open fixture. With multiple projects use its advertised path; switching requires `forceRebind:true`. Reattachment may show TIA's Openness access dialog, which a human must answer. Ordinary reads never bind, switch or open.
- Start with the standalone `get_project_status` and `browse_project_tree` tools.
- In read-write or full mode, call standalone `compile_check` for PLC or block compilation.
- Then call `plc_read` with an `operations` array whose items use `list_tag_tables`, `get_block_content`, or `get_type_content`, and `read_cross_references` with a `target` copied from `browse_project_tree` output.
- Use `network_read` with `search_equipment_catalog` before hardware insertion so you can copy an exact `typeIdentifier`.
- Use a `get_block_content` read item on a block path returned by `browse_project_tree`.
- Use `get_project_status` before lifecycle changes.
- Use separately authorized disposable fixtures for writes. PLC writes use `plc_write` and Network writes use `network_write` with explicit `dryRun:true` for preview, then `dryRun:false` for execution; omitted dryRun also executes, with no server elicitation. Lifecycle uses a single guarded call; start with `dryRun:true` to inspect effects and guards.

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

Then use this read smoke-test for `plc_read` (independent items; a failing item does not stop the others):

```json
{
  "operations": [
    { "operationId": "tables", "operation": "list_tag_tables", "plcName": "PLC_1" },
    { "operationId": "main", "operation": "get_block_content", "blockPath": "PLC_1/Blocks/Main" }
  ]
}
```

Then a cross-reference read of one block (copy the `path` segments from `browse_project_tree`):

```json
{
  "target": { "path": [ { "nodeType": "...", "name": "PLC_1" } ] },
  "filter": "ObjectsWithReferences",
  "maxResults": 20
}
```

A value over 60,000 characters is omitted whole with narrowing guidance: narrow `list_tag_tables` with `plcName`, `folderPath` or `tableName`, and a cross-reference sweep with `filter`, `maxResults` or a narrower `target`. For the dedicated network surface, use `network_read`:

```json
{
  "operations": [
    { "operationId": "hardware", "operation": "read_hardware_config", "projectPath": "C:\\Projects\\Sandbox\\Line.ap21" },
    { "operationId": "catalog", "operation": "search_equipment_catalog", "query": "1516", "maxResults": 5 }
  ]
}
```

For Network writes, preview with explicit `dryRun:true`; omitted `dryRun` executes. All initial device/node/subnet/IO targets must resolve before mutation, so a `configure_network_device` cannot target a node created earlier in the *same* batch — add a device first, `network_read` to discover its exact `nodeId`, then configure it in a separate `network_write` call:

```json
{
  "dryRun": true,
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

For separately authorized creation, call `network_write` with explicit `dryRun:false` and the reviewed operations. Then call `network_read` (`read_hardware_config`) to discover the created device's exact `nodeId`:

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
  "dryRun": true,
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

For separately authorized execution, call `network_write` with explicit `dryRun:false`. Execution resolves current state anew; the preview has no token or reserved state:

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
  "dryRun": false
}
```

A `changes` member left out (for example, omitting `ioSystem`) means "leave that setting unchanged" — there is no flat legacy alias and no compatibility converter. The guarded response includes typed immediate/final applied-subset verification. Inspect current state with `network_read` after partial, failed, uncertain or omitted evidence; never automatically replay.

A tag write is a one-item `plc_write` call; preview it with `dryRun:true`, then execute with `dryRun:false`:

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
  ],
  "dryRun": true
}
```

Project lifecycle writes are single-call in read-write and full. Inspect `open_project` without mutation
or elicitation by setting `dryRun:true`:

```json
{
  "projectPath": "C:\\Projects\\Sandbox\\Line.ap21",
  "dryRun": true
}
```

An authorized actual open uses `dryRun:false` (or omits it). The server resolves fresh state;
the dry run creates no token and provides no continuing acknowledgement:

```json
{
  "projectPath": "C:\\Projects\\Sandbox\\Line.ap21"
}
```

Use archive mode values `None`, `DiscardRestorableData`, `Compressed`, or `DiscardRestorableDataAndCompressed`.

Read-write elicits once for every actual lifecycle call, including info-only calls and calls without
guards, requiring `accept` plus boolean `confirm:true`. Unsupported clients, decline/cancel,
timeout, or transport failure deny mutation. Full applies under policy without server elicitation.
Block guards stop mutation in every mode, and dry runs never prompt. Confirm that all six tools
expose `dryRun` and structured outputs with no agent confirmation array or public `confirm`
or token argument; no tool accepts token inputs. Audit v2 records `user`, `policy`, or `none`.

Read the lifecycle `result` and `verification` as typed outcomes. A mutation or verification
attempt that fails has `success:false`, `error:null`, and `isError:false`, with failure evidence
inside those outcomes. Inspect state and persisted artifacts after an uncertain outcome before
retrying. The client returning acceptance does not prove that a human saw the dialog.

For lifecycle acceptance, freeze the candidate after the serial offline build/test/coverage gates
and the installed-V21 reference build. Obtain fresh authorization for the exact disposable source
and destinations before running open/create/save/save-as/archive/close, all seven guards (including
target-exists for both create and save-as), accepted/declined elicitation, unsupported capability,
and no-mutation/no-prompt dry runs. Record worker runtime, client interaction, persisted artifacts,
and restoration separately. Any code/base change invalidates that frozen acceptance evidence.
Offline tests, a successful reference build, and a rendered prompt cannot establish the entire live
matrix. No automatic replay follows timeout, crash, disconnect, or possible mutation.

## PR4 installed-public-tool local-session acceptance

The [definition](../superpowers/acceptance/2026-10-07-multiuser-pr4-live-definition.md) and
[October 8 report](../superpowers/acceptance/reports/2026-10-08-multiuser-pr4-live-verification.md)
record the installed-public-MCP protocol and scoped maintainer acceptance. Freeze the exact
implementation commit/package, hash the actual installed host DLL and adjacent worker, and
record launch mode/startup assertions before serialized calls. Do not use a new PR4 harness
or direct-worker substitute; offline doubles and historical Inspector/Network runs cannot pass
PR4 live cases.

Use synthetic A (Exclusive), B (Multiuser), H (headless) and E (endpoint) roles in tracked reports;
keep their exact private mapping only in ignored evidence. Discover with
`bind_project({action:"list_portals"})`, adopt an exact observed AMC owner/PID, then read basic
status. For an authorized closed target in a retained empty Portal, preview
`open_project({projectPath:A_ALS,forceRebind:true,dryRun:true})`, inspect unchanged inventory,
then make one authorized actual call. Record exact requests/canonical text and structured
responses, owner/generation/PID and binding, independent before/after UI/source preservation,
human MCP/vendor prompt observations, exact audit record/document hashes and restoration.

The recorded online recovery used two UI Portals, not the whole sole-Portal matrix. Both offline
read-write actual opens needed Siemens dialog dismissal; retain noninteractive FAIL separately
from UI-assisted completion. Operator-owned endpoint stop/start and project-only closure require
their exact authorization. Never save/commit/generic-close a local session, auto-clean up, or
replay an uncertain opener. Full-offline/headless/race rows remain unexecuted; baseline read-only
and general lifecycle retesting were expressly excluded for this run.
