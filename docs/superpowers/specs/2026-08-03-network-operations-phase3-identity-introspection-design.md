# Network Operations Phase 3 Identity and Introspection Design

## Objective

Establish the read-only identity and introspection foundation required by later
network phases. An MCP client must be able to discover a network object, copy an
explicit selector, inspect its modeled and dynamic attributes, and preserve typed
values plus availability evidence without relying on an implicit first interface,
first node, or name-only fallback.

Phase 3 is intentionally provisional until read-only live testing against TIA Portal
V21 confirms the actual attribute metadata, CLR value types, access modes, exception
behavior, selector evidence, and practical value of bounded object listing.

## Approved Decisions

- `read_hardware_config` remains the lightweight hierarchical identity index.
- `network_read` gains targeted `inspect_network_object` and provisional
  `list_network_objects` operations; neither is a new top-level MCP tool.
- Public requests use typed, discriminated selectors. A generic traversal path is not
  part of the public MCP contract.
- Internally, typed adapters reuse a generic attribute-inspection kernel.
- Inspection returns current typed values together with metadata and availability
  evidence; it is not metadata-only.
- Selectors are deterministic within the captured project snapshot. They are not
  promised to survive renames, moves, deletion/recreation, reordering, or other
  topology edits.
- Missing or ambiguous selector evidence fails closed. Presentation names never become
  fallback selectors.
- `list_network_objects` is retained only if live testing demonstrates measurable
  value. Otherwise it is removed before Phase 3 is declared stable, without an alias
  or deprecation path.
- Phase 3 remains read-only. It does not add generic attribute writes or change the
  preview/apply safety mechanism.

## Current State

Phase 2 already provides the reusable canonical JSON and structured MCP-result gate:

- `network_read` and `network_write` emit the same canonical document in text and
  `structuredContent`;
- successful worker payloads are decoded as exactly one declared CLR type per
  operation;
- malformed successful payloads fail as `protocol_error` without being echoed;
- operation and document budgets omit whole values instead of truncating JSON text;
- network previews bind the ordered request, resolved target evidence, and typed
  hardware state; and
- node, subnet, and IO-system selectors are exact and fail closed.

The existing authoritative identities are:

- node: device name plus `nodeId`;
- subnet: `subnetId`; and
- IO system: subnet ID plus modeled system number.

The existing hardware tree does not provide complete selectors for device items or
network interfaces, does not enumerate project communication connections, and does not
expose modeled/dynamic attribute metadata. `read_hardware_config` also has no narrowing
parameters. If its result exceeds the 60,000-character per-item budget, splitting the
outer batch cannot make that individual result fit.

## Scope

Phase 3 includes:

- additive selector evidence in `read_hardware_config`;
- provisional, bounded `list_network_objects` discovery;
- targeted `inspect_network_object` reads;
- typed selectors and exact resolvers for device items, network interfaces, nodes,
  subnets, IO systems, and communication connections;
- modeled-property adapters;
- a reusable internal `IEngineeringObject` attribute inspector;
- typed value normalization and explicit availability states;
- lightweight communication-connection summaries and read-only introspection;
- canonical ordering and payload-budget behavior;
- unit, contract, FakeWorker, actual MCP protocol, stub-build, and read-only live-TIA
  verification; and
- documentation of live evidence per object and connection kind.

## Non-Goals

Phase 3 does not add:

- network writes or changes to safety-token issuance, validation, consumption, or
  auditing;
- subnet creation, deletion, or editing;
- PROFINET IO-system or DP master-system editing;
- generic network attribute writes;
- communication-connection creation, editing, or deletion;
- transfer-area, address, channel, process-image, IO timing, watchdog, RT class,
  sync-role, send-clock, isochronous, MRP-management, or online-path operations;
- automatic project save, compile, download, or commissioning;
- persistent topology indexing or cross-call object caching;
- a public raw-reflection or arbitrary object-path API; or
- selector stability across project changes.

Hardware engineering reads and live Openness inspection do not certify commissioned
hardware behavior.

## Architecture and Ownership

### Public boundary

`network_read` remains the only top-level MCP tool involved. Its operation catalog
expands from two to four read operations:

- `read_hardware_config`;
- `search_equipment_catalog`;
- `list_network_objects`; and
- `inspect_network_object`.

Each operation retains the shared structured batch envelope, operation ID correlation,
whole-item failure semantics, canonical JSON mirror, and existing payload budgets.

### Host boundary

