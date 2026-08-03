# Architecture

This document describes the current `tia-portal-mcp` runtime architecture, tool
surface, access-mode enforcement, project binding, write-safety model, and test
strategy.

## 1. Process topology

Siemens TIA Portal Openness is a .NET Framework 4.8 API. The MCP host targets
.NET 8, so the product is intentionally split into two processes:

```text
MCP client
    |
    | MCP JSON-RPC over stdio
    v
TiaMcpServer (net8.0 host)
    |
    | newline-delimited JSON over private stdin/stdout pipes
    v
TiaMcpServer.OpennessWorker (net48 worker)
    |
    | Siemens Openness / .NET Remoting
    v
TIA Portal V21
```

The host owns the MCP protocol, dependency injection, tool registration,
access policy, session binding, batching, diagnostics, and write-safety tokens.
The worker owns every Siemens API call and keeps one long-lived TIA Portal
attachment for its process lifetime.

`TiaMcpServer.Contracts` targets `netstandard2.0` and contains the request,
response, failure-category, path-normalization, and access-policy contracts
shared by both processes.

## 2. Host startup and access modes

`TiaMcpServer/Program.cs` handles three entry paths:

- `doctor` runs environment diagnostics.
- `--version` or `-v` prints version information.
- All other invocations start the MCP server.

The access mode is resolved once at startup with this precedence:

1. `--access-mode read-only|read-write`, `--read-only`, or `--read-write`
2. `TIA_MCP_ACCESS_MODE`
3. `read-write` by default

Malformed explicit access-mode arguments are rejected instead of silently
falling back to another mode.

### Read-write mode

Read-write mode exposes 14 tools: the four read-only observation tools plus ten
read-write-only tools. It preserves the preview-then-apply safety-token model.

### Read-only mode

Read-only mode exposes observation tools only. It never opens, creates, saves,
archives, switches, or closes a project; never compiles; never controls a PLC;
and never performs project-data mutations. It operates only on a project that
is already open in the attached TIA Portal instance.

The read-only surface contains exactly four tools.

A supplied `projectPath` in read-only mode is an assertion. It must identify the
currently open project; it is never used to open or switch projects.

## 3. Explicit MCP tool registration

Tool registration is explicit and mode-dependent. The host always registers:

- `ProjectReadTools`
- `ReadBatchTools`
- `NetworkReadTools`

It registers the following only in read-write mode:

- `ProjectEngineeringTools`
- `ProjectWriteTools`
- `WriteBatchTools`
- `NetworkWriteTools`

This prevents write tools from appearing in MCP discovery when the server is
read-only. Decorated tool classes that are not explicitly registered are not
part of the active tool surface.

### Read-only tool surface

| Tool | Purpose |
|---|---|
| `get_project_status` | Return status and metadata for the project already open in TIA Portal. |
| `browse_project_tree` | Return a bounded project subtree using optional `depth` and `startPath`. |
| `execute_read_batch` | Execute up to 50 validated observation operations. |
| `network_read` | Execute up to 50 validated network observation operations. |

The read batch supports:

- `read_cross_references`
- `get_block_content`
- `list_tag_tables`
- `get_type_content`

`network_read` owns the network-read catalog:

- `read_hardware_config`
- `search_equipment_catalog`

### Additional read-write tools

| Tool | Purpose |
|---|---|
| `compile_check` | Compile a PLC or selected block and return compiler messages. This engineering tool is read-write-only and does not use a safety token. |
| `open_project` | Open and bind a project. |
| `create_project` | Create and bind a project. |
| `save_project` | Save the active project. |
| `save_project_as` | Save a copy and rebind to the worker-reported project path. |
| `archive_project` | Archive the active project. |
| `close_project` | Close the active project and clear the binding. |
| `preview_write_batch` | Validate writes, capture current state, and issue a safety token. |
| `apply_write_batch` | Redeem the token and execute writes sequentially. |
| `network_write` | Preview or apply an ordered dedicated-network write request. |

## 4. Defense-in-depth access enforcement

Read-only mode is enforced independently at three layers.

### 4.1 Tool discovery

Write tool classes are not registered in read-only mode, so MCP clients cannot
discover or invoke them through the normal protocol surface.

### 4.2 Host authorization

`OperationAccessPolicy` checks each worker operation before the child process is
started or a request is written. The shared `OperationPolicyCatalog` classifies
all known worker operations as observation, temporary export, compilation,
project lifecycle, project mutation, or online control.

Read-only mode allows only observation and temporary-export capabilities.
Unknown operations are denied by default.

### 4.3 Worker authorization

The host passes its resolved access mode to the worker process. The worker runs
`WorkerOperationAuthorization` before dispatching any request handler or
calling Siemens APIs.

Missing worker configuration preserves the historical read-write default.
Explicit but malformed worker configuration fails closed to read-only, so an
argument-propagation defect cannot silently enable mutations.

In read-only mode the Siemens-facing `TiaPortalSession` also refuses automatic
confirmation dialogs. Read-write mode may accept confirmations where the
existing write workflow explicitly allows them.

