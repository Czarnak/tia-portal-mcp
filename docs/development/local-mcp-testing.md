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
- Click `List Tools` and verify 15 tools in read-write/full or 5 in read-only. Full is required for OnlineControl (PLC run/stop); lifecycle works in both writable modes.
- Call `bind_project` to select the already-open fixture. With multiple projects use its advertised path; switching requires `forceRebind:true`. Reattachment may show TIA's Openness access dialog, which a human must answer. Ordinary reads never bind, switch or open.
- Start with the standalone `get_project_status` and `browse_project_tree` tools.
- In read-write or full mode, call standalone `compile_check` for PLC or block compilation.
- Then call `execute_read_batch` with an `operations` array whose items use retained operations such as `list_tag_tables`, `read_cross_references`, or `get_block_content`.
- Use `network_read` with `search_equipment_catalog` before hardware insertion so you can copy an exact `typeIdentifier`.
- Use a `get_block_content` read item on a block path returned by `browse_project_tree`.
- Use `get_project_status` before lifecycle changes.
- Use separately authorized disposable fixtures for writes. Generic writes go through `preview_write_batch`, then `apply_write_batch`; Network writes use `network_write` with explicit `dryRun:true` for preview, then `dryRun:false` for execution; omitted dryRun also executes, with no server elicitation. Lifecycle uses a single guarded call; start with `dryRun:true` to inspect effects and guards.

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

A tag write retains the generic token flow with a one-item batch, e.g. `preview_write_batch` then `apply_write_batch` over:

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
expose `dryRun` and structured outputs with no agent confirmation array or public `confirm`/
`safetyToken`; token inputs remain only on generic batches. Audit v2 records `user`, `policy`, or `none`.

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

## Project-tree v3 read-only live acceptance

The maintained [project-tree v3 harness](../../scripts/live-test-project-tree-v3.ps1) is a separately authorized live check, not part of the offline test suite. It never opens, switches, saves, compiles, confirms, or mutates a project. Before running it, build the Release server and manually open an approved read-only fixture in TIA Portal V21. Then provide that exact path explicitly or through `TIA_MCP_LIVE_PROJECT_PATH`:

```powershell
pwsh -NoProfile -File .\scripts\live-test-project-tree-v3.ps1 -ProjectPath C:\Projects\Sandbox\Line.ap21
```

The harness starts the Release executable with `--read-only --project <exact path>` and writes JSON/local protocol evidence under `artifacts/issue-32-live/`. Its historical four-tool discovery assertion predates the five-tool surface including `bind_project`; update and review that harness assertion before a future authorized run. It times three cursor-free calls for each full-project, depth-limited, and selected-device mode; walks the final snapshot for every mode; verifies canonical text/structured equality, sequence and parent ordering, limits, selector reconstruction, and categorized missing/ambiguous/invalid selector behavior; and always stops the host in `finally`.

An ambiguous-selector pass requires a naturally ambiguous direct-child `(nodeType, name)` pair in the observed project. If the fixture has none, the harness fails that check explicitly. Do not alter a project to manufacture the case; obtain authorization for a different read-only fixture. A parser pass and the static `ProjectTreeLiveHarnessContractTests` prove only the harness source boundary, not live TIA behavior. Create a live acceptance report only after an authorized run has produced and reviewed the evidence.


## Frozen guarded Network acceptance harnesses

The configuration script is prepared for future separately authorized live acceptance. No Inventory, Preview,
Apply or Restore invocation is part of offline qualification. Static tests parse source/AST and run
isolated synthetic helpers only; they do not start the MCP host, TIA or a worker. Historical Phase4
live evidence does not qualify the guarded candidate.

The [configuration/ordered-outcome harness](../../scripts/live-test-network-guarded-write.ps1) uses the
public MCP framing helpers: initialize/list/call, status of the exact already-open disposable
`ProjectPath`, `bind_project`, fresh hardware/identity reads, explicit `dryRun:true`, then an actual
`dryRun:false` only within authorized Apply. It supports `-AccessMode read-write|full`, defaults
`-Mode Inventory`, freezes `ExpectedCommit`, `ExpectedTree`, `ExpectedHarnessSha256`, `ExpectedSharedHelperSha256`, and rejects
unexpected server elicitation. Client gates do not represent server elicitation or continuing consent.
No save, close, compile, download, PLC control, automatic retry, or batch rollback.

The entrypoint uses [the Network MCP helper](../../scripts/network-live-mcp-helpers.ps1) for process/framing, canonical status/binding, pagination and node discovery. The required `ExpectedSharedHelperSha256` must match that file before it is imported; missing or changed helpers abort before host startup. Frozen commit/tree cleanliness covers the entrypoint and the shared helper, and artifacts include `testedSharedHelperSha256`. Authorization and scenario checks remain in the entrypoint.

