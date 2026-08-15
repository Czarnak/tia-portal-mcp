# TIA Portal Network and Topology Operations

Phase 3 status: snapshot-scoped network-object discovery and typed read-only inspection are
implemented on the Phase 2 single-layer JSON contract. The separately authorized TIA Portal V21
evidence run completed on 2026-08-05, but final stabilization is pending design review because
only three of eight observed communication connections had complete selectors. Its measurements,
provenance, retention decision, and coverage limits are recorded in
[NETWORK_PHASE3_LIVE_ACCEPTANCE.md](NETWORK_PHASE3_LIVE_ACCEPTANCE.md).

Phase 4 status: Ethernet and PROFIBUS subnet create/update/delete are added to `network_write`
without a new MCP tool. Implementation is statically verified — both builds, the full test suite,
and a whole-plan contract audit against the Locked Public Contract pass — but public-path live
acceptance against a real TIA Portal V21 project is a separately authorized run that has not yet
been performed. See
[NETWORK_PHASE4_SUBNET_LIFECYCLE.md](NETWORK_PHASE4_SUBNET_LIFECYCLE.md) for the full request
shapes, writable values, targeting, deletion semantics, minimal result, and evidence status.

See [../roadmap/network-operations.md](../roadmap/network-operations.md) for later-phase scope.

## Supported operations

The MCP provides a bounded device and network-identity surface:

| Entry point | Operation | Inputs and behavior |
|---|---|---|
| `network_read` | `read_hardware_config` | Reads devices and their network DTOs: interfaces, nodes, subnets, and IO systems where present. Optional `deviceName` filter, optional `plcName` tag-matching selector, and opt-in structured I/O extraction (`includeIoDetails`, `includeTagMatches`) — see "Structured I/O map" below. |
| `network_read` | `search_equipment_catalog` | Searches the hardware catalog for a device type before creation (`query`, optional `maxResults`). |
| `network_read` | `list_network_objects` | Pages deterministic summaries for one or more `objectKinds`; accepts optional device-scoped filtering, `pageSize` 1-200, and an opaque continuation `cursor`. Complete identities include a selector that can be copied into inspection. |
| `network_read` | `inspect_network_object` | Resolves one exact `target`, verifies its captured identity evidence, and returns modeled and generic attributes. Optional `attributeNames` is case-sensitive, duplicate-free, and limited to 200 names. |
| `network_write` | `add_network_device` | Creates a device from an exact catalog `typeIdentifier`; requires `deviceName` and accepts optional `deviceItemName`. Flat by design — it names something that does not exist yet. |
| `network_write` | `configure_network_device` | Configures one exact existing node: `target: { deviceName, nodeId }` plus `changes: { ipAddress?, subnetMask?, pnDeviceName?, subnet?: { subnetId }, ioSystem?: { subnetId, number } }`. |
| `network_write` | `create_subnet` | Creates a new Ethernet or PROFIBUS subnet from `subnet: { name, networkType, highestAddress?, transmissionSpeed? }`. PROFIBUS-only fields are rejected for Ethernet. |
| `network_write` | `update_subnet` | Renames or changes PROFIBUS attributes on an existing subnet: `target: { kind: "subnet", subnetId }` plus `subnetChanges` (at least one member). |
| `network_write` | `delete_subnet` | Deletes an existing subnet by exact `target: { kind: "subnet", subnetId }`. Connected subnets are deletable; devices are never deleted. |

`configure_network_device` is not a general network-editor proxy. Its writable contract is limited to the listed `changes` fields, and every selector is exact — see "Selector resolution" below.
`create_subnet`, `update_subnet`, and `delete_subnet` are the Phase 4 subnet lifecycle operations,
detailed in [NETWORK_PHASE4_SUBNET_LIFECYCLE.md](NETWORK_PHASE4_SUBNET_LIFECYCLE.md).

### Discovery and inspection requests

```jsonc
{
  "operations": [
    {
      "operationId": "list-device-network",
      "operation": "list_network_objects",
      "objectKinds": ["deviceItem", "networkInterface", "node", "communicationConnection"],
      "deviceName": "ET 200SP station_2",
      "pageSize": 50
    },
    {
      "operationId": "inspect-node",
      "operation": "inspect_network_object",
      "target": {
        "kind": "node",
        "deviceName": "ET 200SP station_2",
        "nodeId": "<node id returned by discovery>"
      },
      "attributeNames": ["Name", "Address"]
    }
  ]
}
```