## 5. Project attachment and binding

`ProjectSessionBinding` is the host's view of the project associated with the
MCP session. It can be seeded from `--project` or `TIA_MCP_PROJECT_PATH`.
Successful lifecycle operations bind only to the resolved path returned by the
worker, never to a path reconstructed from caller input.

`OpennessWorkerClient` owns binding transitions:

- open, create, and rebinding save-as bind to worker-reported ground truth;
- close clears the binding;
- ordinary reads and writes do not implicitly bind an unbound session;
- divergence between the host binding and the worker-reported project is
  surfaced as a warning.

On the worker side, `WithProject` first attaches to the running TIA Portal
instance and then applies the project-open policy. In read-only mode it reuses
only the project discovered during that attachment.

## 6. Worker transport and execution

`PersistentWorkerTransport` owns one child process and serializes requests with
a `SemaphoreSlim`. Each call writes one JSON line and reads one JSON response
line. A timeout, crash, broken pipe, null response, or protocol desynchronization
terminates the worker; the next request starts a fresh process.

`OpennessWorkerClient` is the typed host facade. It constructs `WorkerRequest`
objects, performs host authorization, invokes the transport, normalizes failure
categories, caps warning output, and applies project-binding transitions.

The worker dispatch loop maps every method name to a focused Siemens operation.
`Execute` centralizes exception mapping and response stamping. The actual
Openness implementations live under `TiaMcpServer.OpennessWorker/Openness/`.

Temporary exports such as `get_block_content` use isolated temporary
directories and remove them in `finally` blocks.

## 7. Batch and network execution

`BatchOperationCatalog` and `BatchWorkerInvoker` own only generic batch operations.
`NetworkOperationCatalog` and `NetworkWorkerInvoker` own the four dedicated network
operations. Each domain validates against its own request type and catalog before a worker
invocation; a new worker method belongs to its owning domain catalog and is not implicitly
a generic batch operation.

`OperationBatches` provides request-agnostic shared execution, result formatting, and
payload-budget infrastructure to both domains. Its network call sites use network-specific
payload-budget hints, such as narrowing `query`/`maxResults` or splitting a network batch.

Both catalogs enforce a maximum of 50 operations and reject unknown operations, missing
required fields, inapplicable fields, and invalid bounds before worker invocation.

Read batches execute items independently. One failed read does not prevent the
remaining items from running.

Write batches execute sequentially and stop on the first failure. Already
completed writes are not rolled back.

`network_write` is self-previewing. A call with `confirm:false` and no token snapshots the
topology once and issues a token bound to the exact ordered request. A call with
`confirm:true`, the unchanged operation list, and that token takes one fresh topology
snapshot for validation, applies sequentially with no rollback, and appends an audit record.

`compile_check` is absent from read-only tool discovery. The underlying access
policy also rejects internal compile requests in read-only mode before they are
sent to the worker.

## 7a. The opt-in canonical JSON seam and the Network Phase 2 structured contract

`network_read` and `network_write` are the first (and, as of this writing, only) tools to opt
into a reusable canonical-JSON gate. This is deliberately additive: every other tool keeps its
existing text contract unchanged.

- `TiaMcpServer/Json/CanonicalJson.cs` provides strict typed parsing (rejects duplicate
  properties, unmapped members, and case-mismatched names) and a repository-defined canonical
  serialization (recursive ordinal property ordering, preserved array order, explicit nulls,
  compact UTF-8). It is *not* an RFC 8785 claim — see the plan's Global Constraints.
- `TiaMcpServer/Tools/StructuredToolResult.cs` renders one canonical JSON document and returns it
  as both the `content` text block and a detached `structuredContent` `JsonElement`, from a single
  `CanonicalJson.Serialize` call, so the two representations cannot drift apart.
- `TiaMcpServer/OperationBatches/StructuredOperationBatch*.cs` provides the shared
  item/failure/omission/count/truncation batch model and a read/write execution engine whose
  stop decision covers `protocol_error` alongside ordinary worker failures.
- `TiaMcpServer/Safety/CanonicalWriteSafety.cs` adds canonical, typed counterparts
  (`CreateCanonicalPreview`, `ValidateCanonicalEnvelope`, `ValidateAndConsumeCanonical`,
  `AppendCanonicalAudit`) to `WriteSafetyService`. Binding still happens through
  `CanonicalJson.Serialize`, so a token survives pure JSON-property reordering while still
  rejecting a changed value, type, or array order.

Any future tool that wants a single-layer structured JSON contract reuses this same seam rather
than inventing a parallel one.

### Typed Network payload registry

`TiaMcpServer/Network/NetworkPayloadContract.cs` is the only decoder of Network worker success
payloads. It maps each of the four network operations to exactly one declared CLR result type
(`HardwareConfigInfo`, `CatalogEntryInfo[]`, `AddDeviceResultInfo`,
`ConfigureNetworkDeviceResultInfo`) and rejects anything that does not match — a malformed,
unknown, wrongly cased, or wrongly typed payload becomes a failed item with category
`protocol_error` rather than being forwarded under a schema that does not describe it. The
rejected payload is never echoed back to the caller.

