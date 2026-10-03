# Lifecycle in Read-Write, Mode-Derived Confirmation, and Runtime Project Binding

**Date:** 2026-10-01
**Status:** Implemented; Task13 live-accepted 2026-10-03 on `3bb504b` in full/read-write/read-only,
including the separately recorded human read-only Openness dialog observation. Task14 current
documentation/spec updates are complete. See the
[live report](../acceptance/reports/2026-10-03-lifecycle-tiers-bind-project-live-validation.md) for
bounded runtime, client, artifact/restoration evidence and unqualified scenarios.
**Amends:** [write-safety redesign (2026-09-29)](2026-09-29-write-safety-redesign-design.md)
§4.6 (asking the human), §4.11 (access-mode tiers), and §9 questions 1 and 9. Those sections stay
as the historical record; this document is the current rule where they disagree.
**Release:** rides the unreleased next major version (v4.0.0; the last tag is v3.0.1, and the
redesign tags nothing until its Phase 5).

## 1. Why this document exists

Two problems were reported against `main` at 6b6599b.

**A. Lifecycle tools ask in the wrong mode.** The six lifecycle tools (`open_project`,
`create_project`, `save_project`, `save_project_as`, `archive_project`, `close_project`) are
registered only in `full`. There, with `--confirm-with-user` on (the default), they elicit only
when an `acknowledge`-severity guard fires. The lifecycle guard catalog has exactly one such guard,
`discards_unsaved_changes` (`close_project` without saving a modified project), so open, create,
save, save-as and archive never ask anyone. The maintainer wants the opposite split: the lifecycle
tools available in `read-write`, where every call asks the user, and `full` executing without
asking.

**B. A read-write session cannot bind.** Without `--project`, the host binding stays `unbound`.
Reads attach the worker to whatever single project TIA Portal has open, but a read deliberately
never promotes the host binding. The only production paths to a verified binding are a configured
`--project` promoted by a matching status read, and `open_project`/`create_project`/rebinding
`save_project_as`, which are full-only. Every write and `compile_check` therefore fails with:

> A worker-verified project binding is required before previewing or executing a write. Configure
> --project and verify it, or call open_project explicitly first.

That advice names a tool `read-write` does not register. Several documents (README, AGENTS.md,
ARCHITECTURE, the installation guide, the project operations summary, and redesign §4.11) claim
that `read-write` binds through a read or through the project open in the TIA UI; the code never
did this.

## 2. Decisions (2026-10-01)

1. **Every lifecycle call asks in `read-write`.** Each non-`dryRun` call to any of the six tools
   shows one elicitation prompt before mutating.
2. **`full` never asks.** `acknowledge`-severity guards are satisfied by policy and audited as
   such; `block` guards still stop the call.
3. **`--confirm-with-user` is removed**, together with the `acknowledge` tool parameter and the
   `agent` audit provenance. The access mode is the only switch.
4. **No implicit opens.** No request other than `open_project`/`create_project` opens a project, in
   any mode. This closes a path that would otherwise open projects in `read-write` without a prompt.
5. **A new `bind_project` tool in every mode** adopts a project already open in a running TIA
   Portal. It never opens, creates, closes or saves.
6. **`bind_project` may switch** an existing binding to another already-open project, including one
   in a different TIA Portal process, only with `forceRebind: true`.
7. **Switching re-attaches inside the worker** (a new worker operation), not by restarting the
   worker process.
8. Delivery in two phases (§6), testing and documentation as in §7–§8, release as in the header.

## 3. Access tiers and confirmation

### 3.1 Presets

| Mode | Capability classes | Registered tools | Count |
| --- | --- | --- | --- |
| `read-only` | `Observe`, `TemporaryExport`, `SafetyRead`, `SessionSelection` | four read tools + `bind_project` | 5 |
| `read-write` | + `Compile`, `ProjectMutation`, `ProjectLifecycle` | + `compile_check`, batch write pair, `network_write`, six lifecycle tools | 15 |
| `full` | + `OnlineControl` | same tools as `read-write` | 15 |

`SessionSelection` is a new capability class (§5.1). `ProjectLifecycle` moving into `read-write`
also admits its three internal worker probes (`get_basic_project_status`,
`probe_project_status_for_lifecycle`, `probe_open_project_rebind`) at host and worker dispatch;
they stay unregistered as MCP tools. `OnlineControl` (`start_plc`/`stop_plc`, reachable only
through `apply_write_batch`) stays full-only. `read-write` and `full` therefore advertise identical
tool lists and differ in PLC run/stop and in whether lifecycle calls ask. Lifecycle tool
descriptions say so. `LifecycleWriteDomain.Validate` denies only `read-only`.

