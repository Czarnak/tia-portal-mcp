# TIA Portal Network and Topology Operations

`network_write` uses contract **2.0**; `network_read` remains **1.0**. The native-rejections
candidate `75c0568` passed offline qualification and all 17 expected outcomes in the scoped installed
TIA Portal V21 rerun. See the [2026-10-29 acceptance report](../superpowers/acceptance/reports/2026-10-29-network-write-native-rejections-live-verification.md)
for provenance, timing, restoration and untested limits. The later host-only outcome-validation and
tool-help follow-up has separate offline evidence; it was not installed or retimed and adds no
native acceptance. Older dated evidence below applies only to its recorded source and contract.

Phase 3 historical status: snapshot-scoped network-object discovery and typed read-only inspection are
implemented on the Phase 2 single-layer JSON contract. The separately authorized TIA Portal V21
evidence run completed on 2026-08-05, but final stabilization is pending design review because
only three of eight observed communication connections had complete selectors. Its measurements,
provenance, retention decision, and coverage limits are recorded in
[NETWORK_PHASE3_LIVE_ACCEPTANCE.md](NETWORK_PHASE3_LIVE_ACCEPTANCE.md).

Historical Phase 4 status: Ethernet and PROFIBUS subnet create/update/delete are added to `network_write`
without a new MCP tool. The earlier audit found contract gaps; focused static gates verify the
PR 1 repairs. The 2026-09-23 public run stopped at a TIA safety-permission rejection of connected
Ethernet deletion. On 2026-09-24, a fresh guarded public run passed all eight lifecycle operations,
including both connected deletes, and a separate final read observed zero subnets and 81 aggregate
hardware devices. Retained-device identities and node/IO-system attributes were not independently
read back. See the [bounded live PASS report](../superpowers/acceptance/reports/2026-09-21-network-phase4-current-revision-live.md)
and [Phase 4 operation reference](NETWORK_PHASE4_SUBNET_LIFECYCLE.md) for the full request
shapes, writable values, targeting, deletion semantics, minimal result, and evidence status.

See [../roadmap/network-operations.md](../roadmap/network-operations.md) for later-phase scope.

Fatal Portal loss escaping Network discovery, selection, connection/attribute reads
or mutation propagates to the worker's shared `portal_lost` mapping.
Ordinary unreadable evidence and optional-property failures retain their existing
behavior. The host invalidates verified binding/cursors without replay; inspect
any attempted mutation and explicitly bind again before retrying. Existing typed
post-mutation outcomes and cleanup warnings retain their established contracts. The
[B-16/Q3 record](../superpowers/acceptance/reports/2026-10-20-b16-q3-worker-portal-loss-live-verification.md) distinguishes
offline fault-injection evidence from maintainer-accepted scoped live read
testing. Native write interruptions and broader fault locations remain untested.

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
`ioSystem`. The default page size is 100 on initial and continued calls. Public cursors are opaque and authenticated by the host,
bound to its exact project-binding epoch and worker session identity as well as the normalized
filters, stable item order and current discovery snapshot. A malformed or stale cursor is
rejected rather than restarted or silently retargeted.

Each list item reports `kind`, `selectable`, `selector`, captured `evidence`, and `diagnostics`.
When required identity cannot be read, the item remains visible with `selectable:false` and a
null selector. Callers must not construct a selector from partial evidence; re-list after project
changes because selectors are snapshot-scoped locators, not persistent TIA identifiers.

### Network-object continuation cursors

Use the returned `nextCursor` unchanged, keep `objectKinds` and `deviceName` unchanged, and
continue through `network_read`. Explicit `pageSize` may change within `1..200`. The host seals
the existing worker continuation together with the binding ID/revision/path and the worker
session ID, generation, Portal PID and project path. The worker retains its existing
filter, snapshot and range checks; this is authentication, not encryption or a new snapshot store.
Both initial and continued calls use the same guarded host path.

