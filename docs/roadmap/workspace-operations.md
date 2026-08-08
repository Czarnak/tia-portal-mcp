# VCI Workspace Operations Roadmap

Status: Proposed. This is a general roadmap, not a detailed implementation plan. Each phase will
receive its own design, acceptance criteria, and file-level implementation plan in a separate
session.

This roadmap follows the public-tool and safety direction established by the
[Network Operations Roadmap](network-operations.md), but it does not repeat Network's historical
JSON migration. Workspace operations will use the shared canonical JSON, structured-result,
operation-batch, and write-safety infrastructure from their first public phase.

## Objective

Create a first-class, agent-friendly surface for the TIA Portal Version Control Interface (VCI)
that:

- exposes VCI through two focused MCP tools rather than the generic read and write batches;
- covers VCI workspace groups, workspaces, mappings, supported formats, comparison state, and
  synchronization;
- preserves preview-before-apply safety for every operation that can change the TIA project,
  VCI configuration, mappings, or workspace files;
- treats the TIA project and the workspace filesystem as separate state authorities that can
  change independently;
- uses typed, deterministic, canonical JSON suitable for agent inspection and safety binding; and
- distinguishes automated, compile-time, and live TIA Portal V21 evidence.

## Scope Boundary

In this roadmap, `workspace` always means a **TIA Portal VCI workspace**.

The roadmap includes:

- access to the VCI system group and nested user groups;
- workspace enumeration, creation, property updates, and deletion;
- workspace language and VCI-specific global-library properties;
- enumeration and inspection of mapped engineering objects;
- object-specific supported-file-format discovery;
- connecting existing workspace files to TIA engineering objects;
- exporting individual engineering objects into a workspace;
- disconnecting or deleting mappings through the supported V21 API;
- individual and hierarchical comparison status; and
- explicit project-to-workspace and workspace-to-project synchronization.

The roadmap does **not** include:

- Git repositories, commits, branches, remotes, or merge workflows;
- persistent content indexing, full-text search, or rich source diffs;
- generic file browsing or arbitrary file editing;
- server-owned hidden workspaces or automatic background synchronization;
- Multiuser, project-server, Teamcenter, or Add-In repository-provider workflows;
- compilation, project save, download, online, or commissioning workflows; or
- documentation-only or removed API members that are absent from the installed V21 PublicAPI.

In particular, the installed V21 metadata does not expose the documented legacy
`IndividualObjectSynchronizationStatus.InitializeStatus()` service. It is not part of this
roadmap unless a later installed V21 API exposes a compilable replacement.

## Approved Public Tool Direction

The domain will expose exactly two public MCP tools:

- `workspace_read`: ordered batch VCI reads, registered in both read-only and read-write modes.
- `workspace_write`: ordered batch VCI writes, registered only in read-write mode.

`workspace_write` will be self-previewing like `network_write`. A call without confirmation
returns a preview and safety token. A second call with `confirm=true`, the unchanged ordered
operation list, and the token applies the batch. Existing token expiry, single-use behavior,
project binding, auditing, and access-mode enforcement remain mandatory.

The operation names inside each batch will be locked during the detailed design for the phase
that introduces them. They remain operations under these two tools, not additional public MCP
tools.

## Shared Structured Contract Baseline

Workspace operations will adopt the repository's shared structured contract immediately:

- `content` text and `structuredContent` come from the same canonical serialization;
- every successful worker payload is decoded into the one declared result type for its operation;
- malformed successful payloads become `protocol_error` and are not echoed to the client;
- operation results contain JSON objects and arrays, never nested escaped JSON documents;
- unknown operations and unknown, missing, or inapplicable fields fail closed;
- selectors, collection ordering, status values, warnings, omissions, and truncation markers are
  deterministic; and
- response budgets omit whole values with explicit evidence rather than silently truncating JSON.

Read documents are evidence, not implicitly writable snapshots. Writes use explicit operations
with deterministic targets and intended changes.

## VCI Write-Safety Boundary

VCI writes can affect more than the open project. Depending on the operation, they can change VCI
configuration, project objects, mappings, or files beneath a workspace root. The shared safety
gate must therefore bind both project and workspace evidence.

At roadmap level, every write preview must identify:

- the active project and current project-state snapshot;
- the selected workspace group, workspace, and resolved workspace root;
- the selected engineering object or mapped object;
- the requested direction, format, relative path, and filename where applicable;
- relevant comparison and mapping state;
- existing destination-file evidence, including multi-file formats where applicable; and
- expected project, mapping, and filesystem side effects.

Apply must fail if bound project, VCI, mapping, or filesystem evidence changed after preview.
Relative paths must remain confined to the resolved workspace root. File collisions, missing
files, ambiguous targets, unsupported formats, and unknown comparison states must never be
silently accepted.

VCI and filesystem operations must not be presented as atomic unless live V21 evidence proves an
applicable transaction boundary. Ordered batches report applied, failed, and skipped operations,
stop according to the locked batch policy, and return reconciliation evidence after uncertain or
partially completed outcomes. Automatic retry of an uncertain synchronization is prohibited.

## Delivery Phases

### Phase 1: Lock the VCI Contract and Safety Baseline

