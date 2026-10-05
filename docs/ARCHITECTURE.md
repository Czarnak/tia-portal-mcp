# Architecture

This document describes the current `tia-portal-mcp` runtime architecture, tool
surface, access-mode enforcement, project binding, write-safety model, and test
strategy.

## 1. Process topology

Siemens TIA Portal Openness is a .NET Framework 4.8 API. The MCP host targets
.NET 10, so the product is intentionally split into two processes:

```text
MCP client
    |
    | MCP JSON-RPC over stdio
    v
TiaMcpServer (net10.0 host)
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

The source-owned references under `reference-stubs/` generate the two compile-only `ref/`
assemblies and retain version `21.0.0.0` and public-key token `29bfe5fdf4ba5d3b` using a
public-only delay-signing key. Only the net48 worker and compile-only probe reference Siemens;
the host and shared contracts remain Siemens-free. Generated stubs are never runtime inputs,
and all Siemens binaries are excluded from packages; the worker loads installed real DLLs.
The passive Multiuser contracts add no public operation or binding behavior. The worker's
internal active project context supplies typed ownership; public `.als21` selection/open
remains deferred. See [the Multiuser boundary](SupportedOperations/MULTIUSER_OPERATIONS_SUMMARY.md) and
[build boundary and qualification](development/building.md#generated-openness-references).

## 2. Host startup and access modes

`TiaMcpServer/Program.cs` handles four entry paths:

- `install` registers a client or previews registration with `--dry-run`; its default is read-only.
- `doctor` runs environment diagnostics.
- `--version` or `-v` prints version information.
- All other invocations start the MCP server.

The access mode is resolved once at startup with this precedence:

1. `--access-mode read-only|read-write|full`, `--read-only`, or `--read-write`
2. `TIA_MCP_ACCESS_MODE`
3. `read-write` by default

Malformed explicit access-mode arguments are rejected instead of silently
falling back to another mode.

### Read-write mode

Read-write exposes sixteen tools: five observation tools, `bind_project`, `compile_check`, the
batch write pair, `network_write`, and six lifecycle tools. It permits `Observe`, `TemporaryExport`,
transitional `SafetyRead`, `SessionSelection`, `Compile`, `ProjectMutation`, and `ProjectLifecycle`.
Legacy batches retain tokens. Network never elicits; every actual lifecycle call asks once through client form elicitation.
Legacy batch PLC control is denied before binding, snapshots, or dispatch. Reads never bind,
switch, or open; `bind_project` selects an already-open project.

### Full mode

Full exposes the same sixteen tools and adds `OnlineControl` (PLC run/stop). Lifecycle runs
under policy without server elicitation. Block guards stop the call in every mode.

### Confirmation configuration

Confirmation derives from the binding gate's access mode. Read-write asks once for every actual
lifecycle call, even with no guards or only info guards; form acceptance needs `accept` and boolean
`confirm:true`. Unsupported capability, decline, cancel, timeout, or transport failure denies with
`access_denied`. Full uses policy satisfaction without server elicitation. Dry runs and block guards
never elicit. Lifecycle has no agent confirmation list; only generic batches retain tokens. Network uses guarded execution without server elicitation.
The removed startup switch is rejected by `RemovedCliOptions` with:

```text
--confirm-with-user was removed. Confirmation follows the access mode: read-write asks for every lifecycle call; use --access-mode full to run lifecycle tools without prompts.
```

### Read-only mode

Read-only mode exposes observation tools and explicit session selection (`SessionSelection`).
It never opens, creates, saves,
archives, or closes a project; never compiles; never controls a PLC;
and never performs project-data mutations. It operates only on a project that
is already open in the attached TIA Portal instance.

The read-only surface contains six tools: five observations and `bind_project` for explicit
session selection. Binding can switch the selected Portal/project without project mutation.

A supplied `projectPath` on ordinary observation/read tools in read-only mode is an assertion.
It must identify the currently open project; those tools never use it to open or switch projects.

## 3. Explicit MCP tool registration

Tool registration is explicit and mode-dependent. The host always registers:

- `ProjectReadTools`
- `ReadBatchTools`
- `NetworkReadTools`
- `ProjectBindingTools`

The shared `McpToolRegistration.WithAccessModeTools` helper registers these in read-write and full:

- `ProjectEngineeringTools`
- `WriteBatchTools`
- `NetworkWriteTools`

`ProjectWriteTools` is registered when the mode permits `ProjectLifecycle` (read-write and full).

This prevents project mutation and lifecycle tools from appearing in MCP discovery when the server is
read-only. Decorated tool classes that are not explicitly registered are not
part of the active tool surface.

`Program.cs` and production-surface protocol tests use the same registration helper.
`BatchTools` remains an unregistered compatibility wrapper for internal callers and tests.
The lifecycle migration removes `ProjectLifecycleTools` and its now-unused token paths; it does
not retire generic-batch token support. Network-only token paths are now retired by the separate guarded Network migration. The earlier registered-surface delegation and
preview-only live V21 evidence are recorded in the
[PR 2 acceptance report](superpowers/acceptance/reports/2026-09-01-pr2-registered-tool-delegation-live.md).

### Read-only tool surface

| Tool | Purpose |
|---|---|
| `bind_project` | Adopt or switch to an already-open project without opening, creating, saving or closing. |
| `get_project_status` | Return status and metadata for the project already open in TIA Portal. |
| `browse_project_tree` | Return a canonical, paged v3 point-in-time snapshot using optional `projectPath`, typed `startSelector`, `depth`, and `pageSize`, or continue it with `cursor`. |
| `plc_read` | Execute up to 50 validated PLC read operations (`get_block_content`, `get_type_content`, `list_tag_tables`) on the structured contract. |
| `read_cross_references` | Read the cross-references of one project-tree target (leaf object, member, or container sweep) on the structured contract. |
| `network_read` | Execute up to 50 validated network observation operations. |

`execute_read_batch` was retired; its four operations moved to `plc_read` (`get_block_content`,
`get_type_content`, `list_tag_tables`) and the standalone `read_cross_references`. See the
[PLC operations summary](SupportedOperations/PLC_OPERATIONS_SUMMARY.md).

`network_read` owns the network-read catalog:

- `read_hardware_config`
- `search_equipment_catalog`
- `list_network_objects`
- `inspect_network_object`

### Project enumeration completeness

The net48 worker owns one ordered `ProjectDeviceEnumerator`: direct `Project.Devices` first, followed by a depth-first walk of `Project.DeviceGroups`. `HardwareConfigReader` and the project-tree snapshot walker both consume it, preventing their definitions of a complete project from drifting. The public project tree deliberately flattens grouped devices into ordinary `Device` nodes.

PLC user block groups and system block groups are different Openness types. The project-tree walker therefore keeps separate recursive walkers and shares only block-node construction. `SystemBlockFolder` and `IsSystemBlock` encode system-hierarchy membership, not provenance. Hardware degradation uses `HardwareConfigInfo.Messages`; project-tree best-effort diagnostics are carried as envelope warnings. The same seam reads `HeaderAuthor`, `HeaderVersion`, `HeaderFamily`, and `HeaderName` through typed `PlcBlock` properties and omits only null, empty, or whitespace values. It does not use dynamic `GetAttribute` calls. These fields report project metadata and do not attest to vendor provenance.

`HeaderVersion` follows the typed Openness value, not UI or SimaticML element presence: a non-blank typed value is emitted even when the TIA UI appears blank or an export has no `<HeaderVersion>` element. In the bounded V21 acceptance-project observation, an omitted SimaticML version was exposed as `0.1`, while clearing another version produced/exposed `0.0.0.0`. These concrete values are observations from that acceptance project, not universal Siemens guarantees. Accordingly, `details.HeaderVersion` means that Openness reported a typed value; it does not prove explicit UI or XML population. Consumers needing XML element-presence semantics should inspect an export.

### Project-tree v3 snapshot and continuation seam

The public v3 cutover uses one canonical response envelope and this exact phase boundary:

```text
cursor-free browse
    -> net48 typed/scoped walk + residual selector/depth filtering
    -> net10 strict decode + flatten
    -> bounded immutable snapshot store
    -> exact canonical page

