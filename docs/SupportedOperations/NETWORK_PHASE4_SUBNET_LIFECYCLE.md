# Network Operations Phase 4: Subnet Lifecycle

Current `network_write` contract: **2.0**, offline qualified with scoped installed-candidate
TIA Portal V21 acceptance. The [2026-10-29 report](../superpowers/acceptance/reports/2026-10-29-network-write-native-rejections-live-verification.md)
records all 17 expected outcomes, timing and restoration. Dated Phase 4 runs below retain their
earlier contracts and evidence limits.

Historical Phase 4 status: subnet create/update/delete is implemented, and focused automated gates verify
the PR 1 contract repairs. The earlier static audit found discrepancies in the original contract
implementation. A historical live run applies only to its recorded older commit. The first
current-revision public attempt on 2026-09-23 failed at connected deletion; the fresh 2026-09-24
guarded public rerun passed all eight lifecycle operations including both connected deletes. A
separate final read observed zero subnets and 81 aggregate hardware devices. See "Evidence status"
below for the bounded live PASS and remaining readback limits.

Design rationale, evidence basis, and rejected alternatives are recorded in
[../superpowers/specs/2026-08-06-network-phase4-subnet-lifecycle-design.md](../superpowers/specs/2026-08-06-network-phase4-subnet-lifecycle-design.md).
The historical task-by-task implementation plan and its earlier Locked Public Contract are at
[../superpowers/plans/2026-08-06-network-phase4-subnet-lifecycle.md](../superpowers/plans/2026-08-06-network-phase4-subnet-lifecycle.md).
See [../roadmap/network-operations.md](../roadmap/network-operations.md) for how this phase fits the
overall network roadmap, and
[NETWORK_OPERATIONS_SUMMARY.md](NETWORK_OPERATIONS_SUMMARY.md) for the previously supported
network operations and the current guarded canonical contract. Historical Phase4 acceptance uses its recorded earlier contract.

## Supported operations

Phase 4 adds exactly three write operations to the existing `network_write` tool. No new MCP tool
was added. The current surface is 8 tools in read-only and 19 in each writable mode. `network_write` is available in read-write and full.

| Operation | Purpose | Required request fields |
|---|---|---|
| `create_subnet` | Create a new Ethernet or PROFIBUS subnet. | `subnet: { name, networkType }` |
| `update_subnet` | Rename or change PROFIBUS attributes on an existing subnet. | `target: { kind: "subnet", subnetId }`, `subnetChanges` (at least one member) |
| `delete_subnet` | Delete an existing subnet, connected or not. | `target: { kind: "subnet", subnetId }` |

These three operations never existed in the retired generic `preview_write_batch` /
`apply_write_batch` catalog, do not have an alias, and are not exposed through `network_read`.

### Request shapes

`create_subnet`:

```json
{
  "operationId": "create-pb-1",
  "operation": "create_subnet",
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
  an Ethernet create is rejected as request validation. An update relies on Portal's native
  applicability rule; the scoped Ethernet update failed with `EngineeringNotSupportedException`
  (`worker_operation_failed`).
- `transmissionSpeed` is PROFIBUS-only and must be exactly one of these ten symbols: `Baud9600`,
  `Baud19200`, `Baud45450`, `Baud93750`, `Baud187500`, `Baud500000`, `Baud1500000`, `Baud3000000`,
  `Baud6000000`, `Baud12000000`. The Siemens enum member `None` is not an accepted request value, and
  no other spelling, case variant, or numeric baud value is accepted.
- Request-shape, create applicability and range/enum rules are enforced twice: once by the host's static request validation
  (`NetworkOperationCatalog`) before any worker call, and again by the worker
  (`TiaMcpServer.OpennessWorker/Program.cs`) immediately before the Openness transaction opens.

## Exact `subnetId` targeting

`update_subnet` and `delete_subnet` require `target.kind` to be exactly `"subnet"` (ordinal,
case-sensitive) and select an existing subnet only by its exact `subnetId`, matched
with ordinal (case-sensitive) string equality against `HardwareConfigInfo.Subnets` in one planning
snapshot per call. A preview and a later apply are separate calls; mutation independently
re-resolves the exact ID in the worker, without another host snapshot:

- there is no fallback to `name`, collection index, connected device, or "first match";
- a different-cased `subnetId` does not match;
- at host preflight, zero matches fail closed with `target_not_found` and more than one candidate
  reporting the same `subnetId` with `target_ambiguous`; late worker re-resolution after the host
  read that finds zero or several fails with `postcondition_failed`;
- a target subnet whose own `networkType` is missing or outside `Ethernet`/`Profibus` fails closed
  with `target_kind_unsupported` and is never resolved as a write target.

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

- **is supported with complete consequence evidence** -- affected nodes/relationships are inventoried
  in the initial planning snapshot. A node connected earlier in the same call is not added to that list.
  Connected deletion is informational; incomplete inventory blocks every mode and is never empty evidence;
- **does not delete devices** -- the worker's `SubnetLifecycleService.Delete` calls only
  `Subnet.Delete()`. Contract 2.0 has no device-count assertion or preservation read-back;
- **may clear network-related device attributes as a logical TIA effect** -- removing a subnet's
  relationship to a node or IO system is an expected consequence of deleting the subnet itself inside
  TIA Portal, not a defect. A caller that needs to see the after-state calls `network_read` (`read_hardware_config` or
  `inspect_network_object`) separately.

For every delete, the worker resolves the exact target once inside the transaction and captures
a nonblank name from that same transaction-local subnet object,
then calls `Delete()` on it. No pre-transaction subnet object supplies the mutation identity.
Supported type validation uses the host's planning snapshot.

## Typed result

Every successful subnet lifecycle item has these two required members, typed by
`TiaMcpServer.Contracts.Network.SubnetLifecycleResultInfo` and enforced by `NetworkPayloadContract`:

```json
{
  "subnetId": "590-5",
  "name": "PROFIBUS_LINE_3"
}
```

- `subnetId` and `name` are nonblank. Missing or wrongly typed members fail decoding as
  `protocol_error`. There is no worker verification object or device count in this result.
- Creation captures the native ID inside the transaction and returns the requested name after
  commit. An unreadable created ID is `postcondition_failed` after commit: inspect before retrying.
- Update returns the requested ID and name captured inside the transaction. If a requested rename
  was silently ignored, its ordinal name comparison fails `postcondition_failed` before commit.
  The L3 collision case proved this mismatch; mixed-property rollback was not exercised live.
- Deletion returns the ID and nonblank name captured immediately before `Delete()`.
- Root verification reports `trusted` for completed operations or `superseded` when a later
  attempted operation owns the same subnet. Failed/skipped items have no verification entry.
  It performs no subnet absence, attribute or device-preservation read-back.
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
- The server never automatically retries a subnet mutation. `postcondition_failed` can report
  late selector drift, an ignored rename before commit, or an unreadable created ID after commit.
  Inspect current project state before deciding whether to retry manually.

## Preview, apply, and safety

Subnet lifecycle uses guarded `network_write(operations, dryRun=false)`. **Omitted dryRun executes**;
explicit `dryRun:true` inspects effects/guards without mutation or elicitation, and `dryRun:false`
executes in caller order. No Network server elicitation in read-write/full. A preview reserves no
state; each call plans one current snapshot under one pinned already-open
verified binding. Connected deletion is `info`; incomplete inventory is a non-overridable `block`.
There is no per-item Network re-planning. L12's delete effect listed five initially connected nodes
and omitted the node connected earlier in that call; fresh untimed N2/N4 timing targets started
with zero listed connections. Split calls and inspect again when the new connection must appear.

Legacy `confirm`, `acknowledge`, unknown root arguments and nonboolean dryRun
are rejected before tool entry with the canonical document (`validation_error`, `isError:true`) and no audit record. Entered validation/
binding/guard denials use a canonical root error and one audit. Attempted partial failures remain
`phase:applied`, `error:null`, MCP `isError:false`; stop on first failure, no batch rollback/replay.

Every entered call appends one audit v2: actual read-write confirmation `none`, actual full `policy`,
previews/denials `none`, info/block satisfaction null. Exact delivered canonical text/hash is stored;
item statuses retain execution truth even if whole-value omissions make delivery success false.
See the [current Network envelope](NETWORK_OPERATIONS_SUMMARY.md#network_write-envelope).

## Save and compile boundary

`SubnetLifecycleService` never calls `Project.Save()` or triggers compilation. Saving and public
PLC software compilation remain separate, explicit operations (`save_project`, `compile_check`).
Hardware compilation is a separate TIA operation; `compile_check` does not establish hardware
configuration validity. Because deleting a connected subnet
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

The Phase 4 evidence has distinct scopes:

- **Internal probe evidence.** The internal, non-public `probe_subnet_lifecycle_mutations` worker
  operation and `SubnetLifecycleMutationProbeService` exercised subnet creation, editing, and
  deletion (including connected subnets) directly against a real TIA Portal V21 project during
  design. This evidence shaped the Locked Public Contract but is not itself the public code path:
  the probe is never registered in `NetworkOperationCatalog`, is absent from the public MCP schema,
  and is not reachable from `network_write`.
- **Repaired static implementation.** Focused automated gates passed for exact target kind,
  required raw result members, late-drift classification, transaction-local delete identity, and
  the restored guarded harness. The executable repair commit
  `6ce302ea6d096aefd92dfcacceff7711a09d8e71` passed 3098/3098 full Debug tests,
  real-reference and stub Release builds, and 3098/3098 Release coverage tests. The 2026-09-24
  docs-only re-pin at `56e2248eacca44ea55c7d246d3c8ceb589b353b9` passed 298 focused tests
  and a normal-user Debug real-reference host build; its sandbox full Debug run returned 3094/3098
  with four environment failures. The full Release and coverage gates were not repeated on that
  re-pin. Static evidence does **not** prove runtime Openness behavior through the public MCP path.
- **Historical public-path run.** The earlier Phase 4 run is evidence only for the older commit
  recorded with that run. It cannot establish behavior of the current PR 1 tree after these repairs.
- **First current-revision public-path attempt -- incomplete on 2026-09-23.** Inventory,
  Preview, expected negative categories, and isolated Ethernet/PROFIBUS create/update/delete
  completed. TIA denied connected Ethernet deletion for missing safety-program modification
  permission and skipped connected PROFIBUS deletion. Portal reported unsaved modifications. The
  user subsequently confirmed that the disposable copy was closed without saving and reopened.
- **Fresh current-revision public-path rerun -- bounded live PASS on 2026-09-24.** After the user
  logged into safety, the guarded harness drove public `network_read`/`network_write` on the
  frozen candidate. Inventory and Preview reported `isModified=false`; all eight lifecycle
  operations succeeded, including both originally connected deletes. A separate final Inventory
  reported zero subnets, 81 aggregate hardware devices (also 81 before Apply), and
  `isModified=true`. Each operation result reported root `networkDeviceCount=10` and
  `networkDeviceCountUnchanged=true`, but the harness lacked an independent pre-Apply root-count
  baseline. It did not independently read back retained-device identities or node and IO-system
  attributes. No save, compile, download, or persistence result is claimed. See the
  [current-revision live report](../superpowers/acceptance/reports/2026-09-21-network-phase4-current-revision-live.md).
