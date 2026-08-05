# Network Operations Roadmap

Status: Phases 1 and 2 are complete. Phase 3 implementation and its separately authorized,
read-only TIA Portal V21 evidence run are complete, but final stabilization is pending design
review because only three of eight observed communication connections had complete selectors.
The measured representative-device query was 66.34% smaller than the full discovery result while
preserving every matching selector, so the separate `list_network_objects` retention gate passed
and the operation is retained. See
[SupportedOperations/NETWORK_PHASE3_LIVE_ACCEPTANCE.md](SupportedOperations/NETWORK_PHASE3_LIVE_ACCEPTANCE.md)
for the evidence and explicit coverage gaps. Phases 4 and later remain open and are separate,
not-yet-scheduled work.

Phase 2 completion is scoped narrowly: Tasks 1-7 of
`docs/superpowers/plans/2026-08-02-network-operations-phase2-json-contract.md` are implemented and
their automated gates pass — the stub build, `dotnet test TiaMcpServer.Tests`, the FakeWorker
protocol tests (including the multi-homed read-select-preview-apply-read proof and the
protocol-error stop proof), and the schema/catalog/access-mode tests. It is **not** based on any
live TIA Portal V21 acceptance run: per the plan's Global Constraints, "a compile, stub build,
FakeWorker run, or contract test is not evidence of live TIA behavior." The separately authorized
live harness at `scripts/live-test-network-phase2.ps1` (Task 8) is available to run under that
separate authorization gate whenever live evidence is wanted, but its absence does not block this
Phase 2 completion mark, and its presence does not itself constitute live-verified evidence until
someone actually runs it against real TIA Portal V21 and records the result.

This document records the architectural direction and delivery sequence. It is not
a detailed implementation plan. Task-level design, acceptance criteria, and file-by-file
steps will be produced later.

## Objective

Create a first-class, agent-friendly network engineering surface that:

- separates network operations from the generic read and write batch tools;
- preserves preview-before-apply safety for every write;
- exposes structured JSON that agents can inspect and transform reliably, per the completed
  Phase 2 contract gate below; and
- exposes the completed Phase 3 snapshot-scoped discovery and typed read-only inspection surface
  for device items, interfaces, nodes, subnets, IO systems, and communication connections; and
- leaves subnet lifecycle, IO-system editing, and generic network-attribute writes to later phases.

The current implemented surface remains documented in
[SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md](SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md).

## Approved Public Tool Direction

The implemented domain tools are:

- `network_read`: batch network reads, registered in both read-only and read-write modes.
- `network_write`: a self-previewing batch write tool, registered only in read-write mode.

Calling `network_write` without confirmation returns a preview and safety token. Calling
the same tool again with `confirm=true`, the unchanged operation list, and that token
applies the batch. Existing token expiry, single-use behavior, project-state binding,
auditing, and access-mode enforcement remain mandatory.

The following operations have moved out of the generic batch surface:

- `read_hardware_config`
- `search_equipment_catalog`
- `add_network_device`
- `configure_network_device`

The separation is a real public contract boundary, not a pair of aliases over the generic
batch catalog. Shared execution, worker transport, session binding, payload budgeting,
safety, and audit infrastructure should be reused underneath the new tools.

## Agent-Facing JSON Contract Gate

Immediately after tool separation, pause capability expansion and evaluate the actual
JSON seen by an MCP client. This gate must be completed before new subnet, IO-system, or
generic attribute operations are added.

Phase 1 intentionally retains string-valued operation results. A serialized network object
can therefore appear as escaped JSON inside an outer JSON envelope. Phase 2 is the mandatory
single-layer JSON contract gate: it must replace this with structured objects and arrays in
each operation result before additional topology capabilities or a frozen network contract.

Contract evaluation will cover:

- public tool output rather than DTO serialization alone;
- stable camelCase property names and deterministic collection shapes;
- clear distinctions between absent, not applicable, unsupported, unreadable, and null;
- deterministic object selectors suitable for subsequent writes;
- typed attribute values and access metadata;
- explicit warnings, failures, omissions, and truncation markers; and
- canonical serialization for safety-token and current-state snapshots.