`objectKinds` is required, non-empty, duplicate-free, and limited to `deviceItem`,
`networkInterface`, `node`, `subnet`, `ioSystem`, and `communicationConnection`. `deviceName` may
be used only when every requested kind is device-scoped; it is invalid with `subnet` or
`ioSystem`. The default page size is 50. Cursors are opaque and bound to the normalized filters,
stable item order, and the current discovery snapshot. A malformed cursor, a cursor reused with
different filters, or one reused after snapshot drift is rejected rather than restarted or
silently retargeted.

Each list item reports `kind`, `selectable`, `selector`, captured `evidence`, and `diagnostics`.
When required identity cannot be read, the item remains visible with `selectable:false` and a
null selector. Callers must not construct a selector from partial evidence; re-list after project
changes because selectors are snapshot-scoped locators, not persistent TIA identifiers.

### Selector shapes

| Kind | Required selector identity | Additional verification evidence |
|---|---|---|
| `deviceItem` | `deviceName`, non-empty `itemPath` | Every path segment carries zero-based sibling `index`, `name`, `positionNumber`, and `typeIdentifier`. |
| `networkInterface` | `deviceName`, non-empty `itemPath` | Optional `interfaceName`, `interfaceType`, and `interfaceOperatingMode` are verified when present. |
| `node` | `deviceName`, `nodeId` | Discovery may also supply `itemPath` plus `nodeIndex`; those two fields are supplied together and verify the owning interface and sibling position. |
| `subnet` | `subnetId` | The exact identifier is re-read before inspection. |
| `ioSystem` | `subnetId`, `number` | Optional `ioSystemIndex` and `ioSystemName` disambiguate and verify duplicate-number candidates. |
| `communicationConnection` | `deviceName`, non-empty owner `itemPath`, `connectionIndex`, `connectionType`, `localConnectionName` | `localConnectionId` is required for the supported non-HMI selector shapes and is inapplicable to `HmiConnection`. |

Resolution follows the recorded path/index first and then verifies all supplied identity evidence.
Zero matches, ambiguity, or any evidence drift fails the target; there is no first-item,
first-interface, first-node, or name-only fallback.

### Typed attribute results

An inspection result contains the verified `target`, modeled identity/relationship `evidence`, an
ordered `attributes` array, and non-fatal `messages`. Every attribute independently reports:

- `source`: `modeled`, `dynamic`, or `modeledAndDynamic` (null only for an unknown requested name);
- `access`: `none`, `readOnly`, `writeOnly`, `readWrite`, or `unknown`;
- `supportedTypes`: the declared CLR type names in metadata order;
- `availability`: `available`, `notApplicable`, `unsupported`, `unreadable`, `readFailed`,
  `unrepresentable`, or `unknownAttribute`;
- `value`: only `null`, `string`, `boolean`, `integer`, `number`, or `enum`; and
- `diagnostic`: category, message, and optional CLR type name when a value is unavailable.

An unknown or failed attribute does not fail the inspection and does not suppress later
attributes. Successfully read CLR null is represented by a value with `kind:"null"`; an arbitrary
CLR object is `unrepresentable` and is never published through `ToString()`.

## Structured I/O map

`read_hardware_config` can return a read-only, opt-in structured I/O map alongside the existing
hardware tree. The legacy per-item `address` string is untouched; the structured map lives under a
new `ioDetails` member that is **absent from a default read** (no flags), so every existing caller
and every safety-token state hash sees byte-identical output.

### Request

```jsonc
{
  "operations": [
    {
      "operationId": "io-map",
      "operation": "read_hardware_config",
      "projectPath": "C:\\Sandbox\\Line.ap21",
      "deviceName": "ET 200SP station_1",   // optional: ordinal-ignore-case, exactly one match
      "plcName": "PLC_1",                   // optional: exact ordinal PLC name for tag matching
      "includeIoDetails": true,             // required for any I/O map output
      "includeTagMatches": true             // optional; requires includeIoDetails
    }
  ]
}
```

- `deviceName` narrows to exactly one device. Zero or multiple matches report a non-fatal
  `messages` entry and return no devices — never a first-match fallback.