| Category | Meaning and recovery |
|---|---|
| `invalid_cursor` | Malformed, tampered, wrong-purpose, previous-host-process or legacy unsigned public cursor. Start again without the old cursor; old cursor values are not migrated. |
| `cursor_binding_mismatch` | The retained host epoch or worker identity no longer matches. Start a fresh sequence under the intended verified binding. A local stale-cursor refusal preserves the healthy current binding. |
| `cursor_filter_mismatch` | The worker rejected changed query filters. Repeat the original filters or start a new sequence. |
| `cursor_snapshot_mismatch` | Current discovery evidence differs. Start a new sequence; do not reuse the old offset. |
| `cursor_out_of_range` | The continuation offset is outside the current result. Start a new sequence. |
| `protocol_error` | The worker returned malformed payload/identity or a non-null blank continuation. The item fails canonically and later independent reads still run. |

A same-owner `bind_project` reverify that keeps the binding epoch does not invalidate a cursor.
An actual worker/Portal loss keeps its original failure and invalidation behavior: inspect the
failure, bind explicitly to recover, and start a fresh sequence. No request is replayed.
An unbound call still returns the call-level `binding_conflict` before cursor processing.

Public cursor values are limited to 4,096 characters. If issuing one exceeds the limit, the item
is `omitted` with null result/failure and reason `networkObjectCursorExceededCharLimit`, actual
size/limit metadata and retry guidance. No unsigned fallback or unusable partial page is returned;
a terminal page with `nextCursor:null` needs no cursor and can still succeed. Shorten the project
path and start again, or narrow the request until its result fits one terminal page.

The public read request/result schemas and `network_read` `contractVersion:"1.0"` are unchanged.

The [2026-10-23 scoped installed-candidate smoke](../superpowers/acceptance/reports/2026-10-23-c5-network-object-cursors-live-verification.md)
passed initial/continued paging, tamper rejection, same-owner reverify and a fresh sequence in
`full` mode on one standalone fixture with `pageSize:1`. The report retains unexecuted fault,
mode and local-session cases; C-9's default changes and combined qualification are separate.

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
continue with an opaque `cursor`. A cursor-only continuation defaults to a page size of 100. A
request with neither field stays on the unpaged path; read envelope version/warnings and additive
connection evidence follow the current declared contract.

