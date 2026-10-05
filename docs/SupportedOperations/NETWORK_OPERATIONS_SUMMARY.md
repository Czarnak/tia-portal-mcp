# TIA Portal Network and Topology Operations

Current guarded Network candidate: implemented with focused offline qualification. The
2026-10-05 live-defect fixes were live accepted on TIA Portal V21 within the bounds recorded under
"Live acceptance evidence" below; final combined qualification remains pending. Older historical
live evidence applies only to its recorded source and does not qualify this candidate.

Phase 3 historical status: snapshot-scoped network-object discovery and typed read-only inspection are
implemented on the Phase 2 single-layer JSON contract. The separately authorized TIA Portal V21
evidence run completed on 2026-08-05, but final stabilization is pending design review because
only three of eight observed communication connections had complete selectors. Its measurements,
provenance, retention decision, and coverage limits are recorded in
[NETWORK_PHASE3_LIVE_ACCEPTANCE.md](NETWORK_PHASE3_LIVE_ACCEPTANCE.md).

Phase 4 status: Ethernet and PROFIBUS subnet create/update/delete are added to `network_write`
without a new MCP tool. The earlier audit found contract gaps; focused static gates verify the
PR 1 repairs. The 2026-09-23 public run stopped at a TIA safety-permission rejection of connected
Ethernet deletion. On 2026-09-24, a fresh guarded public run passed all eight lifecycle operations,
including both connected deletes, and a separate final read observed zero subnets and 81 aggregate
hardware devices. Retained-device identities and node/IO-system attributes were not independently
read back. See the [bounded live PASS report](../superpowers/acceptance/reports/2026-09-21-network-phase4-current-revision-live.md)
and [Phase 4 operation reference](NETWORK_PHASE4_SUBNET_LIFECYCLE.md) for the full request
shapes, writable values, targeting, deletion semantics, minimal result, and evidence status.

See [../roadmap/network-operations.md](../roadmap/network-operations.md) for later-phase scope.

## Supported operations

The MCP provides a bounded device and network-identity surface:

| Entry point | Operation | Inputs and behavior |
|---|---|---|
| `network_read` | `read_hardware_config` | Recursively discovers devices both at project root and in nested device groups, then reads their network DTOs: interfaces, nodes, subnets, and IO systems where present. Optional `deviceName` filter (the station/root device name as in `devices[].name`), optional `plcName` tag-matching selector, and opt-in structured I/O extraction (`includeIoDetails`, `includeTagMatches`) — see "Structured I/O map" below. |
| `network_read` | `search_equipment_catalog` | Searches the hardware catalog for a device type before creation (`query`, optional `maxResults`). |
| `network_read` | `list_network_objects` | Pages deterministic summaries for one or more `objectKinds`; accepts optional device-scoped filtering, `pageSize` 1-200, and an opaque continuation `cursor`. Complete identities include a selector that can be copied into inspection. |
| `network_read` | `inspect_network_object` | Resolves one exact `target`, verifies its captured identity evidence, and returns modeled and generic attributes. Optional `attributeNames` is case-sensitive, duplicate-free, and limited to 200 names. |
| `network_write` | `add_network_device` | Creates a device from an exact catalog `typeIdentifier`; requires `deviceName` and accepts optional `deviceItemName` (blank or whitespace is a `validation_error`). Flat by design — it names something that does not exist yet. The created item is verified by unique exact name among the device's top-level items only, so a head module whose child repeats its name passes. |
| `network_write` | `configure_network_device` | Configures one exact existing node: `target: { deviceName, nodeId }` plus `changes: { ipAddress?, subnetMask?, pnDeviceNameAutoGeneration?, pnDeviceName?, subnet?: { subnetId }, ioSystem?: { subnetId, number } }`. See "PROFINET device name" below. |
| `network_write` | `create_subnet` | Creates a new Ethernet or PROFIBUS subnet from `subnet: { name, networkType, highestAddress?, transmissionSpeed? }`. PROFIBUS-only fields are rejected for Ethernet. |
| `network_write` | `update_subnet` | Renames or changes PROFIBUS attributes on an existing subnet: `target: { kind: "subnet", subnetId }` plus `subnetChanges` (at least one member). |
| `network_write` | `delete_subnet` | Deletes an existing subnet by exact `target: { kind: "subnet", subnetId }`. Connected subnets are deletable; devices are never deleted. |

