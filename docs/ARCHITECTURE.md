# Architecture

This document explains how `tia-portal-mcp` is put together: the process
topology, how a tool call travels from an MCP client down into Siemens
Openness and back, the write-safety model, and exactly what is exposed to
clients today.

It was produced by walking the indexed symbol graph (jcodemunch) and the
`graphify` knowledge graph (`graphify-out/GRAPH_REPORT.md`) rather than by
hand; re-run those tools after structural changes and refresh this file to
match.

## 1. Why two processes

Siemens TIA Portal Openness (`Siemens.Engineering.*`) is a .NET Framework 4.8
API built on .NET Remoting. It cannot be loaded into a modern .NET 8 process.
That single constraint drives the whole topology:

```
MCP client (Claude Desktop / other MCP host)
        │  stdio, JSON-RPC (MCP protocol)
        ▼
┌───────────────────────────────┐
│ TiaMcpServer            (host, net8.0) │
│  - ModelContextProtocol C# SDK          │
│  - 10 MCP tools ([McpServerTool])       │
│  - Batch engine, write-safety tokens    │
└───────────────────────────────┘
        │  newline-delimited JSON over stdin/stdout
        │  (own private child-process pipe, unrelated to the MCP transport)
        ▼
┌───────────────────────────────┐
│ TiaMcpServer.OpennessWorker (worker, net48) │
│  - Loads Siemens.Engineering.*             │
│  - One shared, long-lived TIA session      │
│  - All real Openness calls happen here     │
└───────────────────────────────┘
        │  Siemens Openness / .NET Remoting
        ▼
              TIA Portal V21 process
```

The **host** is what an MCP client actually launches and speaks MCP to. The
**worker** is a plain console EXE the host spawns as a child process purely
for IPC; it never talks MCP and is invisible to the client. The host copies
the worker build into `openness-worker/` next to itself at build time, and
`OpennessWorkerLocator` resolves that path at runtime (`TiaMcpServer.OpennessWorker.exe`
under an `openness-worker/` subdirectory relative to the host's base
directory).

`TiaMcpServer.Contracts` (netstandard2.0) is the only thing both processes
reference — it has zero Siemens dependency, so it compiles under both TFMs and
carries every request/response DTO across the pipe.

## 2. Host process (`TiaMcpServer`, net8.0)

`Program.cs` is a thin dispatcher, not just an MCP entry point:

- `doctor` → `Cli/DoctorCommand` (environment diagnostics, see §6)
- `--version` / `-v` → `Cli/VersionCommand`
- anything else → boots a generic-host MCP server:
  - `AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()` — the
    official ModelContextProtocol SDK scans the assembly for
    `[McpServerToolType]` classes and registers every `[McpServerTool]`
    method it finds. There is no manual tool-registration list; adding a tool
    means adding a decorated static method.
  - DI singletons: `ProjectSessionBinding` (which `.ap21` project this MCP
    session is bound to — seeded from `--project <path>` or
    `TIA_MCP_PROJECT_PATH`), `WriteSafetyService`, and `OpennessWorkerClient`.

Startup project binding matters: once a session opens or creates a project,
`ProjectSessionBinding` remembers it, and subsequent tool calls default to
that project unless a call explicitly targets a different path (which trips
binding-divergence warnings — see `OpennessWorkerClient.WarnOnBindingDivergence`).

### `Worker/` — the IPC client

- **`PersistentWorkerTransport`** owns the actual child `Process` and a
  `SemaphoreSlim(1,1)` gate, so requests are single-flight — one in-flight
  Openness call at a time, which matches Openness's own single-threaded
  session model. It writes one JSON line to the worker's stdin, reads one
  JSON line back, and:
  - keeps the last 30 stderr lines in a ring buffer to surface crash context,
  - restarts the worker process automatically if it has died or was never
    started (`EnsureProcessStarted`),
  - on read failure, captures whatever crash detail it can and fails the call
    rather than hanging.
