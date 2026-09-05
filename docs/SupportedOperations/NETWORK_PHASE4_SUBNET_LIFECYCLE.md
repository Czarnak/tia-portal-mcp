# Network Operations Phase 4: Subnet Lifecycle

Status: Phase 4 (subnet create/update/delete) is statically verified. Both the stub build and the
real V21 reference build pass with zero errors, the full `TiaMcpServer.Tests` suite passes, and the
whole-plan contract audit against the plan's Locked Public Contract found no discrepancy. Phase 4 is
**not** marked live-verified. Public-path live acceptance against a real TIA Portal V21 project is a
separately authorized run (Task 10 of the implementation plan) and has not been attempted, simulated,
or scheduled by this documentation update. See "Evidence status" below.

Design rationale, evidence basis, and rejected alternatives are recorded in
[../superpowers/specs/2026-08-06-network-phase4-subnet-lifecycle-design.md](../superpowers/specs/2026-08-06-network-phase4-subnet-lifecycle-design.md).
The full task-by-task implementation plan, including the Locked Public Contract this document
matches, is at
[../superpowers/plans/2026-08-06-network-phase4-subnet-lifecycle.md](../superpowers/plans/2026-08-06-network-phase4-subnet-lifecycle.md).
See [../roadmap/network-operations.md](../roadmap/network-operations.md) for how this phase fits the
overall network roadmap, and
[NETWORK_OPERATIONS_SUMMARY.md](NETWORK_OPERATIONS_SUMMARY.md) for the previously supported
network operations and the shared canonical JSON contract Phase 4 reuses unchanged.

## Supported operations

Phase 4 adds exactly three write operations to the existing `network_write` tool. No new MCP tool
was added; the read-write tool count remains 14 and the read-only tool count remains 4.

| Operation | Purpose | Required request fields |
|---|---|---|
| `create_subnet` | Create a new Ethernet or PROFIBUS subnet. | `subnet: { name, networkType }` |
| `update_subnet` | Rename or change PROFIBUS attributes on an existing subnet. | `target: { kind: "subnet", subnetId }`, `subnetChanges` (at least one member) |
| `delete_subnet` | Delete an existing subnet, connected or not. | `target: { kind: "subnet", subnetId }` |

These three operations do not exist in the generic `preview_write_batch` / `apply_write_batch`
catalog, do not have an alias, and are not exposed through `network_read`.

### Request shapes

`create_subnet`:

```json
{
  "operationId": "create-pb-1",
  "operation": "create_subnet",
  "projectPath": "C:\\Projects\\Fixture.ap21",
  "subnet": {
    "name": "PROFIBUS_LINE_2",
    "networkType": "Profibus",
    "highestAddress": 126,
    "transmissionSpeed": "Baud1500000"
  }
}
```

`update_subnet`:

```json
{
  "operationId": "update-pb-1",
  "operation": "update_subnet",
  "projectPath": "C:\\Projects\\Fixture.ap21",
  "target": {
    "kind": "subnet",
    "subnetId": "590-5"
  },
  "subnetChanges": {
    "name": "PROFIBUS_LINE_3",
    "highestAddress": 62,
    "transmissionSpeed": "Baud93750"
  }
}
```

`delete_subnet`:

```json
{
  "operationId": "delete-pb-1",
  "operation": "delete_subnet",
  "projectPath": "C:\\Projects\\Fixture.ap21",
  "target": {
    "kind": "subnet",
    "subnetId": "590-5"
  }
}
```

`subnet` (create) and `subnetChanges` (update) are strict nested objects: unmapped JSON members are
rejected by deserialization, matching every other `NetworkOperationRequest` field. Neither DTO
accepts a `subnetId` member, and `subnetChanges` does not accept `networkType` -- a subnet's identity
and network type can never be written through this contract.

## Writable values

- `networkType` (create only) is exact and case-sensitive: `Ethernet` or `Profibus`. No other value
  is accepted, and it cannot be changed once a subnet exists.
- `name` is required and nonblank on create; optional but nonblank when supplied on update. It is
  preserved exactly -- never trimmed or normalized.