`configure_network_device` is not a general network-editor proxy. Its writable contract is limited to the listed `changes` fields, and every selector is exact — see "Selector resolution" below.
`create_subnet`, `update_subnet`, and `delete_subnet` are the Phase 4 subnet lifecycle operations,
detailed in [NETWORK_PHASE4_SUBNET_LIFECYCLE.md](NETWORK_PHASE4_SUBNET_LIFECYCLE.md).
Every Network batch operation requires a unique, nonblank `operationId` of at most 256 characters.
The existing 256-character per-ID limit is unchanged. Writes also reserve the aggregate canonical
encoded identity/protected-summary shape under 180,000 characters before binding or worker activity.
Highly escaped IDs can exceed that reserve even when all individual IDs are valid: an entered
call is rejected with a fixed no-echo `validation_error`, smaller-call guidance and one audit.
This is not a smaller per-ID or universal raw-character aggregate limit; reads retain their admission rules.

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
attributes. Successfully read CLR null is represented by a value with `kind:"null"` and an
explicit `value:null`; an arbitrary
CLR object is `unrepresentable` and is never published through `ToString()`.

## Hardware configuration pagination

`read_hardware_config` pagination is opt-in. Set `pageSize` (`1..200`) on the first request, or
continue with an opaque `cursor`. A cursor-only continuation defaults to a page size of 50. A
request with neither field stays on the unpaged path; envelope version/warnings and additive
connection/root-count evidence follow the current declared contract.

```jsonc
{
  "operations": [
    {
      "operationId": "hardware-page",
      "operation": "read_hardware_config",
      "projectPath": "C:\\Sandbox\\Line.ap21",
      "deviceName": "PLC_1",          // optional, cursor-bound
      "plcName": "PLC_1",             // optional, cursor-bound
      "includeIoDetails": true,        // optional, cursor-bound
      "includeTagMatches": true,       // optional, cursor-bound
      "pageSize": 50,
      "cursor": null                   // omit on the first page
    }
  ]
}
```

The logical sequence is every matching device in stable traversal order, followed by every
matching subnet. `pageSize` counts across that combined sequence, while the public result keeps
`devices[]` and `subnets[]` separate. Canonical size projection may return a smaller complete
prefix than requested; progress and the next cursor advance only past entities actually returned.

Paged results add:

```jsonc
{
  "devices": [],
  "subnets": [],
  "messages": [],
  "pagination": {
    "totalDevices": 0,
    "totalSubnets": 0,
    "returnedDevices": 0,
    "returnedSubnets": 0,
    "nextCursor": "opaque-process-local-value"
  }
}
```

`nextCursor` is nullable and is omitted at the terminal page. Keep `projectPath`, `deviceName`,
`plcName`, `includeIoDetails`, and `includeTagMatches` unchanged for a continuation. `pageSize` is
not query identity and may change within `1..200`. A continuation may omit `projectPath`; the host
then uses the cursor's resolved path. Repeating it is allowed only when it resolves to the same
path. Explicit-project paging does not bind an otherwise unbound host.

Cursors are authenticated with a host-process-lifetime key and intentionally stop working after a
host restart. They also bind the resolved project, bound/unbound host snapshot, observed worker
session, stable candidate snapshot, and combined offset. Recovery is category-specific:

| Category | Meaning | Recovery |
| --- | --- | --- |
| `validation_error` | `pageSize` is outside `1..200`, or a dependent field is invalid. | Correct the first request and start a new sequence. |
| `invalid_cursor` | Encoding, version, schema, canonical form, or signature is invalid; this also covers a cursor from a previous host process. | Start a new sequence without the old cursor. |
| `cursor_filter_mismatch` | A cursor-bound filter or detail flag changed. | Retry the unchanged request at the same cursor, or start a new sequence with the new fields. |
| `cursor_binding_mismatch` | The repeated project path differs, the host binding/path changed, or the observed worker session changed. | Re-read current project status, then start a new sequence; do not reuse the cursor. |
| `cursor_snapshot_mismatch` | The stable matching device/subnet set or order changed. | Start a new sequence against the new snapshot. |
| `cursor_out_of_range` | The saved combined offset is outside the current candidate set. | Start a new sequence. |
| `protocol_error` | The worker omitted envelope identity or returned malformed/incoherent typed candidate evidence. | Treat the page as unusable; inspect diagnostics and retry only after correcting the host/worker mismatch. |

Every successful or omitted operation item remains at most 60,000 canonical characters. Complete
entities and diagnostic strings are never split. The host returns the largest complete prefix that
fits. If page diagnostics alone exceed the limit, the operation is omitted with
`hardwarePageDiagnosticsExceededItemCharLimit`, no subject, and no offset advance. If the first
required entity cannot fit, the reason is `hardwarePageEntityExceededItemCharLimit`; its full
optional `{ kind, name, identifier }` subject is retained only when that complete subject also
fits, otherwise the entire subject is omitted (never truncated). Both omissions preserve safe
guidance: retry the unchanged request at the same cursor, or start a new narrower sequence. The
separate 180,000-character batch limit still applies; if it drops a hardware page, use that same
guidance and never reuse an old cursor after changing bound fields.

## Structured I/O map

`read_hardware_config` can return a read-only, opt-in structured I/O map alongside the existing
hardware tree. The legacy per-item `address` member is always `null`: a TIA `DeviceItem` has no
`Address` attribute, so the worker no longer reads one (which only produced per-item messages). Use
`ioDetails.addresses` for I/O addresses. The structured map lives under a new `ioDetails` member that is **absent from a default read** (no flags), so every existing caller
sees no optional I/O-map expansion; Network no longer uses safety-token state hashes.

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

- `deviceName` narrows to exactly one device. It names the station/root device as reported in
  `devices[].name` (case-insensitive), not a PLC/CPU device-item name; use `plcName` for the PLC.
  Zero or multiple matches report a non-fatal `messages` entry and return no devices — never a
  first-match fallback. A device-scoped read still returns every project subnet.
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
  "address": null,                // legacy member, always null; use ioDetails.addresses
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

Recursive group traversal can make an unfiltered hardware result substantially larger than earlier versions because previously omitted devices are now present. The unpaged path still applies the 60,000-character per-result budget and may omit the whole operation with `reason: "resultExceededItemCharLimit"`. For complete retrieval, use the paged request above, narrow `deviceName`, disable optional detail flags where possible, and place the hardware read in its own `network_read` call. No device is silently skipped merely to fit either path's budget.

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
  "contractVersion": "1.0",
  "warnings": [],
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

Public input is `network_write(operations, dryRun=false)`: **omitting `dryRun` executes**.
An older operations-only call used to preview; migrate every preview caller to `dryRun:true`.
`confirm`, `safetyToken`, `acknowledge`, unknown root keys and nonboolean `dryRun` are rejected
by the SDK/wrapper before tool entry, with a normal MCP error and no write audit. That rejection
need not be a guarded canonical envelope. Entered validation, binding and guard denials have
a typed canonical top-level `error`, MCP `isError:true`, and exactly one audit v2 record.

Both writable modes use zero server Network elicitation, including connected deletion and dry
runs. Client permission prompts remain independent. Lifecycle still confirms every actual
read-write call; generic batch writes still use tokens.