Representative contract tests will exercise:

1. Network read results as single-layer structured JSON.
2. Serialize-deserialize-serialize stability for network contracts.
3. Translation of selected writable read fields into a write request.
4. Preview, canonical safety binding, apply result, and post-read comparison.
5. Rejection of unknown, read-only, ambiguous, or incorrectly typed writes.

The read model and write model do not need to be identical. Reads may contain derived,
read-only, relationship, diagnostic, and availability information. The preferred write
model is an explicit operation containing a deterministic target and intended changes:

```text
network_read snapshot
        -> agent selects intended changes
        -> network_write target + changes
        -> preview -> confirm -> apply -> post-read
```

An editable whole-network document may be evaluated later, after the explicit operation
contract has proved safe and comfortable for agents.

## Delivery Phases

### Phase 1: Separate the Public Tool Surface

Completed: added
`network_read` and `network_write`, a network-specific operation catalog and request
envelope, moved the four existing operations, and removed them from the generic batch
schema, descriptions, dispatch, and tests. The existing worker handlers remain in use where
their behavior is suitable.

### Phase 2: Stabilize the JSON Contract — Complete

Completed: `network_read` and `network_write` both declare an MCP output schema and return one
canonical JSON document identically in `content` and `structuredContent` — no nested JSON string
anywhere in the response. `network_write` is a discriminated `preview | apply | error` envelope.
Configure operations use nested `target: { deviceName, nodeId }` and
`changes: { ipAddress?, subnetMask?, pnDeviceName?, subnet?: { subnetId }, ioSystem?: { subnetId, number } }`
with no flat legacy alias. Selector resolution (device, node, subnet, IO system) is exact and
fail-closed: zero, multiple, or unreadable matches always fail rather than falling back to a
first-match, first-node, or name-only guess. A worker success payload that does not decode as its
declared result type becomes a failed item with category `protocol_error`, never echoing the
rejected payload. Results are bounded against the exact response document: an oversized result is
omitted whole (never substringed) with retry guidance, and the whole document is capped with a
`batch.truncation` record of what was affected. The exact envelopes, all four Phase 2 typed payload
result types, and the multi-homed proof are documented in
[SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md](SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md).

This mark reflects Tasks 1-7 and their automated gates (stub build, `dotnet test
TiaMcpServer.Tests`, FakeWorker protocol tests) passing — not a live TIA Portal V21 acceptance
run. See the Status note above.

### Phase 3: Establish Network Object Identity and Introspection — Stabilization Pending

Completed: `read_hardware_config` now carries snapshot-scoped selectors alongside its modeled
hardware summaries, and `network_read` adds the bounded `list_network_objects` and
`inspect_network_object` operations without adding another public MCP tool. Discovery covers
device items, network interfaces, nodes, subnets, IO systems, and communication connections;
incomplete identity is returned explicitly as `selectable:false` with diagnostics instead of an
invented selector. Inspection verifies the selector against the current object graph, merges
per-kind modeled attributes with generic `IEngineeringObject` metadata, and returns closed typed
values plus independent source, access, availability, and diagnostic fields.

The read-only TIA Portal V21 matrix, payload-repeatability run, value measurement, and raw metadata
probe completed on the prepared fixture. The targeted-list value gate passed, so
`list_network_objects` remains supported. Final stabilization is pending a reviewed resolution of
the original acceptance rule that required identity to distinguish every observed connection:
five of eight observed connections lacked sufficient identity for a selector. The fixture also
did not cover PROFIBUS/DP or non-HMI communication-connection classes. These are explicit coverage
and selectability limits, not evidence that the corresponding Siemens capabilities are absent.
This phase does not certify commissioning or live hardware behavior.

### Phase 4: Add First-Class Subnet Lifecycle Operations

