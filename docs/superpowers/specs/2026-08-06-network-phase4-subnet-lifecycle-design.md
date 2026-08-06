# Network Operations Phase 4 Subnet Lifecycle Design

## Objective

Add first-class Ethernet and PROFIBUS subnet creation, update, and deletion to
the existing `network_write` tool. The implementation must preserve the
canonical preview/apply safety flow, use TIA Portal Openness transactions for
each subnet operation, verify subnet-specific postconditions, and expose only
the minimal result an agent needs for subsequent subnet operations.

Phase 4 is strictly about subnet lifecycle. Devices, device items, nodes, IO
systems, and communication connections are not lifecycle targets in this
phase.

## Evidence Basis

The design is based on the completed TIA Portal V21 probes under
`artifacts/live-network-phase4/`:

- The read-only probe confirmed selectable Ethernet and PROFIBUS subnets,
  stable `subnetId` values, modeled and dynamic attribute metadata, CLR value
  types, and current relationships.
- The mutation probe confirmed creation and editing of isolated Ethernet and
  PROFIBUS subnets.
- `HighestAddress = 125` and `TransmissionSpeed = Baud1500000` persisted on an
  isolated PROFIBUS subnet.
- An invalid subnet type identifier and `HighestAddress = -1` were rejected by
  Openness.
- An empty subnet name was accepted by Openness, so the application must reject
  blank names before calling Siemens APIs.
- Transaction rollback and committed persistence were both observed.
- Empty and connected Ethernet and PROFIBUS subnets were deleted successfully.
- Deleting connected subnets did not delete project devices. It cleared the
  corresponding subnet and IO-system relationships, which is an expected
  consequence of deleting the subnet.

Static builds, contract tests, and FakeWorker tests remain separate from this
live evidence. The probes do not by themselves verify the future public MCP
operation path.

## Approved Decisions

- Extend `network_write`; do not add another public MCP tool.
- Add three explicit operations: `create_subnet`, `update_subnet`, and
  `delete_subnet`.
- Support Ethernet and PROFIBUS only.
- Use `subnetId` as the exact selector for existing subnets.
- Treat `subnetId` as public read-only identity even though Openness metadata
  reports it as writable for the observed subnet types.
- Permit deletion of both empty and connected subnets.
- Do not enumerate dependent communication connections or reject deletion
  because dependency identity is incomplete.
- Do not expose connected nodes, IO systems, device identities, network
  attribute differences, or other dependency detail in subnet mutation
  results.
- Verify that the root project-device count is unchanged for every subnet
  operation.
- Keep project save and hardware compile outside the subnet operation.
- Preserve the existing ordered-batch behavior: operations execute
  sequentially and the batch stops on the first failure.

## Approaches Considered

### Three explicit operations in `network_write` - selected

Explicit create, update, and delete operations provide clear schemas,
operation-specific validation, deterministic target resolution, and readable
audit records while reusing the established safety and worker infrastructure.

### One `mutate_subnet` operation - rejected

An action discriminator inside one mutation operation would reduce the number
of catalog entries but produce a more conditional request schema and less
specific validation messages. It would duplicate the existing ordered batch's
role as the composition mechanism.

### A separate `subnet_write` tool - rejected

A separate tool would duplicate the approved preview/apply surface, safety
token binding, audit behavior, and access-mode registration without creating a
meaningful domain boundary.

### Generic writable subnet attributes - deferred

Metadata-driven generic network-attribute writes remain Phase 6 work. Phase 4
uses only the small typed attribute set exercised by the mutation probe.

## Scope

### Supported subnet types

| Public value | Openness type identifier |
| --- | --- |
| `Ethernet` | `System:Subnet.Ethernet` |
| `Profibus` | `System:Subnet.Profibus` |

The public values match the existing `SubnetInfo.NetworkType` vocabulary.

### Supported writable fields

| Field | Ethernet | PROFIBUS | Notes |
| --- | --- | --- | --- |
| `name` | create/update | create/update | Nonblank; preserved exactly |
| `highestAddress` | rejected | create/update | Integer from 0 through 126 |
| `transmissionSpeed` | rejected | create/update | Closed V21 `BaudRate` symbol |

The accepted `transmissionSpeed` values are:

- `Baud9600`
- `Baud19200`
- `Baud45450`
- `Baud93750`
- `Baud187500`
- `Baud500000`
- `Baud1500000`
- `Baud3000000`
- `Baud6000000`
- `Baud12000000`

The enum member `None` is not a valid requested transmission speed.

### Explicit non-goals

Phase 4 does not support:

- changing an existing subnet's network type;
- setting or changing `subnetId`;
- Ethernet `DefaultSubnet`;
- PROFIBUS `BusProfile`, isochronous settings, cable configuration, cyclic
  distribution, or other `Pb*` attributes;
