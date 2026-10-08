# Multiuser Engineering Design

**Date:** 2026-09-28

**Revised:** 2026-10-03 for lifecycle alignment; 2026-10-05 for the user-selected `bind_project` read surface, against `main` at `ea269c222e7415af347f2929cbc82a366aa2ee0f`

**PR 4 identity revision:** 2026-10-07, user-approved typed `.amc21` owner binding with nullable `.als21` opener provenance, as detailed in the [revised PR 4 plan](../plans/2026-10-07-multiuser-pr4-open-existing-sessions.md). This replaces the earlier ALS adoption/configured-selector assumption; all unaffected successor scope and evidence gates remain.

**Status:** Core design approved; read routing through `bind_project` selected by the user on 2026-10-05. PR 3 is implemented and offline-qualified; the maintainer accepted its live testing as finished and passed on October 6. The [installed-tool live report](../acceptance/reports/2026-10-06-multiuser-pr3-live-verification.md) preserves executed observations and non-blocking unexecuted cases. Successor PR gates remain unchanged.

**Source:** Issue [#65](https://github.com/Czarnak/tia-portal-mcp/issues/65), repository
commit `269f9b94c3e9df19e5a2e51d0ca7da515aca84ca`, the installed TIA Portal V21
Openness type surface, and Siemens V21 product and Openness documentation

**Delivery:** A serial sequence of independently reviewable pull requests. Every newly added or
newly enabled operation must complete operation-specific live TIA Portal V21 verification inside
its designated pull request before that pull request is merge-ready.

**Safety baseline:** The guarded single-call pipeline and lifecycle migration are implemented.
Multiuser lifecycle builds on `WriteExecution`, `LifecycleBindingStrategy`, mode-derived confirmation,
and audit v2. Network also uses guarded single-call writes; legacy generic batch writes retain tokens. New
Multiuser writes must not add a token-bound surface or a parallel safety mechanism.

## Goal

Add first-class TIA Portal V21 Multiuser Engineering support without weakening the existing
two-process boundary, project binding, typed worker protocol, structured JSON contract, or
write-safety model.

Success means:

- `open_project` explicitly opens an existing `.als21` local or exclusive session as well as a
  standalone `.ap21` project;
- `--project` and `TIA_MCP_PROJECT_PATH` accept the typed local owner's `.amc21` path as a configured selection assertion, and
  `bind_project` adopts an already-open session without opening, saving, closing, discarding, or
  committing it;
- project-content operations use one typed worker implementation wherever Siemens exposes a common
  `ProjectBase` surface;
- Project Server connections, groups, projects, local sessions, locks, markings, freshness, and
  connection observations are available through typed inspection actions on `bind_project`;
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

### Current lifecycle and selection baseline

The October 3 revision preserves the delivered standalone behavior described in
[Architecture](../../ARCHITECTURE.md#5-project-attachment-and-binding) and
[Project operations](../../SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md#lifecycle-operations):

- The six lifecycle tools (`open_project`, `create_project`, `save_project`, `save_project_as`,
  `archive_project`, `close_project`) are available in read-write and full. Read-only denies them.
- Every lifecycle tool accepts `dryRun`; public `confirm`, `safetyToken`, and `acknowledge` arguments
  are absent. `--confirm-with-user` is removed. Read-write requires one form elicitation for every
  actual lifecycle call, including calls without acknowledge guards; full uses policy satisfaction.
- `bind_project` is `SessionSelection`, permitted in every mode. It selects an already-open `.ap21`
  project today. Extending it to typed local owners selected by `.amc21` is planned PR 4 work, not delivered behavior.
- `--project` and `TIA_MCP_PROJECT_PATH` configure an assertion; they never open a project. Ordinary
  reads and writes never adopt or switch an unbound selection. Only an explicit opener creates an
  open engineering context.
- Selection, switching, and writes use the existing serialized binding gate. Worker-reported
  identity verifies a binding; a configured path alone does not. Detach transfers no lifecycle
  ownership and does not save or close the previous UI project. A modified headless project with
  this worker as its only Openness client blocks detach to avoid loss.
- Lifecycle output is canonical structured JSON with typed `result` and `verification`, and each
  call produces one audit v2 record. Network and batch tokens remain transitional consistency
  checks; they are not user consent.

This baseline is implemented for standalone projects. The `.als21` changes below remain planned
and require their own offline and live evidence.

## Scope

### Included

- Existing Multiuser and Exclusive `.als21` local sessions.
- Explicit selection and adoption of already-open sessions through `bind_project`.
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

The context describes identity and applicability; it does not authorize a mutation or replace the
shared guarded write pipeline.

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

Adopting an already-open local session does not make it worker-owned. Ownership does not survive
detach or reattachment. A switch must preserve the old UI-owned session and block if safe detach
cannot be established, including unsaved headless-session changes. Standalone open's ability to
close a clean worker-owned source must not automatically become permission to discard a local
session. Require an explicit terminal session operation first when switching would close it.

## Binding and remote identity

The binding extends the existing worker identity, TIA process ID, generation, and canonical path
with `containerKind`, `sessionMode`, and capabilities. Retain the host binding ID/revision and the
`unbound`, `configured_unverified`, `verified`, and `invalidated` states; do not introduce a separate
Multiuser binding state machine. Project-tree cursors continue to reject ID/revision changes as
`cursor_binding_mismatch`.

For local sessions, the canonical binding/status path is the typed `LocalSession.Project.Path`
`.amc21` engineering path. Include exact Portal PID, owner kind and live owner/generation continuity;
path equality alone does not establish continuity after close/reopen. `sessionContainerPath` is null
for cold adoption and may contain an exact `.als21` input only as provenance after its opener returned
a verified typed owner/root. It is not a reverse identity getter or a cold-adoption selector.
Different engineering paths distinguished the observed October 7 fixtures; same/ambiguous paths,
owner replacement and unproved remote-session association fail closed. Filenames, parent directories,
private ALS data and hierarchy establish neither mode nor a remote identity join.

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
returns `target_ambiguous`. The worker never selects `.First()`, index zero, the first open project,
or the first running TIA Portal process as a target-resolution fallback.

`bind_project(projectPath?, forceRebind=false)` retains its exact-one selection and source-conflict
rules for typed local owners selected by `.amc21`. The existing positive `portalProcessId` argument
becomes valid for `bind`, selecting one exact Portal before typed owner enumeration; it adds no
argument or action. Without deterministic advertised metadata or an explicit exact PID, ambiguity
is returned before attachment rather than attaching arbitrary Portals to search. A different PID
or configured/last-bound path requires
`forceRebind`; that flag permits selection, not saving, closing, discard, or commit. Same-context
calls reverify worker identity. Results retain non-null before/after binding state and candidate
inventory obtained during the call. A status read may verify the matching configured assertion,
but cannot establish a different selection. Read-only can adopt an already-open session; it cannot
open one. TIA's Openness access dialog is separate from lifecycle elicitation.

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

### Existing selection and lifecycle tools

- Planned `open_project` support accepts `.ap21` and `.als21` through the existing guarded lifecycle
  tool, preserving `forceRebind`, `dryRun`, and mode-derived confirmation. An `.als21` path means a
  local or exclusive session open through `LocalSessions.Open`; it does not mean direct
  server-project editing.
- Planned `bind_project` and startup-selector support accepts the typed owner's exact `.amc21`
  engineering path for selection of an already-open session. `.als21` remains an explicit opener
  input and nullable provenance, not an adoption/configured assertion. No read, write, or configured
  startup path implicitly opens it.
- `save_project` dispatches to `Project.Save()` for a standalone project and
  `LocalSession.Save()` for a local or exclusive session when PR 6 enables it, with the same lifecycle
  guards, dry-run, confirmation, verification, and audit seam.
- `create_project`, `save_project_as`, and `archive_project` remain standalone-only. Session
  creation is a separate `multiuser_write.create_local_session` operation.
- `close_project` remains standalone-only in this design. Local-session and server-project contexts
  use explicit `discard_local_session` or `close_and_commit`; generic close must not silently map to
  `LocalSession.Close()` or choose a commit. Those terminal operations are delivered in PR 9.
- Existing project-content tools operate through the active context when their capability entry
  permits it.

Opening a server project is intentionally separate because it is connected, lock-sensitive,
exclusive work with terminal discard-or-commit semantics.

### Discovery and inspection through `bind_project`

The user's October 5 decision is to retain `bind_project` as the only public discovery/inspection
tool for this work. No separate Multiuser read tool is added. One optional `action` selects a
single operation; omitted or null means `bind`, preserving the existing no-path auto-selection,
`projectPath`, and `forceRebind` behavior. The explicit inspection actions are:

- `list_portals`: fresh running-Portal discovery, without attaching or selecting a project;
- `list_server_connections`;
- `list_server_groups`;
- `list_server_projects`;
- `list_local_sessions`;
- `get_lock_state`;
- `get_session_state` and `get_markings`, delivered only in their successor PRs.

Inspection never opens, adopts, switches, saves, closes, discards, commits, or changes a binding.
Reject supplied `projectPath`/`forceRebind` on inspection actions, even null/false values; listing
must never fall through to the existing sole-project auto-bind behavior. Inventory accepts an
optional positive `portalProcessId` and action-specific exact server/group/project selectors.
Root groups use `{isRoot:true,name:null}`; named groups require an exact name and `isRoot:false`.

Keep one persistent Portal attachment. Inspection reuses its PID; with a verified host binding,
that PID and session identity are preservation assertions, not permission to select another
context. A deliberate different-PID request is refused locally before dispatch, preserving the
binding. When no Portal is attached, resolve the explicit PID exactly or require one candidate,
then attach without adopting a project. Inspection does not detach or create temporary workers.
To change the attached instance, use explicit binding with its existing conflict/detach guards.
Genuine worker/Portal/context loss still follows normal invalidation; inspection does not promise
that a dead context stays bound.

Every action has one logical request contract and one concrete worker result type. Project Server
reads may operate without an active project when the installed Siemens API permits it;
session-scoped successor reads pin and validate the active binding. Remote inventory failures
never become a prerequisite for ordinary binding, including offline session adoption.

Preserve `BindProjectResponse` and `StandaloneToolOutcome<ProjectBindingResult>`. The default
binding result keeps its existing shape. Inspection adds one conditional `inspection` member
to `ProjectBindingResult`, inside the outcome's budgeted value, containing the action, resolved
Portal PID, and typed result slots; whole-value omission removes the inventory too. Successful server inventory
fills exactly its action's slot, with other slots explicitly null. `list_portals` uses the existing
`portals` array. Inspection reports `transition:none`, `project:null`, and identical healthy
before/after binding snapshots. Whole-value omissions and strict payload decoding reuse the
existing standalone structured seam. Discovery counts remain 6/16/16; the combined tool retains
`ReadOnly=false` because its default action changes selection.

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

`multiuser_write` uses the shared guarded single-call pipeline: typed operation input, `dryRun`,
target planning, guards, mode-derived confirmation, mutation, verification, and audit. It introduces
no `confirm`, `safetyToken`, or caller `acknowledge` input. Lifecycle-classed operations ask once per
actual read-write call and run under policy in full. Operation capability classification and
domain-specific guards are reviewed in the delivering PR. Multiuser adds no parallel
canonicalization, audit, lease, or confirmation mechanism.

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

The extended `bind_project` inspection actions and planned `multiuser_write` use the shared
structured JSON seam:

- one CLR result type per operation;
- strict worker-payload decoding;
- rejected worker payloads become `protocol_error` and are never echoed;
- no nested JSON strings;
- one `CanonicalJson.Serialize` result supplies both the text content block and
  `structuredContent`;
- success and rejection probes are added to tool-output conformance tests; and
- existing tool probes cover every new action; any new mutation tool is removed from a legacy
  register only when its structured probes pass.

Lifecycle extensions preserve `LifecycleWriteResponse`: `phase`, `guards`, `effects`, typed
`result`, and typed `verification`. Rejections have `result:null`, a top-level `error`, and
`isError:true`; attempted mutation or verification failure has `success:false`, `error:null`, and
`isError:false`, with failure details in the typed result. A dry run reports `phase:preview` and
provides no authority for a later call. Use the shared structured seam for new Multiuser responses
and keep pre-dispatch rejection distinct from possible mutation.

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

1. Validate and canonicalize the requested path; enforce `ProjectLifecycle` at host and worker.
2. Prepare the exact source binding or a genuinely unbound open through the lifecycle strategy,
   then pin its revision under the shared lease.
3. Plan the exact source/destination effects and evaluate guards. `dryRun` returns those effects
   without opening, switching the project, or prompting. Block guards stop an actual call.
4. Read-write asks once and requires form `accept` plus boolean `confirm:true`; full satisfies
   acknowledge guards by policy. After acceptance, re-resolve effects and guards under the same
   lease; an accepted consequence cannot expand silently.
5. Only an actual permitted call dispatches the opener by extension:
   - `.ap21` → `TiaPortal.Projects.Open`
   - `.als21` → `TiaPortal.LocalSessions.Open`
6. Verify the returned typed owner/root and worker identity, then adopt its `.amc21` local binding
   and return the typed result with exact `.als21` input as separate nullable opener provenance,
   capabilities and connection observation. Unknown duplicate-destination or source-preservation
   evidence blocks before dispatch; a cold owner cannot be reused by ALS path heuristics.
7. Append one audit v2 record, including dry runs and blocked calls. A pre-dispatch refusal preserves
   the prior binding where continuity is verified. Failure after dispatch may have changed state;
   report actual binding state or invalidation and inspect before retrying.

Adopting an already-open session is a separate `bind_project` flow under `SessionSelection` and the
serialized binding gate. It never invokes either opener and never acquires discard/commit authority.

### Multiuser reads

1. Validate the `bind_project` action and its allowed selectors before worker activity. Default
   binding and inspection dispatch are separate paths.
2. Capture the binding under the existing host serialization gate; pin/verify it when the action
   is project/session scoped, and use a verified identity only to assert inventory continuity.
3. Resolve the Portal and every supplied selector exactly without changing selection or detaching.
4. Perform the typed Siemens read and return the canonical binding response with its inspection
   payload and any observed connection transition. Transport loss may invalidate genuinely stale
   binding; ordinary remote inventory failure preserves a healthy context.

### Multiuser writes

1. Enter `WriteExecution` through a typed domain; validate input and the operation's access policy.
2. Prepare and pin the exact binding. Server-only operations require an explicit, reviewed target
   strategy rather than inventing a fake active project or bypassing the lease.
3. Resolve exact targets and fresh connectivity, lock, freshness, and marking preconditions.
4. Evaluate shared catalog guards. A dry run returns effects and guards; it issues no token.
5. Apply mode-derived confirmation; lifecycle-classed read-write calls always ask once. Re-plan
   under the same lease after acceptance and reject changed consequences or new blocking guards.
6. Execute the declared mutation, verify its postcondition, and update or invalidate the context
   when lifecycle state changes.
7. Compose one canonical structured result and append one audit v2 record.

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

Observation sources are `sessionOpen`, `sessionBind`, `explicitRead`, `operationPreflight`, and
`postFailure`. `sessionBind` identifies an observation made while adopting an already-open session;
binding alone never establishes Project Server connectivity.

PR 4 inventory observations remain independently scoped to their exact endpoint. They are associated
with the active local owner only after a supported exact owner/session join; October 7 prerequisite
probes did not establish such a join. Cold adoption does not require remote inventory and reports
unknown remote identity/observation when association is unavailable. Operator fixture mappings,
ALS opener provenance, engineering paths, names and hierarchy cannot manufacture that association.

The state is observed:

- during local-session opening or explicit adoption of an already-open session;
- through an explicit `get_session_state` read;
- before every server-dependent operation; and
- after a server-related failure when a bounded observation remains possible.

Detected transitions appear in the next tool result. Correctness never depends on asynchronous
delivery. A future subscribed MCP resource may provide best-effort change notification, but it is
not part of this specification and cannot replace fresh preflight checks.

## Safety integration boundary

The implemented safety foundation owns canonicalization, binding preparation, the pinned lease,
guards, `dryRun`, mode-derived confirmation, verification, and audit v2. Multiuser domains extend
that seam with typed targets and preconditions:

- every externally visible or destructive mutation uses the repository-wide safety mechanism;
- selectors, ordered inputs, binding identity, relevant current state, and intended lifecycle
  consequence are resolved under the pinned lease;
- an actual call revalidates exact identity and operation-specific preconditions immediately before
  dispatch;
- changing binding, server/session identity, or relevant state rejects stale planning; a dry run
  grants no continuing authority;
- no operation saves, closes, discards, deletes, commits, or changes a server connection
  implicitly; and
- an unknown mutation outcome is inspected, never automatically replayed.

`info`, `acknowledge`, and `block` retain their shared catalog semantics. Missing form capability,
decline, cancel, timeout, transport failure, or acceptance without boolean `confirm:true` denies an
actual read-write lifecycle call with `access_denied`. Blocks cannot be overridden in full. Audit
confirmation records `user`, `policy`, or `none`; guard satisfaction is `user`, `policy`, or null.
Client-returned acceptance does not prove that a human saw a prompt.

No new snapshot reader, `SafetyRead` catalog entry, or token-bound write tool is introduced for
Multiuser. Existing compatible content writes preserve their current Network/batch token flows
until the relevant migration lands. PRs that only establish references, passive contracts, internal
context, or inventory preserve the delivered lifecycle behavior. Before exposing `.als21` lifecycle
or a new mutation, revalidate the merged pipeline and specify owner-aware planning, verification,
guards, and any unbound/server-only binding strategy in that PR's plan.

## Error and uncertainty model

Reuse the closed repository failure vocabulary for common failures:

- `validation_error`
- `target_not_found`
- `target_ambiguous`
- `binding_conflict`
- `target_kind_unsupported`
- `access_denied`
- `guard_blocked`
- `worker_operation_failed`
- `worker_timeout`
- `worker_crashed`
- `postcondition_failed`
- `protocol_error`

Proposed Multiuser-specific categories require explicit shared-contract registration and tests in
the delivering PR; they are not added by PR 1:

- `unsupported_capability`
- `server_unavailable`
- `authentication_required`
- `project_locked`
- `session_stale`
- `marking_conflict`

Preserve the lifecycle envelope phases (`preview`, `blocked`, `applied`, `error`). Domain diagnostics
may identify validation, selection, precondition, execution, or postcondition; they do not replace
the envelope phase or move attempted-operation failures into a pre-dispatch rejection.

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
- A timeout or worker crash uses `worker_timeout` or `worker_crashed`, preserving unknown-outcome
  evidence. A disconnect or untrustworthy payload after dispatch may also hide a mutation; inspect
  actual state and do not retry automatically.
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
- Migrate the central worker session seam without exposing Multiuser. Preserve `bind_project`,
  configured-selector promotion, detach ownership, and binding ID/revision continuity.
- Gate: full offline suite plus live `.ap21` selection/switching and guarded lifecycle regression,
  including dry runs, mode confirmation, typed outcomes, and audit, because the binding seam changes.

### PR 3 — Read-only Multiuser inventory

- Extend `bind_project` with explicit `list_portals` and server-connection, group, server-project,
  local-session, and server-project lock-state inspection actions; add no public tool. These
  actions do not require an opened `.als21` context and never perform binding as a side effect.
- Preserve default binding inputs/output and counts 6/16/16; use exact typed selectors, a
  conditional typed inspection result, and the existing canonical standalone response/budget.
- Gate: every added action receives operation-specific live verification against its required
  fixture tier; default `.ap21` binding, ambiguity, switching, ownership, and cursor continuity
  receive regression verification on the same frozen candidate.

PR 3 acceptance update (2026-10-06): the maintainer accepted all PR 3 live testing as finished
and passed. Its report retains unexecuted matrix cases as non-blocking evidence limitations;
this decision closes PR 3's gate without changing PR 4–9 scope or acceptance requirements.

### Lifecycle integration checkpoint

Before the first `.als21` selection/lifecycle operation or Multiuser mutation, refresh from merged
`main` and record the current `WriteExecution`/lifecycle strategy, `SessionSelection`, guard catalog,
typed response, and audit integration in the successor plan. The lifecycle foundation is already
available; remaining Network/batch migrations do not justify a new token protocol. Server-only
mutations must resolve their binding/lease strategy before implementation.

### PR 4 — Open existing `.als21` sessions

- Extend explicit `open_project`, already-open `bind_project`, and configured `--project` /
  `TIA_MCP_PROJECT_PATH` selectors; no implicit opening.
- Open exact `.als21` files; bind/configure/status use typed `.amc21` owner paths. Cold adoption
  carries null ALS provenance; successful opening records its exact input separately. Retain exact
  PID/owner/generation continuity, fail-closed same-path/duplicate handling and source/headless guards.
- Add binding, capability, active-session state, initial connection observation, and reconnection
  reporting. Remote inventory remains independent unless a supported exact owner/session join exists.
- Gate: live Multiuser and Exclusive opening online and offline, read-only adoption of already-open
  sessions, exact/ambiguous selection, switching and ownership preservation, dry runs, read-write
  confirmation/full policy, binding/cursor evidence, and observed reconnection behavior. Generic
  session close remains unavailable; fixture cleanup requires separately authorized exact actions.

### PR 5 — Project-content read compatibility

- Enable existing read operations against `ProjectBase` according to the capability matrix.
- Split into additional serial PRs if the live-verifiable operation set is too large for one review.
- Gate: every newly enabled `.als21` read operation is exercised live; affected prior operations
  receive targeted regression.

### PR 6 — Project-content writes and local save

- Enable compatible existing writes, imports, compilation, and `LocalSession.Save()`.
- Preserve existing content-write safety flows and extend guarded `save_project` with owner-aware
  effects, mode-derived confirmation, typed verification, and audit v2.
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
3. **Host/FakeWorker integration:** MCP schemas, structured output, explicit binding/selection,
   lifecycle rebinding, detach ownership, cursor invalidation, failure categories, dry runs,
   per-call confirmation/full policy, fresh re-planning, and audit v2.
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
   already-open sessions are adopted through `bind_project` in every mode, and configured selectors
   never open them implicitly;
4. the capability matrix truthfully covers every registered project-scoped operation;
5. compatible project-content reads and writes use the common typed root;
6. all Project Server and session selectors reject missing and ambiguous targets;
7. connection observations never claim continuous knowledge or turn unknown state into healthy
   state;
8. local offline work remains available while server-dependent operations fail closed;
9. direct server-project editing, discard, and commit have explicit lifecycle ownership;
10. new mutations use the shared guarded single-call pipeline, lifecycle confirmation follows mode,
    and dry runs, typed failure/verification, and audit v2 work without a parallel token protocol;
11. every operation-bearing PR contains current live evidence for every operation it adds or newly
    enables; and
12. maintained documentation states the delivered surface and its evidence boundary accurately.

## References

- [Current architecture and binding](../../ARCHITECTURE.md)
- [Current project selection, lifecycle, guards, and output contract](../../SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md)
- [Write-safety redesign and migration phases](2026-09-29-write-safety-redesign-design.md)
- [Issue #65 — Support for Project Server local sessions](https://github.com/Czarnak/tia-portal-mcp/issues/65)
- [Siemens V21 — Working offline with Multiuser Engineering](https://docs.tia.siemens.cloud/r/en-us/v21/using-team-engineering/using-multiuser-engineering/working-offline-with-multiuser-engineering)
- [Siemens V21 — Opening a local session of a server project](https://docs.tia.siemens.cloud/r/en-us/v21/using-team-engineering/using-multiuser-engineering/working-in-the-local-session/creating-and-managing-a-local-session/opening-a-local-session-of-a-server-project)
- [Siemens V21 Openness — Functions support for Multiuser](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/functions-support-for-multiuser)
- [Reviewed external read-only Multiuser implementation](https://github.com/ArtyomInc/tia-portal-mcp/commit/d262a6a311129bf91f2878cf52c96c58b9bc0c9e)

## Current verification boundary

Initial planning history: this document records the approved conversational design, its October 3 lifecycle alignment,
and the user's October 5 decision to put reads/discovery in `bind_project` without a separate tool.
The revision used current repository source, tests, and maintained documentation; it adds no
implementation or fresh runtime acceptance. Original repository and external-source review,
installed-assembly reflection, serial build, and current-main GitHub CI verification were read-only
or offline activities. They do not prove live Multiuser behavior.

No production code, Project Server connection, local session, server project, TIA Portal project,
PLC, or remote resource was changed while preparing this design. No live acceptance is claimed.
PR 1 and PR 2 were merged in the inspected `main`. The revised
[PR 3 binding/discovery implementation plan](../plans/2026-10-05-multiuser-pr3-read-only-inventory.md)
was subsequently accepted and implemented/offline-qualified. Final review and scoped fix re-review
are Approved. The user subsequently authorized live verification and any actions against three
disposable local sessions under `C:\Users\LCZ\Documents\Automation\Sessions`. The
[October 6 installed-tool report](../acceptance/reports/2026-10-06-multiuser-pr3-live-verification.md)
records Task 5 evidence plus a separately authorized disposable standalone `.ap21` phase:
the exact zero-project/standalone prerequisite comparison is complete. The maintainer accepted
all PR 3 live testing as finished and passed on October 6, with unexecuted cases preserved as
non-blocking evidence limitations. Authorized save/close/reopen and one worker-loss injection
were performed; no installed configuration change or remote write was performed. Successor mutation surfaces remain separately
planned; this PR 3 routing decision adds no mutation action to `bind_project`.

The October 7 PR 4 identity revision follows user approval after separate throwaway online-opener
prerequisite probes on disposable fixtures A/B. Those probes observed distinct typed `.amc21`
owner paths and successful exact ALS/directory opening, but no authoritative reverse ALS identity
or owner-specific remote join. The revised plan retains `.als21` as the public opener input and
uses `.amc21` for owner binding with nullable opener provenance. This documentation revision adds
no production code or live action and claims no complete PR 4 acceptance; offline/reconnect,
same-path owner replacement, ambiguity, duplicates and preservation still need their own evidence.
