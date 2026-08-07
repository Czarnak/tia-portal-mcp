# Network Operations Phase 2 Structured JSON Contract Design

Status: Approved in conversation on 2026-08-02; awaiting review of this written specification.

This document is the detailed design for Phase 2 of
`docs/NETWORK_OPERATIONS_ROADMAP.md`. It supersedes Phase 1's intentionally
provisional network result shape and settles the roadmap's reserved Phase 2
contract decisions. It is not an implementation plan.

## Objective

Replace escaped, string-valued network operation results with a stable,
single-layer JSON contract that an MCP client can inspect and transform without
parsing nested JSON text.

The implementation must introduce reusable, opt-in host infrastructure so other
tools can adopt structured output later. Phase 2 migrates only `network_read` and
`network_write`; it does not force a repository-wide output migration.

Phase 2 also replaces `configure_network_device`'s first-interface/first-node
selection with explicit node, subnet, and IO-system identity. This is required for
multi-homed devices such as PC stations with separate PLC-facing and client-network
ports.

## Approved Decisions

1. Phase 1 network output is provisional and may be broken cleanly in Phase 2.
2. Shared JSON infrastructure belongs outside the Network namespace and is opt-in.
3. Network tools return MCP `structuredContent` plus an identical compact JSON text
   mirror.
4. Worker success payloads are validated against operation-specific CLR contracts;
   malformed payloads never fall back to strings.
5. Canonical JSON is repository-defined and deterministic, but does not claim RFC
   8785 compliance.
6. Structured payload budgets omit complete values instead of truncating JSON.
7. Network safety tokens bind canonical targets, requested operations, and typed
   hardware state.
8. `configure_network_device` uses an explicit `target` plus `changes` request.
9. The authoritative endpoint selector is the Openness `NodeId`, scoped by device.
10. Subnets are selected by `SubnetId`; IO systems are selected by subnet identity
    plus modeled IO-system number.
11. There is no fallback to the first interface, first node, or a name-only network
    object selector.
12. Writes remain sequential, stop after the first failure, and have no rollback.
13. Post-write verification remains an explicit follow-up `network_read`, not a
    hidden read inside `network_write`.
14. Non-network tools retain their current text output contracts in this phase.
15. Worker IPC remains newline-delimited JSON and the two-process architecture is
    unchanged, but selector and identity fields intentionally extend the shared
    worker request/result contracts.

## Current State

Phase 1 introduced:

- `network_read` for `read_hardware_config` and `search_equipment_catalog`;
- self-previewing `network_write` for `add_network_device` and
  `configure_network_device`;
- a strict `NetworkOperationRequest`;
- an independent Network domain over the shared `OperationBatches` kernel; and
- canonical preview/apply ordering, single-use tokens, state binding, sequential
  writes, and auditing.

The remaining Phase 1 result defect is deliberate: `OperationBatchResult.Result` is
a `string`, `OperationBatchExecutionEngine` calls `WorkerCallResult.ToText()`, and
`OperationBatchResultFormatter` serializes the string again. Successful worker JSON
therefore appears as escaped JSON inside the tool's outer JSON document.

The current `configure_network_device` worker implementation also finds a device by
name, then selects the first network interface and its first node. That is not a safe
contract for a multi-homed device.

## Scope

Phase 2 includes:

- shared canonical JSON validation and serialization;
- shared MCP structured-result construction;
- structured operation-batch result and budget contracts;
- typed safety-preview support and canonical safety binding;
- `network_read` and `network_write` structured output schemas;
- operation-specific network payload validation;
- explicit network node, subnet, and IO-system identity;
- exact host-to-worker forwarding for the revised selector;
- protocol-level, FakeWorker, unit, safety, schema, coverage, and documentation
  gates; and
- a PowerShell 7 live-acceptance harness with read-only/preview-only defaults.

Phase 2 does not add subnet lifecycle operations, IO-system attribute editing,
generic scalar attribute operations, automatic compile/save, transactions,
rollback, download, or commissioning behavior.