Writes require one exact already-open verified project binding. One pinned lease covers planning,
ordered re-planning, mutation, immediate/final verification, response composition and audit.
Reads never bind, switch or open a project. Maximum 50 unique-ID operations, caller order,
stop at the first failed write; later items are `skipped` with `skipReason:"earlierOperationFailed"`.
No batch rollback or automatic replay.

| Phase | Meaning |
| --- | --- |
| `preview` | `dryRun:true`; effects/guards only, no mutation or elicitation. |
| `applied` | Mutation was attempted; typed `batch` and `verification`, `error:null`, MCP `isError:false`, even on partial or verification failure. |
| `blocked` | A non-overridable guard prevented execution; root error and MCP `isError:true`. |
| `error` | Entered input/binding/planning rejection; root error and MCP `isError:true`. |

All write roots declare `tool`, `contractVersion:"1.0"`, `phase`, `success`, `error`, `warnings`,
`guards`, `effects`, `batch`, `verification`, `omission`. Arrays stay present and declared nulls
stay explicit. For example, a dry-run envelope contains the resolved effect values in `effects`:

```jsonc
{
  "tool": "network_write", "contractVersion": "1.0", "phase": "preview",
  "success": true, "error": null, "warnings": [], "guards": [],
  "effects": [ /* { operationId, effect: resolved target/current/requested settings, omission:null } */ ],
  "batch": null, "verification": null, "omission": null
}
```

Effect hardware identities come from ordinary reads. Creation names are request-derived and
have no invented device/node/subnet ID. `network_delete_connected_subnet` is `info`: its affected
exact device/node identities explain removed subnet/IO connections. Devices/nodes remain.
`network_state_unverifiable` is `block`: an unreadable inventory is unknown, never empty.
Both guards have null satisfaction; Network has no acknowledge guard.

Effect `currentSettings` come from an exact `inspect_network_object` of the target node or subnet,
so each value reports its real Openness `source` and `access` (for example `Address` is `dynamic`,
`readWrite`). A setting that inspection cannot read falls back to the hardware snapshot with
`source:"modeled"` and `access:"unknown"`; the relationship keys `Subnet`, `IoSystemSubnet` and
`IoSystemNumber` are always that modeled/unknown snapshot evidence.

Sparse result maps `appliedSettings`/`skippedSettings` preserve `Address`, `SubnetMask`,
`PnDeviceNameAutoGeneration`, `PnDeviceName`, `Subnet`, `IoSystem`. The single `IoSystem` key
stands for the requested relationship; effect `currentSettings`/`requestedSettings` and immediate
and final checks split it into scalar `IoSystemSubnet` (subnet ID) and `IoSystemNumber` keys. No
value is a nested JSON string. Unrequested keys are absent. Any requested skip fails the item/call while keeping the typed
result and applied subset. This illustrative partial item belongs to an `applied` envelope
with `success:false`, `error:null`; the later item was never dispatched:

```json
{
  "operationCount": 2,
  "counts": { "succeeded": 0, "failed": 1, "omitted": 0, "skipped": 1 },
  "operations": [
    {
      "operationId": "configure", "operation": "configure_network_device", "status": "failed",
      "result": {
        "deviceName": "PC_1",
        "appliedSettings": { "Address": "192.0.2.99" },
        "skippedSettings": { "PnDeviceName": "Requested setting unavailable on this node." },
        "messages": [],
        "verification": {
          "status": "passed",
          "identity": {
            "deviceName": "PC_1", "deviceItemName": null, "nodeId": "node-plc",
            "interfacePath": [ { "name": "PROFINET interface_1", "positionNumber": 0 } ],
            "interfaceName": null, "subnetId": null
          },
          "checks": [ { "name": "Address", "status": "passed", "expected": "192.0.2.99", "observed": "192.0.2.99", "message": null } ],
          "message": null
        }
      },
      "failure": { "category": "worker_operation_failed", "message": "One or more requested network settings could not be applied." },
      "omission": null, "skipReason": null, "warnings": []
    },
    {
      "operationId": "later", "operation": "configure_network_device", "status": "skipped",
      "result": null, "failure": null, "omission": null,
      "skipReason": "earlierOperationFailed", "warnings": []
    }
  ],
  "truncation": null
}
```