- integrated PROFIBUS or PROFIdrive networks;
- attaching, detaching, creating, editing, or deleting nodes or IO systems;
- communication-connection creation, editing, or deletion;
- generic dynamic-attribute writes;
- automatic project save;
- automatic hardware compile; or
- download, commissioning, or online validation.

## Public Request Contract

The existing strict `NetworkOperationRequest` remains the public batch item.
Unknown JSON members remain rejected. Phase 4 adds two operation-specific
nested objects rather than adding every subnet field at the top level.

### Create subnet

```json
{
  "operationId": "create-pb-2",
  "operation": "create_subnet",
  "projectPath": "C:\\Projects\\Plant.ap21",
  "subnet": {
    "name": "PROFIBUS_2",
    "networkType": "Profibus",
    "highestAddress": 125,
    "transmissionSpeed": "Baud1500000"
  }
}
```

`subnet.name` and `subnet.networkType` are required. PROFIBUS settings are
optional. When supplied, they are set after `Project.Subnets.Create(...)` and
before the same transaction is committed. If any requested setting fails, the
new subnet is rolled back.

### Update subnet

```json
{
  "operationId": "rename-pb-2",
  "operation": "update_subnet",
  "projectPath": "C:\\Projects\\Plant.ap21",
  "target": {
    "kind": "subnet",
    "subnetId": "590-5"
  },
  "subnetChanges": {
    "name": "PROFIBUS_LINE_2",
    "highestAddress": 125,
    "transmissionSpeed": "Baud1500000"
  }
}
```

`target.kind` must be `subnet`, `target.subnetId` is required, and at least one
member of `subnetChanges` must be present. An omitted member means leave the
attribute unchanged.

The target subnet's current network type determines which fields are
applicable. The request cannot supply a replacement network type.

### Delete subnet

```json
{
  "operationId": "delete-pb-1",
  "operation": "delete_subnet",
  "projectPath": "C:\\Projects\\Plant.ap21",
  "target": {
    "kind": "subnet",
    "subnetId": "590-3"
  }
}
```

Deletion requires only the exact subnet selector. Connected nodes and IO
systems do not make the request invalid.

### Request DTO ownership

The host adds:

- `NetworkSubnetDefinition` for create input;
- `NetworkSubnetChanges` for update input;
- `NetworkOperationRequest.Subnet`; and
- `NetworkOperationRequest.SubnetChanges`.

Both nested DTOs disallow unmapped JSON members. The operation catalog owns
required, optional, inapplicable, cardinality, range, enum, and create-time
type-specific validation. Update-time type applicability is checked after the
target subnet is resolved from the current hardware snapshot.

## Validation Rules

Static request validation occurs at the host boundary before worker startup.
Validation that depends on an existing subnet's current type occurs during host
target resolution before token creation or consumption. All mutation-relevant
validation is repeated at the worker boundary before an Openness call.

### Common rules

- `operationId` remains unique and nonblank within the batch.
- All write items continue to use one normalized project path.
- Names containing only whitespace are rejected.
- A nonblank name is preserved exactly; the server does not trim or silently
  normalize it.
- Unknown network types, fields, and transmission-speed symbols are rejected.
- A target subnet must resolve exactly once by ordinal `subnetId` equality.
- Presentation names are never fallback selectors.

### Create rules

- `subnet` is required.
- `subnet.name` is required and nonblank.
- `subnet.networkType` must be `Ethernet` or `Profibus`.
- `highestAddress` and `transmissionSpeed` are rejected for Ethernet.
- Duplicate-name behavior is left to TIA Portal because it was not established
  by the probe. The server does not invent a uniqueness rule.

### Update rules

- `target` and `subnetChanges` are required.
- `target.kind` must be `subnet`.
- `target.subnetId` is required and nonblank.
- At least one update member must be supplied.
- `highestAddress` and `transmissionSpeed` are rejected when the resolved
  subnet is not PROFIBUS.

### Delete rules

- `target` is required.
- `target.kind` must be `subnet`.
- `target.subnetId` is required and nonblank.
- No dependency-completeness or empty-subnet condition is imposed.

## Preview and Safety Binding

The existing `network_write` preview/apply protocol remains unchanged:

1. Preview reads the current `HardwareConfigInfo` snapshot.
2. Each requested target is resolved against that snapshot in request order.
3. The exact operations, resolved targets, normalized project path, and current
   state are bound into the canonical safety token.
4. Apply re-reads state and resolves targets again immediately before consuming
   the token.
5. Any request, order, project path, target, or state mismatch invalidates the
   token.

For `create_subnet`, target evidence contains the requested subnet name and no
`subnetId`. For `update_subnet` and `delete_subnet`, target evidence contains
the current subnet name and exact `subnetId` from the snapshot.