## Architecture and Ownership

### `CanonicalJson`

`TiaMcpServer/Json/CanonicalJson.cs` owns JSON contract validation and canonical
representation. It has no dependency on Network, batching, safety, or MCP tool
classes.

It provides two conceptual operations:

- validate a worker payload through an expected CLR type and return a detached
  canonical `JsonElement`; and
- serialize an owned CLR value into canonical text and a matching detached
  `JsonElement`.

The canonical text is generated once. All hashes, structured content, text mirrors,
length measurements, and audit representations use that owned representation.

### `StructuredToolResult`

`TiaMcpServer/Tools/StructuredToolResult.cs` owns conversion from a typed response to
the SDK's `CallToolResult`.

It must:

- populate `CallToolResult.StructuredContent`;
- add exactly one JSON `TextContentBlock` containing the same canonical document;
- set `CallToolResult.IsError` according to the tool-level error policy; and
- prevent structured content and compatibility text from being produced by
  independent serializers.

The network tools opt into SDK structured output with
`UseStructuredContent = true` and explicit output-schema types. Other tools do not
change until a later migration.

### Shared operation batches

`TiaMcpServer/OperationBatches/` owns:

- structured per-item results;
- typed failure, omission, and truncation metadata;
- read/apply batch bodies and counts;
- conversion of `WorkerCallResult` plus a domain payload decoder into an effective
  operation result; and
- structured response budgeting.

The execution engine remains generic over request type. The Network domain supplies
the decoder that maps each operation name to its expected result type.

### Safety

`TiaMcpServer/Safety/` gains typed, canonical preview/validation entry points.
Existing text-returning entry points remain for non-network tools and retain their
current output behavior.

The new entry points share token storage, lifetime, single-use semantics, validation
ordering, and audit infrastructure with the existing service. They do not create a
Network-specific safety service.

### Network domain

`TiaMcpServer/Network/` owns:

- `NetworkReadResponse` and `NetworkWriteResponse` output schema types;
- the strict nested configure target/change request types;
- the operation-to-result-type registry;
- selector preflight against typed `HardwareConfigInfo`; and
- network-specific projection of resolved targets into preview evidence.

Generic JSON parsing, canonicalization, MCP result construction, batching, and token
mechanics do not live in the Network namespace.

### Worker boundary

The worker transport remains newline-delimited JSON. The following shared contracts
are extended:

- node identity (`NodeId`, node type);
- subnet identity (`SubnetId`, network type);
- IO-system number;
- configure target node ID;
- configure subnet ID; and
- configure IO-system subnet ID and number.

`OpennessWorkerClient`, `WorkerRequest`, worker dispatch, `HardwareConfigReader`, and
`NetworkDeviceConfigurator` forward or consume those exact fields. The public nested
request may be flattened at the internal worker DTO boundary, but no selector may be
dropped or inferred.

## Canonical JSON Contract

Canonicalization follows this sequence:

1. Parse strict JSON with comments and trailing commas disabled.
2. Reject duplicate properties at every object depth.
3. Deserialize through the operation's expected CLR type.
4. Require case-sensitive camelCase member names.
5. Reject unknown members and incompatible JSON types.
6. Serialize with explicit nulls and non-null collection defaults.
7. Recursively sort object properties using ordinal property-name order.
8. Preserve array order.
9. Emit compact JSON.
10. Parse that exact canonical text into a detached `JsonElement`.

The typed projection normalizes primitive representation through the declared CLR
type. Arrays remain ordered because operation order and hardware composition order
are safety-significant. Formatting-only object-property order is not significant.

The implementation must not expose borrowed `JsonDocument` elements whose owning
document has been disposed.

## Public Result Contract

### Shared operation item

Every executed or represented operation uses this logical shape:

```json
{
  "operationId": "hardware",
  "operation": "read_hardware_config",
  "status": "succeeded",
  "result": {},
  "failure": null,
  "omission": null,
  "skipReason": null,
  "warnings": []
}
```