The startup default stays `read-write`; the installer default stays `read-only`.

### 3.2 Confirmation policy

The pipeline derives confirmation from the access mode of the binding gate
(`IWriteBindingGate.AccessMode`), not from a startup flag.

| Mode | `dryRun` | Call with no guard | `acknowledge` guard fired | `block` guard fired |
| --- | --- | --- | --- | --- |
| `read-write` | never prompts; returns `phase: preview` | one prompt (if the domain confirms every call) | one prompt per call, carrying the guard messages | `phase: blocked`, `guard_blocked`, no prompt |
| `full` | never prompts | executes | auto-satisfied; listed with `acknowledged: true`; audited `satisfiedBy: policy` | blocked |

Rules:

- **One prompt per call.** The prompt names the operation, the resolved target, the planned effects
  and any fired `acknowledge` guards, and carries a single boolean `confirm`. Only `accept` with
  `confirm: true` proceeds. Decline, cancel, timeout, transport failure and a client without form
  elicitation deny with `access_denied`; nothing is mutated.
- **Blocks first.** `block` guards are evaluated before prompting; the user is never asked about a
  call that cannot run.
- **Re-check after accept.** The existing stale-confirmation step re-plans after acceptance and
  refuses if the target, effects or guards changed. It now covers call-level prompts as well as
  guard prompts.
- **Per-domain scope.** Whether a domain asks on every call or only when an `acknowledge` guard
  fires is a property of the domain. Lifecycle asks on every call. A future in-project write domain
  (network, block, tag) defaults to guard-only prompts in `read-write`, so it does not inherit a
  prompt on every edit by accident.
- **Honesty clause unchanged** (redesign §4.6): the server knows only that the client returned
  `accept`.

### 3.3 Removed surface

- `--confirm-with-user[=bool]`. Passing it fails startup with a migration message: confirmation now
  follows the access mode; use `--access-mode full` to run lifecycle tools without prompts.
- `UserConfirmationOptions` and `UserConfirmationParser`.
- The `acknowledge` parameter on all six lifecycle tools, `WriteCall.Acknowledge`, acknowledge-list
  validation in `GuardDecisions`, and the decision kinds that exist only for it.
- `GuardSatisfactions.Agent`.

### 3.4 No implicit opens

Today the worker's `EnsureRequestedProjectOpen` opens a requested project in `read-write` and
`full` when nothing is open (`ProjectOpenDecision.OpenRequested`). After this change every mode
takes the path `read-only` takes now: a request never opens a project. A request whose `projectPath`
is not the open project is denied (`access_denied` when nothing is open, `binding_conflict` when a
different project is open), and the guidance names `bind_project` for an already-open project and
`open_project` where the mode registers it. Redesign §4.11's "through a `projectPath` on a read
when nothing is attached" route is withdrawn.

### 3.5 Audit

`WriteAuditRecord` moves to `recordVersion: 2` with a call-level member:

```text
confirmation: { by: "user" | "policy" | "none", outcome: "confirmed" | "declined" | "cancelled" | "unsupported" | "timed_out" | "failed" | "not_requested" }
```

Guard entries keep `satisfiedBy`, now `"user"`, `"policy"`, or null. Dry-run and blocked calls
record `by: "none", outcome: "not_requested"`. The stream (`writes-yyyy-MM-dd.jsonl`) is unchanged.

## 4. `bind_project`

### 4.1 Tool contract

```text
bind_project(projectPath?: string, forceRebind?: boolean = false)
annotations: readOnly=false, destructive=false, idempotent=true, openWorld=false
additionalProperties: false
```

- `projectPath`: absolute path to a `.ap21` file that a running TIA Portal process reports as its
  open project. Anything else is `validation_error`.
- `forceRebind`: required whenever the call would bind a different project than the one the session
  is bound to, configured with, or last bound to.
- The description states: it never opens, creates, closes or saves a project; without a path it
  binds the only open project or lists the candidates; a re-attach to another TIA Portal can show
  TIA's Openness access dialog, which a human must answer.
- `readOnly=false` because the call changes server session state and may re-attach; `destructive`
  is false because no project is changed.

### 4.2 Decision table

The host evaluates this table against the binding snapshot taken after acquiring the serialized
binding gate.