The configuration script accepts a reviewed `-FixturePath` JSON file and configuration operations
only. Each operation has the exact `projectPath`, `operationId`, `target.deviceName` and `target.nodeId`.
Before/after expectations must contain each configured target. Include the exact multi-homed device
name in `MultiHomedDevices`, and include its untouched sibling node in all before/after node arrays
to test preservation. All node expectations require `deviceName`, `nodeId`, `subnetId`,
`ioSystemSubnetId`, `ioSystemNumber` with explicit null for a known absent relationship; optional
`ipAddress`, `subnetMask`, `pnDeviceName` pin readable settings. Node inventory must be complete.
A Subnet-only change does not imply IO attachment/detachment semantics.

Fixture members:

| Member | Meaning |
| --- | --- |
| `Operations` | Exact ordered configuration array, 1..50 items. |
| `ExpectedItemStatuses` | Ordered `succeeded`/`failed`/`skipped` statuses, one per operation; after the first failure later items must use `earlierOperationFailed`. |
| `ExpectedSuccess`, `ExpectedVerificationSuccess` | Explicit expected boolean call and applied-subset verification outcomes. A reproduced requested skip can fail execution while applied-subset checks pass. |
| `ExpectedSettings` | `{operationId,appliedSettings,skippedSettings}` per attempted configuration; exact sparse maps with `Address`, `SubnetMask`, `PnDeviceName`, `Subnet`, `IoSystem`. |
| `Inspections` | `{id,target,attributeNames?}` using exact public `inspect_network_object` selectors. Include exact node and subnet/IO tuple inspections needed for this fixture. |
| `BeforeExpected`, `AfterExpected` | Nonempty maps keyed by inspection ID, containing concrete recursive subsets of the typed inspection result (`target`, `evidence`, `attributes`, etc.). Arrays match exact lengths/order; missing/null/unreadable required evidence aborts. |
| `BeforeNodes`, `AfterNodes`, `MultiHomedDevices` | Concrete exact node/settings/relationship expectations and multi-homed names. Include untouched sibling nodes; no inferred IO effects. |
| `RestoreOperations` | Separate concrete reviewed restoration array; no inferred undo/output references. |
| `BeforeRestore`, `BeforeRestoreNodes` | Fresh inspection/node preconditions matching the actual state authorized for restoration. |
| `RestorationExpected`, `RestorationNodes` | Concrete fresh inspection/node values that establish restoration of the approved fixture. |

For example, a node expectation is:

```json
{
  "deviceName": "PC_1", "nodeId": "node-plc", "ipAddress": "192.0.2.99",
  "subnetId": "<fresh exact subnet ID>",
  "ioSystemSubnetId": "<fresh exact IO subnet ID>", "ioSystemNumber": 1
}
```

A sparse partial-result expectation can be:

```json
{
  "operationId": "configure",
  "appliedSettings": { "Address": "192.0.2.99" },
  "skippedSettings": { "PnDeviceName": "<fixture-qualified exact skip reason>" }
}
```

Do not assume this skip is reproducible on arbitrary hardware. Its exact request/skip reason and
allowed applied subset belong in the reviewed frozen fixture. Unverified or omitted immediate/final
evidence aborts the harness even if a boolean expectation matches. Verification must include exactly the attempted operation IDs in caller order, typed exact node/IO evidence and one immediate check for each applied key. Final checks must cover each attempted node and the effective applied fields; later explicit same-field values supersede earlier final expectations. Device identity compares ordinally ignoring case; node IDs, fields, values and IO tuple identities remain exact. Empty applied subsets require `not_required` with no invented setting checks, while attempted node-preservation evidence remains required. Evidence artifacts preserve the
frozen head/tree/script, fixture SHA-256, exact operations, binding/PID, response and fresh reads;
failures retain an inspect-before-retry classification.

An authorized configuration Apply additionally requires `-AllowMutation`, exact acknowledgement
`APPLY GUARDED NETWORK FIXTURE`, `-AuthorizedProjectPath` equal to `ProjectPath`, and
`-AuthorizedFixtureSha256` equal to the reviewed file SHA-256. The `-Restore` switch is accepted
only with Apply and the same protections. It selects only `RestoreOperations`, performs fresh
`BeforeRestore`/node inspections before its dry run and actual call, then fresh restoration checks.
There is no automatic restoration after failed/uncertain writes. A separate future exact-target
restoration authorization must include the concrete operations and preconditions. Static ordering
checks prove source boundaries; live restoration remains pending.