Add subnet creation, editing, and deletion with type-aware attributes, dependency impact
in previews, explicit destructive-operation safeguards, and post-write reads. Use an
Openness transaction where V21 supports the complete operation; otherwise expose partial
application semantics explicitly.

Before adding any public tool operation or contract, run two PowerShell probes against disposable
TIA Portal V21 project copies:

- a read-only subnet metadata probe that records Ethernet and PROFIBUS attribute names, types,
  access modes, current values, selector identity, and relationships; and
- an explicitly enabled mutation probe that creates and edits isolated Ethernet and PROFIBUS
  subnets, deletes both empty and connected subnets, records Openness validation and transaction
  behavior, and post-reads the project to confirm that subnet deletion does not delete devices.

Write timestamped JSON evidence under the ignored artifact directory. Review that evidence before
settling the writable attributes, validation rules, transaction boundary, deletion guardrails, or
postcondition contract. Ordinary tests and CI must never invoke these live probes.

### Phase 5: Add IO-System Attribute Editing

Support relevant PROFINET IO-system and DP master-system modeled and dynamic attributes.
Validate known constraints before apply, then compile hardware because Openness can accept
some values that TIA compilation later rejects.

### Phase 6: Add Generic Network Attribute Operations

Enumerate attribute name, value, wire type, and access mode for network interfaces, nodes,
subnets, and communication connections. Permit metadata-validated scalar writes. Keep
topology relationships, creation, deletion, connection management, and other structural
changes in typed operations rather than unrestricted free-form attribute writes.

### Phase 7: Verification and Documentation

Verify schemas, access modes, catalogs, field forwarding, FakeWorker behavior, IPC,
safety tokens, audit records, payload budgets, postconditions, and the stub build. Update
the supported-operations documentation only as capabilities are actually delivered.

Live TIA Portal V21 acceptance is a separate, explicitly authorized gate. Static tests,
stub builds, JSON round trips, and compile-result DTOs do not certify runtime Openness or
commissioning behavior.

## Contract Principles

- Prefer domain-specific request and result contracts over extending the existing flat
  `BatchOperationRequest` with every possible network field.
- Use deterministic targets; names alone are insufficient when they are not unique.
- Preserve types across JSON. Do not reduce enums, numbers, booleans, and unavailable
  values to undifferentiated strings.
- Never silently ignore an unknown or non-writable field.
- Keep read diagnostics and missing-data evidence instead of synthesizing values.
- Bind write previews to canonical requested intent and current project state.
- Return enough postcondition evidence for an agent to verify what changed.

## Implementation Anchors

Start later-phase implementation planning from these existing seams:

- `TiaMcpServer/Network/NetworkOperationCatalog.cs`
- `TiaMcpServer/Network/NetworkReadTools.cs`
- `TiaMcpServer/Network/NetworkWriteTools.cs`
- `TiaMcpServer/OperationBatches/`
- `TiaMcpServer/Worker/OpennessWorkerClient.cs`
- `TiaMcpServer.Contracts/WorkerRequest.cs`
- `TiaMcpServer.OpennessWorker/Program.cs`
- `TiaMcpServer.OpennessWorker/Openness/HardwareConfigReader.cs`
- `TiaMcpServer.OpennessWorker/Openness/NetworkDeviceConfigurator.cs`
- network DTOs under `TiaMcpServer.Contracts/`

## Deferred Scope

Unless separately approved, this roadmap does not yet include:

- transfer-area creation or deletion;
- address, channel, process-image, or address-controller operations;
- IO timing, watchdog, RT class, sync-role, send-clock, or isochronous configuration;
- communication-connection creation or deletion;
- online connection-path selection or accessible-device discovery; or
- direct download, commissioning, or hardware-runtime validation.

## Decisions Reserved for Detailed Design

Later detailed design must preserve the stabilized Phase 3 selector and attribute-read contracts
while settling write-specific transaction and partial-failure rules, postcondition envelopes, and
the boundary between typed operations and generic scalar attribute writes.