The host owns:

- strict request-shape and operation-specific field validation;
- typed projection of worker success payloads;
- public output-schema construction;
- canonical serialization;
- operation and document budgeting; and
- conversion of malformed successful payloads into `protocol_error`.

The host validates selector shape but does not perform a hidden hardware read before an
inspection. Exact current-object resolution belongs to the worker.

### Worker boundary

The .NET Framework worker owns every Siemens API call. It:

- traverses the current project and emits selector evidence;
- resolves each selector against current Openness objects;
- obtains typed modeled properties through per-kind adapters;
- obtains dynamic metadata through `IEngineeringObject.GetAttributeInfos()`;
- reads only readable values;
- converts Siemens exceptions into internal inspection outcomes; and
- returns typed contracts over the existing newline-delimited JSON transport.

Where selectors overlap, the real-object resolver is shared with
`NetworkDeviceConfigurator` so inspection and configuration cannot interpret the same
selector differently.

### Internal hybrid

The internal hybrid has three layers:

1. A typed object resolver returns one exact Openness object or a closed failure.
2. A per-kind adapter describes modeled properties and supplies safe typed readers.
3. A reusable engineering-attribute inspector discovers dynamic metadata, reads
   permitted values, and passes raw CLR values to a Siemens-free normalizer.

Raw traversal paths, reflection artifacts, CLR objects, stack traces, and unsupported
raw values never cross the public MCP boundary.

## Object-Kind Vocabulary

The closed Phase 3 object-kind vocabulary is:

- `deviceItem`;
- `networkInterface`;
- `node`;
- `subnet`;
- `ioSystem`; and
- `communicationConnection`.

An unknown kind is a request validation error. Adding a future kind is an explicit
public-contract change.

## Selector Contract

All selectors are copied from current discovery output. String identities and captured
evidence use ordinal comparison except `deviceName`, which preserves the existing
case-insensitive worker lookup semantics.

### Device item

A device-item selector contains `deviceName` and an ordered `itemPath`. Each path
segment contains:

- zero-based sibling `index`;
- observed `name`;
- observed `positionNumber`; and
- observed `typeIdentifier`.

Resolution follows the explicit sibling index and verifies every captured evidence
field. The index is a snapshot address, not an identity promised across edits. A rename,
move, replacement, or sibling reorder invalidates the selector and requires a new read.

```json
{
  "kind": "deviceItem",
  "deviceName": "PLC_1",
  "itemPath": [
    {
      "index": 0,
      "name": "PLC_1",
      "positionNumber": 0,
      "typeIdentifier": "OrderNumber:..."
    },
    {
      "index": 2,
      "name": "PROFINET interface_1",
      "positionNumber": 1,
      "typeIdentifier": "..."
    }
  ]
}
```

### Network interface

A network-interface selector contains the owning device-item selector. TIA V21 exposes
the interface as a single `NetworkInterface` service on that device item. Observed
interface name, interface type, and operating mode are returned as evidence and checked
during resolution when available; interface name alone is never a selector.

### Node

A node selector retains the Phase 2 shape:

```json
{
  "kind": "node",
  "deviceName": "PLC_1",
  "nodeId": "7"
}
```

`nodeId` must identify exactly one node within the named device. Device-item path,
interface evidence, node name, and node type remain resolved evidence rather than
alternative selectors.

### Subnet

A subnet selector is `{ "kind": "subnet", "subnetId": "..." }`. The subnet ID must
identify exactly one subnet across the project.

### IO system

An IO-system selector is
`{ "kind": "ioSystem", "subnetId": "...", "number": 100 }`. The pair must identify
exactly one IO system within the selected subnet.

### Communication connection

A communication-connection selector contains:

- the owning device-item selector;
- zero-based connection-composition index;
- observed connection type;
- observed local connection name; and
- observed local connection ID when the concrete connection type supplies one.

Resolution follows the explicit index and verifies all available evidence. Endpoint and
partner evidence is returned for verification but is not a name-only fallback. The final
field set remains provisional until live testing covers the available connection types
and confirms composition ordering.

### Unselectable objects

Discovery does not invent missing identities. An object with missing or unreadable
identity evidence remains visible with:

- `selectable: false`;
- a null selector; and
- diagnostics describing the missing evidence.

`inspect_network_object` accepts only complete selectors.

## `read_hardware_config` Contract