cursor continuation
    -> authenticate process-local cursor
    -> cache lookup + query/range validation under lock
    -> exact canonical page (zero worker calls)
```

The net48 worker resolves the selector's Device before PLC discovery, walks that device scope, and applies the remaining subtree selector and depth filter to its materialized DTO tree. The host validates the typed payload and flattens it in deterministic pre-order, then stores the immutable flat snapshot before returning a complete-node page. Each initial request makes one tree-observation worker call; configured read-write startup may first make a `get_project_status` call to verify the project binding. A continuation never re-enters Siemens Openness: it authenticates the HMAC-protected cursor, retrieves the same snapshot under the store lock, validates query hash and range, and projects the next canonical page without worker IPC.

The store retains at most four snapshots, 4,000,000 canonical characters per snapshot, and 16,000,000 aggregate characters with a ten-minute sliding idle lifetime. Every public response, including failures and warnings, is capped at 60,000 canonical characters. Oversized diagnostics return a bounded `result_metadata_too_large` failure without echoing them. Cache expiry, eviction, or a cursor from a previous host process returns `snapshot_unavailable`. Malformed or unauthenticated current-process cursors return `invalid_cursor`, repeated query differences return `cursor_filter_mismatch`, and authenticated out-of-range offsets return `cursor_out_of_range`. Cached continuations validate the captured binding ID/revision and return `cursor_binding_mismatch` after a binding change, including a switch away and back. They still serve an immutable snapshot with zero worker calls.

A deeper direct-Openness selector resolver and depth-pruned traversal remains a measured follow-up, not shipped v3 behavior. It must not replace the current seam until live measurements show material benefit and the typed payload, deterministic ordering, selector ambiguity, warning, and pagination contracts remain unchanged.

### Additional read-write tools

| Tool | Purpose |
|---|---|
| `compile_check` | Compile a PLC or selected block and return compiler messages in read-write or full. Does not use a safety token. |
| `preview_write_batch` | Validate writes, capture current state, and issue a safety token. |
| `apply_write_batch` | Redeem the token and execute writes sequentially. |
| `network_write` | Preview or apply an ordered dedicated-network write request. |

### Lifecycle tools in read-write and full

| Tool | Purpose |
|---|---|
| `open_project` | Open and bind a project. |
| `create_project` | Create and bind a project. |
| `save_project` | Save the active project. |
| `save_project_as` | Save a copy and rebind to the worker-reported project path. |
| `archive_project` | Archive the active project. |
| `close_project` | Close the active project and clear the binding. |

## 4. Defense-in-depth access enforcement

All access-mode ceilings are enforced independently at three layers.

### 4.1 Tool discovery

Write tool classes are not registered in read-only mode, so MCP clients cannot
discover or invoke them through the normal protocol surface.

### 4.2 Host authorization

`OperationAccessPolicy` checks each worker operation before the child process is
started or a request is written. The shared `OperationPolicyCatalog` classifies
all known worker operations as observation, temporary export, compilation,
project lifecycle, project mutation, or online control.

Read-only allows observation, temporary export, transitional SafetyRead, and session selection.
Read-write adds compilation, project mutation and lifecycle; full adds online control.
Transitional `SafetyRead` supports the remaining token tools. Unknown operations are denied
in every mode, including full.

### 4.3 Worker authorization

The host passes its resolved access mode to the worker process. The worker runs
`WorkerOperationAuthorization` before dispatching any request handler or
calling Siemens APIs.

Missing worker configuration preserves the historical read-write default.
Explicit but malformed worker configuration fails closed to read-only, so an
argument-propagation defect cannot silently enable mutations.

In read-only mode the Siemens-facing `TiaPortalSession` also refuses automatic
confirmation dialogs. Read-write and full modes may accept Siemens confirmations where the
existing write workflow explicitly allows them.
These Siemens dialogs are independent of MCP user-confirmation elicitation.

## 5. Project attachment and binding

`ProjectSessionBinding` is a four-state host state machine: `unbound`,
`configured_unverified`, `verified`, or `invalidated`. `--project` and
`TIA_MCP_PROJECT_PATH` create only a configured assertion. A guarded write is
not ready until a worker response supplies the matching worker id, Portal PID,
project generation, and canonical path. Successful lifecycle operations bind
only to that complete worker identity, never to caller input alone.

`OpennessWorkerClient` owns binding transitions:

- open, create, and rebinding save-as bind to worker-reported ground truth;
- close clears the binding;
- explicit `bind_project` adopts an already-open project's verified identity in every mode;
- ordinary reads and writes do not implicitly bind an unbound session;
- a configured path is promoted only after a matching status response;
- timeout, crash, worker restart, PID/path/generation drift, or a missing
  identity invalidates the binding and fails closed.

On the worker side, `TiaPortalTargetSelector` enumerates `(PID, ProjectPath)`
candidates before Attach. An exact requested path or a genuinely sole candidate
may be selected; ambiguity returns `target_ambiguous` and no process is attached.
The same exact-one rule selects a project inside the attached Portal, and Attach
is followed by a PID postcondition check. Every bound request carries
`ExpectedSessionIdentity`; the worker checks it both before refreshing live
handles and immediately before the operation body. In read-only mode it reuses
only the uniquely identified project discovered during attachment.

Only `open_project` and `create_project` open projects; no request implicitly opens one.
`bind_project(projectPath?, forceRebind=false)` selects an exact advertised open `.ap21` path.
Without a path an unbound session lists Portals, adopts the sole open project, or reports typed
`target_not_found`/`target_ambiguous`. Verified same-path/no-path calls reverify through Observe
`get_project_status`; binding result status has `metadata:null`. A different configured or last-bound
path requires force. Selection and adoption run under one serialized binding gate.

During switching, pre-detach `target_not_found`, `target_ambiguous`, and wire `guard_blocked`
preserve the previous verified binding. The host maps headless `guard_blocked` to rejected
`binding_conflict`; this refusal follows one worker selection round trip. Other failures invalidate
the binding. Failed outcomes show binding state after the call and transition `none`; binding and
previous binding are never null (an unbound session has state `unbound`). Candidate lists come
only from a listing performed in that call.

Switching detaches without closing/saving the previous UI project. A modified headless project
with this worker as its only Openness client blocks detach to avoid loss. Worker ownership does
not survive detach; a reattached project is treated as UI-owned. Reattachment may show TIA's
Openness access dialog, requiring a human answer. This is separate from lifecycle elicitation.

### Internal active project context

`TiaPortalSession` stores one `ActiveProjectContext`. Its `ProjectBase` engineering root is
derived from a typed lifecycle owner, so the root and owner cannot disagree. A
`StandaloneProjectOwner` retains a concrete `Project`; a passive `LocalSessionOwner` retains
a `LocalSession` and its `MultiuserProject`, with no save, close, discard, or commit methods.
No production route activates a local/server context in PR 2. Existing content services keep
the read-only standalone `Project` compatibility accessor. Status, lifecycle, and worker
dispatch explicitly resolve `RequireStandaloneOwner`; a synthetic local/server context is
rejected with `target_kind_unsupported` before generic lifecycle replacement or content work.

The session remains the authority for worker ID, Portal PID, generation, and live path; the
context stores no duplicate wire identity. Rewrapping the same engineering handle preserves
generation. A different handle at the same path or an accepted SaveAs path change advances it.
An unreadable or blank live path clears the context and ownership without selecting another
project. Adoption and reattachment confer no worker-open ownership. Only explicit standalone
open/create routes obtain worker-owned handles; a clean worker-owned source may close before
replacement, while a dirty or unreadable source blocks it. Disconnect/dispose remove event
handlers and release the Portal without calling project or local-session lifecycle methods.

The internal capability collection is empty, and remote identity/connection observation are
null. These fields describe unpopulated preparation and grant no permission or advertised
compatibility. Host binding epochs, public schemas, protocol version, confirmation, audit,
and 5/15/15 discovery counts remain unchanged. Current-candidate live standalone acceptance
remains a separate gate; see the [Multiuser boundary](SupportedOperations/MULTIUSER_OPERATIONS_SUMMARY.md).

## 6. Worker transport and execution

`PersistentWorkerTransport` owns one child process and serializes requests with
a `SemaphoreSlim`. Each call writes one JSON line and reads one JSON response
line. A timeout, crash, broken pipe, null response, or protocol desynchronization
terminates the worker; the next request starts a fresh process.

Before the first engineering request reaches a newly started worker, the host
sends a `hello` request and requires an exact protocol version and capability
set. A missing or incompatible handshake terminates that worker and returns
`protocol_error`; the original engineering request is never forwarded to it.

`TiaMcpServer.Contracts/WorkerJson.cs` is the single definition of the wire
format, used by the worker, `PersistentWorkerTransport`, and the FakeWorker.
`WorkerJson.Envelope` reads `WorkerRequest` and `WorkerResponse` (camelCase,
case-insensitive) and writes responses with null members omitted;
`WorkerJson.Request` writes requests with every member, null or not.
`WorkerJson.SerializePayload` renders the `payload` document and writes null
members unless the payload root carries `[LegacyNullOmission(reason)]`; a
collection payload follows its element type. The marked set, and the reason each
type has not switched yet, is pinned by `WorkerPayloadNullPolicyRegisterTests`
(see [the JSON contract roadmap](roadmap/json-contract.md)).

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
`NetworkOperationCatalog` and `NetworkWorkerInvoker` own the nine dedicated network
operations, including the Phase 4 subnet lifecycle operations (`create_subnet`,
`update_subnet`, `delete_subnet`). Each domain validates against its own request type and
catalog before a worker invocation; a new worker method belongs to its owning domain catalog
and is not implicitly a generic batch operation.

`OperationBatches` provides request-agnostic shared execution, result formatting, and
payload-budget infrastructure to both domains. Its network call sites use network-specific
payload-budget hints, such as narrowing `query`/`maxResults` or splitting a network batch.

Both catalogs enforce a maximum of 50 operations and reject unknown operations, missing
required fields, inapplicable fields, and invalid bounds before worker invocation.

Read batches execute items independently. One failed read does not prevent the
remaining items from running.

Write batches execute sequentially and stop on the first failure. Already
completed writes are not rolled back.

`network_write(operations, dryRun=false)` is a guarded single-call write. Explicit `dryRun:true`
previews effects/guards; omitting `dryRun` executes. No Network server elicitation occurs in
read-write/full or previews. Planning, sequential re-planning, mutation, immediate/final verification,
composition and audit retain one pinned verified binding to the exact already-open project.
Stops on the first failure, no rollback/replay; generic batches retain their token flow.

`compile_check` is absent from read-only tool discovery. The underlying access
policy also rejects internal compile requests in read-only mode before they are
sent to the worker.

## 7a. The opt-in canonical JSON seam and the Network Phase 2/3 structured contract

`network_read` and `network_write` were the first tools to opt into a reusable canonical-JSON
gate; `browse_project_tree` (project-tree v3, §3), `get_project_status`, `compile_check`, and all six
lifecycle tools use it too. The three excluded batch tools retain their legacy text contracts. The
[JSON contract roadmap](roadmap/json-contract.md) records the target envelope, the migration order,
and the batch tools' exclusion from it.

- `TiaMcpServer/Json/CanonicalJson.cs` provides strict typed parsing (rejects duplicate
  properties, unmapped members, and case-mismatched names) and a repository-defined canonical
  serialization (recursive ordinal property ordering, preserved array order, explicit nulls,
  compact UTF-8). It is *not* an RFC 8785 claim — see the plan's Global Constraints.
- The same file provides the worker-payload reader, `DeserializeWorkerPayload<T>` and
  `NormalizeWorkerPayload<T>` (the latter returns the value, its canonical text and element, and
  runs an optional semantic validator). It applies the strict rules above plus
  `RespectNullableAnnotations` and a rule that every settable member is required, except a member
  declared `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`. A rejected payload
  throws `JsonException`, which the decoders turn into `protocol_error`. The rule applies to a
  payload whose root writes explicit nulls, so the reader refuses (`InvalidOperationException`) a
  root type, or an array root's element type, that carries `[LegacyNullOmission]`: that payload
  omits nulls on the wire and cannot be read with required members. The reader does not check
  collection elements, dictionary values or generic arguments, so the typed validators keep their
  null-element checks alongside their semantic rules.
- A member the worker legitimately omits is conditional, marked `WhenWritingNull` and described
  in `ConditionalMemberRegisterTests`. This includes optional I/O/pagination evidence and new
  Network connection/root-count/immediate-verification evidence; the register is the authority.
- `TiaMcpServer/Tools/StructuredToolResult.cs` renders one canonical JSON document and returns it
  as both the `content` text block and a detached `structuredContent` `JsonElement`, from a single
  `CanonicalJson.Serialize` call, so the two representations cannot drift apart.
- `TiaMcpServer/OperationBatches/StructuredOperationBatch*.cs` provides the shared
  item/failure/omission/count/truncation batch model and a read/write execution engine whose
  stop decision covers `protocol_error` alongside ordinary worker failures.
- Network read/write use version `1.0`, warnings arrays and explicit nulls. Network-only
  `CanonicalWriteSafety` and token preview DTOs are retired. Generic batch `WriteSafetyService`,
  `WriteSafetyTooling`, `SafetyRead` and their audits remain in place.

Any future tool that wants a single-layer structured JSON contract reuses this same seam rather
than inventing a parallel one. `TiaMcpServer.Tests/Tools/ToolOutputContractConformanceTests.cs`
enforces that through the MCP protocol: every registered tool either advertises an output schema
and passes a success probe and a rejection probe (one canonical document in both representations,
no JSON inside strings, the expected `isError`), or is listed in its legacy register with the
reason it has not migrated.

### Standalone status and compilation

`StandalonePayloadContract` declares one strict worker root per operation: `ProjectStatusResultInfo` for direct `get_project_status` and `CompileCheckReport` for `compile_check`. The dedicated status root preserves the worker's `success`, `operation`, `projectPath`, and `project` fields while writing explicit nulls. Lifecycle and basic/probe status routes use the separate `ProjectLifecycleResultInfo` root, now also writing explicit nulls. Removing the compile root's legacy marker does not change its nested serialization inside the null-omitting `WorkerResponse.BlockImportOutcome` envelope.

`GetProjectStatusResponse` and `CompileCheckResponse` use envelope version `1.0` with `tool`, `contractVersion`, `success`, `error`, `warnings`, and a typed `StandaloneToolOutcome<T>` result (`status`, `value`, `failure`, `omission`). Status projects the full `ProjectStatusInfo`; compilation retains diagnostics even when compilation fails. Known successful/warning states with no errors pass; unknown or unavailable states do not. Compiler totals are checked against PLC totals without equating bounded message lists with exhaustive diagnostics.

`StructuredStandaloneResult` measures canonical values at 60,000 characters and the complete response at 180,000, using shared structured-budget constants and omission records. It omits whole values and warning entries, records retry guidance, and sends the final canonical text through `StructuredToolResult.CreateCanonical` for identical text/structured delivery. Strict decoding runs before any omission. Top-level `error` is present only on rejection and agrees exactly with MCP `isError`; attempted failures use `result.failure` or report diagnostics. Host-only `WorkerCallResult.IsPostOperationFailure` distinguishes a successful worker operation subsequently rejected by host identity validation from a pre-operation binding rejection.

Compile authorization precedes the existing verified-binding gate and pinned lease; the standalone
migration leaves compiler inputs and session transitions unchanged. See the [project operations reference](SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md#standalone-status-and-compilation-contract)
for the public migration contract.

### Guarded lifecycle contract

`TiaMcpServer/ProjectLifecycle/` supplies a single-operation adapter to `WriteExecution`. Public
lifecycle requests remain dedicated tools, without caller-supplied batch items or operation IDs.
`LifecyclePayloadContract` reads lifecycle/basic-status worker results through the required-member
reader and rejects incorrect operations, identities, or incomplete evidence as `protocol_error`.
`ProjectLifecycleResultInfo` writes explicit nulls; worker and FakeWorker producers share that
policy. Verification reads basic status only, including no-project status after close.
`get_basic_project_status` remains an internal read-write/full lifecycle observation. Binding uses
Observe `get_project_status`, which also works in read-only. Observing no project
after close does not require an expected identity; a supplied expected identity is still checked.
This does not weaken ordinary project-write binding or make a status read reopen a project.

`LifecycleWriteResponse` advertises version `1.0` with the shared envelope plus `phase`, `guards`,
`effects`, typed `result`, and typed `verification`. The single outcomes reuse
`StandaloneToolOutcome<T>` (`status`, `value`, `failure`, `omission`); `result.value` is
`ProjectLifecycleResultInfo` and `verification.value` is basic `ProjectStatusInfo`, as objects
rather than escaped JSON. A preview/rejection/mutation failure has no verification outcome.
`effects` is one typed `LifecycleEffects` object, with resolved source/destination and saving,
closing, rebinding, existing-target, and archive consequences. Rejections carry top-level
`error` and `isError:true`; attempted mutation/verification failures preserve their typed outcomes,
set `success:false`, and retain `error:null` / `isError:false`. Failed verification cannot erase
evidence of a successful mutation. Inspect current state before any retry after an unknown outcome.

Lifecycle values use the shared 60,000-character whole-value limit and the complete document is
capped at 180,000 canonical characters. Outcome omission is distinct from operation failure:
successful execution/verification may retain `success:true` while an oversized value is omitted.
Strict decoding precedes budgeting; omitted evidence directs the caller to read current status or
inspect persisted artifacts, never to replay the lifecycle mutation.
The document budget also covers effects and guard metadata. Oversized source-status/effects evidence
is omitted whole with a warning; oversized guard messages carry an explicit omission marker while
retaining ID, severity, operation ID, acknowledgement, phase, and verdict. The catalog's consequence
description survives alongside the omission marker. Whole warnings may be removed with a
count notice. This applies to previews and blocked calls as well as applied writes, without cutting
JSON or silently substituting a shortened path for complete identity evidence.
Last-resort failure-prose shortening is disclosed and preserves the failure category and
rejection-versus-attempted-failure classification.

The six tools leave the output-conformance legacy register; only `preview_write_batch` and
`apply_write_batch` remain (`plc_read` and `read_cross_references` are structured). Public `confirm`/`safetyToken` are removed
from lifecycle schemas, while worker-internal confirmation fences remain. The [lifecycle reference](SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md#lifecycle-operations)
describes guards and client migration. This describes the lifecycle delivery unit; separate Network token
retirement is now implemented, while generic-batch retirement and package release remain later gates.

### Typed Network payload registry

`TiaMcpServer/Network/NetworkPayloadContract.cs` is the decoder of direct public Network worker
success payloads. It maps each of the nine network operations to exactly one declared CLR result type
(`HardwareConfigInfo`, `CatalogEntryInfo[]`, `AddDeviceResultInfo`,
`ConfigureNetworkDeviceResultInfo`, `NetworkObjectListInfo`, `NetworkObjectInspectionInfo`, and
`SubnetLifecycleResultInfo` — the last shared by `create_subnet`, `update_subnet`, and
`delete_subnet`) and rejects anything that does not match — a malformed,
unknown, wrongly cased, or wrongly typed payload becomes a failed item with category
`protocol_error` rather than being forwarded under a schema that does not describe it. The
rejected payload is never echoed back to the caller. Every decode, including the nested
`NetworkEnumValueInfo` decode, goes through the worker-payload reader; the network payload roots
write explicit nulls.

The other host decodes of worker payloads use the reader as well:
`HardwarePagePayloadContract`, `ProjectTreeWorkerPayloadContract` and
`ProjectRebindStatePayloadContract`, plus `StandalonePayloadContract` and `LifecyclePayloadContract`. Four call sites deliberately stay on
`CanonicalJson.Deserialize`: the three batch safety snapshot decodes (`BatchWorkerInvoker`,
`ProjectTreeSafetyPayloadContract`, `TagOperationSafetySnapshotContract`), whose roots still omit
nulls until the batch redesign, and the authenticated cursor decode
(`AuthenticatedCursorProtector`), which reads a host-written envelope rather than a worker
payload.

The paged hardware seam described next is the deliberate exception: its private candidate response
is decoded by `HardwarePagePayloadContract` before projection into the same public
`HardwareConfigInfo` shape. The private candidate DTO is never a public operation result.

### Hardware configuration pagination seam

`read_hardware_config` selects a dedicated pagination seam only when `pageSize` or `cursor` is
present. Requests with neither field still use the original worker method, typed
`HardwareConfigInfo` decoder, serialization, and payload-budget path unchanged.

The paged path has one public operation but two internal phases:

```text
NetworkReadOperationExecutor
        -> HardwarePaginationCoordinator
        -> OpennessWorkerClient.ReadHardwarePageCandidatesAsync (serialized worker lease)
        -> internal read_hardware_page_candidates worker method
        -> HardwarePageDescriptorSet (stable devices, then subnets)
        -> HardwarePageCandidateReader (materialize the requested descriptor window)
        -> HardwarePagePayloadContract + HardwarePageProjector
        -> public HardwareConfigInfo + HardwarePaginationInfo
