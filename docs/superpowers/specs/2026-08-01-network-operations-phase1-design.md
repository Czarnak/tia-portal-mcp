# Network Operations Phase 1 Design

**Date:** 2026-08-01  
**Status:** Approved design; implementation not started  
**Roadmap:** `docs/NETWORK_OPERATIONS_ROADMAP.md`, Phase 1

## Objective

Separate the four existing network operations from the generic batch tools and expose
them through two first-class domain tools:

- `network_read` for batch network reads;
- `network_write` for self-previewing batch network writes.

Phase 1 is a public-contract and host-orchestration refactor. It must not change the
underlying TIA Portal Openness behavior, add new network capabilities, or implement the
structured-result contract reserved for Phase 2.

## Approved Decisions

1. Remove all four network operations from generic batches immediately in the same
   change. There are no compatibility aliases, adapters, migration-only recognition
   paths, or deprecation period.
2. Preserve the existing operation names and scalar parameters under the new tools.
3. Preserve the current string-valued per-operation `result` until Phase 2.
4. Build an independent Network domain over a shared, domain-neutral operation-batching
   kernel. Do not map network requests back into `BatchOperationRequest` or route them
   through `BatchOperationCatalog` or `BatchWorkerInvoker`.
5. Register read and write tools from separate decorated classes so read-only mode cannot
   discover `network_write`.
6. Reuse the existing worker client methods, worker request fields, authorization entries,
   worker dispatch handlers, and Openness implementations unchanged.
7. Bind each network write preview to one canonical hardware-configuration snapshot for
   the whole ordered write batch.
8. Do not run live TIA Portal operations for this phase. Verification is limited to unit,
   schema, host/FakeWorker, build, coverage, and diff evidence.

## Current State

The generic public batch request is `BatchOperationRequest`. Its catalog, descriptions,
dispatcher, safety snapshot, result formatter, payload budget, and tests currently include:

| Category | Operation |
|---|---|
| Read | `read_hardware_config` |
| Read | `search_equipment_catalog` |
| Write | `add_network_device` |
| Write | `configure_network_device` |

The worker already exposes suitable handlers through `OpennessWorkerClient`,
`WorkerRequest`, `OperationPolicyCatalog`, and
`TiaMcpServer.OpennessWorker/Program.cs`. The Phase 1 split happens above that worker
boundary.

The existing batch mechanics are not domain-neutral yet:

- `BatchExecutionEngine` accepts `BatchOperationRequest` directly.
- `BatchPayloadBudget` and `BatchResultFormatter` hard-code generic tool names and
  generic narrowing instructions.
- `BatchSafetySnapshot` contains network-specific descriptions and reads.
- `BatchWorkerInvoker` contains both data-domain and network-domain dispatch.

## Public Tool Surface

### `network_read`

`network_read` is registered in read-only and read-write modes. It accepts an ordered
`operations` array containing between 1 and 50 network read requests.

Supported operations:

| Operation | Required fields | Optional fields |
|---|---|---|
| `read_hardware_config` | none | `projectPath` |
| `search_equipment_catalog` | `query` | `projectPath`, `maxResults` |

Reads execute sequentially and independently. A failed item is recorded and does not stop
later items.

### `network_write`

`network_write` is registered only in read-write mode. It accepts an ordered `operations`
array plus tool-level `confirm` and `safetyToken` fields.

Supported operations:

| Operation | Required fields | Optional fields |
|---|---|---|
| `add_network_device` | `typeIdentifier`, `deviceName` | `projectPath`, `deviceItemName` |
| `configure_network_device` | `deviceName` | `projectPath`, `ipAddress`, `subnetMask`, `pnDeviceName`, `subnetName`, `ioSystemName` |

The confirmation contract is explicit:

| `confirm` | `safetyToken` | Behavior |
|---|---|---|
| `false` | omitted | Return preview and a new token. |
| `false` | supplied | Reject the contradictory request. |
| `true` | omitted | Reject and direct the caller to preview first. |
| `true` | supplied | Validate and apply the unchanged batch. |

Writes execute sequentially and stop on the first failure. Later operations are returned
as `skipped`; completed operations are not rolled back.

### Tool counts

Phase 1 adds two MCP tools without removing the three generic batch tools:

| Mode | Before | After |
|---|---:|---:|
| Read-write | 12 | 14 |
| Read-only | 3 | 4 |

## Network Request Contract

Create a public `NetworkOperationRequest` with unmapped JSON members disallowed. It owns
only fields used by the network domain:

- `operationId`
- `operation`
- `projectPath`
- `query`
- `maxResults`
- `typeIdentifier`
- `deviceName`
- `deviceItemName`
- `ipAddress`
- `subnetMask`
- `pnDeviceName`
- `subnetName`
- `ioSystemName`

`confirm` and `safetyToken` belong to the `network_write` tool envelope, not to individual
operations.

The network catalog validates before any worker access:

- batch is non-null and contains 1 through 50 operations;
- every operation is non-null and has a unique, non-empty `operationId`;
- the operation exists and belongs to the called read or write tool;
- required fields are populated;
- fields not applicable to the selected operation are absent;
- `maxResults`, when supplied, is at least 1;
- all explicit write `projectPath` values normalize to one project.

To preserve current behavior, `configure_network_device` with no optional settings remains
valid. The worker returns its existing no-settings result.

## Architecture and Ownership

### Network domain

Create a new `TiaMcpServer/Network/` domain containing:

- `NetworkOperationRequest`
- `NetworkOperationCategory`, `NetworkOperationSpec`, and `NetworkOperationCatalog`
- `NetworkWorkerInvoker`
- `NetworkReadTools`
- `NetworkWriteTools`
- network-specific target descriptions, current-state acquisition, and result projection

`NetworkReadTools` and `NetworkWriteTools` must be separate `[McpServerToolType]` classes.
The host always registers the read class and registers the write class only in read-write
mode.

`NetworkWorkerInvoker` calls the four existing `OpennessWorkerClient` methods directly.
It must not construct `BatchOperationRequest`, consult `BatchOperationCatalog`, or call
`BatchWorkerInvoker`.

### Shared operation-batching kernel

Move or extract the domain-neutral mechanics into a shared host layer, expected under
`TiaMcpServer/OperationBatches/`:

- a minimal common batch-item contract exposing operation ID, operation name, and project
  path;
- generic independent-read and stop-on-first-failure write execution;
- common operation result/status records;
- response-envelope formatting parameterized by public tool name;
- payload budgeting parameterized by tool name and narrowing guidance;
- deterministic ordered current-state composition helpers.

Both the generic Batch domain and the Network domain depend on this kernel. Neither domain
depends on the other.

The shared layer must contain no operation names or operation-specific fields. Catalogs,
field validation, target descriptions, worker dispatch, and tool descriptions remain
domain-owned.

### Generic Batch cleanup

Remove the four network names from:

- `BatchOperationCatalog` specifications and advertised operation-name collections;
- `BatchOperationRequest.Operation` and field descriptions;
- `BatchWorkerInvoker.InvokeAsync` and `ReadCurrentStateAsync`;
- `BatchSafetySnapshot` target descriptions;
- `ReadBatchTools`, `WriteBatchTools`, and the undecorated `BatchTools` compatibility test
  wrapper descriptions;
- generic payload-budget narrowing guidance;
- generic batch tests and fixtures that treat the operations as supported.

Remove network-only properties from `BatchOperationRequest`: `Query`, `TypeIdentifier`,
`DeviceName`, `DeviceItemName`, `IpAddress`, `SubnetMask`, `PnDeviceName`, `SubnetName`, and
`IoSystemName`. Keep `MaxResults` because `read_cross_references` still uses it.

Generic batches reject all four removed operation names as ordinary unknown operations,
matching the completed project-tool separation. Do not retain them in a special rejection
or non-batchable list.

### Worker boundary

The following remain unchanged:

- `OpennessWorkerClient.ReadHardwareConfigAsync`
- `OpennessWorkerClient.SearchEquipmentCatalogAsync`
- `OpennessWorkerClient.AddNetworkDeviceAsync`
- `OpennessWorkerClient.ConfigureNetworkDeviceAsync`
- corresponding fields in `WorkerRequest`
- the four classifications in `OperationPolicyCatalog`
- the four dispatch arms and handlers in `TiaMcpServer.OpennessWorker/Program.cs`
- `HardwareConfigReader`, `EquipmentCatalogSearcher`, `NetworkDeviceCreator`, and
  `NetworkDeviceConfigurator`

In particular, Phase 1 does not alter the currently unverified subnet-connection and
IO-system reflection calls in `NetworkDeviceConfigurator`.

## Data Flow

### Read

```text
network_read
    -> NetworkOperationCatalog validation
    -> host access-mode validation
    -> shared independent-read engine
    -> NetworkWorkerInvoker
    -> existing OpennessWorkerClient method
    -> existing worker handler
    -> shared payload budget
    -> network_read response envelope
```

### Write preview