Immediate checks cover applied settings. Verification retains that evidence before deliberate later
changes, then inspects the effective attempted prefix. A skipped configure setting gets a final
preservation check whose `expected` is the pre-write value; an unreadable pre-write value leaves that
check `unverified`, and an earlier applied expectation for the same field takes precedence.
Missing/unreadable post-read evidence never passes. Later explicitly applied same-field/relationship
values and designed deletion consequences supersede earlier final expectations, while earlier
immediate checks remain. A Subnet-only move does not imply IO detach/attach: an earlier explicit
`IoSystemSubnet`/`IoSystemNumber` expectation remains, so an API-induced side effect can
conservatively fail final verification after mutation. Inspect before retry; do not rewrite earlier
successful item history or replay automatically.

`verification.success` reports only whether the evidence for the attempted prefix matched; execution
failure is reported by batch/root `success`. So `verification.success:true` with root
`success:false` is by design: for example, a skipped setting whose pre-write value was preserved.

Each item's verification `identity` is a typed object `{ deviceName, deviceItemName, nodeId,
interfacePath, interfaceName, subnetId }`; identity members that do not apply are explicit nulls and
`interfacePath` is an array of `{ name, positionNumber, typeIdentifier? }` segments whose
`typeIdentifier` is present only when readable (omitted, not null, otherwise). A device carries
`deviceName`/`deviceItemName`, a node `deviceName`/`nodeId` with its owner path, a subnet `subnetId`.
Each `verification.finalChecks[]` entry is `{ kind, operationId, subject, field, status, expected,
observed, message }`:

| `kind` | `subject` / `operationId` | Example `field` values |
| --- | --- | --- |
| `device` | typed device identity | `deviceName`, `deviceItemName`, `typeIdentifier` |
| `node` | typed node identity | `exists`, `Address`, `SubnetMask`, `PnDeviceNameAutoGeneration`, `PnDeviceName`, `Subnet`, `IoSystemSubnet`, `IoSystemNumber`, `removedSubnet` (subject `subnetId` names the removed subnet) |
| `subnet` | `{ subnetId }` | `exists`, `absent`, `TypeIdentifier`, `Name`, `HighestAddress`, `TransmissionSpeed` |
| `operation` | `subject:null`, `operationId` set | `immediateEvidence`, `affectedInventory` |
| `write` | both null | `finalHardwareState`, `networkDeviceCountUnchanged` |

Names are never interpolated into a check string.

### PROFINET device name

Optional boolean `changes.pnDeviceNameAutoGeneration` writes the node's "Generate PROFINET device
name automatically" setting before `PnDeviceName`, and appears in requested/applied settings,
verification and audit. On V21 it is a `readWrite` Boolean (`true` on a PLC by default) and
`PnDeviceName` reports `readOnly` while it is `true`. When `pnDeviceName` is requested, the node
generates its name automatically (or `PnDeviceName` is not writable), and
`pnDeviceNameAutoGeneration:false` is not supplied, the item fails at plan time with
`validation_error` before any change. Pass `pnDeviceNameAutoGeneration:false` together with
`pnDeviceName` to set the name explicitly. The check blocks only on read evidence: an unreadable
flag with a writable or unreadable `PnDeviceName` does not block the request.

Audit v2 contains exactly one record per entered call under `%LOCALAPPDATA%\TiaMcpServer\audit`.
Actual read-write confirmation is `none`; actual full is `policy`; previews/pre-execution denials
are `none`. Info/block guard satisfaction is null. Audit response text/hash is the exact canonical
document delivered, while audit item statuses describe actual execution. No Network `user` confirmation.

## Selector resolution is exact and fail-closed

- **Device**: matched by `target.deviceName`, case-insensitive.
- **Node**: matched by the exact, device-scoped `target.nodeId` reported by a prior `read_hardware_config` — ordinal comparison, never a name-only or first-interface guess.
- **Subnet**: matched by the exact `changes.subnet.subnetId` (or `changes.ioSystem.subnetId`).
- **IO system**: matched by the exact `changes.ioSystem.number`, scoped to the already-resolved subnet.

All selection failures remain fail-closed. Host preflight reports a device, node, subnet or IO-system selector that matches nothing as `target_not_found` and one that matches several as `target_ambiguous`; a resolved subnet with a blank or unsupported `NetworkType` is `target_kind_unsupported`; an unreadable discovery snapshot, or a match whose competing identities are unreadable, is `worker_operation_failed`. `postcondition_failed` is reserved for late worker-side drift (zero or multiple matches at mutation time) and failed or unverified postchecks. There is no first-match, first-node, or name-only fallback anywhere in this path — this is what makes it safe to target one exact port on a device that exposes several network interfaces.

`update_subnet` and `delete_subnet` require ordinal `target.kind: "subnet"` and match `target.subnetId` with ordinal (case-sensitive) equality against `HardwareConfigInfo.Subnets`, with no name, index, or first-match fallback. A preflight miss is `target_not_found`/`target_ambiguous`; late zero or multiple worker matches fail as `postcondition_failed`. Delete resolves, type-checks, and captures a nonblank name from the same transaction-local object before `Delete()`. See [NETWORK_PHASE4_SUBNET_LIFECYCLE.md](NETWORK_PHASE4_SUBNET_LIFECYCLE.md).

### Multi-homed example

A PC station (`PC_1`) with two ports — one PLC-facing (`nodeId: "node-plc"`), one database-facing (`nodeId: "node-db"`) — is reconfigured by targeting only the PLC-facing node:

```jsonc
{
  "dryRun": true,
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

Use this request with `dryRun:true` to preview, then explicitly use `dryRun:false` for authorized
execution. Stateful FakeWorker tests exercise selection of one multi-homed port and preservation
of its sibling; that is offline evidence. The applied response now includes typed immediate and
final read verification; follow uncertain or incomplete outcomes with fresh filtered `network_read`.
The 2026-10-05 live V21 run is recorded under "Live acceptance evidence" below. The [configuration/ordered-outcome harness](../../scripts/live-test-network-guarded-write.ps1) uses a frozen [MCP helper](../../scripts/network-live-mcp-helpers.ps1), requires its `ExpectedSharedHelperSha256` before import and records its hash in evidence. It requires complete ordered operation-ID immediate/final verification coverage; summary booleans and fresh final reads cannot replace missing immediate evidence. Its exact final-check count does not yet account for skipped-setting preservation checks (open follow-up). See [local acceptance preparation](../development/local-mcp-testing.md#frozen-guarded-network-acceptance-harnesses).

## The typed payload result types

Every direct public network worker result decodes against exactly one declared CLR contract in `TiaMcpServer/Network/NetworkPayloadContract.cs`. The private paged-hardware candidate result is decoded separately by `HardwarePagePayloadContract` before public projection. A payload that does not match its declared contract — malformed, unknown, wrongly cased, wrongly typed, or structurally invalid — becomes a **failed** item with category `protocol_error`; the rejected payload is never echoed back.

| Operation | Result type | Notable shape |
|---|---|---|
| `read_hardware_config` | `HardwareConfigInfo` | `devices[]` includes project-root and recursively grouped devices (each with nested `items[]`, each item with `networkInterfaces[].nodes[]`), `subnets[]` (each with `ioSystems[]` and `connectedNodeNames[]`), and a payload-level `messages[]` remains the hardware degradation channel (deduplicated; a null device-item type identifier, which TIA reports for some items, is not a message); `items[].address` is always null; only when requested, `items[].ioDetails` contains addresses, channels, and tag matches. Opt-in pages add `HardwarePaginationInfo`; the worker candidate payload is decoded separately and never exposed. |
| `search_equipment_catalog` | `CatalogEntryInfo[]` | `typeName`, `typeIdentifier`, optional `articleNumber`/`version`/`catalogPath`/`description`. |
| `list_network_objects` | `NetworkObjectListInfo` | `items[]`, exact `totalCount`/`returnedCount`, and nullable `nextCursor`; each item preserves selector completeness and discovery diagnostics. |
| `inspect_network_object` | `NetworkObjectInspectionInfo` | Verified `target`, typed `evidence`, independent per-attribute results, and non-fatal `messages[]`. |
| `add_network_device` | `AddDeviceResultInfo` | `deviceName`, `rootItemName`, `typeIdentifier`, `warnings[]`. |
| `configure_network_device` | `ConfigureNetworkDeviceResultInfo` | `deviceName`, sparse `appliedSettings`/`skippedSettings`, `messages[]`, and conditional worker `verification` (required on public guarded writes). |
| `create_subnet`, `update_subnet`, `delete_subnet` | `SubnetLifecycleResultInfo` | Core members are `subnetId`, `name`, `networkDeviceCount`, `networkDeviceCountUnchanged`; conditional worker `verification` is required on public guarded writes. Typed failed/unverified evidence is retained, rather than reporting it as success. All three subnet lifecycle operations share this one result type. See [NETWORK_PHASE4_SUBNET_LIFECYCLE.md](NETWORK_PHASE4_SUBNET_LIFECYCLE.md). |

`NodeInfo.NodeId` and `SubnetInfo.SubnetId` are empty strings, and `IoSystemInfo.Number` is `null`, when the engineering system could not report that identity — an empty/null identity must never satisfy a write selector (see "Selector resolution is exact and fail-closed" above).

## Omission and truncation semantics

Bound individual canonical values at 60,000 characters and the **complete document** at 180,000.
Strict decoding, guards and mutation use full internal values before presentation bounding.
Whole effect/result/verification values can be omitted with shared `{reason,limitChars,originalChars,
retryTool,guidance}` metadata; identity, actual execution failure/skip statuses, guards and compact
verification summaries survive. Root `omission` is explicitly null when complete; it carries shared
metadata when diagnostic delivery is incomplete. Diagnostic strings over 512 canonical encoded
characters are replaced whole with concise summaries; diagnostic collections may be dropped whole
under pressure. No raw diagnostic prefix or cut JSON is delivered.

Delivery `success:false` may coexist with successful mutation, item status `succeeded`, immediate
status `passed` and verification `success:true` when required requested evidence was omitted.
Attempted calls remain `phase:applied`, `error:null`, MCP `isError:false`. The exact delivered
document/hash goes to audit; audit item status retains execution truth. Omission records/digests
are not selectors. Inspect via filtered/paged `network_read` using the original exact selectors,
or discover fresh selectors if unavailable. **Never replay a write to recover an omitted result.**

Read batches retain shared whole-value omission/truncation behavior. Hardware pages return the
largest complete prefix: diagnostics-only overflow uses `hardwarePageDiagnosticsExceededItemCharLimit`,
first-entity overflow uses `hardwarePageEntityExceededItemCharLimit`, and neither advances the
cursor. Its optional complete subject is retained only if it fits. Continue with the unchanged
cursor-bound request, or start a narrower sequence without an old cursor.

## Recommended workflow

1. Use `list_network_objects` with the narrowest useful `objectKinds` and, for device-scoped
   kinds, `deviceName`. Follow `nextCursor` until null and preserve only complete returned
   selectors.
2. Copy a returned selector unchanged into `inspect_network_object`; use `attributeNames` when a
   bounded targeted read is sufficient. Re-list if the project changes or a snapshot cursor or
   selector is rejected.
3. Use `search_equipment_catalog` to obtain an exact catalog `typeIdentifier` before creation.
4. Bind the exact already-open project with `bind_project`; status and hardware reads never bind.
5. Preview the complete exact request with `dryRun:true`; inspect guards and resolved effects.
   Creation has no output-reference syntax: discover the actual created identities before later
   calls that need those identities. All initial targets must resolve before mutation.
6. Explicitly execute the reviewed operations with `dryRun:false`. Read typed item and verification
   outcomes and fresh state after partial failure, timeout, crash, omission or uncertain mutation.
   A preview reserves no state and emits no token; execution resolves its own current state.

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

## Live acceptance evidence

Legacy task-specific acceptance procedures and their source-inspection tests have been removed.
Their removal does not change the evidence boundary: automated, stub, and FakeWorker results are
not live TIA Portal acceptance.

Hardware pagination was not exercised against live TIA Portal before the legacy procedure was
removed, so live pagination behavior remains unverified. The completed 2026-08-05 Phase 3 run,
including the decision to retain `list_network_objects`, is documented in
[NETWORK_PHASE3_LIVE_ACCEPTANCE.md](NETWORK_PHASE3_LIVE_ACCEPTANCE.md).

Repeatability is evaluated over the canonical discovery payload (`result`, `omission`, and
`truncation`) and targeted inspections. The full MCP envelope may legitimately differ when the
first attachment reports the one-time warning `Connected to running TIA Portal instance.`; that
warning remains visible and is not treated as selector or payload drift.

Phase 2 live acceptance remains a separate authorization boundary; read-only authorization does
not authorize a write preview or apply. A structured I/O-map live run completed on 2026-08-14
against a real TIA Portal V21 project (Project20.ap21); the results are recorded in
[`../superpowers/acceptance/reports/2026-08-14-io-map-defect-fixes-live.md`](../superpowers/acceptance/reports/2026-08-14-io-map-defect-fixes-live.md).

A historical Phase 4 public MCP run is evidence only for its recorded older commit. The first
current-revision attempt on 2026-09-23 was incomplete after a TIA safety-permission rejection.
The fresh 2026-09-24 run passed public Inventory, Preview, isolated Ethernet and PROFIBUS
create/update/delete, both connected deletes, and a separate final read. It observed zero subnets
and 81 aggregate hardware devices after Apply. The root count of 10 comes from lifecycle results,
without an independent pre-Apply root baseline. See
[NETWORK_PHASE4_SUBNET_LIFECYCLE.md](NETWORK_PHASE4_SUBNET_LIFECYCLE.md) and the
[current-revision live report](../superpowers/acceptance/reports/2026-09-21-network-phase4-current-revision-live.md).
Subnet lifecycle operations do not save the project or compile hardware.

The 2026-10-05 live-defect fixes (build from `3642062`, disposable `SimpleProject_copy`, V21)
were confirmed live: 15/15 `list_network_objects` nodes selectable; a connected `delete_subnet`
applied with `subnetAbsent`, `affectedNodesPreserved` and `affectedConnectionsRemoved` passed;
`add_network_device` of an ET200SP IM 155-6 PN ST passed; an unknown device (`NOPE-99`) returned
`target_not_found`; typed verification identity and `finalChecks[]`; an IoSystem attach to IO
system 100 on `PN/IE_1` passed its `IoSystemSubnet`/`IoSystemNumber` checks; an invalid IP
produced a passed `Address` preservation final check; a single-device hardware read returned an
empty `messages[]`; and `pnDeviceNameAutoGeneration:false` followed by a name write applied and
verified. The run is recorded in the [improvement log](../IMPROVEMENT_LOG.md).

## Future roadmap

The approved high-level direction for expanded topology operations beyond this contract is
documented in [network-operations.md](../roadmap/network-operations.md).
