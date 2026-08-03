# TIA Portal Network and Topology Operations

Phase 2 status: the single-layer JSON contract gate described here is complete and covers
`network_read` and `network_write` as implemented today. See
[../NETWORK_OPERATIONS_ROADMAP.md](../NETWORK_OPERATIONS_ROADMAP.md) for delivery-phase status
and what remains out of scope (Phase 3 and later).

## Supported operations

The MCP provides a bounded device and network-identity surface:

| Entry point | Operation | Inputs and behavior |
|---|---|---|
| `network_read` | `read_hardware_config` | Reads devices and their network DTOs: interfaces, nodes, subnets, and IO systems where present. |
| `network_read` | `search_equipment_catalog` | Searches the hardware catalog for a device type before creation (`query`, optional `maxResults`). |
| `network_write` | `add_network_device` | Creates a device from an exact catalog `typeIdentifier`; requires `deviceName` and accepts optional `deviceItemName`. Flat by design — it names something that does not exist yet. |
| `network_write` | `configure_network_device` | Configures one exact existing node: `target: { deviceName, nodeId }` plus `changes: { ipAddress?, subnetMask?, pnDeviceName?, subnet?: { subnetId }, ioSystem?: { subnetId, number } }`. |

`configure_network_device` is not a general network-editor proxy. Its writable contract is limited to the listed `changes` fields, and every selector is exact — see "Selector resolution" below.

## The single-layer JSON contract

Both `network_read` and `network_write` declare an MCP `outputSchema` and return **one canonical
JSON document** identically in the tool result's `content` (as text) and in `structuredContent`.
There is no nested JSON-inside-a-string layer anywhere in the response: every operation result is
a real JSON object or array under `batch.operations[n].result`, not an escaped string an agent has
to parse a second time.

### `network_read` envelope

```jsonc
{
  "tool": "network_read",
  "success": true,
  "batch": {
    "operationCount": 1,
    "counts": { "succeeded": 1, "failed": 0, "omitted": 0, "skipped": 0 },
    "operations": [
      {
        "operationId": "hardware",
        "operation": "read_hardware_config",
        "status": "succeeded",
        "result": { "devices": [ /* ... */ ], "subnets": [ /* ... */ ], "messages": [] },
        "failure": null,
        "omission": null,
        "skipReason": null,
        "warnings": []
      }
    ],
    "truncation": null
  },
  "error": null
}
```

`success` describes the whole call: a batch that ran but contains a failed item reports
`success:false` while remaining a successful MCP result (`isError:false`). Only a call rejected
before any operation ran (bad request shape, access denied) populates `error` instead of `batch`
and sets `isError:true`.

### `network_write` envelope

`network_write` is a discriminated envelope: `phase` names exactly which one of `preview`,
`batch`, or `error` is populated — the other two are always `null`.

**`phase: "preview"`** (returned when called with `confirm:false` and no token — nothing is changed):

```jsonc
{
  "tool": "network_write",
  "phase": "preview",
  "success": true,
  "preview": {
    "target": [
      {
        "operationId": "configure",
        "operation": "configure_network_device",
        "deviceName": "PC_1",
        "deviceTypeIdentifier": null,
        "deviceItemPath": ["Ethernet interface"],
        "networkInterfaceName": "PROFINET interface_1",
        "nodeName": "PLC-facing port",
        "nodeId": "node-plc",
        "subnetName": null,
        "subnetId": null,
        "ioSystemName": null,
        "ioSystemNumber": null
      }
    ],
    "summary": "Apply 1 network write operation(s) sequentially; stops on first failure (no rollback).",
    "currentStateHash": "...",
    "requestedInputHash": "...",
    "expiresAtUtc": "2026-01-01T00:10:00Z",
    "safetyToken": "...",
    "diff": null,
    "instructions": "Preview only — nothing was changed. To apply, call network_write with the identical operations list, confirm=true, and this safetyToken."
  },
  "batch": null,
  "error": null
}
```

`preview.target` is evidence, not caller input: every hardware-identity field (`networkInterfaceName` through `ioSystemNumber`) is what `NetworkIdentityResolver` matched against the hardware configuration — never an echo of what the caller typed. For `add_network_device` (creation) those fields stay `null` because nothing exists yet to resolve; only `deviceName`/`deviceTypeIdentifier` come from the request.