Rules:

- `result` is a JSON value, never JSON encoded as a string.
- A successful JSON null is `status: "succeeded"` plus `result: null`.
- A failed item has `result: null` and a `failure` containing `category` and
  `message`.
- An omitted item has `result: null` and structured retry guidance in `omission`.
- A skipped write has `result: null` and
  `skipReason: "earlierOperationFailed"`.
- `warnings` is always an array.
- Fields not applicable to the current status are explicit JSON nulls.

### Shared batch body

```json
{
  "operationCount": 2,
  "counts": {
    "succeeded": 2,
    "failed": 0,
    "omitted": 0,
    "skipped": 0
  },
  "operations": [],
  "truncation": null
}
```

Counts must be derived from the final presented items after budgeting, not from the
pre-budget list.

### `network_read`

```json
{
  "tool": "network_read",
  "success": true,
  "batch": {
    "operationCount": 1,
    "counts": {
      "succeeded": 1,
      "failed": 0,
      "omitted": 0,
      "skipped": 0
    },
    "operations": [],
    "truncation": null
  },
  "error": null
}
```

A request/access failure has `batch: null` and a typed top-level `error`. A valid
batch containing failed or omitted operations has `success: false`, retains the
batch, and is not an MCP tool error.

### `network_write`

`network_write` uses one discriminated response envelope:

```json
{
  "tool": "network_write",
  "phase": "preview",
  "success": true,
  "preview": {
    "target": [],
    "summary": "Apply one network write operation.",
    "currentStateHash": "...",
    "requestedInputHash": "...",
    "expiresAtUtc": "...",
    "safetyToken": "...",
    "diff": null,
    "instructions": "..."
  },
  "batch": null,
  "error": null
}
```

- `phase: "preview"` sets only `preview`.
- `phase: "apply"` sets only `batch`.
- `phase: "error"` sets only `error`.
- Inactive branches remain explicit nulls.

Top-level errors use the existing `WorkerFailureCategories` vocabulary. Phase 2 adds
`protocol_error` for a valid worker response envelope whose successful payload
violates the expected contract.

## MCP Error Policy

MCP `isError: true` is reserved for a whole-tool failure before a usable batch result
can be returned, including:

- invalid request shape or access mode;
- invalid preview/apply token arguments;
- preview-state read or decode failure;
- token binding, expiry, consumption, or state failure; and
- inability to construct the declared response contract.

An executed batch item's worker or payload-contract failure is item-level evidence.
The response has `success: false`, contains the batch, and uses MCP `isError: false`.

Error text must not include rejected worker payloads, stack traces, Siemens internal
details beyond already-approved worker messages, or secrets.

## Network Payload Types

Successful worker payloads are decoded as:

| Operation | Expected result type |
| --- | --- |
| `read_hardware_config` | `HardwareConfigInfo` |
| `search_equipment_catalog` | array of `CatalogEntryInfo` |
| `add_network_device` | `AddDeviceResultInfo` |
| `configure_network_device` | `ConfigureNetworkDeviceResultInfo` |

This typed gate locks property names, nulls, collections, numbers, booleans, objects,
and arrays. It also detects host/worker contract drift.

Existing `messages` and `warnings` retain unavailable-read evidence. Phase 2 does not
infer a value from missing data. Later generic attribute operations must use an
explicit value/access-state contract rather than overloading null or a string.

## Configure Request Contract

`configure_network_device` changes from flat selector/change fields to explicit
nested objects:

```json
{
  "operationId": "configure-pc-plc-port",
  "operation": "configure_network_device",
  "projectPath": "C:\\Projects\\Plant.ap21",
  "target": {
    "deviceName": "PC_1",
    "nodeId": "node-id-from-network-read"
  },
  "changes": {
    "ipAddress": "192.168.0.20",
    "subnetMask": "255.255.255.0",
    "pnDeviceName": "pc-plc-side",
    "subnet": {
      "subnetId": "plc-subnet-id"
    },
    "ioSystem": {
      "subnetId": "plc-subnet-id",
      "number": 100
    }
  }
}
```