```text
network_write(confirm=false, no token)
    -> NetworkOperationCatalog validation
    -> host read-write enforcement
    -> resolve common project target
    -> one read_hardware_config snapshot
    -> build ordered human-readable targets
    -> WriteSafetyService.CreatePreview(tool=network_write)
```

### Write apply

```text
network_write(confirm=true, token)
    -> repeat request and access validation
    -> validate token envelope against exact ordered request and target
    -> one fresh read_hardware_config snapshot
    -> validate state and consume token
    -> shared sequential write engine
    -> NetworkWorkerInvoker
    -> existing worker handlers
    -> append one batch audit record
    -> network_write apply envelope
```

## Safety Model

One token covers the complete ordered network write batch. It is bound to:

- tool name `network_write`;
- normalized project path;
- exact serialized `NetworkOperationRequest[]` input;
- ordered human-readable targets;
- one canonical hardware-configuration snapshot.

Reordering operations, modifying any field, retargeting the project, changing the hardware
configuration, reusing a consumed token, or waiting past token expiry invalidates apply.

Reading hardware configuration once per preview or apply attempt is intentional. Both
network writes mutate the same project topology, so one snapshot is sufficient, avoids
redundant Openness calls, and cannot contain conflicting per-item views.

If preview state acquisition fails, no token is issued. If apply state acquisition fails,
the write does not start and the token is not consumed. Once a valid token is consumed,
the existing sequential no-rollback semantics apply.

No automatic save, compile, transaction, exclusive-access scope, or post-write hardware
read is added in Phase 1.

## Result Contract

Phase 1 preserves the existing operation-result representation:

```json
{
  "tool": "network_read",
  "success": true,
  "operationCount": 1,
  "succeeded": 1,
  "failed": 0,
  "omitted": 0,
  "operations": [
    {
      "operationId": "hardware",
      "operation": "read_hardware_config",
      "status": "succeeded",
      "result": "{\"devices\":[...]}",
      "warnings": null
    }
  ]
}
```

The public `tool` value changes to `network_read` or `network_write`; the per-operation
`result` remains a string produced from `WorkerCallResult.ToText()`. Phase 2 owns parsing
worker payloads into single-layer structured values and defining serialization stability.

Warnings, worker failure categories, partial-read messages, and the current
`configure_network_device` applied/skipped setting behavior pass through unchanged. If at
least one requested setting is applied, the existing partial result is successful. If all
requested settings are skipped, the existing worker handler fails the operation.

## Payload Budget

Apply the existing item and combined response limits to `network_read`. Refactor the
budget calculation so it serializes the actual requested tool envelope rather than
hard-coding `execute_read_batch`.

Truncation and omission markers must:

- identify `network_read` as the retry tool;
- recommend splitting the operation into its own call;
- recommend `query` or `maxResults` only where applicable;
- preserve every failure status even when successful payloads must be omitted;
- never silently drop warnings or failure evidence.

Write results keep the current unbudgeted apply behavior unless an existing shared limit
already applies; Phase 1 does not introduce a new write-response contract.

## Access-Mode Enforcement

The split preserves three enforcement layers:

1. Tool discovery: `NetworkWriteTools` is not registered in read-only mode.
2. Host authorization: `network_read` validates every underlying operation against the
   current access policy, and `network_write` rejects read-only mode defensively.
3. Worker authorization: existing underlying operation names remain classified and are
   checked before their handlers run.

`NetworkReadTools` is registered alongside `ProjectReadTools` and `ReadBatchTools`.
`NetworkWriteTools` is registered alongside the existing read-write-only tool classes.

## Testing Strategy

Implementation follows TDD. A focused failing test must be observed before each production
behavior is introduced.

### Network contract tests

- Exact catalog membership and read/write categories.
- Required, optional, universal, and inapplicable field validation.
- Empty, oversized, null-item, missing-ID, duplicate-ID, unknown-operation, wrong-category,
  invalid-bound, and mixed-project rejection.
- JSON camel-case binding and rejection of unknown or misspelled properties.
- Preservation of the no-settings `configure_network_device` request.

### Network forwarding and execution tests

- Every declared field reaches the matching `OpennessWorkerClient` call by value.
- `deviceItemName` defaults to `deviceName` only for `add_network_device`.
- Independent read continuation after failure.
- Ordered successful read results and warnings.
- Sequential writes, first-failure stopping, and later-item skipping.
- Network-specific payload truncation and omission guidance.

### Safety tests

- Preview performs no mutation and reads one hardware snapshot.
- Contradictory `confirm` and token combinations are rejected.
- Apply requires the identical ordered request and project target.
- Reordering or changing any field invalidates the token.
- A changed hardware snapshot invalidates the token.
- A token is single-use and expires according to existing policy.
- Apply reads exactly one fresh hardware snapshot before consuming the token.
- One audit record captures the whole batch request, prior state, and apply result.