**`phase: "apply"`** (returned when called with `confirm:true` and the valid `safetyToken`):

```jsonc
{
  "tool": "network_write",
  "phase": "apply",
  "success": true,
  "preview": null,
  "batch": {
    "operationCount": 1,
    "counts": { "succeeded": 1, "failed": 0, "omitted": 0, "skipped": 0 },
    "operations": [
      {
        "operationId": "configure",
        "operation": "configure_network_device",
        "status": "succeeded",
        "result": { "deviceName": "PC_1", "appliedSettings": { "ipAddress": "192.168.0.99" }, "skippedSettings": {}, "messages": [] },
        "failure": null,
        "omission": null,
        "skipReason": null,
        "warnings": []
      }
    ],
    "truncation": null
  },
  "error": null
}
```

`success` on an applied batch reflects whether **every** item succeeded — an applied batch with a failed item still reports `success:false` while the MCP call itself stays `isError:false`, because the batch ran.

**`phase: "error"`** — the call was rejected before any operation ran (validation, access denial, a dead/expired/mismatched safety token, or a hardware-state read failure before preview/apply could proceed):

```jsonc
{
  "tool": "network_write",
  "phase": "error",
  "success": false,
  "preview": null,
  "batch": null,
  "error": { "category": "validation_error", "message": "..." }
}
```

## Selector resolution is exact and fail-closed

- **Device**: matched by `target.deviceName`, case-insensitive.
- **Node**: matched by the exact, device-scoped `target.nodeId` reported by a prior `read_hardware_config` — ordinal comparison, never a name-only or first-interface guess.
- **Subnet**: matched by the exact `changes.subnet.subnetId` (or `changes.ioSystem.subnetId`).
- **IO system**: matched by the exact `changes.ioSystem.number`, scoped to the already-resolved subnet.

Zero matches, more than one match, or a candidate whose own identity could not be read (an empty/null identity field) are all treated identically: resolution fails with `postcondition_failed`. There is no first-match, first-node, or name-only fallback anywhere in this path — this is what makes it safe to target one exact port on a device that exposes several network interfaces.

### Multi-homed example

A PC station (`PC_1`) with two ports — one PLC-facing (`nodeId: "node-plc"`), one database-facing (`nodeId: "node-db"`) — is reconfigured by targeting only the PLC-facing node:

```jsonc
{
  "operations": [
    {
      "operationId": "configure",
      "operation": "configure_network_device",
      "projectPath": "C:\\Sandbox\\Line.ap21",
      "target": { "deviceName": "PC_1", "nodeId": "node-plc" },
      "changes": { "ipAddress": "192.168.0.99" }
    }
  ]
}
```

After preview → confirm → apply, a `network_read` (`read_hardware_config`) post-read shows `node-plc`'s `ipAddress` changed to `192.168.0.99`, while `node-db` is byte-for-byte unchanged — every field, not merely "still reports the same IP". This is proved end-to-end against a stateful worker by
`NetworkWrite_MultiHomedFlow_ReadSelectPreviewApplyRead_ChangesOnlySelectedPortAndLeavesTheOtherByteForByteUnchanged`
in `TiaMcpServer.Tests/NetworkStructuredProtocolTests.cs`. Always perform this explicit post-read after an apply: the write response itself never re-reads or echoes the written value.

## The four typed payload result types

Every network operation decodes its worker payload against exactly one declared CLR contract in `TiaMcpServer/Network/NetworkPayloadContract.cs`. A payload that does not match its declared contract — malformed, unknown, wrongly cased, wrongly typed, or structurally invalid — becomes a **failed** item with category `protocol_error`; the rejected payload is never echoed back.

| Operation | Result type | Notable shape |
|---|---|---|
| `read_hardware_config` | `HardwareConfigInfo` | `devices[]` (each with nested `items[]`, each item with `networkInterfaces[].nodes[]`), `subnets[]` (each with `ioSystems[]` and `connectedNodeNames[]`), and a payload-level `messages[]` for unreadable members. |
| `search_equipment_catalog` | `CatalogEntryInfo[]` | `typeName`, `typeIdentifier`, optional `articleNumber`/`version`/`catalogPath`/`description`. |
| `add_network_device` | `AddDeviceResultInfo` | `deviceName`, `rootItemName`, `typeIdentifier`, `warnings[]`. |
| `configure_network_device` | `ConfigureNetworkDeviceResultInfo` | `deviceName`, `appliedSettings` (map), `skippedSettings` (map), `messages[]`. |

