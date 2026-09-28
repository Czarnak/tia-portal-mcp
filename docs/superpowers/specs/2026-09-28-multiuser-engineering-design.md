# Multiuser Engineering Design

**Date:** 2026-09-28

**Status:** Approved; PR 1 detailed planning authorized

**Source:** Issue [#65](https://github.com/Czarnak/tia-portal-mcp/issues/65), repository
commit `269f9b94c3e9df19e5a2e51d0ca7da515aca84ca`, the installed TIA Portal V21
Openness type surface, and Siemens V21 product and Openness documentation

**Delivery:** A serial sequence of independently reviewable pull requests. Every newly added or
newly enabled operation must complete operation-specific live TIA Portal V21 verification inside
its designated pull request before that pull request is merge-ready.

**Safety dependency:** This design classifies mutations and defines stable safety invariants, but
does not define a new preview/apply protocol. Concrete mutation integration waits for the separate
write-safety redesign and adopts the repository's then-current safety mechanism.

## Goal

Add first-class TIA Portal V21 Multiuser Engineering support without weakening the existing
two-process boundary, project binding, typed worker protocol, structured JSON contract, or
write-safety model.

Success means:

- `--project` and `open_project` accept an existing `.als21` local or exclusive session as well as
  a standalone `.ap21` project;
- project-content operations use one typed worker implementation wherever Siemens exposes a common
  `ProjectBase` surface;
- Project Server connections, groups, projects, local sessions, locks, markings, freshness, and
  connection observations are available through typed Multiuser operations;
- supported Project Server and local-session lifecycle mutations are explicit, exactly targeted,
  capability-gated, and integrated with the final repository safety mechanism;
- online and offline local-session behavior is truthful, including unknown collaboration state
  while the server is unavailable;
- direct server-project editing, discard, and revision-producing commit are explicit lifecycle
  operations rather than cleanup side effects;
- every operation has deterministic offline evidence and current-PR live evidence before merge;
  and
- standalone-project behavior remains compatible.

This specification governs architecture, public contracts, sequencing, and evidence. It does not
authorize implementation, GitHub writes, Project Server mutations, live TIA Portal execution, or
any specific disposable test target.

## Background

The current worker stores a concrete `Siemens.Engineering.Project` and opens only through
`TiaPortal.Projects`. A V21 local session is instead opened through
`TiaPortal.LocalSessions.Open(FileInfo)` and owns a `MultiuserProject`. `Project` and
`MultiuserProject` are siblings derived from `ProjectBase`; `LocalSession` owns lifecycle methods
such as `Save`, `Close`, `IsUptoDate`, and `CloseAndCommit`.

Consequently, Issue #65 is not an extension check around the existing opener. Project-content
services must receive the common engineering root, while lifecycle behavior must retain the
concrete standalone-project or local-session owner.

The reviewed `ArtyomInc/tia-portal-mcp` fork contains a read-only Multiuser inventory path, not an
implementation of `.als21` opening or Multiuser writes. It may inform behavioral comparison and API
provenance, but this repository implements the feature independently against current `main`; no
wholesale merge or cherry-pick is part of this design.

Installed API signatures and Siemens documentation prove the available type and method surface.
They do not prove Project Server reachability, credentials, locks, mode transitions, conflict
behavior, persistence, discard, or revision creation. Those claims require the per-PR live gates
defined below.

## Scope

### Included

- Existing Multiuser and Exclusive `.als21` local sessions.
- Project Server connection inventory and supported connection lifecycle.
- Server groups and server-project inventory.
- Local-session inventory, creation, opening, saving, and supported deletion.
- Compatible existing project-content reads and writes through `MultiuserProject`.
- Online and offline local-session engineering.
- Direct server-project opening through `LocalSessions.OpenServerProject`.
- Lock, freshness, marking, and conflict-related observations exposed by V21 Openness.
- Mark and unmark operations.
- Explicit discard and `CloseAndCommit` flows supported by V21 Openness.
- Multiuser and Exclusive session modes.
- Typed connection observations and transition reporting in tool results.

### Excluded

- Version Control Interface.
- Multiuser Commissioning orchestration.
- Server-library workflows.
- Project Server installation and administrative-tool automation.
- Project-group creation or deletion where V21 Openness exposes no supported API.
- Credential storage, Windows-account management, or a new authentication system.
- Automatic export or conversion of `.als21` to `.ap21`.
- UI automation as a substitute for Openness.
- Guaranteed unsolicited messages to an MCP agent.
- Parsing localized Siemens notifications or exception text as application state.
- Undocumented `CheckIn`, `Update`, conflict-options, or access-options APIs.

## Selected architecture

Three approaches were considered:

1. **Typed common root plus lifecycle owner.** Project-content services receive `ProjectBase`, and
   one active context retains either a standalone `Project` or a `LocalSession` owner.
2. **Repository-owned project facade.** A broad local interface mirrors the Siemens object model
   and wraps both project types.
3. **Separate Multiuser implementations.** Existing operations are duplicated for
   `MultiuserProject`.

The first approach is selected. It follows the installed inheritance model, keeps one engineering
implementation, and makes lifecycle differences explicit. A broad facade would duplicate a large
and changing API surface. Separate implementations would duplicate behavior, tests, fixes, and
safety integration.

Reflection or `dynamic` is not the primary architecture. It may be used only for a narrowly
documented compatibility gap after the typed installed and stub surfaces have been exhausted.

## Active project context

The net48 worker owns one `ActiveProjectContext`:

```text
ActiveProjectContext
├── EngineeringRoot: ProjectBase
├── Owner
│   ├── StandaloneProjectOwner → Project
│   └── LocalSessionOwner      → LocalSession
├── BindingIdentity
├── ContainerKind
├── SessionMode
├── CapabilitySet
├── RemoteIdentity?
└── ConnectionObservation?
```

`EngineeringRoot` is the only project root passed to compatible project-content services.
Standalone lifecycle methods use `Project`. Local-session and server-project lifecycle methods use
`LocalSession`. Host and contract projects remain Siemens-free.

The context describes identity and applicability; it does not authorize a mutation and does not
own the preview/apply protocol.

### Container kind

- `standaloneProject`
- `localSession`
- `serverProject`

### Session mode

- `multiuser`
- `exclusive`
- `unknown`
- `notApplicable`

An arbitrary directly opened `.als21` does not necessarily expose its creation mode through the
public object model. The worker does not infer an authoritative mode from `_LS` or `_ES` filename
conventions. Mode is populated only from trusted operation provenance or exact server/session
resolution; otherwise it remains `unknown`.

### Lifecycle ownership

The worker records whether it opened the project or session. It never calls `LocalSession.Close()`
as unconditional cleanup because Siemens defines that call as discarding pending changes. A worker
shutdown, transport failure, or host disposal must not be reported as a successful discard or
commit.

## Binding and remote identity

The binding extends the existing worker identity, TIA process ID, generation, and canonical path
with `containerKind`, `sessionMode`, and capabilities.

When known, `RemoteIdentity` contains:

- exact Project Server alias;
- root or exact server-group name;
- exact server-project name;
- exact local-session ID; and
- canonical local-session path.

Remote operations require every identity component needed by that operation. A server alias alone
does not identify a project. A project name without its group context does not resolve an ambiguous
project. A session path and ID must agree when both are available.

Selection is exact and deterministic. Zero matches returns `target_not_found`; more than one match
returns `ambiguous_target`. The worker never selects `.First()`, index zero, the first open project,
or the first running TIA Portal process as a target-resolution fallback.

Changes to worker instance, TIA process, binding generation, canonical session path, server alias,
host, port, protocol, project, group, or session identity invalidate affected observations and
capabilities.

## Capability model

Every registered tool operation is classified as one of:

- supported for standalone and Multiuser project content;
- standalone-only;
- supported for local sessions with Multiuser preconditions;
- server-project-only;
- supported but not yet delivered; or
- intentionally unsupported.

Capabilities are descriptive, not authorization. They say whether an operation is structurally
applicable to the active context. Binding validation, the current safety system, authentication,
connectivity, locks, freshness, markings, and operation-specific preconditions still determine
whether a call may execute.

No existing operation is advertised as `.als21` compatible until its worker path, capability
entry, offline tests, and designated-PR live verification are complete. Unsupported operations fail
before mutation dispatch with `unsupported_capability`.

Standalone-only operations such as `save_project_as` and archive do not silently export or convert
a local session. Export to a single-user project is outside this specification.

## Public tool surface

The public surface is hybrid.

### Existing lifecycle tools

- `open_project` accepts `.ap21` and `.als21`. An `.als21` path always means an ordinary local or
  exclusive session open through `LocalSessions.Open`; it does not mean direct server-project
  editing.
- `save_project` dispatches to `Project.Save()` for a standalone project and
  `LocalSession.Save()` for a local or exclusive session.
- Existing project-content tools operate through the active context when their capability entry
  permits it.

Opening a server project is intentionally separate because it is connected, lock-sensitive,
exclusive work with terminal discard-or-commit semantics.

### `multiuser_read`

`multiuser_read` is a structured operation-catalog tool. Its initial operation catalogue is:

- `list_server_connections`
- `list_server_groups`
- `list_server_projects`
- `list_local_sessions`
- `get_session_state`
- `get_lock_state`
- `get_markings`

Every operation has one request type and one result type. Project Server reads may operate without
an active project when the Siemens navigator allows it. Session-scoped reads pin and validate the
active binding.

### `multiuser_write`

`multiuser_write` is the structured mutation and lifecycle catalogue. Its planned operation set is:

- `create_server_connection`
- `update_server_connection`
- `delete_server_connection`
- `add_project_to_server`
- `create_local_session`
- `delete_local_session`
- `open_server_project`
- `mark_objects`
- `unmark_objects`
- `discard_local_session`
- `close_and_commit`

The final request form for preview/apply is owned by the safety redesign. Multiuser operations do
not introduce a parallel token, canonicalization, audit, lease, or confirmation mechanism.

`mark_objects` and `unmark_objects` manipulate markings only. A successful marking operation is not
reported as check-in. V21 Openness exposes no parameterless `CheckIn()` or `Update()` method, so
this design does not fabricate one.

### Direct server-project editing

`multiuser_write.open_server_project` calls `LocalSessions.OpenServerProject` for an exact local
session file associated with an exact server project. It requires current connectivity, complete
remote identity, and lock preflight. It has no offline mode.

The resulting context has `containerKind=serverProject`. It must end through an explicit
`discard_local_session` or `close_and_commit` request. Generic cleanup never chooses between them.

## Structured contract

Both new tools use the shared structured JSON seam:

- one CLR result type per operation;
- strict worker-payload decoding;
- rejected worker payloads become `protocol_error` and are never echoed;
- no nested JSON strings;
- one `CanonicalJson.Serialize` result supplies both the text content block and
  `structuredContent`;
- success and rejection probes are added to tool-output conformance tests; and
- each new tool is removed from any legacy register only when its structured probes pass.

The new contracts live in `TiaMcpServer.Contracts` and contain no Siemens types.

## Open and execution flow

```text
MCP client
  → host tool and typed request
  → binding/capability validation
  → newline-delimited WorkerRequest
  → net48 worker operation catalogue
  → ActiveProjectContext
  → Siemens Openness API
  → typed WorkerResponse
  → canonical structured MCP result
```

### Opening a project or session

1. The host validates and canonicalizes the requested path.
2. The worker selects the opener by extension:
   - `.ap21` → `TiaPortal.Projects.Open`
   - `.als21` → `TiaPortal.LocalSessions.Open`
3. The worker builds a replacement active context only after Siemens reports success.
4. The response contains the complete binding snapshot, capabilities, and initial connection
   observation.
5. A failed open leaves the prior binding unchanged unless Siemens has independently invalidated
   it, in which case the failure reports that invalidation explicitly.

### Multiuser reads

1. Pin the active binding when the operation is project or session scoped.
2. Resolve every supplied selector exactly.
3. Perform the typed Siemens read.
4. Return the typed result plus any connection transition observed during the call.

### Multiuser writes

1. Enter the repository's then-current safety integration seam.
2. Pin and revalidate the binding.
3. Resolve the exact target again.
4. Perform fresh operation-specific connectivity, lock, freshness, and marking checks.
5. Execute one declared mutation.
6. Read the operation-specific postcondition.
7. Update or invalidate the active context when lifecycle state changes.

## Offline sessions and connectivity

Siemens V21 explicitly supports opening and editing a valid Multiuser or Exclusive local session
while its associated Project Server is unavailable. Offline work cannot see other users'
selections, remote markings, or outdated-object indicators, and it cannot update or check changes
into the server until connectivity returns.

The server therefore supports an offline-capable local-session context:

- compatible local project reads, edits, imports, compilation, and `LocalSession.Save()` may
  proceed;
- remote session management, lock-dependent work, update/check-in-equivalent workflows, direct
  server-project open, and `CloseAndCommit` are unavailable;
- freshness, lock ownership, other-user markings, and remote conflict state are `unknown`, not
  `false`, empty, unlocked, or current; and
- reconnecting requires fresh conflict and state observations before a server-dependent operation.

The product documentation describes confirmation dialogs when an offline session opens. It does
not prove how non-interactive `LocalSessions.Open` behaves for this server. Online and offline
non-interactive opening are mandatory live cases in the `.als21` opening PR.

## Connection observation model

V21 Openness exposes no documented typed connection-changed event on `LocalSession` or
`ProjectServer`. General TIA `Notification` events are human-facing messages and are not parsed as
state. The current MCP host also has no guaranteed agent-visible unsolicited event path.

Connection state is therefore observed rather than continuously asserted:

```text
state: connected | unavailable | unknown
observedAt
observationSource
previousState
transition
```

The state is observed:

- during local-session opening;
- through an explicit `get_session_state` read;
- before every server-dependent operation; and
- after a server-related failure when a bounded observation remains possible.

Detected transitions appear in the next tool result. Correctness never depends on asynchronous
delivery. A future subscribed MCP resource may provide best-effort change notification, but it is
not part of this specification and cannot replace fresh preflight checks.

## Safety integration boundary

The concurrent safety redesign owns the concrete preview/apply request shape, token or handle
format, canonicalization, lease, audit, and confirmation flow. Multiuser design depends only on
these stable invariants:

- every externally visible or destructive mutation uses the repository-wide safety mechanism;
- selectors, ordered inputs, binding identity, relevant current state, and intended lifecycle
  consequence are bound by that mechanism;
- apply revalidates exact identity and operation-specific preconditions immediately before
  dispatch;
- changing input, binding, server/session identity, or relevant state invalidates stale authority;
- no operation saves, closes, discards, deletes, commits, or changes a server connection
  implicitly; and
- an unknown mutation outcome is inspected, never automatically replayed.

PRs that only establish stubs, contracts, internal context, or read-only inventory may proceed
before the safety redesign. The first PR exposing `.als21` lifecycle behavior or a mutation must
reconcile with the merged safety design before implementation.

## Error and uncertainty model

Stable failure categories are:

- `invalid_input`
- `unsupported_capability`
- `target_not_found`
- `ambiguous_target`
- `binding_changed`
- `server_unavailable`
- `authentication_required`
- `access_denied`
- `project_locked`
- `session_stale`
- `marking_conflict`
- `operation_failed`
- `postcondition_failed`
- `protocol_error`
- `timeout_unknown_outcome`

Each failure identifies one phase: `validation`, `selection`, `precondition`, `execution`, or
`postcondition`.

Rules:

- Server unavailability updates the observation but does not unbind a successfully opened local
  session.
- Local-compatible operations may continue offline.
- A disposed TIA Portal process or invalid local session clears the affected binding.
- `MultiuserException` maps to a specific category only when the API call and typed state establish
  that category. Localized exception text is not parsed.
- Authentication and authorization remain distinct from network unavailability.
- Ambiguity always stops execution.
- A failed postcondition is not success.
- A timeout, worker crash, disconnect, or possible mutation after dispatch becomes
  `timeout_unknown_outcome` and is not retried automatically.
- Discard or commit failure leaves lifecycle ownership unresolved until inspected.
- Returned diagnostics exclude credentials and do not echo rejected worker payloads.

## Reference-stub strategy

The tracked `ref/` assemblies already expose `ProjectBase`, `MultiuserProject`, `LocalSession`,
`LocalSessionComposition`, and the Project Server type family. Metadata revalidation after design
approval confirmed that they are strong-named V21 reference assemblies with a much broader,
opaque surface than this server needs; their source definition is not present in the repository.

Before production code depends on those types, the reference-foundation PR must replace that
opaque baseline with a reproducible, repository-owned way to build the minimal compile-time
surface. The result must:

- contain no Siemens proprietary binaries;
- model only signatures required by production compilation and deterministic tests;
- make the stub source reviewable;
- preserve the real-reference worker build against installed V21 assemblies; and
- fail CI when the generated stub artifact and its source definition drift.

The detailed PR plan chooses the smallest implementation consistent with those requirements after
revalidating the current build pipeline. Production behavior is never inferred from a stub.

## Serial pull-request sequence

Successor plans are written just in time from merged `main`, after predecessor evidence is
reviewed. If a predecessor changes an assumption, the successor is replanned rather than
mechanically rebased.

### PR 1 — V21 reference and contract foundation

- Add reproducible Multiuser compile stubs.
- Add Siemens-free container-kind, session-mode, capability, remote-identity, and connection-
  observation contracts.
- Add no public operation.
- Gate: stub and real-reference builds plus contract tests.

### PR 2 — Active project context

- Introduce the typed common root and lifecycle owners.
- Preserve standalone `.ap21` behavior.
- Migrate the central worker session seam without exposing Multiuser.
- Gate: full offline suite plus live `.ap21` lifecycle regression because the binding seam changes.

### PR 3 — Read-only Multiuser inventory

- Add `multiuser_read` with server-connection, group, server-project, local-session inventory, and
  server-project lock-state operations that do not require an opened `.als21` context.
- Use exact typed selectors and canonical structured output.
- Gate: each added read operation receives live verification against the fixture tier it requires.

### Safety reconciliation checkpoint

Before the first `.als21` lifecycle operation or Multiuser mutation, refresh from the merged safety
redesign and record the final integration seam in the detailed successor plan. This is a dependency
gate, not a competing safety PR.

### PR 4 — Open existing `.als21` sessions

- Extend `open_project` and `--project`.
- Add binding, capability, active-session state, initial connection observation, and reconnection
  reporting.
- Gate: live Multiuser and Exclusive opening online and offline, binding evidence, close ownership,
  and observed reconnection behavior.

### PR 5 — Project-content read compatibility

- Enable existing read operations against `ProjectBase` according to the capability matrix.
- Split into additional serial PRs if the live-verifiable operation set is too large for one review.
- Gate: every newly enabled `.als21` read operation is exercised live; affected prior operations
  receive targeted regression.

### PR 6 — Project-content writes and local save

- Enable compatible existing writes, imports, compilation, and `LocalSession.Save()`.
- Integrate with the merged safety mechanism.
- Gate: every newly enabled write/import/compile/save path receives live verification in this PR or
  is moved into its own PR.

### PR 7 — Project Server and session lifecycle

- Add supported connection create/update/delete, add-project, local-session create/delete, and
  direct server-project open operations.
- Gate: each lifecycle operation receives exact-target live verification and postcondition evidence.

### PR 8 — Locks and markings

- Complete active-session freshness and marking reads, integrate the previously delivered lock
  reads into mutation preconditions, and add mark/unmark operations.
- Gate: live ownership, other-user marking, conflict, stale-session, and negative precondition cases.

### PR 9 — Discard and remote commit

- Add explicit discard and `CloseAndCommit` for supported contexts.
- Gate: live discard evidence or returned revision plus authoritative server-state postcondition.
  Failure and unknown-outcome recovery are exercised without automatic replay.

No operation-bearing PR is merge-ready without its own live evidence. A final accumulated matrix
may be rerun for release qualification, but it never substitutes for designated-PR evidence.

## Verification model

Every PR uses the layers appropriate to its scope:

1. **Contract tests:** selectors, capability matrix, payload types, canonical JSON, null policy, and
   conformance registration.
2. **Worker tests:** dispatch, context ownership, binding invalidation, operation mapping, and stub
   compilation.
3. **Host/FakeWorker integration:** MCP schemas, structured output, lifecycle rebinding, failure
   categories, and the safety seam.
4. **Real-reference build:** compile the net48 worker against installed V21 assemblies.
5. **Operation-specific live acceptance:** mandatory for each new or newly enabled operation.

Live fixture tiers are capability-based:

- a disposable local Project Server fixture for ordinary local/exclusive sessions, offline work,
  content reads/writes, compilation, local save, and basic commit behavior;
- a disposable dedicated Project Server fixture for remote connection management, groups, remote
  session management, connectivity loss/reconnect, locks, and revisions; and
- a multiple-client or multiple-user fixture for other-user markings, ownership, conflicts,
  stale-session behavior, and concurrency-sensitive postconditions.

Each PR's evidence records:

- frozen commit SHA;
- TIA Portal and Project Server versions;
- fixture alias and exact authorized operation scope;
- sanitized request and response;
- before/after server and session inventory;
- returned session IDs or revisions where applicable;
- expected and observed postconditions;
- cleanup or recovery state; and
- explicit limitations.

TIA Portal and Project Server state are shared across worktrees, so live execution is serialized.
Live mutation requires fresh authorization for the exact disposable fixture and operation. If the
required fixture tier is unavailable, the PR remains unqualified. Stub, FakeWorker, static,
real-reference compilation, or historical evidence never substitutes for the required live run.

A timeout, crash, disconnect, or possible mutation stops the run for inspection. The harness does
not replay automatically. State is restored or a fresh exact-target authorization is obtained
before any later mutation.

## Documentation and issue lifecycle

Each implementation PR updates maintained documentation for the behavior it actually delivers and
live-verifies:

- `docs/ARCHITECTURE.md` for active context, binding, and lifecycle changes;
- `docs/SupportedOperations/MULTIUSER_OPERATIONS_SUMMARY.md` for operation contracts,
  capabilities, and live-evidence boundaries;
- `README.md` when the public tool list or startup behavior changes;
- `docs/IMPROVEMENT_LOG.md` for remaining follow-ups; and
- operation-specific development or acceptance instructions when required.

Issue #65 is the initial user-visible requirement: open `.als21` and use compatible engineering
operations. This design governs the broader Multiuser sequence, but issue-closing metadata is used
only when Issue #65's own accepted behavior and evidence are complete. Later Multiuser operations
remain independently traceable even if Issue #65 closes earlier.

Current behavior remains authoritative in code, `docs/ARCHITECTURE.md`, and SupportedOperations.
This design remains historical process material after implementation.

## Acceptance criteria for the program

The Multiuser program is complete when:

1. all delivered operations have strict typed host and worker contracts;
2. `.ap21` behavior remains compatible;
3. `.als21` Multiuser and Exclusive sessions open online and offline through `open_project`;
4. the capability matrix truthfully covers every registered project-scoped operation;
5. compatible project-content reads and writes use the common typed root;
6. all Project Server and session selectors reject missing and ambiguous targets;
7. connection observations never claim continuous knowledge or turn unknown state into healthy
   state;
8. local offline work remains available while server-dependent operations fail closed;
9. direct server-project editing, discard, and commit have explicit lifecycle ownership;
10. mutation operations use the merged repository safety mechanism without a parallel protocol;
11. every operation-bearing PR contains current live evidence for every operation it adds or newly
    enables; and
12. maintained documentation states the delivered surface and its evidence boundary accurately.

## References

- [Issue #65 — Support for Project Server local sessions](https://github.com/Czarnak/tia-portal-mcp/issues/65)
- [Siemens V21 — Working offline with Multiuser Engineering](https://docs.tia.siemens.cloud/r/en-us/v21/using-team-engineering/using-multiuser-engineering/working-offline-with-multiuser-engineering)
- [Siemens V21 — Opening a local session of a server project](https://docs.tia.siemens.cloud/r/en-us/v21/using-team-engineering/using-multiuser-engineering/working-in-the-local-session/creating-and-managing-a-local-session/opening-a-local-session-of-a-server-project)
- [Siemens V21 Openness — Functions support for Multiuser](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/functions-support-for-multiuser)
- [Reviewed external read-only Multiuser implementation](https://github.com/ArtyomInc/tia-portal-mcp/commit/d262a6a311129bf91f2878cf52c96c58b9bc0c9e)

## Current verification boundary

This document records the approved conversational design. Repository and external-source review,
installed-assembly reflection, serial build, and current-main GitHub CI verification were read-only
or offline activities. They do not prove live Multiuser behavior.

No production code, Project Server connection, local session, server project, TIA Portal project,
PLC, or remote resource was changed while preparing this design. No live acceptance is claimed.
The written specification is approved. The next gate is review of the detailed PR 1 implementation
plan and selection of its execution method. Implementation remains unauthorized until that gate is
complete.