Define the VCI-only domain boundary, the two-tool schemas, typed operation/result envelopes,
error categories, path representation, selector requirements, and dual project/filesystem safety
snapshot. Reuse the shared canonical JSON and safety infrastructure rather than creating a VCI
variant.

Establish an installed-V21 capability matrix for workspace properties, supported object types,
file formats, mapping behavior, and operation side effects. Compile-time metadata proves API
shape; separately authorized live probes are required before claiming runtime behavior.

### Phase 2: Add Read-Only Discovery, Capability, and Status Operations

Introduce `workspace_read` with bounded operations covering the VCI group tree, workspaces,
workspace properties, mapped objects, supported formats, individual comparison status, and
hierarchical child status.

Results must provide deterministic selectors and enough evidence for later writes without relying
on display names alone. Unsupported, unavailable, missing, unknown, and unreadable states remain
distinct. Complete this phase with a separately authorized read-only V21 acceptance run against a
representative project and workspace.

### Phase 3: Add Workspace-Group and Workspace Lifecycle Operations

Introduce the first `workspace_write` operations for supported group and workspace creation,
property updates, and deletion. Include workspace language and the VCI-specific global-library
properties exposed by V21.

Preview must make configuration and filesystem implications explicit. Live qualification must
measure deletion and root-path behavior rather than infer whether files are preserved, moved, or
removed. Project saving remains a separate lifecycle concern and is never performed implicitly.

### Phase 4: Add Mapping, Connect, Export, and Disconnect Operations

Add object-specific format discovery to the write workflow, connect existing workspace files,
export supported engineering objects, and remove mappings through the verified V21 API.

This phase must settle deterministic engineering-object selection, relative-path normalization,
multi-file format evidence, collision and overwrite behavior, mapping identity, and post-operation
status. Preview and apply bind exact object, workspace, format, path, file, and current-state
evidence.

### Phase 5: Add Bidirectional Synchronization and Conflict Handling

Add explicit mapped-object synchronization in both supported directions:

- `SynchronizationMode.ProjectToWorkspace`; and
- `SynchronizationMode.WorkspaceToProject`.

Direction is mandatory and never inferred. Preview exposes current comparison details, expected
side effects, conflicts, missing files, and unknown states. Apply revalidates all bound evidence,
executes once, and then performs status and state reads sufficient to classify the result as
applied, failed, or requiring reconciliation.

The detailed phase design will lock conflict policy, partial-failure behavior, and the treatment
of mappings whose project object and workspace file both changed.

### Phase 6: Qualify, Stabilize, and Document the Complete Surface

Complete the automated contract, schema, access-mode, FakeWorker, protocol-error, payload-budget,
safety-token, path-confinement, audit, and postcondition matrix. Verify both stub and installed V21
reference builds.

Run separately authorized live acceptance against a disposable project copy and disposable
workspace root. Cover representative object types and formats, both synchronization directions,
file collisions and missing files, mapping deletion, workspace deletion, conflict states, and
repeatability. Publish the supported operation matrix and retain explicit gaps for anything not
proved live.

## Contract Principles

- Keep Siemens Openness access in the .NET Framework worker.
- Reuse the shared structured-result, operation-batch, and canonical-safety seams.
- Prefer typed VCI requests and results over extending the generic batch DTO.
- Use deterministic, snapshot-verifiable selectors; names alone are insufficient.
- Treat a workspace root as an explicit external-write boundary.
- Query supported formats for the selected engineering object; do not hardcode a universal list.
- Preserve compare states and details instead of reducing them to a boolean.
- Never infer synchronization direction or resolve a conflict silently.
- Never silently overwrite, delete, disconnect, save, or retry.
- Return enough postcondition and filesystem evidence for an agent to verify the outcome.
- Do not claim live behavior from stubs, FakeWorker tests, metadata inspection, or compilation.

## Implementation Anchors

The detailed phase plans should build on these existing seams:

- `TiaMcpServer/Json/CanonicalJson.cs`
- `TiaMcpServer/Tools/StructuredToolResult.cs`
- `TiaMcpServer/OperationBatches/StructuredOperationBatch*.cs`
- `TiaMcpServer/Safety/CanonicalWriteSafety.cs`
- `TiaMcpServer/Worker/OpennessWorkerClient.cs`
- `TiaMcpServer.Contracts/WorkerRequest.cs`
- `TiaMcpServer.OpennessWorker/Program.cs`
- new VCI-domain host, contract, and worker components whose exact organization is reserved for
  the relevant detailed plan

## Decisions Reserved for Detailed Design

Later phase designs must settle:

- exact operation names and request/result property shapes;
- group, workspace, engineering-object, and mapped-object selector contracts;
- external file-state hashing, budgets, and change detection;
- nested-group and duplicate-name identity rules;
- supported object and file-format allowlists derived from live evidence;
- semantics for multi-file formats and partial file sets;
- overwrite, disconnect, mapping-delete, workspace-delete, and user-group-delete behavior;
- transaction availability and ordered-batch stop/skip behavior;
- conflict handling for `Unknown`, missing-file, and both-sides-changed states;
- reconciliation envelopes for uncertain project or filesystem outcomes; and
- the boundary between a VCI mutation and explicit project saving.