- `highestAddress` is PROFIBUS-only and must be an integer from `0` through `126`. Supplying it for
  an Ethernet subnet (on create, by network type; on update, by the target's current type) is
  rejected.
- `transmissionSpeed` is PROFIBUS-only and must be exactly one of these ten symbols: `Baud9600`,
  `Baud19200`, `Baud45450`, `Baud93750`, `Baud187500`, `Baud500000`, `Baud1500000`, `Baud3000000`,
  `Baud6000000`, `Baud12000000`. The Siemens enum member `None` is not an accepted request value, and
  no other spelling, case variant, or numeric baud value is accepted.
- These rules are enforced twice: once by the host's static request validation
  (`NetworkOperationCatalog`) before any worker call, and again by the worker
  (`TiaMcpServer.OpennessWorker/Program.cs`) immediately before the Openness transaction opens.

## Exact `subnetId` targeting

`update_subnet` and `delete_subnet` select an existing subnet only by its exact `subnetId`, matched
with ordinal (case-sensitive) string equality against `HardwareConfigInfo.Subnets` as read
immediately before preview and again immediately before apply:

- there is no fallback to `name`, collection index, connected device, or "first match";
- a different-cased `subnetId` does not match;
- zero matches or more than one candidate reporting the same `subnetId` both fail closed with
  `postcondition_failed` -- neither is treated as success;
- a target subnet whose own `networkType` is missing or outside `Ethernet`/`Profibus` fails closed
  and is never resolved as a write target.

`create_subnet` never accepts a caller-supplied `subnetId`: Openness assigns it, and the created
subnet's identity is reported back only in the result after the transaction commits.

## Ethernet and PROFIBUS scope only

Phase 4 supports only the two subnet types already exposed by `SubnetInfo.NetworkType`. It does not
add, and does not claim to add:

- node connect/disconnect, IO-system creation or editing, or communication-connection management;
- integrated PROFIBUS or PROFIdrive network handling;
- Ethernet `DefaultSubnet`, PROFIBUS `BusProfile`, isochronous settings, or other `Pb*` attributes;
- generic dynamic-attribute writes on subnets or any other network object;
- device creation, deletion, or other device lifecycle behavior;
- project save or hardware compile as part of any subnet operation.

## Deletion semantics

`delete_subnet` accepts both empty and connected subnets. Deleting a connected subnet:

- **is supported** -- there is no dependency inventory, no connected-node/IO-system enumeration, and
  no "dependency blocker" that refuses the delete because identity evidence is incomplete;
- **does not delete devices** -- the worker's `SubnetLifecycleService.Delete` calls only
  `Subnet.Delete()` and verifies `project.Devices.Count` is unchanged after the transaction commits,
  for every delete, not only empty ones;
- **may clear network-related device attributes as a logical TIA effect** -- removing a subnet's
  relationship to a node or IO system is an expected consequence of deleting the subnet itself inside
  TIA Portal, not a defect. The subnet lifecycle result never reports which attributes were cleared;
  a caller that needs to see the after-state calls `network_read` (`read_hardware_config` or
  `inspect_network_object`) separately.

## Minimal result

Every successful subnet lifecycle item -- create, update, or delete -- returns exactly these four
members, typed by `TiaMcpServer.Contracts.SubnetLifecycleResultInfo` and enforced by
`NetworkPayloadContract`:

```json
{
  "subnetId": "590-5",
  "name": "PROFIBUS_LINE_3",
  "networkDeviceCount": 10,
  "networkDeviceCountUnchanged": true
}
```

- `subnetId` and `name` are nonblank strings; `networkDeviceCount` is a non-negative integer;
  `networkDeviceCountUnchanged` must be `true` -- a payload reporting `false`, a missing member, or
  any extra member (`networkType`, `highestAddress`, connected-node names, device names, IO systems,
  connections, or any other detail) is rejected as `protocol_error` before it ever reaches the caller.
- For deletion, `subnetId` and `name` are the identity captured immediately before deletion, and the
  post-read proves that `subnetId` is absent afterward.
- For creation and update, `subnetId` and `name` are the post-read identity after the transaction
  commits.
- The enclosing structured operation item already carries the operation name (`create_subnet`,
  `update_subnet`, or `delete_subnet`) and status, so the result itself does not repeat a
  created/updated/deleted verb.

## Transactions, batching, and retry

- Each subnet operation opens exactly one Openness `ExclusiveAccess` and one `Transaction`, performs
  every requested setter inside it, and calls `CommitOnDispose()` only after every setter has
  succeeded. An exception before that call rolls the operation back.
- A `network_write` batch containing subnet operations remains sequential and **non-atomic** across
  items, exactly like every other `network_write` batch: the batch stops on the first failed item,
  and any earlier successful item in the same call -- subnet or otherwise -- is not rolled back. The
  tool description states this as "no batch-wide rollback."
- After a stopped batch, the failed item carries an explicit warning that this operation and any
  earlier operation in the same call may already have changed TIA state, and that the caller must
  re-read with `network_read` before retrying.
- The server never automatically retries a subnet mutation. A postcondition mismatch after a
  transaction has already committed is reported as `postcondition_failed`, and the caller must
  inspect current project state before deciding whether to retry manually.

## Preview, apply, and safety

Subnet lifecycle operations reuse the existing `network_write` preview/apply protocol unchanged --
there is no separate subnet-specific safety mechanism:

- **Preview** (`confirm=false`, no token): reads current `HardwareConfigInfo`, resolves every
  requested target against it (request-derived for `create_subnet`; exact `subnetId` match for
  `update_subnet`/`delete_subnet`), and issues a single-use safety token bound to the exact ordered
  operations, the resolved target evidence, and the current hardware state.
- **Apply** (`confirm=true` plus the returned token): re-reads hardware state, re-resolves every
  target against that fresh read, and only then validates and consumes the token. Reordering the
  operations, changing any request field, or a project-state change since preview (a rename, a
  deletion, a newly ambiguous `subnetId`) invalidates the token.
- The token expires after ten minutes and is single-use; a replayed token is rejected.
- Read-only mode denies all three operations before any worker call: `create_subnet`,
  `update_subnet`, and `delete_subnet` are classified `ProjectMutation` in `OperationPolicyCatalog`,
  enforced independently at host tool discovery, host `OperationAccessPolicy`, and worker
  `WorkerOperationAuthorization`.
- A successful apply appends an audit record under `%LOCALAPPDATA%\TiaMcpServer\audit`, carrying the
  exact response document the caller received, exactly like every other `network_write` apply.

## Save and compile boundary

`SubnetLifecycleService` never calls `Project.Save()` and never triggers a hardware compile. Saving
and compiling remain separate, explicit operations (`save_project`, `compile_check`) that a caller
invokes after reviewing the subnet lifecycle result. Because deleting a connected subnet
intentionally clears the corresponding network/IO-system relationships, a subsequent compile may
report diagnostics caused by that disconnection; those diagnostics do not mean the subnet deletion
itself failed.

## Explicit non-goals

Phase 4 does not add, and this documentation does not claim, any of the following:

- node attach/detach, IO-system creation/editing, or communication-connection creation, editing, or
  deletion;
- generic network-attribute writes (deferred to a later phase, per the roadmap);
- integrated PROFIBUS or PROFIdrive network handling;
- project save or hardware compile as a side effect of a subnet operation;
- device creation, deletion, or any other device lifecycle behavior;
- online connection-path selection, accessible-device discovery, download, commissioning, or
  hardware-runtime validation;
- automatic retry of any subnet mutation.

### Deferred: GSD-derived hardware-name issue

The Phase 3 read-only probe observed repeated network-component node names for ABB VFDs installed
through GSD hardware definitions, distinct from their names in the source project. This suggests
names or attributes exposed by GSD-derived components may not be readable through the same paths as
native catalog hardware. This observation is recorded for later hardware-introspection work only.
Phase 4 never uses device-item names, node names, or other GSD-derived attributes as subnet identity,
safety evidence, postcondition evidence, or public result data -- subnet identity is always the
Openness-assigned `subnetId`.

## Evidence status

Two independent lines of evidence exist for Phase 4, and they are not interchangeable:

- **Internal probe evidence.** The internal, non-public `probe_subnet_lifecycle_mutations` worker
  operation and `SubnetLifecycleMutationProbeService` exercised subnet creation, editing, and
  deletion (including connected subnets) directly against a real TIA Portal V21 project during
  design. This evidence shaped the Locked Public Contract but is not itself the public code path:
  the probe is never registered in `NetworkOperationCatalog`, is absent from the public MCP schema,
  and is not reachable from `network_write`.
- **Static implementation verification (this document's basis).** The stub build, the real V21
  reference build, the full `TiaMcpServer.Tests` suite, and a direct whole-plan contract audit of the
  actual source against the plan's Locked Public Contract all pass. This proves API-shape
  compatibility, request/response contract correctness, safety-token behavior, and worker dispatch
  logic on .NET without a live TIA Portal attachment. It does **not** prove runtime Openness
  behavior through the public MCP path.
- **Public-path live acceptance -- outstanding.** A separately authorized live run against a
  disposable TIA Portal V21 project, driving the actual public `network_read`/`network_write` MCP
  protocol for create, update, and delete (including connected-subnet deletion) on both Ethernet
  and PROFIBUS, has not been performed. The legacy procedure was removed; a newly reviewed
  procedure and separate authorization are required before running this gate. Phase 4 must not be
  marked live-verified until that run completes and its results are recorded here.
