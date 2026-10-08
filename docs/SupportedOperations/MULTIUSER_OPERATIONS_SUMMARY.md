# Multiuser local sessions, inventory and acceptance boundary

`bind_project` exposes explicit Portal discovery and five Project Server inventory actions in
all modes. Omitted/null `action` or `action:"bind"` selects already-open standalone `.ap21`
or typed local-session `.amc21` owners. Discovery remains **7/16/16** tools in
read-only/read-write/full. PR4 adds explicit existing `.als21` opening in writable modes and
basic local status. PR1/PR2 ownership contracts remain the foundation; Issue #65 is incomplete.
PR 3 has [installed-tool live acceptance](../superpowers/acceptance/reports/2026-10-06-multiuser-pr3-live-verification.md)
for all six inspection actions against authorized session copies and a standalone `.ap21`,
including remote reads with zero open projects. The maintainer accepted live testing as finished
and passed on October 6; the report retains non-blocking unexecuted cases as evidence limitations.

## Inspection actions and selectors

One call performs one action. Action names and server alias, group name and project name match
ordinal-exactly, without trimming or case folding. Zero matches returns `target_not_found`;
multiple matches returns `target_ambiguous`, without first-target fallback.

| `action` | Allowed selectors | Result |
| --- | --- | --- |
| `list_portals` | None | Fresh candidates in `result.value.portals`; never adopts the sole open project. |
| `list_server_connections` | Optional `portalProcessId` | Configured alias, host and port; configuration does not prove connectivity/authentication. |
| `list_server_groups` | Optional PID; required `serverAlias` | Exact endpoint's groups and connection observation. |
| `list_server_projects` | Optional PID; required alias and `group` | Projects within the exact root or named group. |
| `list_local_sessions` | Optional PID; required alias, group and `serverProjectName` | Integer `sessionId` (no positivity restriction) and canonical paths on this machine for the current user. |
| `get_lock_state` | Optional PID; required alias, group and project | Observed flag, nullable owner and timestamp. |

An explicitly supplied PID must be a positive integer, not null, text or a fraction. Root group
is `{"isRoot":true,"name":null}`; named group is `{"isRoot":false,"name":"Exact name"}`.
Both group members are required, including explicit root `name:null`. Unknown members, blank
names, missing selectors and cross-action keys reject before worker activity. Inspections reject
supplied `projectPath`/`forceRebind` keys even when null/false. `list_portals` accepts only `action`;
`bind` rejects server inventory selectors; an optional exact Portal PID is allowed for binding.

```json
{ "action": "list_portals" }
```

```json
{ "action": "list_server_connections", "portalProcessId": 1234 }
```

```json
{ "action": "list_server_groups", "portalProcessId": 1234, "serverAlias": "Fixture" }
```

```json
{
  "action": "list_server_projects",
  "portalProcessId": 1234,
  "serverAlias": "Fixture",
  "group": { "isRoot": true, "name": null }
}
```

```json
{
  "action": "list_local_sessions",
  "portalProcessId": 1234,
  "serverAlias": "Fixture",
  "group": { "isRoot": false, "name": "Exact group" },
  "serverProjectName": "Exact project"
}
```

```json
{
  "action": "get_lock_state",
  "portalProcessId": 1234,
  "serverAlias": "Fixture",
  "group": { "isRoot": false, "name": "Exact group" },
  "serverProjectName": "Exact project"
}
```

## Attachment and binding continuity

Discovery does not attach. Inventory reuses a healthy persistent attachment. Without attachment,
it selects the exact requested PID or requires exactly one fresh candidate, then attaches without
adopting a project. Inspection cannot switch an existing attachment. A verified binding defaults
the PID assertion to its verified PID; explicit foreign PID returns local `binding_conflict`
before dispatch, preserving the binding revision and cursors. An unbound/configured-unverified
session remains so after healthy inspection; configured or last-bound paths/PIDs are not promoted.

Inspection never opens, adopts, switches, saves, closes, discards or commits a project/session.
There is no temporary detach, second worker, lifecycle elicitation or write audit. Openness
attachment may still require TIA's human-answerable access dialog. Ordinary server/selector failure
preserves healthy context and ownership. Genuine Portal/project identity loss, worker crash or
timeout invalidates binding evidence and cursors. This also applies to identity loss observed by
`list_portals` or a supplemental candidate listing; `isBound` reflects the resulting snapshot.
Failed inventory is never automatically replayed.
Use explicit `bind` with its existing guards to select another instance.