| Session state | Request | Behaviour | `transition` |
| --- | --- | --- | --- |
| unbound | no path | list Portals; exactly one with an open project → select and adopt it; none → failed `target_not_found`; several → failed `target_ambiguous` | `bound` / `none` |
| unbound | path P | select P and adopt it | `bound` |
| configured_unverified or invalidated (path C) | no path, or C | select C and adopt it | `bound` |
| configured_unverified or invalidated (path C) | P ≠ C | rejected `binding_conflict` unless `forceRebind`; then select P and adopt it | `bound` |
| verified A | no path, or A | re-verify through the existing status read; a mismatch invalidates as today | `unchanged` |
| verified A | B ≠ A | rejected `binding_conflict`, nothing sent, unless `forceRebind`; then select B and adopt it | `switched` |

"Select" is the worker operation `select_portal_project` (§5.2); "adopt" is
`ProjectSessionBinding.TryAdoptVerified` (§5.1). Host validation or unforced foreign-path rejection
sends nothing to the worker. **Errata E1/E2:** the headless refusal instead follows one selection
round trip: wire `guard_blocked` maps to rejected `binding_conflict` at the host. Pre-detach
`target_not_found`, `target_ambiguous`, and `guard_blocked` occur before any handle/path/generation
change and preserve the previous verified binding. Every other switch failure invalidates it,
including worker `binding_conflict`, timeout, crash, `protocol_error`, or postcondition failure.
**E4:** state names are the `ProjectBindingSnapshot` constants, including `configured_unverified`.

### 4.3 Response document

The standalone envelope (`tool`, `contractVersion: "1.0"`, `success`, `error`, `warnings`,
`result`), with `result` a `StandaloneToolOutcome<ProjectBindingResult>`:

```text
ProjectBindingResult {
  transition: "bound" | "unchanged" | "switched" | "none",
  binding: ProjectBindingInfo,                 // after the call; never null
  previousBinding: ProjectBindingInfo,         // before the call; never null
  project: ProjectStatusInfo | null,           // verification status; Metadata = null
  portals: PortalProcessInfo[]                 // always present, possibly empty
}
ProjectBindingInfo { state, projectPath, portalProcessId }
PortalProcessInfo { processId, projectPath | null, hasUserInterface, isBound }
```

Every member is always written; there are no conditional members. **E3:** an unbound session
projects as state `unbound`, not null. Failed values report binding state after the call, which can
be `invalidated`, with transition `none` on every non-success. **E5:** `portals` comes only from a
listing sent in this call. A failed path selection with `target_not_found` or `target_ambiguous`
sends one subsequent listing; same-path verified rechecks do not invent candidate inventory.

Outcome mapping, consistent with the JSON contract roadmap:

- **Rejected** (`isError: true`, `error` set, `result: null`): `validation_error` (bad arguments),
  `binding_conflict` (a different project without `forceRebind`, or the headless guard of §4.4).
- **Ran but did not bind** (`isError: false`, `success: false`, `result.status: "failed"` with
  `failure` set): `target_not_found`, `target_ambiguous`, worker or postcondition failures. `value`
  is still present with `transition: "none"`, the after-call `binding`, and any in-call `portals`, so the
  agent can retry with a path.
- **Succeeded:** `result.status: "succeeded"` with `value` filled.

### 4.4 Switch safety

- **Nothing is closed.** Detaching leaves the previous project open in its TIA Portal window,
  unsaved changes included. If the previous project was opened by the worker and is modified, the
  result warns that it remains open with unsaved changes and must be saved or discarded in TIA.
- **Headless guard.** If the previous Portal has no user interface, the worker is its only Openness
  client, and its project is modified, the switch is refused before anything detaches, because
  disconnecting could close that instance and lose changes. **E1:** the worker emits `guard_blocked`
  on the wire; the host maps it to rejected `binding_conflict` after that selection round trip.
- **Openness dialog.** When a re-attach happened the result warns that TIA may have asked to allow
  access, and that "Yes to all" stops the prompt for this worker binary.
- **Ownership is not carried across a detach.** A project the worker opened becomes, after a
  re-attach, indistinguishable from a UI-opened one; the worker will not close it on a later
  rebind. This is accepted and documented.

### 4.5 Effects on outstanding state

Every binding change bumps the revision. Safety tokens and hardware-page cursors already reject a
different binding. Project-tree cursors are not tied to the binding today and would replay the old
project's snapshot after a switch; they gain the binding ID and revision and reject a mismatch with
`cursor_binding_mismatch`.

### 4.6 Guidance and doctor

- The "binding required" message and every guidance string that names only `open_project` or
  `--project` name `bind_project` first: the write-binding message, `RebindInstruction`, the
  worker's session guidance, `ProjectOpenPolicy.RefusalMessage`, the ambiguous-process message, and
  the worker's read-only messages.
- Doctor: `ProjectBindingCheck` and `TiaPortalProcessCheck` report **Warning** instead of
  **Failed** for a writable mode without `--project`, with remediation naming `bind_project`.