### Public schema and access tests

- Exact 14-tool read-write surface.
- Exact 4-tool read-only surface.
- `network_write` is absent from read-only discovery.
- Both tool methods hide injected service parameters from the MCP schema.
- Tool and property descriptions list every supported operation and field.
- Read-only host and worker authorization remain aligned.

### Generic removal and regression tests

- All four network names are absent from generic read/write operation collections.
- Generic validation reports them as unknown operations.
- Network-only CLR and JSON fields are absent from `BatchOperationRequest`.
- Generic descriptions, payload hints, forwarding invariants, fixtures, and examples contain
  no network operation names or fields.
- Existing generic execution, safety, result, and payload-budget tests continue passing
  against the extracted shared kernel.

### Test project integration

`TiaMcpServer.Tests` links host source files explicitly. Add every new Network and shared
operation-batching source file to `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`. Tests must
fail clearly if a future production file is added without its required linked test seam.

## Documentation Changes

Update all of the following in the same implementation change:

### `README.md`

- Change tool counts to 14 read-write and 4 read-only.
- Add `network_read` and `network_write` to the public tool list.
- Remove network operations from generic read/write batch lists and examples.
- Document the self-previewing `network_write` flow.
- Move hardware reads, catalog search, device addition, and network configuration examples
  to the dedicated tools.
- Update read-only discovery and smoke-test instructions.

### `docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md`

- Replace generic batch entry points with `network_read` and `network_write`.
- Preserve the existing capability and runtime-validation limits.
- Update the recommended workflow and no-rollback wording.

### `docs/NETWORK_OPERATIONS_ROADMAP.md`

- Mark Phase 1 complete only after every completion gate passes.
- Record that Phase 1 deliberately retains string-valued results and that Phase 2 remains
  the structured JSON contract gate.

### `docs/ARCHITECTURE.md`

- Update tool counts and read-only/read-write registration tables.
- Add `NetworkReadTools` and `NetworkWriteTools` to explicit registration.
- Separate generic Batch and Network domain operation lists.
- Describe the shared operation-batching kernel and dependency direction.
- Document the self-previewing network safety flow and single hardware snapshot.
- Update payload-budget, testing, and access-enforcement sections.

### `AGENTS.md` and `CLAUDE.md`

- Correct the stale public tool count and describe the 14/4 mode split.
- Add the Network domain and shared operation-batching layer to the solution conventions.
- State that network operations must use the Network request/catalog/invoker and must not
  be added to generic batches.
- Add the self-previewing `network_write` safety workflow.
- Extend the linked-source test rule to the new directories.
- Clarify that new worker operations are registered in their owning domain catalog rather
  than automatically in `BatchOperationCatalog`.
- Preserve guidance unique to either file instead of mechanically replacing one with the
  other.

A repository-wide documentation search must find no remaining claim that the four network
operations are available through generic batches.

## Verification and Completion Gates

Phase 1 is complete only when all of the following pass with fresh evidence:

1. Focused red tests were observed before production changes and pass afterward.
2. The serialized stub build passes:

   ```powershell
   dotnet build TiaMcpServer.sln --no-restore -m:1 /p:UseTiaPortalReferenceStubs=true
   ```

3. The full `TiaMcpServer.Tests` suite passes.
4. The repository coverage threshold passes for materially changed production logic.
5. A public-schema test proves exactly 14 tools in read-write mode and 4 in read-only mode.
6. A repository search proves the generic batch surface contains none of the four network
   operations.
7. Diff review confirms the worker handlers and Openness implementation files were not
   modified.
8. Documentation consistently describes the new surface and the Phase 2 boundary.

Build, tests, schema checks, and coverage are not evidence of TIA Portal runtime behavior.
No live TIA Portal operation is run or claimed for Phase 1.

## Non-Goals

Phase 1 does not include:

- structured per-operation result objects or removal of nested JSON strings;
- subnet lifecycle operations;
- IO-system attribute editing;
- generic network attribute reads or writes;
- deterministic selectors for future topology objects;
- post-write network reads or comparisons;
- automatic save, compile, download, or commissioning;
- transaction, rollback, or exclusive-access changes;
- compatibility aliases or a deprecation period in generic batches;
- changes to worker request contracts, worker dispatch handlers, or Openness logic;
- validation or replacement of the unverified subnet/IO-system reflection calls;
- live TIA Portal or hardware testing.