The existing human-readable hierarchy remains intact. Selector and selectability
evidence is added to represented device items, network interfaces, nodes, subnets, and
IO systems. Lightweight communication-connection summaries are added under the owning
device item.

Connection summaries contain only:

- selector or non-selectable evidence;
- connection type and validity;
- local and partner endpoint evidence; and
- diagnostics needed to explain missing identity.

They do not contain complete type-specific attribute values.

All newly enumerated collections are non-null and deterministically ordered. This is
also a safety invariant: `NetworkSafetySnapshot` currently hashes the complete typed
hardware document for network writes. Phase 3 does not change the safety mechanism, so
repeated unchanged reads must produce identical selector and connection ordering.

## `list_network_objects` Contract

### Purpose

`list_network_objects` is a bounded alternative when the hierarchical hardware result
is too large or the client needs only selected object kinds. It is provisional and must
earn retention through the live value gate.

### Request

```json
{
  "operationId": "list-1",
  "operation": "list_network_objects",
  "projectPath": null,
  "objectKinds": ["node", "communicationConnection"],
  "deviceName": "PLC_1",
  "pageSize": 50,
  "cursor": null
}
```

Rules:

- `objectKinds` is required, non-empty, duplicate-free, and contains only the closed
  Phase 3 vocabulary.
- `deviceName` is optional and is valid only when every requested kind is device-scoped:
  device item, network interface, node, or communication connection. Global subnet and
  IO-system queries use a separate operation item.
- `pageSize` defaults to 50 and initially accepts 1 through 200. The live value gate may
  revise these values before stabilization.
- `cursor` is absent on the first page. A subsequent opaque cursor is bound to the
  original filters, canonical ordering, and selector-snapshot fingerprint.
- A filter change, malformed cursor, or changed selector snapshot invalidates the page
  request. Topology drift requires restarting from the first page.

### Result

The result contains:

- canonically ordered `items`;
- each item's kind, selector, selectability, lightweight evidence, and diagnostics;
- total matching count;
- returned count; and
- nullable `nextCursor`.

It contains no attribute values. Filtering and selector construction happen in the
worker while producing the typed index; the host does not request a full hardware JSON
payload and discard most of it afterward.

### Live value gate

The live harness records, for full hardware discovery and representative list queries:

- canonical response characters;
- elapsed time;
- selector count and completeness;
- omissions or truncation;
- number of client calls needed for discovery followed by inspection; and
- connection-discovery usability.

The operation is retained only if at least one condition is demonstrated:

1. It retrieves complete selectors when `read_hardware_config` exceeds the
   60,000-character per-item budget.
2. A representative targeted query returns at least 50 percent less canonical JSON
   while preserving every matching selector.
3. A connection-only query returns every live connection selector within the item
   budget and avoids at least one otherwise necessary full-tree discovery call in the
   measured discovery-to-inspection workflow.

If no material benefit is demonstrated, the operation and its request/result contracts
are removed before Phase 3 is declared stable. There is no compatibility alias.

## `inspect_network_object` Contract

### Request

One operation item targets exactly one object:

```json
{
  "operationId": "inspect-1",
  "operation": "inspect_network_object",
  "projectPath": null,
  "target": {
    "kind": "node",
    "deviceName": "PLC_1",
    "nodeId": "7"
  },
  "attributeNames": ["Address", "PnDeviceName"]
}
```

`attributeNames` is optional. When supplied, it is non-empty, duplicate-free, and
matched ordinally. Results follow request order. When omitted, the adapter and dynamic
inspector enumerate the merged attribute set in ordinal name order. The initial request
count limit is 200 names and remains subject to live revision before stabilization.

### Result

```json
{
  "target": {
    "kind": "node",
    "deviceName": "PLC_1",
    "nodeId": "7"
  },
  "evidence": {
    "deviceItemPath": ["PLC_1", "PROFINET interface_1"],
    "interfaceName": "X1",
    "nodeName": "PROFINET",
    "nodeType": "Ethernet"
  },
  "attributes": [
    {
      "name": "Address",
      "source": "dynamic",
      "access": "readWrite",
      "supportedTypes": ["System.String"],
      "availability": "available",
      "value": {
        "kind": "string",
        "value": "192.168.0.10"
      },
      "diagnostic": null
    }
  ],
  "messages": []
}
```

The target is the accepted request selector. Evidence contains the canonical current
location and presentation data resolved by the worker.

## Attribute Metadata Contract

### Source

`source` is one of:

- `modeled`;
- `dynamic`; or
- `modeledAndDynamic`.