- **`OpennessWorkerClient`** is the typed façade the tools actually call
  (`BrowseProjectTreeAsync`, `CreateTagAsync`, `OpenProjectAsync`, …). It
  builds a `WorkerRequest`, sends it through the transport, and layers
  session-binding logic on top of the raw call — e.g. `OpenProjectAsync`
  transitions `ProjectSessionBinding` on success, `SaveProjectAsAsync` always
  rebinds (Siemens `SaveAs` switches the *active* project in Openness, so a
  non-rebinding save would desync the worker's active project from the MCP
  session's belief about it), and long warning arrays coming back from the
  worker are capped (`CapWarnings`, 20 lines / 1000 chars per line) before
  they reach the client.
- **`OpennessWorkerLocator`** just resolves the worker executable path.

### `Batch/` — the multi-operation engine

- **`BatchOperationCatalog`** is the single source of truth for which
  operation names exist, whether each is a `Read` or `Write`, and its
  required/optional fields. It validates a batch request array against that
  spec before anything is dispatched: unknown operation name, missing
  required field, or a field that doesn't apply to that operation all fail
  fast with a structured error — the worker is never invoked for a
  malformed batch. `MaxBatchSize = 50`.
- **`BatchOperationRequest`** is one flat DTO with every possible field
  across every operation (nullable), keyed by `operationId` + `operation`.
  Flat-and-nullable was a deliberate tradeoff: one shape for 25 operations
  instead of 25 polymorphic request types, at the cost of catalog-based
  validation instead of the type system doing it.
- **`BatchExecutionEngine`** encodes the two execution semantics:
  - `ExecuteReadsAsync` — every read runs independently; one failing item is
    recorded in its own result slot and never stops the others.
  - `ApplyWritesAsync` — writes run **sequentially** and stop at the first
    failure; remaining items are marked skipped. There is no rollback of
    already-applied writes.
- **`BatchTools`** is where the three batch operations become MCP tools (see
  §4) — it also builds the *combined current state* string (concatenation of
  each operation's current-state read) that becomes part of the safety
  token's binding for `preview_write_batch`/`apply_write_batch`.
- `BatchPayloadBudget`, `BatchResultFormatter`, `BatchSafetySnapshot`,
  `BatchWorkerInvoker` are supporting pieces: bounding oversized batch
  payloads, formatting the per-item result array, and snapshotting safety
  state.

### `Safety/` — preview-then-apply tokens

- **`WriteSafetyService`** issues and redeems single-use tokens:
  - `CreatePreview(toolName, projectPath, target, summary, requestedInput, currentState, …)`
    hashes `requestedInput` and `currentState`, stores a `SafetyTokenEntry`
    keyed by a random token, and returns a preview payload containing that
    token. Default lifetime is `TimeSpan.FromMinutes(10)`.
  - `ValidateAndConsume(...)` re-hashes the *current* requested input and
    current state and compares against what's stored for that token +
    tool name + project path. Any mismatch — different input, different
    project state, wrong tool, expired, already consumed — is rejected with
    a categorized reason (`FailureCategory`, e.g. `validation_error`,
    `binding_conflict`). On success the token is removed (single-use) and the
    write proceeds.
  - `AppendAudit(...)` writes a JSONL record for every completed write to
    `%LOCALAPPDATA%\TiaMcpServer\audit`.
- **`WriteSafetyTooling`** is the reusable glue each write tool calls:
  `ValidateForApplyAsync` (read current state, then validate+consume),
  `CreatePreview` (read current state, then create the token), plus
  presentation helpers (`CreateLineDiff`, `DescribePathState`,
  `DescribeProjectCreationState`) used to make previews human-readable.

This preview→token→apply loop is why the write safety model is described as
non-negotiable in the project's `CLAUDE.md`: every write tool, whether a
single lifecycle tool or a batch, goes through the same
`WriteSafetyService`/`WriteSafetyTooling` pair.

### `Diagnostics/` + `Cli/` — the `doctor` subcommand

`TiaMcpServer doctor` runs a fixed pipeline of `IDiagnosticCheck`
implementations (`OperatingSystemCheck`, `DotNetFrameworkCheck`,
`DotNetRuntimeCheck`, `TiaPortalInstallationCheck`, `TiaPortalProcessCheck`,
`OpennessAssembliesCheck`, `OpennessGroupCheck`, `OpennessWorkerCheck`,
`HostWorkerVersionCheck`, `ProjectBindingCheck`) and renders a pass/fail
report via `DoctorTextRenderer` or `DoctorJsonRenderer` (`DoctorCliParser`
picks the format). This is the operator-facing "is my environment sane"
tool — separate from anything an MCP client calls.

## 3. Worker process (`TiaMcpServer.OpennessWorker`, net48)

`Program.cs` here is a request-dispatch loop, not a hosted service:

1. `Main()` reads newline-delimited JSON from stdin in a loop.
2. `HandleLine` deserializes a `WorkerRequest`, dispatches on
   `request.Method` to one private static handler per operation (30+
   handlers — `BrowseProjectTree`, `CreateTag`, `OpenProject`, … one per
   entry in `BatchOperationCatalog` plus the 6 project-lifecycle ops that
   are intentionally excluded from batches), and writes back one JSON
   `WorkerResponse` line.
3. Two composition helpers wrap every handler:
   - `WithSession` / `WithProject` acquire the module-level
     `_sharedSession` (`WorkerTiaPortalSession`, constructed once with
     `allowTiaConfirmations: true`) and ensure the requested project is the
     one currently open before running the handler body. The session is
     process-lifetime and shared across every request — TIA's own
     "attach to running instance" confirmation dialog would otherwise pop up
     on every single call, so the worker attaches once and reuses that
     attachment.
   - `Execute(Func<WorkerResponse> body)` is the single place that catches
     Openness exceptions and maps them to a categorized `WorkerResponse`
     failure, and stamps timing/operation metadata (`Stamp`) onto every
     response, success or failure.

The actual Openness logic (project tree walking, block import/export,
hardware config reads, tag/constant mutation, cross-reference reads, compile
checks, network device configuration, PLC online start/stop, block source
generation and postcondition verification, …) lives in
`TiaMcpServer.OpennessWorker/Openness/*` — roughly three dozen focused
classes, each owning one concern (e.g. `BlockImportCoordinator`,
`CrossReferenceReader`, `HardwareConfigReader`, `TagMutationService`,
`PlcOnlineService`). The dispatch handlers in `Program.cs` are deliberately
thin: they parse the request, call into one of these services, and hand the
result to `Execute`/`Stamp`.

`AssemblyResolver.cs` handles locating the Siemens PublicAPI assemblies at
runtime so the worker can load `Siemens.Engineering.dll` without them being
shipped in the repo (they're licensed, machine-installed DLLs — see
`ref/` for the compile-time stubs used in CI).

## 4. What's exposed to an MCP client today

Exactly **10** tools, registered via `[McpServerToolType]` /
`[McpServerTool(Name = "...")]` and auto-discovered by
`WithToolsFromAssembly()` — confirmed by decorator census over the indexed
source, not by reading a registration list (there isn't one).

### `Tools/ProjectLifecycleTools.cs` — 7 self-previewing lifecycle tools

Each of these is call-twice-yourself: call without `safetyToken` to get a
preview + token, review it, call again with the same arguments plus
`confirm=true` and the token.

| Tool | Purpose |
|---|---|
| `get_project_status` | Read-only; status/metadata for the active project. No safety token needed. |
| `open_project` | Open a `.ap21` and bind this MCP session to it (`forceRebind` to rebind an already-bound session). |
| `create_project` | Create a new project and bind the session to it. |
| `save_project` | Save the active project. |
| `save_project_as` | Save-as to a copy directory. `rebind` must be `true` — Siemens `SaveAs` switches the active project in Openness itself, so a non-rebinding save would desync worker state from session state. |
| `archive_project` | Archive the active project (`mode`: None / DiscardRestorableData / Compressed / DiscardRestorableDataAndCompressed). |
| `close_project` | Close the active project and clear the session binding. |

### `Batch/BatchTools.cs` — 3 generic batch tools

| Tool | Purpose |
|---|---|
| `execute_read_batch` | Up to 50 read operations in one call, independent execution, one shared `BatchOperationRequest[]` shape. |
| `preview_write_batch` | Validate up to 50 write operations, read their combined current state, and issue one batch-level safety token. |
| `apply_write_batch` | Redeem that token and apply the same operation list sequentially, stopping at the first failure. |

**8 read operations** reachable through `execute_read_batch`:
`browse_project_tree`, `read_hardware_config`, `search_equipment_catalog`,
`read_cross_references`, `get_block_content`, `list_tag_tables`,
`compile_check`, `get_project_status`.

**17 write operations** reachable through `preview_write_batch` /
`apply_write_batch`: `update_block_logic`, `create_tag_table`,
`delete_tag_table`, `create_tag`, `update_tag`, `delete_tag`,
`create_user_constant`, `update_user_constant`, `delete_user_constant`,
`add_network_device`, `configure_network_device`, `create_block`,
`delete_block`, `create_block_group`, `delete_block_group`, `start_plc`,
`stop_plc`.

The 6 project-lifecycle operations (`open_project`, `create_project`,
`save_project`, `save_project_as`, `archive_project`, `close_project`) exist
as real single tools but are deliberately excluded from the batch catalog
(`BatchOperationCatalog.NonBatchableOperations`) — they change which project
is active/bound, which doesn't compose with "50 independent operations in
one call."

So the effective surface area is **10 MCP tools** covering **31 distinct
underlying operations** (8 read + 17 write + 6 lifecycle), all funneled
through one write-safety model and one IPC transport.

## 5. Write safety, end to end

For any write — single-tool or inside a batch — the shape is always:

1. **Preview call** (no `safetyToken`): the tool reads current state via the
   worker, builds a human-readable preview/diff, and calls
   `WriteSafetyService.CreatePreview` to mint a token bound to
   `(toolName, projectPath, target, hash(requestedInput), hash(currentState))`.
   Response includes the token and expiry (10 minutes).
2. **Apply call** (`confirm=true` + `safetyToken`): `ValidateAndConsume`
   re-derives both hashes from what was just requested and what state is
   *now* current, and only proceeds if both still match what's stored under
   that token. Any drift — someone else changed the project, the caller
   changed the request, the token expired or was already used — is rejected
   with a categorized error, never silently applied.
3. On success, `AppendAudit` writes a JSONL line to
   `%LOCALAPPDATA%\TiaMcpServer\audit` and the worker performs the mutation.

This is why reordering a batch, changing an operation's parameters, or the
project state changing between preview and apply all invalidate the token —
by design, not as an edge case.

## 6. Testing shape (context for the architecture, not a full test guide)

`TiaMcpServer.Tests` doesn't reference the host/worker projects — it
compiles their source files directly via `<Compile Include>`, which is why
it can exercise `TiaMcpServer/Worker`, `TiaMcpServer/Batch`,
`TiaMcpServer/Safety`, `TiaMcpServer/Tools`, `TiaMcpServer/Diagnostics`, and
`TiaMcpServer/Cli` on net8.0 without ever needing the net48 Openness worker
to build. `TiaMcpServer.FakeWorker` is a scripted stand-in process used to
test the IPC layer (`PersistentWorkerTransport`, `OpennessWorkerClient`)
without a real TIA Portal installation. Fake `I*Service` implementations
(`FakeFileSystemService`, `FakeRegistryService`, `FakeProcessEnumerationService`,
…) play the same role for the `doctor` diagnostic checks.

## 7. Keeping this document current

- Re-run `graphify update .` after structural changes (AST-only, no API
  cost) and skim `graphify-out/GRAPH_REPORT.md` for new god nodes or
  communities before updating this file.
- If a tool is added/removed, it will show up as a change in the
  `[McpServerTool]` decorator census — update §4's table and the operation
  counts in §4/§5.
- If an operation is added to `BatchOperationCatalog.BuildSpecs`, update the
  read/write operation lists in §4.