Contract rules:

- `target` and `changes` are required for `configure_network_device` and forbidden
  for the other three Phase 2 operations.
- `target.deviceName` and `target.nodeId` are required, nonblank strings.
- At least one supported change must be present; an empty change object is rejected
  as a no-op.
- A null or absent change means "do not change"; Phase 2 adds no clear/disconnect
  semantics.
- `changes.subnet` selects a subnet by nonblank `subnetId`.
- `changes.ioSystem` requires a nonblank `subnetId` and an IO-system number.
- If both subnet and IO-system targets are present, their subnet IDs must match.
- Unknown nested fields and fields inapplicable to the selected operation are
  rejected.
- Legacy flat configure fields are removed without aliases.

`add_network_device` retains `typeIdentifier`, `deviceName`, and optional
`deviceItemName` because it identifies a new object, not an existing endpoint.

## Hardware Identity Contract

`read_hardware_config` exposes enough evidence to select an exact endpoint:

- `NodeInfo.nodeId` and node type;
- `SubnetInfo.subnetId` and network type; and
- `IoSystemInfo.number`.

The existing device/item/interface/node hierarchy remains the human-readable
location model. The preview target expands the authoritative IDs back into:

- canonical device name and type identifier;
- device-item path;
- network-interface name;
- node name and node ID;
- selected subnet name and ID, when present; and
- selected IO-system name and number, when present.

The human-readable fields are evidence, not alternative selectors.

## Selector Resolution

### Device and node

The resolver:

1. matches exactly one project device by the worker's existing case-insensitive name
   semantics;
2. traverses every readable device item and network interface under that device;
3. matches exactly one node by `NodeId`; and
4. returns the node plus its canonical location evidence.

Zero or multiple matches fail closed. A missing/unreadable identity needed to prove
the match produces `postcondition_failed`. There is no first-object fallback.

### Subnet

When requested, exactly one project subnet must match `SubnetId`. The name is read
for preview evidence but does not participate in identity.

### IO system

When requested, exactly one IO system must match within the selected subnet context
using subnet identity plus modeled IO-system number. Its name is presentation
evidence only.

### Multi-homed devices

A device may expose any number of interfaces and nodes. The host and worker accept
that topology and operate only on the selected node ID. A multi-homed PC station is
an acceptance fixture, not an error case.

## Data Flow

### Read

1. Validate the request and access mode.
2. Invoke each worker operation independently.
3. Preserve an ordinary worker failure and its warnings.
4. Decode worker success through the registered result type.
5. Convert a decode/contract failure into item-level `protocol_error`.
6. Continue later read items.
7. Apply structured budgeting.
8. Build the typed response and one canonical MCP result.

### Write preview

1. Validate the request, access mode, confirm flag, and token argument.
2. Resolve the common project path and requested target descriptions.
3. Read hardware state once.
4. Decode it as `HardwareConfigInfo`.
5. Resolve every node/subnet/IO-system selector against that state.
6. Reject incomplete or ambiguous selector evidence before issuing a token.
7. Canonicalize resolved targets, ordered requests, and hardware state.
8. Create the typed preview and token.
9. Return the structured preview response.

### Write apply

1. Validate the token envelope against canonical targets and ordered requested input.
2. Read and decode fresh hardware state.
3. Validate and consume the token against canonical state.
4. Resolve the same exact selectors defensively against the validated state.
5. Execute writes sequentially.
6. Validate each successful worker payload before allowing the next write.
7. Treat invalid success payload as `protocol_error`; the mutation may already have
   occurred, so stop and skip later operations.
8. Build and canonicalize the apply response.
9. Append the audit record.
10. Return all batch evidence.

## Safety Binding

Network preview/apply binds:

- canonical ordered resolved targets;
- canonical ordered `NetworkOperationRequest[]`;
- normalized project path;
- tool name; and
- canonical typed `HardwareConfigInfo` state.

Object-property order differences do not invalidate a token. Changed values, array
order, operation order, target IDs, requested changes, project state, tool, or project
path do invalidate it.

The token remains single-use and expires after ten minutes. Validation and state
checks occur before any mutation. Audit hashes use the same canonical request, state,
and response representations.

## Partial Failure and Transactions

Phase 2 does not introduce Openness transactions or rollback.

- Reads remain independent.
- Writes remain sequential.
- The first worker or payload-contract failure stops later writes.
- Later writes are marked skipped.
- A failed write may already have changed TIA state.
- The result and documentation must never claim atomicity or unchanged state.

This settles the roadmap's transaction and partial-failure decision for the Phase 2
surface.

## Payload Budget

The logical limits remain:

- 60,000 characters per operation result; and
- 180,000 characters per canonical batch document.

Budgeting measures the exact canonical JSON document. It must not substring a JSON
result.

An oversized successful result becomes:

- `status: "omitted"`;
- `result: null`; and
- structured omission metadata with reason, applicable limit, retry tool, and
  narrowing guidance.

When the combined document is too large, complete successful payloads are omitted
while operation order is retained. Failed-operation evidence has priority over
successful results.

Long failure/warning strings may be shortened without breaking JSON. The contract
records truncation flags, original character counts, omitted warning counts, and
affected operation IDs. `batch.truncation` summarizes every alteration.

The compatibility text and `structuredContent` each carry the same bounded logical
document. Their transport duplication is intentional for client compatibility.

## Postcondition Flow

`network_write` does not perform a hidden post-write hardware read. The explicit agent
flow is:

```text
network_read snapshot
        -> select target IDs and intended changes
        -> network_write preview
        -> network_write apply
        -> network_read post-state
        -> compare intended fields
```

The apply result reports what the worker returned. The post-read remains an explicit,
independently visible operation so a read failure cannot hide or rewrite the actual
write outcome.

## Typed Operations Versus Future Generic Attributes

Phase 2 retains typed operations for device creation and endpoint configuration. It
does not add generic network attribute writes.

Future generic attribute operations may reuse `CanonicalJson`,
`StructuredToolResult`, operation batches, and canonical safety. Their domain contract
must additionally encode:

- the typed value;
- availability state;
- readable/writable access metadata;
- target identity; and
- rejected unknown or non-writable attributes.

They must not collapse types or availability states into undifferentiated strings.

## Testing Strategy

### First failing test

The first production-independent test calls the real MCP protocol against the
FakeWorker and proves the Phase 1 defect:

- network tools have no usable structured response contract;
- `structuredContent` is absent; or
- a successful operation result is escaped JSON text rather than an object/array.

Production work starts only after that failure is observed.

### Canonical JSON tests

Cover:

- recursive property ordering;
- array-order preservation;
- primitive and explicit-null preservation;
- duplicate-member rejection;
- unknown, incorrectly cased, missing, and incorrectly typed member rejection;
- detached `JsonElement` lifetime; and
- canonicalize/parse/canonicalize stability.

### Structured MCP tests

Through an actual `McpClient` call:

- `tools/list` exposes output schemas for both network tools;
- non-network output schemas remain unchanged;
- `tools/call` returns `structuredContent`;
- the single text block is the same canonical document;
- read, preview, apply, partial-failure, and error fixtures match exact property
  names and null/collection shapes; and
- MCP `isError` follows the approved policy.

### Operation-batch tests

Cover:

- independent reads after worker and protocol errors;
- conversion of invalid success payloads to `protocol_error`;
- write stop/skip after such a failure;
- warning propagation;
- final counts after budgeting; and
- structured omission/truncation behavior within exact limits.

### Safety tests

Cover:

- property-order-only state changes accepted;
- changed values and array order rejected;
- operation reorder, target-ID changes, and change-value changes rejected;
- preview/apply canonical projection parity;
- expiry and single use;
- exact audit hashes; and
- no worker mutation before complete validation.

### Identity and forwarding tests

Cover:

- node ID/type, subnet ID/type, and IO-system number round-trip;
- strict nested target/change JSON;
- exact forwarding through host client, `WorkerRequest`, worker dispatch, and worker
  call;
- missing/duplicate/unreadable identity rejection;
- removal of all first-interface/first-node fallback paths; and
- selector-rich preview evidence.

### Multi-homed acceptance fixture

The FakeWorker fixture contains a PC station with:

- one node connected to the PLC subnet; and
- one node representing the client/database network.

The protocol test reads both nodes, selects the PLC-facing node ID, previews the
resolved location, applies the changes, reads again, and proves the database-facing
node is unchanged.

### Read-to-write contract test

The protocol test performs:

```text
network_read -> select nodeId -> construct target + changes
-> preview -> apply -> network_read -> compare
```

It also rejects unknown fields, wrong JSON types, read operations passed to
`network_write`, changed preview input, missing identity, and ambiguous identity.

### Regression tests

Prove:

- non-network tools retain current text output;
- generic batches still reject the four dedicated network operations;
- tool registration remains fourteen tools in read-write mode and four in read-only
  mode;
- access-mode enforcement is unchanged;
- audit/safety invariants remain covered; and
- test-project linked-source declarations include every new host file explicitly.

## Documentation Changes

Implementation updates:

- `README.md` with structured output and configure-target examples;
- `docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md` with exact Phase 2
  contracts and explicit post-read guidance;
- `docs/NETWORK_OPERATIONS_ROADMAP.md` with Phase 2 completion status and settled
  reserved decisions;
- `docs/ARCHITECTURE.md` with opt-in shared JSON/MCP result infrastructure and the
  revised worker selector boundary; and
- repository agent instructions with the reusable structured-result contract,
  canonical safety rule, and prohibition on nested result JSON.

The documentation must say that the infrastructure is reusable by future tools even
though Phase 2 migrates only Network.

## Verification and Completion Gates

Required automated evidence:

1. Focused red/green evidence for each TDD task.
2. Actual MCP `tools/list` and `tools/call` contract tests.
3. Exact canonical fixture drift tests.
4. Serialized Release stub build:

   ```powershell
   dotnet build TiaMcpServer.sln --no-restore -m:1 --configuration Release /p:UseTiaPortalReferenceStubs=true
   ```

5. Full test suite.
6. Scoped line coverage at or above 80%.
7. `git diff --check`.
8. Tool-count, generic-removal, access-mode, and audit/safety regression gates.
9. Review that unrelated worker operations and Siemens-facing domains did not change.

Passing these gates proves the host protocol contract and stub-compiled worker code.
It does not prove live TIA V21 behavior.

## Live TIA Acceptance Boundary

A committed PowerShell 7 harness supports:

- read-only inspection of node/subnet/IO-system identity;
- preview-only validation of an explicitly selected multi-homed endpoint; and
- an apply/post-read run only with separate user authorization, a user-supplied test
  project, and user-approved safe values.

No live TIA operation runs during ordinary automated verification. Static/stub
acceptance and live Openness acceptance are reported separately. Phase 2 may be
implemented and statically accepted while the live selector/write evidence remains an
explicit commissioning gate.

## Non-Goals

Phase 2 does not:

- migrate generic batch, lifecycle, PLC, HMI, diagnostics, or standalone tool output;
- change the two-process architecture or newline-delimited worker transport;
- add compatibility aliases for the old flat configure request;
- infer identities from list position or presentation names;
- add subnet creation/deletion;
- add IO-system attribute editing;
- add generic attribute reads/writes;
- add automatic project save, compile, download, or commissioning;
- add hidden post-write reads;
- introduce transactions or rollback; or
- claim live V21 correctness from tests or a stub build.