- Ordinary reads still never bind or switch. Only `bind_project` and the lifecycle tools change the
  binding.

## 5. Components

### 5.1 Contracts (`TiaMcpServer.Contracts`, netstandard2.0)

- `OperationCapability.SessionSelection`, allowed by all three presets. Operations of this class do
  not require an expected session identity (`RequiresExpectedSessionIdentity` returns false).
- `OperationPolicyCatalog` entries: `list_tia_portal_processes` → `Observe`;
  `select_portal_project` → `SessionSelection`.
- Payload types, both full-null contracts decoded through the worker-payload reader (no
  `[LegacyNullOmission]`, no conditional members):
  - `TiaPortalProcessListInfo { attachedProcessId, processes[] }` with
    `TiaPortalProcessInfo { processId, projectPath, hasUserInterface, attachedByThisWorker }`;
  - `PortalProjectSelectionInfo { previousProcessId, previousProjectPath,
    previousProjectWasWorkerOpened, previousProjectIsModified, reattached }`, returned with the
    usual stamped session identity.
- `ProjectSessionBinding.TryAdoptVerified(ProjectBindingSnapshot expected, WorkerSessionIdentity
  identity, out string? error)`: compare-and-set on the snapshot taken under the gate; transitions
  to `verified` for the identity's project, with no transition when the identity equals the
  current verified one. `BindToResolvedSessionIdentity` is not reused for a switch because its
  continuity check rejects a new Portal process by design.
- The worker protocol's required capabilities gain the two new operations, so an older worker
  binary is refused at handshake.
- **E5:** post-adopt verification and Reverify use `get_project_status` (Observe), so they work in
  read-only. `get_basic_project_status` belongs to ProjectLifecycle. Binding's `project` retains
  status with `Metadata = null`; candidate inventory is only from an in-call listing (§4.3).

### 5.2 Worker (`TiaMcpServer.OpennessWorker`, net48)

- `TiaPortalProcessInventory` enumerates `TiaPortal.GetProcesses()` without attaching, reading each
  process's ID, advertised project path and mode defensively. `TiaPortalSession.Connect` and the
  listing share it, so selection and listing see the same candidates. The listing handler bypasses
  `WithSession`, which would attach.
- `TiaPortalSession.Disconnect()`, extracted from `Dispose(bool)` without marking the session
  disposed: unsubscribe the three event handlers first, then clear the project handle, ownership
  flag and selected path, then release the Portal handle. The generation keeps increasing.
- `TiaPortalSession.SelectPortalProject(string projectPath)`: requires an exact advertised match
  (a new exact-only selector; no fallback to the sole Portal). Same process: re-select the project
  in the attached Portal (adopt only). Different process: headless guard (§4.4), `Disconnect`,
  attach to the target, verify the attached process ID, select the project. Never opens, closes or
  saves.
- `EnsureRequestedProjectOpen` loses its opening branch (§3.4).
- On stdin end-of-file, `Main` disposes the shared session so the detach is real rather than a
  process kill.

### 5.3 Host (`TiaMcpServer`, net10.0)

- `OpennessWorkerClient.BindOpenProjectAsync(string? projectPath, bool forceRebind,
  CancellationToken)` runs entirely inside one serialized binding section: snapshot, decision table
  (§4.2), listing when no path is given, `select_portal_project`, strict payload decode,
  `TryAdoptVerified`.
- `ExecuteSerializedBindingOperationAsync` gains a cancellable overload; a pending lifecycle prompt
  holds the gate, and a bind call must not wait on it uncancellably.
- `ProjectBindingTools` (new `[McpServerToolType]`), registered unconditionally next to the read
  tools, with an argument-validating wrapper that returns a canonical `validation_error` document,
  and a `bind_project` case in `StructuredStandaloneResult`.
- Write pipeline: a confirmation policy resolved from the gate's access mode; two domain members, a
  flag for "confirm every call" and a short effects summary for the prompt text; acknowledge
  plumbing removed; audit record v2.
- `AccessModeParser`/startup: `--confirm-with-user` rejected with the migration message.
- Project-tree cursor codec carries the binding ID and revision.
- Doctor checks as in §4.6.

### 5.4 Concurrency and timeouts

All worker sends already run under `_bindingOperationGate`, so a bind or switch has exclusive use of
the transport and the binding. `Attach` blocks while TIA shows its Openness dialog; the existing
5-minute request timeout covers a human answer. On timeout the worker is killed and the binding
invalidated, and the error tells the agent that TIA's Openness dialog needs an answer.

## 6. Delivery