```

Siemens objects and traversal remain inside the net48 worker. Structural locators are internal
evidence used to materialize the selected descriptor window; they are never emitted in the public
page, cursor, omission, error, or log. The internal worker method is authorized by the worker
policy but is absent from `NetworkOperationCatalog`, the MCP schema, tool descriptions, and public
host dispatch.

Worker-session identity has one authority: the `WorkerResponse.SessionIdentity` observed in the
`WorkerCallResult`/`HardwarePageWorkerCallResult` envelope. Candidate payloads do not duplicate
it. The serialized client call returns that envelope identity together with the bound or unbound
host snapshot captured under the same lease; continuations pin the worker call through the
existing `ExpectedSessionIdentity` field. Explicit-project pagination is therefore valid while
the host remains unbound and does not create or rotate a binding merely to read pages.

The host signs opaque cursors with a random process-lifetime HMAC-SHA256 key. Cursor state binds
the normalized `deviceName`, exact `plcName`, detail flags, resolved project path, host binding
snapshot, worker session, ordered descriptor snapshot, and next combined offset. `pageSize` is not
query identity and may change between pages. Restarting the host discards the key, so every old
cursor becomes `invalid_cursor` by design. Cursors are authenticated, not encrypted, and no
decoded state or key material is returned in failures or diagnostics.

The worker counts the requested window across matching devices followed by matching subnets. The
host keeps those entities in separate arrays and projects only the largest complete canonical
prefix whose operation item is at most 60,000 characters, so actual progress may be smaller than
`pageSize`. It never splits an entity or diagnostic string. Diagnostics-only overflow and a first
oversized entity produce bounded omissions with no offset advance; the full optional entity
subject is retained only when the complete subject fits. The ordinary 180,000-character batch
budget then runs independently with pagination-safe retry guidance.

### Canonical safety flow (network writes)

`NetworkWriteDomain` implements the shared guarded runner with `ConfirmsEveryCall=false` and
no acknowledge guards. Public root argument rejection (`confirm`, `safetyToken`, `acknowledge`,
unknown roots, nonboolean `dryRun`) happens before tool entry through the normal SDK/wrapper MCP
error path and has no write audit. Entered validation/binding/guard denials have a canonical root
error and one audit. Aggregate encoded-ID/protected-core admission runs before binding; it retains
the existing 256-character per-ID limit, without a smaller per-ID or raw-character aggregate rule.

`NetworkWritePlanner` reads ordinary typed hardware, resolves exact targets and records complete
node/subnet/IO relationship evidence. Unknown inventory is not empty. Connected deletion fires
`network_delete_connected_subnet` (`info`); incomplete consequences fire `network_state_unverifiable`
(`block`). Both satisfaction fields are null. Blocks cannot be overridden in any mode.

`NetworkWriteVerifier` retains immediate applied-setting evidence before later deliberate changes,
then reads the effective attempted prefix. Sparse skipped settings fail the item/call while keeping
the typed result; no check claims an unapplied setting. Subnet-only moves do not imply IO detach/attach:
an earlier explicit IO tuple remains expected unless explicitly superseded or removed by designed
deletion consequences, so unexpected side effects can conservatively fail final verification.
Root `project.Devices.Count` is separate from exact preservation of grouped/ungrouped devices/nodes.

`NetworkWritePayloadBudget` bounds the complete canonical document at 180,000 characters and each
value at 60,000. It omits whole effect/result/verification values, preserves IDs/guards/outcome
summaries and uses shared omission metadata. Diagnostic strings over 512 canonical encoded
characters are replaced whole, never raw prefixes; collections can be dropped whole under pressure.
Root `omission` is explicitly null when complete. Incomplete evidence delivery makes root success
false without changing actual execution/item/verification truth. Attempted calls stay `phase:applied`,
`error:null`, MCP `isError:false`. Recover by `network_read` with original exact selectors; omission
metadata is not a selector and never authorizes replay. Audit text/hash exactly matches delivery,
while audit items retain execution truth. Actual read-write confirmation is `none`; actual full
is `policy`; dry runs/denials are `none`. No Network server elicitation is attempted.
See the [Network reference](SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md#network_write-envelope).

### Exact host-to-worker selector boundary

Selector resolution happens **twice, independently, on both sides of the process boundary** —
this is defense-in-depth, the same pattern used for read-only access enforcement (§4):

- **Host side** (`TiaMcpServer/Network/NetworkIdentityResolver.cs`): resolves device, node,
  subnet, and IO-system identity from a `HardwareConfigInfo` snapshot the host itself just read.
  This resolution produces the `NetworkWriteTargetEvidence` retained in guarded effects and
  preview responses — it never touches Siemens Openness.
- **Worker side** (`TiaMcpServer.OpennessWorker/Openness/NetworkDeviceConfigurator.cs`):
  independently matches the same `deviceName`/`nodeId` selector against live Openness objects at
  the moment of the actual write, walking every nested device item and network interface.

Both sides apply the identical rule: a selector that matches zero, more than one, or a candidate
whose own identity could not be read all fail closed rather than
resolving to a first match, a first node, or a name-only guess. The host never forwards a
pre-resolved object reference to the worker — only the caller's own `deviceName`/`nodeId` (and,
where applicable, `subnetId`/IO-system `number`) cross the process boundary, so the worker's
independent resolution is a real second check, not a formality. Newly preflighted requested subnet/IO
failures and unreadable discovery use `worker_operation_failed`; established complete device/node/
subnet-lifecycle selector categories remain. Actual failed/unverified postchecks use `postcondition_failed`.

### Phase 3 read identity and introspection seam

Phase 3 extends the same typed boundary without moving Siemens objects into the host:

```text
public NetworkObjectSelectorInfo
        -> NetworkWorkerInvoker / WorkerRequest
        -> NetworkObjectSelectorResolver
        -> ResolvedNetworkObject
        -> per-kind modeled adapter + generic attribute inspector
        -> NetworkObjectInspectionInfo