`NodeInfo.NodeId` and `SubnetInfo.SubnetId` are empty strings, and `IoSystemInfo.Number` is `null`, when the engineering system could not report that identity — an empty/null identity must never satisfy a write selector (see "Selector resolution is exact and fail-closed" above).

## Omission and truncation semantics

Every response is bounded against the **exact canonical document** the caller receives (envelope, counts, and truncation record included), not against an unrelated per-payload serialization:

- **Per-item limit** (~60,000 characters): a single oversized result is dropped **whole** and replaced with an `omission` — never cut mid-value, so the response can never contain a half-written JSON document. The omission carries `reason` (`resultExceededItemCharLimit` or `responseExceededDocumentCharLimit`), `limitChars`, `originalChars`, a `retryTool` (`network_read` — deliberately the read tool even for an omitted write result, since re-running a write to see what it returned would perform the write a second time), and per-operation `guidance`.
- **Whole-document limit** (~180,000 characters): if the document is still too large after per-item bounding, complete successful results are dropped whole, largest first (ties broken by request order), until it fits.
- Only after that: complete `warnings` entries are dropped, then failure messages are shortened as a last resort (a failure's `category` and `status` are never dropped).
- `batch.truncation` (`StructuredBatchTruncation`) records whether anything was changed (`truncated`), the original vs. presented character counts, how many results/warnings were omitted, and the affected `operationId`s — so a caller always knows what it is missing and can retry precisely.

## Recommended workflow

1. Use `network_read` with `search_equipment_catalog` to obtain the exact catalog `typeIdentifier`.
2. Call `network_write` with `add_network_device` and `confirm:false` (or omit `confirm`) to preview, then `confirm:true` with the unchanged list and the returned token to create the device.
3. Use `network_read` with `read_hardware_config` to discover the new device's exact `nodeId` (and, if needed, `subnetId`/IO-system `number`) — `configure_network_device` cannot target a node created earlier in the *same* `network_write` batch, because target resolution runs against one hardware snapshot taken before any operation in that batch executes.
4. Call `network_write` with `configure_network_device`, the exact `target`/`changes`, preview, then apply.
5. Use `network_read` with `read_hardware_config` after every write to confirm the outcome — the write response never echoes back a re-read of the written value.

Network writes are sequential and stop on the first failure. Completed operations are **not** rolled back; a failed item carries an explicit warning that this operation and any earlier operation in the same call may already have changed TIA state, so re-read before retrying rather than re-running the batch blindly.

## Current limits

The current surface does not provide:

- First-class subnet creation, deletion, or editing.
- Node attributes beyond the device-configuration fields listed above.
- PROFINET IO-system or DP master-system attribute editing.
- Transfer-area creation or deletion.
- Address objects, process-image settings, channels, or address-controller services.
- IO connector timing, watchdog, RT class, sync role, send-clock, or isochronous settings.
- S7, FDL, ISO, ISO-on-TCP, TCP, UDP, PTP, or HMI communication-connection management.
- Online connection path selection, accessible-device discovery, gateways, or `ApplyConfiguration`.
- Generic network interface, node, subnet, or connection attribute enumeration and writes.

Hardware configuration results and compile results are engineering data. They do not by themselves certify a live hardware configuration or commissioning outcome.

## Live acceptance harness

A separately authorized, PowerShell 7 acceptance harness for this contract lives at
`scripts/live-test-network-phase2.ps1`. It launches the real MCP host and performs the actual
MCP `initialize`/`tools/list`/`tools/call` sequence — proving the public protocol, not direct
worker IPC — with `Read`, `Preview`, and `Apply` modes. It is never run by any automated test
(`TiaMcpServer.Tests/NetworkLiveHarnessContractTests.cs` proves this statically) and requires a
disposable or backed-up TIA Portal V21 project plus explicit authorization to run.

## Future roadmap

The approved high-level direction for expanded topology operations beyond this contract is
documented in [NETWORK_OPERATIONS_ROADMAP.md](../NETWORK_OPERATIONS_ROADMAP.md).