## Typed results and observation limits

The [binding envelope](PROJECT_OPERATIONS_SUMMARY.md#explicit-binding-bind_project) remains
contract version `1.0`, with one canonical text/structured document. Inspection returns
`transition:"none"`, `project:null`, before/after binding snapshots and conditional
`result.value.inspection`. Default binding omits `inspection`. Required inspection members are
`action`, nullable observed `portalProcessId`, and five explicitly nullable slots:
`serverConnections`, `serverGroups`, `serverProjects`, `localSessions`, `lockState`. Successful
inventory fills exactly its action's slot and reports actual attached PID. Discovery uses
`portals` and fills no slot; failed inspection fills no slot. Invalid input/local refusal has
top-level error and MCP `isError:true`; completed failure retains a typed failed outcome with
`error:null`, `isError:false`. Malformed/incoherent worker payload becomes sanitized `protocol_error`
without payload echo.

Sessions declare `scope:"currentMachineCurrentUser"`. An empty successful list does not establish
absence for other users/machines; failed/incomplete enumeration is never empty success. Lock
reading is non-atomic: unlocked observations have `owner:null`; locked reads fetch owner and
recheck the flag. A changed flag fails `state_changed`; success is not lock acquisition or a
guarantee of continued state. Connection observations are descriptive, not authorization or proof
of future connectivity. Generic Multiuser failures do not establish authentication failure/server
unavailability. History compares only the same actual attachment and endpoint, and resets after
attachment/configuration changes.

Each session's `projectPath` is the normalized `LocalSessionInfo.ProjectFileInfo.FullName`
returned by Siemens. The authorized live fixture returned session directories, rather than
`.als21` file paths. Treat this field as inventory evidence; it is not a guaranteed opening or
binding selector, and PR 3 does not enable `.als21` opening or adoption.
PR4 accepts an independently known exact existing `.als21` file for `open_project`; it never
derives that filename from this inventory field.

Standalone budgets are 60,000 characters per whole value and 180,000 per complete document.
Oversized inventory is omitted whole, including `inspection`, with omission metadata and guidance
to inspect a narrower applicable exact group/project or use TIA Portal. Arrays are never silently
truncated; repeating the same oversized request may still exceed the limit.

## Internal context

`TiaPortalSession` stores an `ActiveProjectContext` whose `ProjectBase` root comes from its
typed owner. `StandaloneProjectOwner` retains a `Project`; `LocalSessionOwner` retains a
`LocalSession` and its `MultiuserProject`. Production selection resolves the exact typed owner
and its absolute `.amc21` engineering path. Local basic status and explicit `.als21` opening
use that owner; content services retain the standalone compatibility projection. Local content,
compile, save and standalone-only lifecycle calls reject with `unsupported_capability` before
mutation, including previews. The local owner exposes no save, close, discard or commit method.

Identity remains a projection of the authoritative session. Rewrapping the same root does
not advance generation; a replacement root or accepted path change does. Detach releases
ownership, and adoption never restores it. Shutdown releases Portal resources without
claiming project save, session discard, or revision commit. See
[architecture](../ARCHITECTURE.md#internal-active-project-context).

Local status adds conditional `context`; standalone/closed status omits it. Its basic status
has `metadata:null`. Context includes descriptive capabilities, exact `engineeringProjectPath`,
nullable `sessionContainerPath` and `openedByWorker`. Cold adoption records no ALS provenance
and no worker ownership. A successful exact ALS opener records provenance only after typed
owner/root verification; known same-ALS reuse needs continuous owner identity. Same-owner
observation refresh preserves generation/binding revision; owner replacement at the same path
invalidates identity. Detach relinquishes ownership and known opener provenance.

The product reports `sessionMode:"unknown"`, `remoteIdentity:null`, and an active connection
observation of `unknown`. Fixture creation modes and endpoint mapping are independent operator
evidence; filenames, directories and inventory IDs cannot populate an unsupported reverse join.
Explicit endpoint inventory observations have their own attachment/endpoint history and do not
prove active-session connectivity, freshness, markings or remote session identity. Capabilities
describe applicability; access modes and guarded execution still decide authorization.

Worker-owned local sources block a different open with
`local_session_requires_terminal_operation` even when clean. Borrowed sources require proved
coexistence/duplicate preservation; uncertainty blocks with
`local_session_source_preservation_unproved`. Force cannot override either guard. Generic
`close_project` is not a local-session terminal operation; shutdown releases Portal resources
without session save, discard or commit. See the [project reference](PROJECT_OPERATIONS_SUMMARY.md#local-session-identity-and-basic-status).

## Public Multiuser work still pending

Local-session content compatibility, compilation, `get_session_state`, `get_markings`, local
save, discard, commit and server mutation remain undelivered. `.als21` remains an opening input,
never a bind/status/startup assertion; those use observed `.amc21` identity. Inventory
does not open/bind implicitly to satisfy an open-project prerequisite; current-PR live evidence
must establish the prerequisite and scope. Generic standalone lifecycle cannot replace a synthetic
local/server source. Later PRs must individually establish capability, typed contracts,
guarded execution, and operation-specific live evidence before enabling their public paths.

## Acceptance boundary

PR4 implementation candidate `3772edad0ce9e90390bc36deb71dbd8140511424` was offline qualified
and accepted by the maintainer on October 8 **within the documented scope**. The
[definition](../superpowers/acceptance/2026-10-07-multiuser-pr4-live-definition.md) retains the
original matrix; the [live report](../superpowers/acceptance/reports/2026-10-08-multiuser-pr4-live-verification.md)
records 73 installed public calls with canonical equality and 21 exact lifecycle audit matches.
Online recovery/opening passed in two retained UI Portals in full/read-write. Offline read-write
previews passed; both actual opens completed only after operator-dismissed Siemens dialogs, so
the original noninteractive offline expectation failed. Full-mode offline, headless/race and
other matrix gaps remain unexecuted. This is scoped maintainer acceptance, not all L01–L19 PASS.
Read-only and general standalone lifecycle retesting were expressly excluded by the maintainer.
No save, commit, generic local close, automatic cleanup or replay formed part of this run.

Source-linked in-memory Siemens doubles exercise the actual context/session source offline;
FakeWorker tests establish host protocol, binding, confirmation, and audit behavior. These
checks cannot prove installed Openness runtime behavior. Generated references are compile-only;
installed-reference compilation and package exclusion are separate qualification checks.

The [PR 3 offline report](../superpowers/acceptance/reports/2026-10-06-multiuser-pr3-offline-validation.md)
records the combined candidate and qualification. The October 6
[installed-tool live report](../superpowers/acceptance/reports/2026-10-06-multiuser-pr3-live-verification.md)
records the authorized three-session fixture: all six inspection actions succeeded while the host
remained unbound, including current-user session directories and locked/unlocked observations.
Inventory was exercised with opened `.als21` sessions. The separate zero-project/standalone `.ap21`
phase demonstrated all five inventories without opened `.als21`, both with a standalone project
open and with zero open projects. Healthy verified binding/cursors, worker-loss invalidation and
explicit recovery were observed. Actual named/duplicate group/project cases, empty session lists,
read-only/read-write live hosts, guarded switching to a second standalone target and other report
limits remain untested. On October 6 the maintainer accepted PR 3 live testing as finished and
passed, closing its acceptance gate with those non-blocking evidence limitations. This decision
does not claim the unexecuted cases ran or enable successor operations. Historical standalone
acceptance is not inventory acceptance.

Frozen PR 2 candidate `a47bf0dbc3efba4ff654464b6393d01163dc7900` passed bounded standalone
live selection/switching and lifecycle acceptance, including headless last-client and adverse-state
cases: 131 captured tool calls, 64 lifecycle calls, matching canonical documents/audits, and
restored fixtures. The [acceptance report](../superpowers/acceptance/reports/2026-10-05-multiuser-pr2-active-project-context-live.md)
records exact evidence, failures and limits: project-comment dirtying, independent Openness
external changes, and scripted elicitation. These results qualify the internal migration's
standalone behavior; they do not qualify public Multiuser operations. The
[implementation plan](../superpowers/plans/2026-10-05-multiuser-pr2-active-project-context.md) defines that boundary.