### Canonical safety flow (network writes)

`network_write` is self-previewing and binds three canonical representations through
`CanonicalWriteSafety`: the resolved `NetworkWriteTargetEvidence[]` (what will be acted on), the
caller's `NetworkOperationRequest[]` (what was asked), and the `HardwareConfigInfo` current state
(what exists right now). Preview issues a token bound to all three; apply re-reads state, re-resolves
targets against that fresh read, and only then validates and consumes the token — so a state change
between preview and apply (a rename, a deletion, a newly ambiguous selector) invalidates it. The
audit record appended by `AppendCanonicalAudit` stores the exact response document the caller
received as structured JSON, not a re-rendering of it.

### Exact host-to-worker selector boundary

Selector resolution happens **twice, independently, on both sides of the process boundary** —
this is defense-in-depth, the same pattern used for read-only access enforcement (§4):

- **Host side** (`TiaMcpServer/Network/NetworkIdentityResolver.cs`): resolves device, node,
  subnet, and IO-system identity from a `HardwareConfigInfo` snapshot the host itself just read.
  This resolution produces the `NetworkWriteTargetEvidence` the safety token binds to and the
  preview response reports — it never touches Siemens Openness.
- **Worker side** (`TiaMcpServer.OpennessWorker/Openness/NetworkDeviceConfigurator.cs`):
  independently matches the same `deviceName`/`nodeId` selector against live Openness objects at
  the moment of the actual write, walking every nested device item and network interface.

Both sides apply the identical rule: a selector that matches zero, more than one, or a candidate
whose own identity could not be read all fail closed (`postcondition_failed`) rather than
resolving to a first match, a first node, or a name-only guess. The host never forwards a
pre-resolved object reference to the worker — only the caller's own `deviceName`/`nodeId` (and,
where applicable, `subnetId`/IO-system `number`) cross the process boundary, so the worker's
independent resolution is a real second check, not a formality.

## 8. Write safety

Generic batch data writes use a two-tool flow; lifecycle and network writes are
self-previewing:

1. The preview call (`preview_write_batch`, or the same lifecycle/network tool with no
   token and `confirm:false`) reads current state, produces a human-readable description,
   and creates a short-lived, single-use safety token bound to the tool,
   project, requested input, and current-state hashes.
2. The apply call (`apply_write_batch`, or the same lifecycle/network tool) supplies
   `confirm=true` and the token. The server reads current
   state again and consumes the token only when every bound value still matches.

Changed input, changed project state, wrong tool, wrong project, expiry, or token
reuse causes rejection. Completed writes are appended to the audit log under
`%LOCALAPPDATA%\TiaMcpServer\audit`.

Read-only mode is categorically stronger than this token flow: confirmation and
a valid token cannot override the access policy.

## 9. Diagnostics

`tia-mcp doctor` runs the environment diagnostic pipeline without starting the
MCP host. It supports text or JSON output and reports the resolved access mode.
The command accepts the same access-mode options as normal startup, in addition
to `TIA_MCP_ACCESS_MODE`.

## 10. Testing

`TiaMcpServer.Tests` links selected host and worker source files directly into
the test assembly, allowing policy, parsing, tool metadata, generic-batch and network
catalog/invoker behavior, diagnostics, and IPC behavior to be tested on .NET 8 without a
live TIA Portal installation. `TiaMcpServer.FakeWorker` covers the linked-source network
requests and forwarded worker methods without a live TIA Portal installation.

`TiaMcpServer.FakeWorker` exercises persistent transport behavior. Fake service
implementations isolate filesystem, registry, process, identity, and diagnostic
checks.

The read-only test suite covers:

- CLI and environment resolution;
- malformed-configuration behavior;
- operation-catalog classifications and deny-by-default behavior;
- host and worker authorization;
- conditional tool surfaces;
- batch access validation;
- confirmation and safety-token bypass prevention;
- doctor output and CLI parity.

Manual integration testing with a live TIA Portal remains necessary to validate
Siemens-specific attachment, confirmation, project-path, packaging, and worker
launch behavior.

`scripts/live-test-network-phase2.ps1` is a separately authorized PowerShell 7 harness that
launches the real MCP host and drives the actual `initialize`/`tools/list`/`tools/call` sequence
against `network_read`/`network_write` — proving the public protocol against real TIA Portal V21,
not direct worker IPC as `live-test-db.ps1`/`live-test-udt.ps1` do. It is never run by any
automated test or CI gate; `TiaMcpServer.Tests/NetworkLiveHarnessContractTests.cs` proves this,
and every other harness invariant (PowerShell-7 requirement, non-mutating default mode, Preview's
inability to reach a confirming apply call, Apply's explicit-switch-plus-identity gate), by
reading the script's own source text rather than executing it.

## 11. Keeping this document current

Update this document when tool registration, operation classification, access
modes, worker launch arguments, binding rules, or write-safety behavior changes.
The architecture must describe the explicitly registered runtime surface, not
only the set of decorated tool classes present in the assembly.