- `plcName` selects the PLC whose tag tables are matched, by exact ordinal name (PLC software name
  or owning device name). When omitted, tag matching uses a PLC only when exactly one PLC exists in
  the project; otherwise a non-fatal `messages` entry reports that no tag matches were produced.
- `includeTagMatches` without `includeIoDetails` is rejected by request validation.

### Response shape

A device item with I/O details carries:

```jsonc
{
  "name": "DI_16",
  "typeIdentifier": "OrderNumber:TEST",
  "address": "0..1",              // legacy string, unchanged
  "ioDetails": {
    "addresses": [
      {
        "ioType": "Input",        // AddressIoType: Input, Output, Substitute, Diagnosis
        "startAddress": 4,        // raw Openness start, BYTES
        "length": 2,              // raw Openness length, BYTES
        "context": "Device",      // dynamic AddressContext where readable; null otherwise
        "controllerNames": ["PLC_1"]  // ordinal, deduplicated owning device names
      }
    ],
    "channels": [
      {
        "number": 0,
        "ioType": "Input",        // ChannelIoType: Input, Output, Complex
        "type": "Digital",        // ChannelType: Analog, Digital, Technology
        "channelAddressBits": 32, // raw Openness start, BITS
        "channelWidthBits": 1,    // raw Openness width, BITS
        "logicalAddress": "%I4.0",// formatted ONLY when evidence is aligned; null otherwise
        "tagMatches": [
          {
            "name": "StartButton",
            "dataType": "Bool",
            "logicalAddress": "%I4.0",
            "tableName": "Tag table_1",
            "folderPath": "/"
          }
        ]
      }
    ]
  }
}
```

### Unit and formatting semantics

- `startAddress`/`length` on an address are **bytes** as reported by Openness.
  `channelAddressBits`/`channelWidthBits` on a channel are **absolute bits** as reported by
  Openness. The two unit families are never mixed or converted silently.
- `logicalAddress` is formatted only when the channel's I/O type, bit start, and width are all
  present and correctly aligned: width 1 → `%I4.0`/`%Q4.0` (any bit), width 8 → `%IB4`/`%QB4`
  (byte boundary), width 16 → `%IW64`/`%QW64` (even byte), width 32 → `%ID64`/`%QD64`
  (byte divisible by four). Any other combination leaves `logicalAddress` null while
  `channelAddressBits`/`channelWidthBits` stay raw and untouched. `%M` memory, DB, and
  symbolic-only addresses are never emitted.
- Unreadable scalars stay null (never `0`/empty-string defaults); unreadable members add a
  non-fatal `messages` entry. The payload contract additionally rejects an explicit null
  collection inside `ioDetails` as `protocol_error` without echoing the payload.
- TIA V21 reports a negative start address (and length) for some `Diagnosis`-type addresses; the
  worker normalizes these to null with a non-fatal `messages` entry. Channel `ChannelAddress`/
  `ChannelWidth` dynamic attributes are accepted when Openness reports them as 64-bit integers
  (`Int64`/`UInt64`) within the DTO range.

### Conservative tag matching

- The tag index is built **once** per selected PLC from its tag tables (folder path preserved).
- A tag matches a channel only when its normalized absolute I/O interval **exactly equals** the
  channel's interval **and** the I/O areas agree (`I` vs `Q`). There is no overlap, containment, or
  first-match fallback; several tags may match one channel (they all name the same interval).
- The channel must belong to the selected PLC's controller per Openness association evidence
  (`Address.AddressControllers` → owning device name). Tags are never matched across controllers.
  If the controller association is unreadable or ambiguous, the channel keeps its evidence with an
  empty `tagMatches` array and a clear non-fatal `messages` entry.
- `%M`/DB/symbolic-only tags are skipped; harmless casing and surrounding whitespace in tag
  addresses are normalized.

### Payload size

Detailed I/O output can be large. When a `read_hardware_config` result is omitted or truncated,
the response guidance recommends narrowing with `deviceName`, disabling `includeIoDetails`/
`includeTagMatches` where possible, or re-running the operation in its own `network_read` call.

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

`update_subnet` and `delete_subnet` targets follow the identical exact, fail-closed rule: `target.subnetId` is matched with ordinal (case-sensitive) equality against `HardwareConfigInfo.Subnets`, with no name, index, or first-match fallback. See [NETWORK_PHASE4_SUBNET_LIFECYCLE.md](NETWORK_PHASE4_SUBNET_LIFECYCLE.md).

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

