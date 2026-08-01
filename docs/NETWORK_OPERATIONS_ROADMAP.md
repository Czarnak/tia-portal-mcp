# Network Operations Roadmap

Status: approved high-level direction as of 2026-08-01. Implementation is intended
for the current `feature/network-edit-operations` branch.

This document records the architectural direction and delivery sequence. It is not
a detailed implementation plan. Task-level design, acceptance criteria, and file-by-file
steps will be produced later.

## Objective

Create a first-class, agent-friendly network engineering surface that:

- separates network operations from the generic read and write batch tools;
- preserves preview-before-apply safety for every write;
- exposes structured JSON that agents can inspect and transform reliably; and
- expands from the current bounded device-configuration surface to subnet, IO-system,
  and generic network-attribute operations.

The current implemented surface remains documented in
[SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md](SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md).

## Approved Public Tool Direction

Introduce two domain tools:

- `network_read`: batch network reads, registered in both read-only and read-write modes.
- `network_write`: a self-previewing batch write tool, registered only in read-write mode.

Calling `network_write` without confirmation returns a preview and safety token. Calling
the same tool again with `confirm=true`, the unchanged operation list, and that token
applies the batch. Existing token expiry, single-use behavior, project-state binding,
auditing, and access-mode enforcement remain mandatory.

The following existing operations move out of the generic batch surface:

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

The current hardware DTO hierarchy is readily serializable, but the generic batch result
stores each worker payload as text. A serialized network object can therefore appear as
escaped JSON inside an outer JSON envelope. The dedicated network tools should instead
return structured objects and arrays in each operation result so an agent does not need
to parse nested JSON strings.

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

Add `network_read` and `network_write`, introduce a network-specific operation catalog
and request envelope, move the four existing operations, and remove them from the generic
batch schema, descriptions, dispatch, and tests. Preserve the existing worker handlers
where their behavior remains suitable.

### Phase 2: Stabilize the JSON Contract

Run the agent-facing JSON contract gate above. Resolve nested JSON, collection/null
semantics, selector ambiguity, typed values, and result-envelope consistency before
freezing the network tool contract.

### Phase 3: Establish Network Object Identity and Introspection

Introduce deterministic selectors for device items, interfaces, nodes, subnets,
IO systems, and communication connections. Do not rely on an implicit first interface
or first node. Add attribute metadata needed to distinguish modeled, dynamic, readable,
writable, and unsupported members.

### Phase 4: Add First-Class Subnet Lifecycle Operations

Add subnet creation, editing, and deletion with type-aware attributes, dependency impact
in previews, explicit destructive-operation safeguards, and post-write reads. Use an
Openness transaction where V21 supports the complete operation; otherwise expose partial
application semantics explicitly.

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

Start future implementation planning from these existing seams:

- `TiaMcpServer/Batch/BatchOperationCatalog.cs`
- `TiaMcpServer/Batch/ReadBatchTools.cs`
- `TiaMcpServer/Batch/WriteBatchTools.cs`
- `TiaMcpServer/Batch/BatchResultFormatter.cs`
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

The later detailed design must settle the exact selector schema, operation names,
attribute-value encoding, transaction and partial-failure rules, postcondition envelope,
and the boundary between typed operations and generic scalar attribute writes.
