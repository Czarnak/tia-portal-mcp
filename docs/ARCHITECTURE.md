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

Read-write mode preserves the complete tool surface and the existing
preview-then-apply safety-token model.

### Read-only mode

Read-only mode exposes observation tools only. It never opens, creates, saves,
archives, switches, or closes a project; never compiles; never controls a PLC;
and never performs project-data mutations. It operates only on a project that
is already open in the attached TIA Portal instance.

A supplied `projectPath` in read-only mode is an assertion. It must identify the
currently open project; it is never used to open or switch projects.

## 3. Explicit MCP tool registration

Tool registration is explicit and mode-dependent. The host always registers:

- `ProjectReadTools`
- `ReadBatchTools`

It registers the following only in read-write mode:

- `ProjectWriteTools`
- `WriteBatchTools`

This prevents write tools from appearing in MCP discovery when the server is
read-only. Decorated tool classes that are not explicitly registered are not
part of the active tool surface.

### Read-only tool surface

| Tool | Purpose |
|---|---|
| `get_project_status` | Return status and metadata for the project already open in TIA Portal. |
| `execute_read_batch` | Execute up to 50 validated observation operations. |

The read batch supports:

- `browse_project_tree`
- `read_hardware_config`
- `search_equipment_catalog`
- `read_cross_references`
- `get_block_content`
- `list_tag_tables`
- `get_project_status`

`compile_check` remains part of the read-batch catalog for read-write mode, but
it is denied in read-only mode because the Siemens compilation API may modify
internal project state.

### Additional read-write tools

| Tool | Purpose |
|---|---|
| `open_project` | Open and bind a project. |
| `create_project` | Create and bind a project. |
| `save_project` | Save the active project. |
| `save_project_as` | Save a copy and rebind to the worker-reported project path. |
| `archive_project` | Archive the active project. |
| `close_project` | Close the active project and clear the binding. |
| `preview_write_batch` | Validate writes, capture current state, and issue a safety token. |
| `apply_write_batch` | Redeem the token and execute writes sequentially. |

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

## 7. Batch execution

`BatchOperationCatalog` is the batch source of truth for operation names,
categories, required fields, optional fields, and the maximum batch size of 50.
It rejects unknown operations, missing required fields, inapplicable fields,
and invalid bounds before worker invocation.

Read batches execute items independently. One failed read does not prevent the
remaining items from running.

Write batches execute sequentially and stop on the first failure. Already
completed writes are not rolled back.

Access-mode validation runs before a read batch starts. This is why a
`compile_check` item is rejected as a whole-batch validation error in read-only
mode rather than being sent to the worker.

## 8. Write safety

Every write uses a two-step flow:

1. The preview call reads current state, produces a human-readable description,
   and creates a short-lived, single-use safety token bound to the tool,
   project, requested input, and current-state hashes.
2. The apply call supplies `confirm=true` and the token. The server reads current
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
the test assembly, allowing policy, parsing, tool metadata, batch, diagnostics,
and IPC behavior to be tested on .NET 8 without a live TIA Portal installation.

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

## 11. Keeping this document current

Update this document when tool registration, operation classification, access
modes, worker launch arguments, binding rules, or write-safety behavior changes.
The architecture must describe the explicitly registered runtime surface, not
only the set of decorated tool classes present in the assembly.