```jsonc
{
  "operations": [
    {
      "operationId": "hardware-page",
      "operation": "read_hardware_config",
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

`nextCursor` is nullable and is omitted at the terminal page. Keep `deviceName`,
`plcName`, `includeIoDetails`, and `includeTagMatches` unchanged for a continuation. `pageSize` is
not query identity and may change within `1..200`. A continuation carries no project path; the host uses the cursor's resolved path, requires the same verified binding, and otherwise fails with `cursor_binding_mismatch`. Content tools act on the bound project only; with no verified binding (a configured `--project` is grounded by status first) they fail closed with `binding_conflict`; call `bind_project` first.

Cursors are authenticated with a host-process-lifetime key and intentionally stop working after a
host restart. They also bind the resolved project, bound/unbound host snapshot, observed worker
session, stable candidate snapshot, and combined offset. Recovery is category-specific:

| Category | Meaning | Recovery |
| --- | --- | --- |
| `validation_error` | `pageSize` is outside `1..200`, or a dependent field is invalid. | Correct the first request and start a new sequence. |
| `invalid_cursor` | Encoding, version, schema, canonical form, or signature is invalid; this also covers a cursor from a previous host process. | Start a new sequence without the old cursor. |
| `cursor_filter_mismatch` | A cursor-bound filter or detail flag changed. | Retry the unchanged request at the same cursor, or start a new sequence with the new fields. |
| `cursor_binding_mismatch` | The host binding/path changed, or the observed worker session changed. | Re-read current project status, then start a new sequence; do not reuse the cursor. |
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

## Paging matrix

| Reader | Public size and position fields | Defaults and opt-in behavior | Bounds | Scan and continuation semantics |
|---|---|---|---|---|
| [`browse_project_tree`](PROJECT_OPERATIONS_SUMMARY.md#browse_project_tree-v3) | `pageSize`, optional `cursor` | 100 initially and on continuation | `pageSize` 1..200 | Cached point-in-time flat-node snapshot; continuations page the saved snapshot. |
| [`network_read/read_hardware_config`](#hardware-configuration-pagination) | `pageSize`, optional `cursor` | First paged call supplies size; cursor-only defaults to 100; omitting both fields keeps unpaged output | `pageSize` 1..200 | Devices then subnets; current candidate snapshot checked. Budget projection can shorten the complete returned prefix. |
| [`network_read/list_network_objects`](#network-object-continuation-cursors) | `pageSize`, optional `cursor` | 100 on every page | `pageSize` 1..200 | Current deterministic inventory checked against the worker snapshot; public cursor is host-sealed. |
| [HMI paged reads](HMI_OPERATIONS_SUMMARY.md#cursor-paging) | `offset`, `limit`, optional `cursor` | `offset` 0, `limit` 100; `cursor` excludes `offset` | `offset` >= 0; `limit` 1..2000 | Cursor continues the saved snapshot; without a cursor, each call performs a fresh scan. |
| [`vci_read/list_mappings`](VCI_OPERATIONS_SUMMARY.md#reads) | `offset`, `limit` | `offset` 0, `limit` 100 | `offset` >= 0; `limit` 1..200 | Plain live offset paging without a snapshot or cursor; mapping changes can shift rows. |

The seven HMI paged operations are `list_tags`, `list_system_tags`, `list_alarms`,
`list_logging_tags`, `list_screen_items`, `list_faceplate_instances`, and `validate`.

Explicit `pageSize` or `limit` may change between pages within the reader's bounds. Follow the
returned continuation metadata rather than assuming exactly the requested count: terminal pages
can be shorter, and budget projection can shorten a complete prefix. Snapshot lifetime, eviction,
budget and recovery rules remain reader-specific; use the linked references when continuing or
retrying. Related PLC reads are covered in the [PLC reference](PLC_OPERATIONS_SUMMARY.md#supported-read-operations).

The paging-default change leaves `search_equipment_catalog.maxResults` at 50: it is a truncation
cap without continuation.

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
`confirm`, `acknowledge`, unknown root keys and nonboolean `dryRun` are rejected
before tool entry with the tool's canonical document (`validation_error`, `isError:true`) and no
audit record, like every argument-shape error in all 19 tools. Entered validation, binding and guard denials have
a typed canonical top-level `error`, MCP `isError:true`, and exactly one audit v2 record.

Binding-gate failures preserve their original category, including `worker_timeout`, `worker_crashed`,
`portal_lost` and `protocol_error`, in the top-level error and audit. A gate denial stops before planning
or mutation; a missing, mismatched or changed binding is `binding_conflict`.

Both writable modes use zero server Network elicitation, including connected deletion and dry
runs. Client permission prompts remain independent. Lifecycle still confirms every actual
read-write call; no tool uses safety tokens.

Writes require one exact already-open verified project binding. One pinned lease covers planning,
ordered mutation, verification, response composition and audit. Planning reads one hardware snapshot;
Network does not re-plan between items, and its shared verification performs no read-back.
Reads never bind, switch or open a project. Maximum 50 unique-ID operations, caller order,
stop at the first failed write; later items are `skipped` with `skipReason:"earlierOperationFailed"`.
No batch rollback or automatic replay.

| Phase | Meaning |
| --- | --- |
| `preview` | `dryRun:true`; effects/guards only, no mutation or elicitation. |
| `applied` | Mutation was attempted; typed `batch` and `verification`, `error:null`, MCP `isError:false`, even on partial or verification failure. |
| `blocked` | A non-overridable guard prevented execution; root error and MCP `isError:true`. |
| `error` | Entered input/binding/planning rejection; root error and MCP `isError:true`. |

All Network write roots declare `tool`, `contractVersion:"2.0"`, `phase`, `success`, `error`, `warnings`,
`guards`, `effects`, `batch`, `verification`, `omission`. Arrays stay present and declared nulls
stay explicit. For example, a dry-run envelope contains the resolved effect values in `effects`:

```jsonc
{
  "tool": "network_write", "contractVersion": "2.0", "phase": "preview",
  "success": true, "error": null, "warnings": [], "guards": [],
  "effects": [ /* { operationId, effect: resolved target/current/requested settings, omission:null } */ ],
  "batch": null, "verification": null, "omission": null
}
```

Effect hardware identities come from ordinary reads. Creation names are request-derived and
have no invented device/node/subnet ID. `network_delete_connected_subnet` is `info`: its affected
exact device/node identities explain removed subnet/IO connections. Devices/nodes remain.
`network_state_unverifiable` is `block` only when a delete's connection inventory is incomplete
or contains unresolved/duplicate identities: unknown is never empty. Incomplete project discovery
fails planning with `worker_operation_failed`. Both guards have null satisfaction; Network has no
acknowledge guard. Delete effects describe the initial snapshot. A node connected earlier in the
same call is absent from that list; split calls and inspect again when that connection must appear
in the deletion effect.

Effect `currentSettings` contains plain strings or explicit nulls from that hardware snapshot,
without a separate `inspect_network_object` call or `source`/`access` metadata. The snapshot lacks
`PnDeviceNameAutoGeneration`, `HighestAddress` and `TransmissionSpeed`, so their current values are
null. Only requested settings appear. Relationship effects split `IoSystem` into scalar
`IoSystemSubnet` and `IoSystemNumber` keys.

Configure applies settings in this order: `Address`, `SubnetMask`, `PnDeviceNameAutoGeneration`,
`PnDeviceName`, `Subnet`, `IoSystem`. The first native rejection stops the item and the batch;
earlier settings may remain applied. A failed configure can retain the same typed result as a
successful one: `deviceName`, sparse `appliedSettings`, `messages[]`. Applied keys preserve those
six setting names; `IoSystem` reports its number. The host validates a carried outcome's raw JSON
before permissive envelope normalization can supply missing members or discard malformed evidence.
Malformed members and carried keys/values that contradict the request fail `protocol_error` without
echoing the rejected evidence. An absent or explicit-null outcome leaves the applied state unknown;
it does not prove that no setting changed. A missing IO connector fails `target_not_found` before
the first setter. Other failed Network operations have `result:null`.

This excerpt is the captured L11c failure: Address and SubnetMask applied before an incompatible
subnet connection threw `EngineeringTargetInvocationException`. Its envelope has `phase:"applied"`,
`success:false`, `error:null`, MCP `isError:false`; the later item was never dispatched. The full
canonical response, warnings and landed reads are in the [functional rerun input](../superpowers/acceptance/reports/inputs/2026-10-29-network-write-native-rejections-live-rerun.md#l11c-corrected-two-key-partial-write-rejection).

```json
{
  "operationCount": 2,
  "counts": { "succeeded": 0, "failed": 1, "omitted": 0, "skipped": 1 },
  "operations": [
    {
      "operationId": "l11c-partial", "operation": "configure_network_device", "status": "failed",
      "result": {
        "deviceName": "S7-1500/ET200MP station_2",
        "appliedSettings": { "Address": "192.168.203.42", "SubnetMask": "255.255.254.0" },
        "messages": []
      },
      "failure": {
        "category": "engineering_rejected",
        "message": "TIA Portal rejected the operation: Error when calling method 'ConnectToSubnet' of type 'Siemens.Engineering.HW.Node'.\r\n\r\nAssignment to subnet failed.",
        "engineeringError": {
          "type": "EngineeringTargetInvocationException",
          "message": "Error when calling method 'ConnectToSubnet' of type 'Siemens.Engineering.HW.Node'.\r\n\r\nAssignment to subnet failed.",
          "inner": []
        }
      },
      "omission": null, "skipReason": null,
      "warnings": [ "This call stopped at this operation. This operation and any earlier operation in the same call may already have changed TIA state; no rollback was attempted. Re-read the affected objects before retrying." ]
    },
    {
      "operationId": "l11c-after", "operation": "create_subnet", "status": "skipped",
      "result": null, "failure": null, "omission": null,
      "skipReason": "earlierOperationFailed", "warnings": []
    }
  ],
  "truncation": null
}
```

Root `verification` is `{ success, operations, omission }`. Each operation entry has
`{ operationId, success, check, expected, actual, message }`. `check:"trusted"` means the Openness
call completed without rejection; nothing was read back. A successful item changed by a later
attempted item on the same object is `superseded`, including a later failure that may already have
mutated it. Precondition refusals do not supersede. Configure identity includes the device,
interface path names/positions and raw node ID; equal raw IDs on different interfaces stay distinct.
Failed and skipped Network items have no verification entry. A failed configure's `appliedSettings`
is recovery evidence, not a verification result. Inspect with `network_read` before retrying.

`verification.success` is the conjunction of its entries, so it may be true when root `success`
is false, including when the only attempted item failed and `operations` is empty. Contract 2.0
removes `skippedSettings`, immediate worker `verification`, `identity`/`checks`, `finalChecks`,
`networkDeviceCount` and `networkDeviceCountUnchanged`.

Classification depends on the Siemens exception type: an `EngineeringTargetInvocationException`
in a mutation worker method is `engineering_rejected`; another `EngineeringException`, such as
`EngineeringNotSupportedException`, is `worker_operation_failed`. Both carry conditional
`engineeringError { type, message, inner[] }`. Configure wrap sites use the shared classification
and preserve their partial result. `portal_lost` takes precedence and invalidates the binding;
wrapped engineering failures retain any partial configure outcome. Native messages follow the
Portal UI language; message/inner-chain bounds remain as described in the
[PLC reference](PLC_OPERATIONS_SUMMARY.md#failures-from-tia-portal).

### PROFINET device name

Optional boolean `changes.pnDeviceNameAutoGeneration` writes the node's "Generate PROFINET device
name automatically" setting before `PnDeviceName`, and appears in requested/applied settings and
audit. Pass `pnDeviceNameAutoGeneration:false` together with `pnDeviceName` to set the name
explicitly. The former planning pre-check is retired. In the scoped L6 run, a name write while
automatic naming remained enabled failed natively with `EngineeringNotSupportedException`
(`worker_operation_failed`); its partial result had no applied settings.

Audit v2 contains exactly one record per entered call under `%LOCALAPPDATA%\TiaMcpServer\audit`.
Actual read-write confirmation is `none`; actual full is `policy`; previews/pre-execution denials
are `none`. Info/block guard satisfaction is null. Audit response text/hash is the exact canonical
document delivered, while audit item statuses describe actual execution. No Network `user` confirmation.

## Selector resolution is exact and fail-closed

- **Device**: matched by `target.deviceName`, case-insensitive.
- **Node**: matched by the exact, device-scoped `target.nodeId` reported by a prior `read_hardware_config` — ordinal comparison, never a name-only or first-interface guess.
- **Subnet**: matched by the exact `changes.subnet.subnetId` (or `changes.ioSystem.subnetId`).
- **IO system**: matched by the exact `changes.ioSystem.number`, scoped to the already-resolved subnet.

All selection failures remain fail-closed. Host preflight reports a device, node, subnet or IO-system selector that matches nothing as `target_not_found` and one that matches several as `target_ambiguous`; a resolved subnet with a blank or unsupported `NetworkType` is `target_kind_unsupported`; an unreadable discovery snapshot, or a match whose competing identities are unreadable, is `worker_operation_failed`. `postcondition_failed` covers late worker selector drift, an ignored subnet rename and an unreadable created subnet ID. There is no first-match, first-node, or name-only fallback anywhere in this path — this is what makes it safe to target one exact port on a device that exposes several network interfaces.

`update_subnet` and `delete_subnet` require ordinal `target.kind: "subnet"` and match `target.subnetId` with ordinal (case-sensitive) equality against `HardwareConfigInfo.Subnets`, with no name, index, or first-match fallback. A preflight miss is `target_not_found`/`target_ambiguous`; late zero or multiple worker matches fail as `postcondition_failed`. Delete resolves once and captures a nonblank name from the same transaction-local object before `Delete()`; supported-type validation uses the host snapshot. See [NETWORK_PHASE4_SUBNET_LIFECYCLE.md](NETWORK_PHASE4_SUBNET_LIFECYCLE.md).

For a requested subnet rename, `update_subnet` compares the name it already reads inside the transaction with the requested name using ordinal (case-sensitive) equality. A mismatch fails with `postcondition_failed` before `CommitOnDispose()`, leaving the subnet update uncommitted. Portal can silently ignore a colliding rename; the failure includes the requested and actual names and identifies a collision as a possible cause. This check adds no native getter or worker call and does not preflight name collisions.

### Multi-homed example

A PC station (`PC_1`) with two ports — one PLC-facing (`nodeId: "node-plc"`), one database-facing (`nodeId: "node-db"`) — is reconfigured by targeting only the PLC-facing node:

```jsonc
{
  "dryRun": true,
  "operations": [
    {
      "operationId": "configure",
      "operation": "configure_network_device",
      "target": { "deviceName": "PC_1", "nodeId": "node-plc" },
      "changes": { "ipAddress": "192.168.0.99" }
    }
  ]
}
```

Use this request with `dryRun:true` to preview, then explicitly use `dryRun:false` for authorized
execution. Stateful FakeWorker tests exercise selection of one multi-homed port and preservation
of its sibling; that is offline evidence. Contract 2.0 reports completion through `trusted` or
`superseded`, without read-back. Follow uncertain or incomplete outcomes with fresh filtered
`network_read`. The 2026-10-05 run below used the earlier verification contract; current scoped
evidence is recorded in the [2026-10-29 report](../superpowers/acceptance/reports/2026-10-29-network-write-native-rejections-live-verification.md).

## The typed payload result types

Every direct public network worker result decodes against exactly one declared CLR contract in `TiaMcpServer/Network/NetworkPayloadContract.cs`. The private paged-hardware candidate result is decoded separately by `HardwarePagePayloadContract` before public projection. A payload that does not match its declared contract — malformed, unknown, wrongly cased, wrongly typed, or structurally invalid — becomes a **failed** item with category `protocol_error`; the rejected payload is never echoed back.

| Operation | Result type | Notable shape |
|---|---|---|
| `read_hardware_config` | `HardwareConfigInfo` | `devices[]` includes project-root and recursively grouped devices (each with nested `items[]`, each item with `networkInterfaces[].nodes[]`), `subnets[]` (each with `ioSystems[]` and `connectedNodeNames[]`), and a payload-level `messages[]` remains the hardware degradation channel (deduplicated; a null device-item type identifier, which TIA reports for some items, is not a message); `items[].address` is always null; only when requested, `items[].ioDetails` contains addresses, channels, and tag matches. Opt-in pages add `HardwarePaginationInfo`; the worker candidate payload is decoded separately and never exposed. |
| `search_equipment_catalog` | `CatalogEntryInfo[]` | `typeName`, `typeIdentifier`, optional `articleNumber`/`version`/`catalogPath`/`description`. |
| `list_network_objects` | `NetworkObjectListInfo` | `items[]`, exact `totalCount`/`returnedCount`, and nullable `nextCursor`; each item preserves selector completeness and discovery diagnostics. |
| `inspect_network_object` | `NetworkObjectInspectionInfo` | Verified `target`, typed `evidence`, independent per-attribute results, and non-fatal `messages[]`. |
| `add_network_device` | `AddDeviceResultInfo` | `deviceName`, `rootItemName`, `typeIdentifier`, `warnings[]`. |
| `configure_network_device` | `ConfigureNetworkDeviceResultInfo` | `deviceName`, sparse `appliedSettings`, `messages[]`; the same typed result can accompany a failed configure when the worker carries its partial outcome. |
| `create_subnet`, `update_subnet`, `delete_subnet` | `SubnetLifecycleResultInfo` | Required `subnetId`, `name`; all three operations share this result type. Failed items have `result:null`. See [NETWORK_PHASE4_SUBNET_LIFECYCLE.md](NETWORK_PHASE4_SUBNET_LIFECYCLE.md). |

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

The historical 2026-10-05 live-defect fixes (build from `3642062`, disposable `SimpleProject_copy`, V21)
were confirmed live: 15/15 `list_network_objects` nodes selectable; a connected `delete_subnet`
applied with `subnetAbsent`, `affectedNodesPreserved` and `affectedConnectionsRemoved` passed;
`add_network_device` of an ET200SP IM 155-6 PN ST passed; an unknown device (`NOPE-99`) returned
`target_not_found`; typed verification identity and `finalChecks[]`; an IoSystem attach to IO
system 100 on `PN/IE_1` passed its `IoSystemSubnet`/`IoSystemNumber` checks; an invalid IP
produced a passed `Address` preservation final check; a single-device hardware read returned an
empty `messages[]`; and `pnDeviceNameAutoGeneration:false` followed by a name write applied and
verified. The run is recorded in the [improvement log](../IMPROVEMENT_LOG.md).

### Contract 2.0 scoped outcomes

The [2026-10-29 acceptance report](../superpowers/acceptance/reports/2026-10-29-network-write-native-rejections-live-verification.md)
and its frozen functional input record 17 passing expected outcomes against one disposable fixture.
O1 means a native rejection, O2 an accepted write with public `compile_check` diagnostics, and O3
an accepted write with silent public software compilation. Controls and MCP postconditions are
identified separately. A clean `compile_check` is PLC software evidence, not hardware acceptance.

| Case | Probe | Observed outcome | Fresh public software compilation |
| --- | --- | --- | --- |
| L1 | Duplicate device create | O1: `engineering_rejected`, `EngineeringTargetInvocationException`. | Existing PLC clean; requested new PLC absent. |
| L2 | Duplicate subnet create | O1: `engineering_rejected`, `EngineeringTargetInvocationException`. | Clean. |
| L3 | Colliding subnet rename | Portal ignored the rename; MCP `postcondition_failed` before commit. The initial O3 finding caused this check. | Clean. |
| L4 | Duplicate IP address | O3 in the public software probe; value landed. | Clean; maintainer separately observed hardware compiler collision diagnostics. |
| L5 | Duplicate PROFINET name | O3 in the public software probe; value landed. | Clean; maintainer separately observed hardware compiler collision diagnostics. |
| L6 | PN name with automatic naming enabled | O1: `worker_operation_failed`, `EngineeringNotSupportedException`; no settings applied. | Clean. |
| L7 | PROFIBUS attribute on Ethernet subnet | O1: `worker_operation_failed`, `EngineeringNotSupportedException`; transaction not committed. | Clean. |
| L8 | Malformed IP address | O1: `engineering_rejected`, `EngineeringTargetInvocationException`. | Not captured for this rerun case. |
| L9 | PROFINET node to PROFIBUS subnet | O1: `engineering_rejected`, `EngineeringTargetInvocationException`. | Clean. |
| L10 | Interface without IO connector | Preflight control: `target_not_found` before setters; later item skipped. | Clean. |
| L11 | GSD mask rejection after Address | O1: `worker_operation_failed`, `EngineeringNotSupportedException`; Address retained, later PN setter and batch item not reached. | Clean. |
| L11b | PN input accepted with automatic naming disabled | Accepted control: Address, mask and PN input completed; later subnet created. A prior maintainer observation recorded the converted PN name. | Clean. |
| L11c | Incompatible connect after Address and mask | O1: `engineering_rejected`, `EngineeringTargetInvocationException`; both applied keys retained; later item skipped. | Clean. |
| L12 | Five-operation lifecycle path | All five completed; rename `superseded` by delete, four other entries `trusted`. Delete guard listed five initial nodes and omitted the same-call connection. | O2: three missing hardware-ID-symbol errors on one PLC after deletion; two other PLCs clean. |
| L13 | Rejected third item | O1: two succeeded/trusted, one `engineering_rejected`, two skipped. | Created PLC clean. |
| L13mini | Rejected second item | O1: one succeeded/trusted, one `engineering_rejected`, one skipped. | Not captured for this rerun case. |
| L14 | Dry run of L13 | Preview control: five effects, no mutation or verification. | Not run. |

These outcomes do not establish arbitrary-device compatibility or hardware/online acceptance.
The L3 check has no live mixed-property rollback evidence; direct fatal loss after a partial
configure remains unexercised. Re-read affected objects and use the appropriate hardware review
and compilation steps before commissioning.

## Future roadmap

The approved high-level direction for expanded topology operations beyond this contract is
documented in [network-operations.md](../roadmap/network-operations.md).