One branch, `feat/lifecycle-tiers-bind-project`, two phases, each green on its own:

Phase A shipped separately at `85aec97` with offline qualification, before Phase B on
`feat/bind-project`. The final source `3bb504b` passed 4,946 offline tests with 93.24% linked
host/contracts line coverage and Task13 live acceptance on 2026-10-03, including the human
read-only Openness dialog observation. Task14 current documentation/spec updates are complete;
the live report bounds the worker-lifetime conclusion, and no release/tag is implied.

- **Phase A, tiers and confirmation:** presets, lifecycle domain validation, confirmation policy,
  removal of the flag and `acknowledge`, audit v2, no implicit opens. After Phase A the existing
  "call open_project" guidance is accurate in `read-write`; doctor is unchanged.
- **Phase B, `bind_project`:** contracts, worker listing and selection, `TryAdoptVerified`, host
  binding method, the tool, project-tree cursor binding, guidance strings naming `bind_project`,
  and the doctor Warning downgrade (§4.6).

## 7. Testing

Test-first per task.

- **Unit:** preset matrix (lifecycle in `read-write`, `OnlineControl` full-only, `SessionSelection`
  in all modes); confirmation policy over mode × `dryRun` × guard severity; `TryAdoptVerified`
  compare-and-set; every row of §4.2; exact-only process selector; headless guard; no implicit
  open in every mode; project-tree cursor binding mismatch.
- **Protocol (FakeWorker):** `bind_project` bound, unchanged, switched, ambiguous, not found,
  `forceRebind` required; each lifecycle tool in `read-write` with accept, decline, cancel and no
  elicitation capability; `full` with no prompt and `policy` provenance; lifecycle hidden in
  `read-only`; startup failure on the removed flag.
- **Contract:** conformance success and rejection probes for `bind_project`; tool counts 5/15/15;
  `acknowledge` absent from the six schemas; audit v2 shape.
- **Deliberate rewrites:** tests that pin the old surface: the 4/8/14 counts, `read-write` ceiling
  suites that deny lifecycle, `UserConfirmationParser` and its call sites, elicitation tests that
  run in `full`, acknowledge and `agent`-provenance assertions, and the `FakeWriteBindingGate`
  default mode (pipeline tests must state their mode, or every one becomes `access_denied`).
- **Live acceptance (maintainer, TIA Portal V21, two Portal processes):** bind in each mode; switch
  across Portals including the Openness dialog; `read-write` lifecycle prompts in Claude Code;
  no prompts in `full`; a read with an unopened `projectPath` refused. The change invalidates the
  frozen 2026-10-01 lifecycle live evidence, so that run is repeated.

## 8. Documentation

- This spec, listed in `docs/README.md`; supersession notes in the redesign spec at §4.6, §4.11
  and §9 questions 1 and 9.
- Current documents: README (counts, tool list, write-safety model; absolute links), AGENTS.md
  (project overview counts, write-safety model), ARCHITECTURE (tiers, binding, §7a),
  `docs/guides/installation.md` (flags, modes, binding), `docs/SupportedOperations/` project
  operations summary, and an IMPROVEMENT_LOG entry. The false "read-write binds on its own"
  statements are corrected.
- Stale lines fixed in the same pass: `SECURITY.md` (every write uses tokens), README's batch write
  list, and the IMPROVEMENT_LOG/roadmap entries that still call lifecycle live acceptance pending.
- Follow-up outside this repository: the `totally-integrated-claude` plugin's `tia-portal-mcp`
  skill describes the old modes and flag.

## 9. Risks

- Clients without form elicitation (Codex in unrestricted mode, headless runs) cannot run lifecycle
  tools in `read-write`; they need `full`, which never asks.
- Existing `full` users lose the one prompt they had (close without save on a modified project).
- An agent that relied on a read with `projectPath` to open a project now gets a refusal and must
  call `open_project` (prompted in `read-write`).
- `TiaPortalProcess.ProjectPath` is assumed to report the primary project; secondary projects are
  not bindable. Verified only in the live run.
- `read-only` can now change which already-open project it reads, but only through `bind_project`.
  The read-only promise becomes "never opens, creates, saves or closes".
- A bind waits while a lifecycle prompt holds the gate; the cancellable overload bounds it.

## 10. Non-goals

- Binding multiuser local sessions (`.als21`) or secondary projects.
- Selecting a Portal by process ID as input; IDs are reported, the path identifies the target.
- Changing the `OnlineControl` tier, the network token flow, or the generic batch token flow.
- `bind_project` opening a project.
- Per-client elicitation behaviour matrices.
- Any claim that a human saw a prompt.