```

`NetworkObjectIndexReader` traverses the live object graph in deterministic order and emits
`NetworkObjectSummaryInfo` records. A complete identity includes a selector; incomplete or
unreadable identity remains in the list as `selectable:false`, `selector:null`, and deterministic
diagnostics. Device-item paths use recorded zero-based sibling indices and then verify name,
position number, and type identifier. Nodes, IO systems, and communication connections may carry
additional sibling-index and name/type evidence. The resolver follows the recorded locator and
then verifies every supplied evidence field; it never searches for a more convenient match after
evidence drift.

`NetworkObjectCursorCodec` treats pagination state as opaque. A cursor binds the normalized
filter, stable ordered item fingerprint, and current snapshot; invalid encoding, filter mismatch,
snapshot mismatch, or an out-of-range offset fails explicitly rather than returning a different
page. Selectors and cursors are therefore snapshot-scoped evidence, not persistent identities.

`NetworkModeledAttributeAdapters` and `ConnectionModeledAttributeAdapters` expose typed,
kind-specific fields. `NetworkObjectInspector` supplements them with
`IEngineeringObject.GetAttributeInfos()` and guarded reads. `NetworkAttributeResultBuilder`
merges modeled and dynamic metadata while keeping source, access, availability, value, and
diagnostic independent for every requested name. Public values use only `null`, `string`,
`boolean`, `integer`, `number`, or `enum`; unsupported CLR objects become `unrepresentable` and
are never serialized through arbitrary `ToString()` output.

The worker-only `probe_network_object_attributes` method exists solely for the explicitly
authorized Phase 3 raw-metadata acceptance mode. It is read-only and absent from
`NetworkOperationCatalog`, the public MCP schema, and host dispatch.

### Phase 4 subnet lifecycle seam

Phase 4 adds `create_subnet`, `update_subnet`, and `delete_subnet` to `network_write` without a new
MCP tool, reusing every existing seam described above rather than adding a parallel one:

```text
network_write request
  -> strict catalog validation
  -> current subnet resolution and canonical safety binding
  -> worker request
  -> SubnetLifecycleService transaction
  -> post-read/device-count assertion
  -> minimal typed canonical result
