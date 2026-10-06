# Multiuser inventory and acceptance boundary

`bind_project` exposes explicit Portal discovery and five Project Server inventory actions in
all modes. Omitted/null `action` or `action:"bind"` retains standalone `.ap21` binding behavior
and its response shape. Discovery remains **6/16/16** tools in read-only/read-write/full.
PR 1 contracts and PR 2 passive context remain the foundation; Issue #65 is incomplete.
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
`bind` rejects inspection selectors.

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

Standalone budgets are 60,000 characters per whole value and 180,000 per complete document.
Oversized inventory is omitted whole, including `inspection`, with omission metadata and guidance
to inspect a narrower applicable exact group/project or use TIA Portal. Arrays are never silently
truncated; repeating the same oversized request may still exceed the limit.

## Internal context

`TiaPortalSession` stores an `ActiveProjectContext` whose `ProjectBase` root comes from its
typed owner. `StandaloneProjectOwner` retains a `Project`; `LocalSessionOwner` passively
retains a `LocalSession` and its `MultiuserProject`. A local/server owner exposes no save,
close, discard, or commit abstraction, and no production caller activates one in this PR.
Current content services use a standalone `Project` compatibility projection. Status,
lifecycle, and worker dispatch resolve the standalone owner explicitly; local/server owners
are refused with `target_kind_unsupported` at these boundaries.

Identity remains a projection of the authoritative session. Rewrapping the same root does
not advance generation; a replacement root or accepted path change does. Detach releases
ownership, and adoption never restores it. Shutdown releases Portal resources without
claiming project save, session discard, or revision commit. See
[architecture](../ARCHITECTURE.md#internal-active-project-context).

Capabilities are internal, read-only, and empty; remote identity and connection observation
are null. They are not an advertised compatibility matrix and grant no authorization.

## Public Multiuser work still pending

`.als21` bind/open or startup selection, local-session content compatibility, `get_session_state`,
`get_markings`, local save, discard, commit and server mutation remain undelivered. Inventory
does not open/bind implicitly to satisfy an open-project prerequisite; current-PR live evidence
must establish the prerequisite and scope. Generic standalone lifecycle cannot replace a synthetic
local/server source. Later PRs must individually establish capability, typed contracts,
guarded execution, and operation-specific live evidence before enabling their public paths.

## Acceptance boundary

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