## The typed payload result types

Every network operation decodes its worker payload against exactly one declared CLR contract in `TiaMcpServer/Network/NetworkPayloadContract.cs`. A payload that does not match its declared contract — malformed, unknown, wrongly cased, wrongly typed, or structurally invalid — becomes a **failed** item with category `protocol_error`; the rejected payload is never echoed back.

| Operation | Result type | Notable shape |
|---|---|---|
| `read_hardware_config` | `HardwareConfigInfo` | `devices[]` (each with nested `items[]`, each item with `networkInterfaces[].nodes[]`), `subnets[]` (each with `ioSystems[]` and `connectedNodeNames[]`), a payload-level `messages[]` for unreadable members, and — only when requested — `items[].ioDetails` (addresses, channels, tag matches). |
| `search_equipment_catalog` | `CatalogEntryInfo[]` | `typeName`, `typeIdentifier`, optional `articleNumber`/`version`/`catalogPath`/`description`. |
| `list_network_objects` | `NetworkObjectListInfo` | `items[]`, exact `totalCount`/`returnedCount`, and nullable `nextCursor`; each item preserves selector completeness and discovery diagnostics. |
| `inspect_network_object` | `NetworkObjectInspectionInfo` | Verified `target`, typed `evidence`, independent per-attribute results, and non-fatal `messages[]`. |
| `add_network_device` | `AddDeviceResultInfo` | `deviceName`, `rootItemName`, `typeIdentifier`, `warnings[]`. |
| `configure_network_device` | `ConfigureNetworkDeviceResultInfo` | `deviceName`, `appliedSettings` (map), `skippedSettings` (map), `messages[]`. |
| `create_subnet`, `update_subnet`, `delete_subnet` | `SubnetLifecycleResultInfo` | Exactly `subnetId`, `name`, `networkDeviceCount`, `networkDeviceCountUnchanged` (must be `true`). All three subnet lifecycle operations share this one result type. See [NETWORK_PHASE4_SUBNET_LIFECYCLE.md](NETWORK_PHASE4_SUBNET_LIFECYCLE.md). |

`NodeInfo.NodeId` and `SubnetInfo.SubnetId` are empty strings, and `IoSystemInfo.Number` is `null`, when the engineering system could not report that identity — an empty/null identity must never satisfy a write selector (see "Selector resolution is exact and fail-closed" above).

## Omission and truncation semantics

Every response is bounded against the **exact canonical document** the caller receives (envelope, counts, and truncation record included), not against an unrelated per-payload serialization:

- **Per-item limit** (~60,000 characters): a single oversized result is dropped **whole** and replaced with an `omission` — never cut mid-value, so the response can never contain a half-written JSON document. The omission carries `reason` (`resultExceededItemCharLimit` or `responseExceededDocumentCharLimit`), `limitChars`, `originalChars`, a `retryTool` (`network_read` — deliberately the read tool even for an omitted write result, since re-running a write to see what it returned would perform the write a second time), and per-operation `guidance`.
- **Whole-document limit** (~180,000 characters): if the document is still too large after per-item bounding, complete successful results are dropped whole, largest first (ties broken by request order), until it fits.
- Only after that: complete `warnings` entries are dropped, then failure messages are shortened as a last resort (a failure's `category` and `status` are never dropped).
- `batch.truncation` (`StructuredBatchTruncation`) records whether anything was changed (`truncated`), the original vs. presented character counts, how many results/warnings were omitted, and the affected `operationId`s — so a caller always knows what it is missing and can retry precisely.

## Recommended workflow

1. Use `list_network_objects` with the narrowest useful `objectKinds` and, for device-scoped
   kinds, `deviceName`. Follow `nextCursor` until null and preserve only complete returned
   selectors.
2. Copy a returned selector unchanged into `inspect_network_object`; use `attributeNames` when a
   bounded targeted read is sufficient. Re-list if the project changes or a snapshot cursor or
   selector is rejected.
3. Use `search_equipment_catalog` to obtain an exact catalog `typeIdentifier` before creation.
4. Call `network_write` with `add_network_device` and `confirm:false` (or omit `confirm`) to
   preview, then `confirm:true` with the unchanged list and returned token to create the device.
5. Re-read discovery after creation. `configure_network_device` cannot target a node created
   earlier in the same write batch because target resolution uses one pre-write hardware snapshot.
6. Call `network_write` with `configure_network_device`, the exact `target`/`changes`, preview,
   then apply. Use `read_hardware_config` after every write to confirm the outcome; a write result
   never substitutes for a post-read.

Network writes are sequential and stop on the first failure. Completed operations are **not** rolled back; a failed item carries an explicit warning that this operation and any earlier operation in the same call may already have changed TIA state, so re-read before retrying rather than re-running the batch blindly.

## Current limits

The current surface does not provide:

- Node attributes beyond the device-configuration fields listed above.
- PROFINET IO-system or DP master-system attribute editing.
- Transfer-area creation or deletion.
- Address-object, process-image, channel, or address-controller **writes**; the I/O map is a
  read-only view (`ioDetails`), not an editing surface.
- I/O-map reads beyond addresses and channels: diagnostics data, module-specific hardware
  parameters, and hardware identifiers are not exposed.
- IO connector timing, watchdog, RT class, sync role, send-clock, or isochronous settings.
- S7, FDL, ISO, ISO-on-TCP, TCP, UDP, PTP, or HMI communication-connection management.
- Online connection path selection, accessible-device discovery, gateways, or `ApplyConfiguration`.
- Generic network attribute writes. Phase 3 inspection is read-only and exposes only the closed
  typed value vocabulary described above.
- Creation, deletion, or editing of communication connections. Phase 3 only discovers and
  inspects existing connections whose identity is complete.

Hardware configuration results and compile results are engineering data. They do not by themselves certify a live hardware configuration or commissioning outcome.

## Live acceptance harnesses

`scripts/live-test-network-phase3.ps1` is the separately authorized, read-only PowerShell 7
harness for discovery and inspection. `Matrix`, `Repeatability`, and `MeasureListValue` launch the
real MCP host and drive its actual `initialize`/`tools/list`/`tools/call` protocol. `RawProbe`
starts the worker directly for an internal metadata diagnostic; that probe is read-only and is not
a public MCP operation. The script is not run by automated tests or CI. The completed 2026-08-05
run, including the decision to retain `list_network_objects`, is documented in
[NETWORK_PHASE3_LIVE_ACCEPTANCE.md](NETWORK_PHASE3_LIVE_ACCEPTANCE.md).

Repeatability is evaluated over the canonical discovery payload (`result`, `omission`, and
`truncation`) and targeted inspections. The full MCP envelope may legitimately differ when the
first attachment reports the one-time warning `Connected to running TIA Portal instance.`; that
warning remains visible and is not treated as selector or payload drift.

The Phase 2 read/write harness remains at `scripts/live-test-network-phase2.ps1`. Its `Read`,
`Preview`, and `Apply` modes require a disposable or backed-up project and separate authorization;
the Phase 3 read-only authorization does not authorize any Phase 2 write mode.

`scripts/live-test-network-io-map.ps1` is the separately authorized, read-only harness for the
structured I/O map. It drives `network_read` (`read_hardware_config`) with the I/O-map options
through the real MCP protocol and contains no write path and no confirming call site; it is never
run by automated tests (`TiaMcpServer.Tests/NetworkIoMapLiveHarnessContractTests.cs` proves that
statically). A live run completed on 2026-08-14 against a real TIA Portal V21 project
(Project20.ap21); the results are recorded in
[`../superpowers/acceptance/reports/2026-08-14-io-map-defect-fixes-live.md`](../superpowers/acceptance/reports/2026-08-14-io-map-defect-fixes-live.md).

`scripts/live-test-network-phase4-subnets.ps1` is the separately authorized harness for the Phase 4
subnet lifecycle operations. Its default `Inventory` mode is read-only; `Preview` constructs the
exact create/update/delete operations without applying; `Apply` is double-gated behind
`-AllowMutation` plus an exact acknowledgement string and requires an explicit disposable `.ap21`
project path. The script exists and its static contract is verified
(`TiaMcpServer.Tests/NetworkPhase4SubnetLiveHarnessContractTests.cs`), but it has not yet been run
against a live TIA Portal V21 project; see
[NETWORK_PHASE4_SUBNET_LIFECYCLE.md](NETWORK_PHASE4_SUBNET_LIFECYCLE.md) for the outstanding
public live acceptance gate.

## Future roadmap

The approved high-level direction for expanded topology operations beyond this contract is
documented in [network-operations.md](../roadmap/network-operations.md).