`NetworkWriteTargetEvidence.DeviceName` becomes nullable so a subnet target can
be represented without inventing a device identity. Existing device operation
responses keep their current non-null values.

Preview does not enumerate connected nodes, IO systems, communication
connections, or affected device attributes. Connection identity completeness
does not participate in subnet preview or token validation.

## Host Architecture

### Operation catalog

`NetworkOperationCatalog` registers the three write operations and validates
the nested subnet request shapes. Existing category, batch-size, project-path,
and inapplicable-field behavior remains shared.

### Target resolution

A focused subnet lifecycle resolver extends the current
`NetworkIdentityResolver` dispatch:

- create target evidence is derived from the request;
- update/delete target evidence is resolved from `HardwareConfigInfo.Subnets`;
- zero or multiple matches fail closed; and
- connected relationship data is ignored.

### Worker transport

`NetworkWorkerInvoker` dispatches the three public operations through explicit
`OpennessWorkerClient` methods. Worker transport fields are:

- `SubnetId` for update/delete;
- `SubnetName`;
- `SubnetNetworkType` for create;
- `SubnetHighestAddress`; and
- `SubnetTransmissionSpeed`.

Every worker write request sets `Confirm = true` and uses the existing bound
project transport.

### Typed payload projection

`NetworkPayloadContract` maps each of the three operation names to the shared
`SubnetLifecycleResultInfo` CLR type. A worker success payload that does not
decode and validate as that type becomes `protocol_error` and is not echoed.

## Worker Architecture

### Dispatch and authorization

The worker registers `create_subnet`, `update_subnet`, and `delete_subnet` as
project mutations in `OperationPolicyCatalog`. Read-only mode rejects them
before dispatch and before any Siemens API call.

`Program` adds three confirmed handlers. Because Openness transactions require
the active `TiaPortal` instance as well as `Project`, subnet handlers use a
focused session helper that supplies both objects after enforcing the existing
project binding.

### Subnet lifecycle service

A production `SubnetLifecycleService` owns:

- exact subnet lookup by dynamic `SubnetId`;
- mapping `Ethernet` and `Profibus` to their Openness type identifiers;
- runtime conversion of the transmission-speed symbol to the actual V21
  `BaudRate` enum type;
- creation through `project.Subnets.Create(typeIdentifier, name)`;
- updates through modeled `Name` and dynamic `HighestAddress` and
  `TransmissionSpeed` writes;
- deletion through `Subnet.Delete()`;
- transaction boundaries; and
- focused postcondition reads.

Each public operation executes under one `ExclusiveAccess` and one
`Transaction`. `CommitOnDispose()` is called only after all requested Siemens
calls succeed. An exception before that call rolls back the operation.

The service does not automatically retry an Openness mutation. An unknown
outcome after a worker transport failure requires a fresh subnet read before a
caller decides whether to retry.

### Save and compile boundary

The service does not call `Project.Save()`. Saving remains an explicit project
lifecycle operation after the caller accepts the mutation result.

Hardware compilation is not a subnet-operation postcondition. Deleting a
connected subnet intentionally clears network and IO-system relationships, so
compile diagnostics caused by those disconnections do not mean the subnet
deletion failed. A caller may request compilation separately when evaluating
the resulting hardware configuration.

## Postcondition Contract

Before each individual subnet mutation, the worker captures
`project.Devices.Count`. This is the same root-device collection used by
`HardwareConfigReader.Read` and by the successful mutation probe.

After the transaction is disposed, the worker verifies:

- create: exactly one subnet with the returned `subnetId` exists and its name,
  type, and requested supported attributes match;
- update: exactly one subnet with the target `subnetId` exists and every
  requested supported attribute matches;
- delete: no subnet with the target `subnetId` exists; and
- all operations: `project.Devices.Count` is unchanged.

The worker reads only the target subnet and the root-device count for these
postconditions. It does not build or return dependency inventories.

A postcondition mismatch returns `postcondition_failed`; the operation is not
reported as successful. Because the Siemens transaction may already have
committed before a readback failure, the response follows the existing
inspect-before-retry guidance rather than retrying automatically.

## Minimal Result Contract

Every successful subnet operation returns the same typed shape:

```json
{
  "subnetId": "590-5",
  "name": "PROFIBUS_LINE_2",
  "networkDeviceCount": 10,
  "networkDeviceCountUnchanged": true
}
```

The enclosing structured operation item already carries the operation name, so
the result does not repeat `created`, `updated`, or `deleted`. For deletion, the
name and `subnetId` are the identity captured immediately before deletion. For
creation and update, they are the post-read identity.

The result does not contain:

- network type or changed attribute values;
- before/after device identities;
- connected-node names or counts;
- IO-system names or counts;
- communication connections;
- warnings about expected disconnection; or
- GSD-derived hardware details.