When the same logical attribute is known through both channels, the typed modeled
adapter supplies the value reader and dynamic metadata supplements access and supported
type evidence where available.

### Access

`access` is one of:

- `none`;
- `readOnly`;
- `writeOnly`;
- `readWrite`; or
- `unknown`.

The installed V21 XML metadata documents `EngineeringAttributeInfo.Name`, `AccessMode`,
and `SupportedTypes`, plus `AttributeAccessOptions.None`, `ReadOnly`, `WriteOnly`, and
`ReadWrite`. Static metadata does not prove that each live object reports complete or
accurate access information.

### Availability

`availability` is one of:

- `available`;
- `notApplicable`;
- `unsupported`;
- `unreadable`;
- `readFailed`;
- `unrepresentable`; or
- `unknownAttribute`.

A modeled adapter returns `notApplicable` when it recognizes the attribute but the
current object subtype or configuration cannot have a value. `unsupported` means the
installed API recognizes the member but reports that this object does not support the
operation. `unreadable` means metadata does not permit a read. `readFailed` means a read
was permitted and attempted but failed. `unrepresentable` means the read succeeded but
the returned CLR value is outside the closed value vocabulary. `unknownAttribute` means
neither the modeled adapter nor live dynamic metadata recognizes a specifically
requested name.

A successful null read is `available` with `value.kind` equal to `null`. It is not
absent, unreadable, or unsupported.

An unknown requested name produces an explicit `unknownAttribute` entry rather than
failing the inspection. When all attributes are requested, only attributes known to the
modeled adapter or dynamic metadata are enumerated.

### Typed value

The initial closed `value.kind` vocabulary is:

- `null`;
- `string`;
- `boolean`;
- `integer`;
- `number`; and
- `enum`.

An enum value preserves its declared type name, symbolic name, and numeric value. An
unrecognized CLR value is never converted with `ToString()`: the entry is
`unrepresentable`, records the observed type name, and omits the value. Live testing may
justify adding another explicit value kind before stabilization.

### Diagnostics

Per-attribute diagnostics contain a stable category and sanitized message. They preserve
useful failure evidence but exclude stack traces, raw rejected payloads, and arbitrary
object string representations.

## Error and Partial-Failure Policy

- Invalid operation fields, selector shape, filters, page size, or cursor shape are
  validation failures.
- A missing, ambiguous, or drifted target fails the individual operation item closed.
- A changed pagination snapshot invalidates that page instead of returning a mixed
  snapshot.
- Failure to inspect one requested attribute does not suppress later attributes.
- Write-only or access-none attributes return metadata without attempting a value read.
- A successful worker response that does not decode as the declared result type becomes
  `protocol_error`; the rejected payload is not echoed.
- An executed batch remains an MCP-success response even when one operation item fails.
  Whole-tool failures retain `isError: true`.
- Existing whole-value item and document budgets apply. JSON values are never substring
  truncated.

## Determinism and Safety Interaction

Phase 3 adds no write operations and does not alter token rules. However, additive
hardware-tree fields participate in the current canonical hardware-state document used
by network-write previews. Therefore:

- device, item, interface, node, subnet, IO-system, and connection collections are
  canonically ordered;
- unchanged repeated reads must serialize identically;
- transient diagnostics are not allowed to reorder identity collections;
- live testing includes repeated-read comparison; and
- any discovered nondeterminism must be fixed or excluded from the safety snapshot
  before Phase 3 stabilization.

Broader state binding may conservatively invalidate a preview after a relevant
connection change. It must never allow a changed state to validate.

## Data Flow

### Hardware discovery

```text
network_read/read_hardware_config
    -> worker traverses hardware and communication services
    -> worker emits typed tree plus selectors and diagnostics
    -> host validates HardwareConfigInfo
    -> shared budget gate
    -> one canonical JSON document in text and structuredContent
```

### Bounded selector discovery

```text
network_read/list_network_objects
    -> host validates filters and cursor shape
    -> worker builds filtered, canonically ordered selector summaries
    -> worker validates cursor against current selector snapshot
    -> worker returns one typed page
    -> host validates declared payload type
    -> shared budget gate and canonical MCP result
```

### Object inspection

```text
network_read/inspect_network_object
    -> host validates selector and optional names
    -> worker resolves exactly one live Openness object
    -> typed adapter describes modeled properties
    -> generic inspector discovers dynamic metadata and readable values
    -> Siemens-free normalizer creates closed typed values
    -> worker returns NetworkObjectInspectionInfo
    -> host validates the declared payload type
    -> shared budget gate and canonical MCP result
```