```

- **Strict catalog validation** (`TiaMcpServer/Network/NetworkOperationCatalog.cs`): the same
  deterministic order used by every other network operation — inapplicable fields, missing
  required fields, nested DTO shape, target selector shape, type applicability, then numeric
  range/enum value. `NetworkSubnetDefinition` and `NetworkSubnetChanges` are strict nested DTOs
  with no writable `subnetId` and no writable `networkType` on update. Update and delete require
  `target.kind` to be exactly `"subnet"` (ordinal comparison) and a nonblank `subnetId`.
- **Current subnet planning** (`NetworkIdentityResolver`, `NetworkWritePlanner`): creation
  identity is request-derived; update/delete use exact ordinal subnet IDs with no name/index
  fallback. Guarded effects retain resolved identities and complete affected-node evidence.
- **Worker request** (`TiaMcpServer.Contracts/WorkerRequest.cs`,
  `TiaMcpServer/Worker/OpennessWorkerClient.cs`, `TiaMcpServer/Network/NetworkWorkerInvoker.cs`):
  production fields `SubnetName`, `SubnetNetworkType`, `SubnetHighestAddress`,
  `SubnetTransmissionSpeed` (plus the existing `SubnetId` for update/delete) are forwarded through
  three explicit typed client methods, classified `ProjectMutation` in `OperationPolicyCatalog` and
  denied in read-only mode before any worker call. These fields are distinct from the `Probe*`
  fields reserved for the internal mutation-probe evidence fixture; production calls never populate
  a `Probe*` member.
- **`SubnetLifecycleService` transaction**
  (`TiaMcpServer.OpennessWorker/Openness/SubnetLifecycleService.cs`): each of `Create`, `Update`,
  and `Delete` opens exactly one `ExclusiveAccess`/`Transaction`, performs every requested setter,
  and calls `CommitOnDispose()` only after every setter succeeds. Subnet lookup is ordinal, exact-one
  `SubnetId` matching with no fallback to `Name`, index, or connected device. Late zero or multiple
  worker matches fail as `postcondition_failed`. Delete resolves and type-checks the target inside
  its transaction, captures a nonblank name from that same object, and only then calls `Delete()`;
  the mutation cannot use a pre-transaction subnet object. This file is a
  distinct production implementation from `SubnetLifecycleMutationProbeService`, the internal-only
  evidence probe behind `probe_subnet_lifecycle_mutations` — the probe is absent from
  `NetworkOperationCatalog`, the public MCP schema, and host dispatch, exactly like Phase 3's raw
  metadata probe.
- **Post-read/device-count assertion**: after the transaction is disposed, the service re-reads the
  target subnet (or confirms its absence for delete) and re-reads `project.Devices.Count`; any
  mismatch — including a changed device count — fails with `WorkerFailureCategories.PostconditionFailed`
  rather than reporting success, and the service never retries automatically.
- **Typed canonical result** (`SubnetLifecycleResultInfo`): required core `subnetId`, `name`,
  `networkDeviceCount`, `networkDeviceCountUnchanged`, plus conditional worker `verification`
  required on the guarded public path. Strict projection preserves failed/unverified postchecks
  as attempted failures. Root count means `project.Devices.Count`; grouped/ungrouped node/device
  preservation and detached relationships receive independent identity checks.

The connected-subnet delete path is implemented through this seam without deleting devices;
the worker and host capture affected identities/consequences for guards and postchecks. The
service never calls `Project.Save()` or triggers a hardware compile. Current-candidate live acceptance
is pending; the dated Phase4 evidence below is historical and does not qualify this migration. Focused static gates verify
the repaired contract. A historical Phase 4 run applies only to its recorded older commit. The
first current-revision live attempt on 2026-09-23 stopped at a TIA safety-permission rejection of
connected Ethernet deletion. The fresh 2026-09-24 guarded public rerun passed all eight lifecycle
operations, including both connected deletes; a separate final read observed zero subnets and 81
aggregate hardware devices. It did not independently read back per-device identities or node and
IO-system attributes, and its root count of 10 has no independent pre-Apply baseline. See
`docs/SupportedOperations/NETWORK_PHASE4_SUBNET_LIFECYCLE.md` for the contract and
`docs/superpowers/acceptance/reports/2026-09-21-network-phase4-current-revision-live.md` for the
observed run.

### Phase 5 temporary IO-system qualification probe

`probe_io_system_qualification` is a temporary worker-only diagnostic, classified as a project
mutation and absent from the public MCP schema, host dispatch, and `NetworkOperationCatalog`.
Its `inspectOwner` mode checks that an IO system has exactly one controller interface
`DeviceItem` owner. The effectful modes separately prove the deepest strict ancestor
`DeviceItem` that directly hosts the unique master `PlcSoftware` through
`SoftwareContainer` and exposes `ICompilable`; they compile that hardware item, not the
interface owner or containing `Device`. Missing or ambiguous owner, PLC role, compiler
service, or pre/post identity continuity stops the operation. An applied change commits before post-read and hardware compile; a known post-commit failure must not be reported as rollback.

The 2026-09-26 worker-only V21 matrix verified distinct owner and compiler targets for PN-B
and DP-B, baseline hardware compiles, and bounded one-field edit/restoration evidence. It
qualified PN and DP modeled `Name`/`Number` and PN
`UseIoSystemNameAsDeviceNameExtension`; it excluded PN `MultipleUseIoSystem` after a
committed edit made linked PN-name evidence unavailable, and excluded
`MaxNumberIWlanLinksPerSegment` because the fixture reported `unknownAttribute`.
The [historical acceptance report](superpowers/acceptance/reports/2026-09-24-network-phase5-pr2-qualification.md)
records the qualification limits and final clean reopen; the
[qualified contract](superpowers/specs/2026-09-24-network-phase5-qualified-contract.md)
is input to later public slices. This diagnostic has no public safety token or write audit,
and `update_io_system` is not shipped. Retire the probe with the final live-harness cleanup
after the later public acceptance work.

## 8. Write safety

Lifecycle and Network writes use the guarded single-call pipeline described below. Lifecycle
read-write confirms every actual call; Network has no server elicitation. Generic batches retain
preview/apply tokens as server-side consistency checks, never proof of human consent. Client
permission prompts and accepted elicitation do not establish that a human saw the consequence.

### MCP tool annotations

The MCP `readOnlyHint`, `destructiveHint`, and `openWorldHint` annotations are explicit,
client-facing metadata. They are untrusted advisory hints for a client deciding how to present a
tool; they neither authorize a request nor relax server behavior. In particular,
`preview_write_batch` is marked as a non-destructive preview even though the follow-up
`apply_write_batch` is destructive, and lifecycle writes carry conservative mutating hints even
for a `dryRun`. The server enforces the access policy, binding lease, current-state checks, guards
or transitional tokens, and audit. A client may prompt independently on any destructive tool call.

### Preview→apply token flow

Generic batch data writes alone retain the two-tool flow:

1. The preview call (`preview_write_batch`) reads current state, produces a human-readable description,
   and creates a short-lived, single-use safety token bound to the tool,
   host binding revision, requested input, and current-state hashes. Project-scoped
   writes require that revision to contain a complete verified worker/Portal/project
   identity.
2. The apply call (`apply_write_batch`) supplies
   `confirm=true` and the token. `confirm` is an argument the caller sets, not a user
   confirmation. The server reads current
   state again and consumes the token only when every bound value still matches.

For `update_block_logic` and `update_type_content`, `preview_write_batch` may include a
response-only structured `diff` object built from the already-bound exact-format current text and
the submitted replacement text. This object is outside safety-token issuance and validation.

The complete apply critical section is protected by a pinned binding lease:
fresh-state read, token validation and consumption, Siemens mutation,
post-verification, and audit capture all see the same worker id, Portal PID,
project generation, and canonical path. Rebind, close, and save-as transitions
use the same lease. This prevents a valid token for project A from being applied
to project B, and prevents two concurrent applies based on the same old state
from both succeeding.

Lifecycle responses are also continuity-checked. An already verified session
may change path or generation only through the authorized lifecycle transition,
and the response must keep the same worker id and Portal PID. A restart, PID
change, same-path close/reopen, or malformed close result is rejected rather
than adopted as a new binding.

Changed input, changed project state, wrong tool, wrong project, expiry, or token
reuse causes rejection. Completed writes are appended to the audit log under
`%LOCALAPPDATA%\TiaMcpServer\audit`.

Read-only mode is categorically stronger than this token flow: `confirm=true` and
a valid token cannot override the access policy.

The eight tag/table/user-constant writes bind typed, operation-specific safety snapshots instead
of the full `list_tag_tables` payload. All identities retain the resolved PLC, folder, table,
and canonical object path. Collision probes retain matching candidate identity, kind, name,
logical address where applicable, and whether the candidate is the target; unrelated candidate
state is excluded. Table-name collisions search the selected PLC's complete tag-table hierarchy,
including sibling and nested folders, while retaining the exact destination-folder identity.
Tag and user-constant name probes compare names case-insensitively across PLC tags, user constants,
and blocks in the selected PLC's unqualified CPU namespace. They traverse all tag-table folders
and the ordinary program-block hierarchy, including nested user groups and system-block groups.
Each matching candidate retains its actual kind (`tag-name`, `user-constant-name`, or `block-name`)
and canonical path; target marking requires both the kind and exact path. Logical-address probes
remain tag-only. All probes bind only matching names/addresses in deterministic order, and traversal
or discovery errors propagate even after a match has been found.

These scopes follow the Siemens V21 rules for [PLC tag and tag-table names](https://docs.tia.siemens.cloud/r/en-us/v21/declaring-plc-tags/rules-for-plc-tags/valid-names-of-plc-tags)
and [global user-constant names](https://docs.tia.siemens.cloud/r/en-us/v21/declaring-plc-tags/declaring-global-constants/rules-for-global-user-constants).
Software Unit namespace resolution is not modeled by these operations: unit-local block names are
not folded into the unqualified CPU namespace. Namespace-aware coverage remains a design/live
qualification follow-up.

| Operation / exact selector | Bound current state |
| --- | --- |
| `create_tag_table` | Resolved PLC and destination folder, requested table name, and matching table-name occupancy throughout that PLC's tag-table hierarchy |
| `delete_tag_table` | Exact target-table identity and its normalized Simatic ML export, SHA-256, and character count |
| `create_tag` | Exact target-table identity, effective name/address, matching tag/constant/block names, and tag-only address probes |
| `update_tag` | Exact target-table and tag identity/state, effective name/address, matching tag/constant/block names, and tag-only address probes |
| `delete_tag` | Exact target-table and tag identity/state |
| `create_user_constant` | Exact target-table identity, effective constant name, and matching tag/constant/block names |
| `update_user_constant` | Exact target-table and constant identity/state, effective constant name, and matching tag/constant/block names |
| `delete_user_constant` | Exact target-table and constant identity/state |

Tag state includes data type, logical address, and the three external access flags. Constant state
includes data type and value. A requested external flag whose current value is unreadable still
fails before token issuance; unreadable required evidence or a malformed typed worker payload
fails closed. Table deletion binds the selected table's export, including exported content that
the public table list does not expose; it does not bind sibling-table exports.

Within one preview or apply read phase, identical tag selectors share one worker read. The key
contains operation kind, canonical project path, PLC selector, folder, table, object name,
effective name, and requested logical address; request fields conservatively distinguish keys.
The shared result is validated for each requesting operation and expanded back into the original
operation order before composing the combined state. Operation IDs and list order remain bound.
The map is local to that one phase: **there is no cross-phase cache**. Apply always reads fresh
state under the pinned binding lease. Deduplication does not make the sequential reads atomic.

Offline tests execute the safety reader against test-only Siemens object graphs, including
cross-kind name drift, nested user/system blocks, CPU-wide table-name occupancy, strict traversal
failures, and unrelated-name tolerance. They pass those snapshots through the real typed decoder
and token validator without loading Openness assemblies. Offline and FakeWorker tests also cover
typed selectors, ordered expansion, within-phase deduplication,
fresh apply reads, same-object/collision drift rejection, unrelated sibling tolerance, authorized
apply, and replay rejection. The
[guarded live TIA Portal V21 acceptance](superpowers/acceptance/reports/2026-09-01-pr5-tag-operation-safety-scopes-live.md)
completed against the exact recorded host, PID, disposable copy, and fixtures: all eight previews
and ordered duplicate selection passed; same-object and name/address collision drift returned
`state_changed`; unrelated sibling drift preserved the original token; one authorized unchanged-token
apply succeeded; no-save discard preserved the saved baseline; and the clean source project was
restored open. Static/offline and bounded live evidence remain separate, complementary claims.

PR 5 explicitly defers multilingual per-tag comment binding, public `list_tag_tables`
completeness changes, broader snapshot narrowing, Software Unit namespace-aware collisions, and
PLC `start_plc` / `stop_plc` safety work. The public table list remains best-effort and unchanged.

The three structural block operations use typed project-tree safety snapshots instead of a broad
project-tree browse. `BlockAddress` and `BlockTargetResolver` are the shared ownership authority:
they resolve either the PLC-global block root or the exact Software Unit block root, and the
snapshot reader returns that owner scope, PLC name, optional Software Unit name, and canonical
root path. Parent and ancestor paths retain that owner prefix end to end.

| Operation / exact selector | Bound current state |
| --- | --- |
| `create_block` | Exact owner scope, parent group, ancestor chain, requested-name occupancy in that parent, and the exact occupied block's deterministic XML export when the name is already occupied by a block |
| `create_block_group` | Exact owner scope, parent group, ancestor chain, and both block and group occupancy for the requested name |
| `delete_block_group` | Exact owner scope and target group, parent membership, ancestor chain, and the complete content-bearing descendant hierarchy, including deterministic XML exports for every contained block |

Occupied and descendant block content goes through the authoritative block-export path. The
safety-only export fails closed unless it has authoritative XML; it does not accept a companion
document as the block export. The host strictly validates required members, canonical owner/path
relationships, uniqueness, and cardinality before canonical serialization. Missing, empty,
malformed, or conflicting payloads return `protocol_error`, and the rejected worker payload is not
echoed to the caller.

Identical project-tree selectors share one internal worker read only within the current preview or
apply state-read phase. The validated result is expanded back into ordered
`OperationBatchCurrentState` entries before the combined state is hashed. A repeated preview and
the apply phase each start with an empty cache, so apply still performs a fresh read under the
pinned binding lease.

The
[guarded PR 6 live TIA Portal V21 acceptance](superpowers/acceptance/reports/2026-09-01-pr6-project-tree-safety-scopes-live.md)
covered both PLC-global and Software Unit owners. Occupied-block content, descendant-block
content, requested-name occupancy, and relevant descendant membership drift rejected stale
tokens with `state_changed`; unrelated sibling-tree drift preserved the original token. Authorized
applies were followed by byte-equivalent content restoration, public status re-verification, and
successful compile checks. The report keeps repository-auditable coverage separate from the
redacted live-only observations and makes no save, persistence, plant, or hardware-commissioning
claim.

PR 6 leaves the following scope unchanged: broader snapshot narrowing and `start_plc` /
`stop_plc`.

Internal exact-target selectors use the shared `SafetyRead` capability. A `SafetyRead` is
side-effect-free and allowed in read-only mode, but it is not an ordinary observe: every request
must carry the complete, exact worker/Portal/project session identity previously observed from the
worker. This prevents an internal safety read from silently moving to a different same-path
session while a preview is assembled.

### Guarded write pipeline (foundation and lifecycle)

`TiaMcpServer/Safety/Pipeline/` holds the single-call write pipeline that replaces the token flow.
The six registered lifecycle tools and Network use it; generic batches migrate later. Network has no server elicitation; lifecycle read-write confirms every actual call.
The pipeline is a consistency and safety mechanism. Client-returned acceptance and mode policy
are recorded separately; client acceptance does not prove that a person saw the consequence.

`WriteExecution.RunAsync(domain, call)` owns the order and the safety rules. An
`IWriteDomain<TItem, TEffect, TVerification, TResponse>` supplies validation, target planning, guard
evaluation, the mutation, verification, and the response shape. Stages, in order:

1. **Validate** before any worker call: at least one operation, non-blank unique `operationId`s,
   and the domain's own validation. There is no agent confirmation list.
   Failure is `phase: error`.
2. **Bind.** The default strategy retains `RequireVerifiedWriteBindingAsync` and verifies promotion
   of the same project. An explicit lifecycle strategy prepares the exact source revision or permits
   genuinely unbound open/create; other lifecycle writes retain their active-source requirement.
   Input JSON cannot select that strategy. Stale revisions and continuity failures deny the call.
3. **Lease.** Everything that follows, including the audit append, runs in `RunUnderLeaseAsync`
   under the pinned snapshot; a refused lease is `binding_conflict`.
4. **Plan.** `PlanAsync` returns one `ItemPlan` (effect, optional `DependsOn`, checked
   preconditions) per item, or fails the call (`target_not_found`, `target_ambiguous`, and so on).
   A count mismatch is a programming error and throws.
5. **Guards and confirmation.** `EvaluateGuards` is pure. A `dryRun` stops as `phase: preview`
   without mutation or elicitation and reports guards even when they would block an actual call.
   Block guards refuse without prompting. Read-write lifecycle asks once for every actual call;
   full uses policy without server elicitation. After an accepted prompt, re-plan and re-evaluate
   under the same binding lease before dispatch: the TIA UI can edit while the prompt is visible.
6. **Mutate** sequentially. The first item that does not succeed stops the call; later items are
   `skipped`. A dependent item is re-planned just before its own mutation.
7. **Verify, compose, audit.** `VerifyAsync`, then `Compose`, then one audit record; the tool result
   carries the same canonical text that was audited. `isError` is true only for `error` and
   `blocked`; a write that ran and failed reports `success: false` under `applied`.

#### Guards and acknowledgement

A domain fires guards from the closed `WriteGuardCatalog`; severity comes from the catalog.
`info` guards become warnings. `acknowledge` guards are satisfied by accepted client elicitation in
read-write or mode policy in full. Every actual read-write lifecycle call asks once, including
info-only calls and calls without guards. Missing capability, decline, cancel, timeout, transport
failure, or acceptance without boolean `confirm:true` denies with `access_denied`. Block guards
stop mutation in every mode; dry runs report guards without mutation or prompting.

A dry run reports `acknowledged:false` for acknowledge guards in read-write and `true` in full,
while guard audit satisfaction remains null and confirmation is `none/not_requested`. Info/block
guard acknowledgement is null. See the [seven lifecycle guards](SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md#guards-and-confirmation).

#### Dependent items

Every item is planned and every guard evaluated before the first mutation. An item planned with
`DependsOn` is re-planned just before its own mutation, against state the earlier items changed.
`DecideLate` judges re-planned guards: any `block` guard stops the item with `guard_blocked`.
In read-write a newly discovered acknowledge-severity guard not covered by initial client
acceptance also stops the item; full satisfies that severity by policy. Registered lifecycle
calls are single-item; this dependency mechanism supports future domain batches.

#### Partial writes

There is no rollback. In a multi-item call, the failed item that was attempted carries the
`partial_write_no_rollback` guard and warning (`WriteExecution.PartialWriteMessage`). An item that
stopped the call before its own mutation (a failed re-plan or late guard) gets a separate "not
mutated" warning that says only earlier items stay applied. A single-item call and a first-item
failure before mutation add neither.

#### Audit stream

Every call, in every phase, appends exactly one record to `writes-yyyy-MM-dd.jsonl` (UTC date,
UTF-8 without a BOM) beside the legacy audit files, which it never touches. The record has
`recordKind: "write"` and `recordVersion: 2`, and holds the tool, contract version, access mode,
project path, the pinned binding, the requested operations, the phase, the response text and its
`sha256:` hash, every fired guard, and per item the target, checked preconditions, status, failure,
warnings, and duration. Call-level `confirmation.by` is `user`, `policy`, or `none`; outcomes include
`confirmed`, `declined`, `cancelled`, `unsupported`, `timed_out`, `failed`, and `not_requested`.
Accepted elicitation guard satisfaction is `user`, including when a fresh check then refuses stale
state; full apply satisfaction is `policy`. Dry runs and blocks before confirmation record
`none/not_requested` and null guard satisfaction. Validation and binding rejections are audited
outside the lease.

An exception after the lease starts is audited as an `error` record and then rethrown unchanged.
If live apply was interrupted, the record includes the collected batch outcomes, the failed
current item, skipped remaining items, and the same partial-write warnings as an ordinary failure.
Exceptions during verification or response composition preserve an already completed batch.
Caller cancellation propagates after auditing known mutation outcomes. A cancelled call is not
proof that nothing changed; inspect current state and retained audit evidence before any retry.
The recorded error response comes from the pipeline's own report, without calling the domain
again. A failed append is reported on stderr and never hides the write result.

#### Content hash

`plc_read` returns `contentHash` on each succeeded `get_block_content` and `get_type_content` read:
`xml:sha256:<hex>` or `source:sha256:<hex>` over the exact served text (`PlcContentHashes`,
`ContentHashes.Compute`). It is an explicit `null` on `withDependencies` reads, and a result over the
60,000-character value is omitted whole, so no hash is reported for it. A guarded write
compares an expected hash with a fresh read through `ContentHashes.Check`: a malformed hash or a
format that differs from the write's format is `validation_error`, content that no longer matches
is `state_changed`, and the comparison is recorded as a `contentHash` precondition.

## 9. Diagnostics

`tia-mcp doctor` runs the environment diagnostic pipeline without starting the
MCP host. It supports text or JSON output and reports the resolved access mode.
The command accepts the same access-mode options as normal startup, in addition
to `TIA_MCP_ACCESS_MODE`.

Doctor never attaches to TIA Portal or opens a project. Its project-binding check
validates an absolute, existing `.ap21` file but reports a warning because no live
project match was inspected. Its process check uses the Windows process list:
one detected process can pass that process-only check, while multiple processes
produce a Warning. An unbound project check is a Warning in every mode with remediation naming
`bind_project`. Invalid paths remain failures. These diagnostics prevent an absent path or ambiguous process set from
being reported as fully ready without turning Doctor into an Openness client.

## 10. Testing

`TiaMcpServer.Tests` links selected host and worker source files directly into
the test assembly, allowing policy, parsing, tool metadata, generic-batch and network
catalog/invoker behavior, diagnostics, and IPC behavior to be tested on .NET 10 without a
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
- doctor output and CLI parity;
- the output contract of every registered tool (§7a).

Lifecycle regressions cover read-write/full binding preparation and stale-revision
refusal, all seven guards, mode-derived guard satisfaction, client elicitation outcomes, post-prompt
state changes, typed attempted failures and verification, canonical audit provenance, and removal
of public token inputs. Production-surface protocol tests distinguish actual applied lifecycle
calls from dry-run previews and keep generic-batch tokens active. Network tests cover zero elicitation, exact binding, sparse partial outcomes and complete-envelope omission.

Manual integration testing with a live TIA Portal remains necessary to validate
Siemens-specific attachment, confirmation, project-path, packaging, and worker
launch behavior.

Earlier delivery phases used separately authorized, task-specific live acceptance procedures.
Those legacy procedures and their source-inspection tests were removed after their durable
evidence and limitations were recorded under `docs/superpowers/acceptance/reports/` and the
relevant operation references. Their removal does not upgrade offline, stub, or FakeWorker results
to live acceptance. Any future TIA Portal qualification requires a newly reviewed procedure,
separate authorization, and a durable acceptance record.

## 11. Keeping this document current

Update this document when tool registration, operation classification, access
modes, worker launch arguments, binding rules, or write-safety behavior changes.
The architecture must describe the explicitly registered runtime surface, not
only the set of decorated tool classes present in the assembly.