Multiple subnet operations remain separate ordered items in the existing batch
envelope. The envelope, payload budgeting, canonical JSON identity between text
and `structuredContent`, audit behavior, and stop-on-first-failure semantics do
not change.

## Error and Partial-Application Policy

- Public-shape, field, range, type, enum, and confirmation failures use
  `validation_error`.
- Missing or ambiguous current targets fail closed before mutation.
- Recoverable Siemens failures use the existing worker failure mapping.
- A nonrecoverable Siemens failure terminates the worker session and is never
  swallowed by the subnet service.
- Malformed worker success payloads use `protocol_error`.
- Readback mismatches use `postcondition_failed`.
- One subnet operation is transaction-scoped, but the surrounding
  `network_write` batch is not atomic. Earlier successful items remain applied
  if a later item fails.
- The server never retries a subnet mutation automatically.

## GSD-Derived Hardware Observation

The read-only probe returned repeated network-component node names for ABB VFDs
that have distinct names in the source project and were installed through GSD
hardware definitions. This indicates that names or attributes exposed by
GSD-derived components may not be readable through the same standard paths as
native catalog hardware.

This is recorded for later hardware-introspection work only. Phase 4 does not
use component node names or GSD-derived attributes as identity, safety evidence,
postcondition evidence, or public result data.

## Testing Strategy

Implementation follows strict red-green-refactor cycles. Every behavior change
starts with a focused failing test and the failure is observed before production
code is added.

### Request and catalog tests

- JSON round trips for `subnet` and `subnetChanges` with unmapped members
  rejected.
- Operation registration and read/write category enforcement.
- Required and inapplicable fields for all three operations.
- Blank-name, empty-update, type-specific, range, and baud-symbol validation.
- `subnetId` and network-type mutation attempts rejected by contract shape.

### Safety and identity tests

- Create target evidence without an invented device identity.
- Exact update/delete target resolution by `subnetId`.
- Missing and duplicate target failures.
- Safety-token invalidation after request, order, target, project path, or state
  changes.
- Connected relationship data does not affect target resolution or authorize a
  deletion blocker.

### Worker forwarding and authorization tests

- Every public field maps exactly once into `WorkerRequest`.
- `Confirm = true`, project path, and access policy are preserved.
- All three worker methods are denied in read-only mode and allowed in
  read-write mode.
- Worker dispatch reaches only the intended subnet handler.

### Payload contract tests

- All three operation names decode only `SubnetLifecycleResultInfo`.
- Missing, null, malformed, or contradictory result members become
  `protocol_error`.
- The result never exposes dependency or device identity details.
- Text and `structuredContent` remain the same canonical document.

### FakeWorker and protocol tests

- Preview and apply for create, update, and delete.
- Ordered mixed subnet batches.
- Stop-on-first-failure and skipped later operations.
- Device-count postcondition failure.
- Malformed payload handling.
- Payload budget and audit regressions.

### Build and live evidence gates

- Full `TiaMcpServer.Tests` suite.
- Serial stub build with `UseTiaPortalReferenceStubs=true`.
- Serial real V21 build against the installed PublicAPI assemblies.
- PowerShell parser and live-harness contract tests.
- A separately authorized public-tool live acceptance run against a disposable
  project copy. Ordinary tests and CI never execute that live mutation.

The public live run must cover create, update, and delete for Ethernet and
PROFIBUS, including connected deletion, and verify the minimal public result plus
the unchanged root-device count. The already completed internal mutation probe
is supporting evidence, not a substitute for this public-path acceptance run.

## Documentation

After static implementation gates pass:

- update `docs/NETWORK_OPERATIONS_ROADMAP.md` with the settled Phase 4 scope and
  evidence status;
- update `docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md` with request,
  preview/apply, result, validation, transaction, and save/compile boundaries;
- record the public live acceptance separately after it is explicitly
  authorized and run; and
- retain the GSD-derived naming observation as deferred introspection work.

## Acceptance Criteria

Phase 4 implementation is statically complete when:

1. `network_write` exposes the three explicit operations without adding a new
   MCP tool.
2. Requests use exact `subnetId` selectors and the approved typed fields only.
3. Connected deletion is permitted without dependency-identity blocking.
4. Every subnet operation uses `ExclusiveAccess` and an Openness transaction.
5. Supported create and update attributes are read back after commit.
6. Delete absence and unchanged root-device count are verified.
7. Successful results contain only subnet identity and the device-count
   invariant.
8. Canonical JSON, typed payload, safety token, access mode, auditing, payload
   budgeting, and batch stop behavior remain intact.
9. The complete automated test suite and both stub and real V21 builds pass.
10. Documentation distinguishes static completion from the separately
    authorized public live acceptance gate.