## Component Boundaries

The detailed implementation plan will preserve these responsibilities:

- shared contract DTOs: selector evidence, list pages, inspection results, attribute
  metadata, typed values, and diagnostics;
- host request DTOs: strict discriminated selectors and operation-specific inputs;
- Network catalog: required, optional, inapplicable, and cross-field validation;
- Network payload contract: one declared result type and invariant validator per
  operation;
- worker selector resolver: Siemens-object traversal and exact match semantics;
- communication reader: lightweight connection discovery only;
- object adapters: modeled properties and canonical current evidence per kind;
- engineering-attribute inspector: dynamic metadata and access-aware reads;
- value normalizer: Siemens-free CLR-to-contract conversion; and
- live harness: raw probe capture, normalized inspection, measurements, and evidence
  matrix.

Siemens-dependent shells remain thin. Pure selector, merge, normalization, validation,
cursor, and contract logic is separated so it can be tested without a live TIA process.

## Testing Strategy

Implementation follows TDD. Every behavior change starts with the narrowest failing
test before production code.

### Pure tests

Cover:

- strict discriminated selector shapes;
- exact device-item path verification;
- interface ownership;
- existing node, subnet, and IO-system identity rules;
- connection index plus evidence verification;
- missing, duplicate, unreadable, reordered, renamed, and drifted evidence;
- modeled/dynamic attribute merging;
- request-order and ordinal-name ordering;
- access and availability mapping;
- null, scalar, enum, and unrepresentable value normalization;
- cursor filter binding and snapshot invalidation.

### Contract and protocol tests

Cover:

- exact camelCase JSON names and unknown-member rejection;
- non-null collection semantics;
- serialize-deserialize-serialize stability;
- one declared payload type for each of four read operations;
- malformed-success `protocol_error` behavior without payload leakage;
- identical canonical text and `structuredContent`;
- mixed success and failure in an actual MCP batch;
- whole-value payload omission and retry guidance; and
- unchanged top-level MCP tool count.

### Stub build

The serialized Release stub build verifies source compatibility, project wiring, and
host/worker contracts. It does not verify live attribute availability, selector
behavior, communication compositions, or runtime values.

### Read-only live TIA harness

The minimum live matrix contains:

- one nested device item;
- one network interface;
- one Ethernet node;
- one Ethernet subnet;
- one PROFINET IO system; and
- at least one configured communication connection.

For each object, the harness records raw attribute metadata, access mode, supported
types, CLR value type, normalized value, availability, and exception outcome. It also:

- compares two unchanged reads for canonical stability;
- verifies discovery selectors resolve through inspection;
- compares full hardware discovery with representative bounded list queries;
- evaluates the `list_network_objects` retention gate; and
- produces an object-kind evidence matrix.

PROFIBUS/DP and unrepresented communication-connection types remain explicitly
live-unverified until fixtures exist. The common contract may stabilize after the
minimum matrix passes, while each unobserved kind remains separately documented as
unverified.

The live harness performs no write, project save, compile, download, or commissioning
operation.

## Documentation

Phase 3 implementation updates:

- `docs/NETWORK_OPERATIONS_ROADMAP.md` with Phase 3 completion and the final retention
  decision for bounded listing;
- `docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md` with request/result examples,
  selector stability, availability semantics, and the live evidence matrix;
- `docs/ARCHITECTURE.md` with the typed-adapter plus generic-inspector worker seam; and
- the live harness usage documentation with its read-only authorization boundary.

Documentation must distinguish installed metadata, static/stub verification, live TIA
engineering evidence, and hardware commissioning. It must not describe a provisional or
unobserved object kind as live-verified.

## Stabilization Gate

Phase 3 is not stable merely because tests and the stub build pass. Stabilization
requires:

1. All static, FakeWorker, protocol, regression, and scoped coverage gates pass.
2. The minimum read-only live matrix completes.
3. Repeated unchanged discovery is canonically deterministic.
4. Selector output resolves every selectable live fixture object exactly.
5. Attribute metadata and value normalization are revised from captured V21 evidence.
6. `list_network_objects` is either retained with recorded material value or removed
   completely.
7. Remaining unverified object and connection kinds are listed explicitly.
8. No Phase 4 through Phase 6 write capability enters the public surface.
