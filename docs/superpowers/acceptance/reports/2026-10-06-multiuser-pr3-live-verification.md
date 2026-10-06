# Multiuser PR 3 installed-tool live verification

**Status:** Partial Task 5 live verification completed on the installed reviewed candidate.
All six inspection actions succeeded; the five remote inventories also succeeded with a standalone
`.ap21` open and with zero open projects. Healthy binding/cursor continuity and genuine worker-loss
invalidation were observed. Remaining matrix cases below prevent a full acceptance or merge-ready claim.

## Authorized scope and frozen runtime

On October 6 the user authorized installed-tool live verification and any actions against three
disposable local-session copies under `C:\Users\LCZ\Documents\Automation\Sessions`. Two were
already opened and the third had been created by the user. No session/project/lock mutation,
fixture setup/cleanup, installed-tool/configuration change or remote publication was needed or
performed by the agent in this initial phase. Only root conducted the live calls, serially.

The user subsequently changed the fixture and authorized all actions against the sole opened,
fully disposable `.ap21` project. That later phase is recorded separately below.
It intentionally saved/closed/reopened that project and stopped one identity-verified
worker to test recovery. Initial session-state preservation observations apply only to the initial phase.

| Provenance | Observed value |
| --- | --- |
| Branch and reviewed docs head | `feature/multiuser-pr3`; `afb27414a48229e196502076bbaad402d6763422` |
| Qualified production/test source head | `3f45b940ebc66752f14590b900218fcca336d665`; subsequent reviewed changes were docs only |
| Installed tool version | `3.0.1-local.320.gafb2741` |
| Installed root | `C:\Users\LCZ\.dotnet\tools\.store\tiamcpserver\3.0.1-local.320.gafb2741\tiamcpserver\3.0.1-local.320.gafb2741\tools\net10.0\any` |
| Machine/current user | `S1614W` / `LCZ` |
| TIA Portal file version | `2100.0.121.1` |
| Standalone-phase installed mode | `full`; host PID `32012`, initial worker PID `38848` |
| Project Server service | `prjsrv`, observed `Running` |
| Project Server executable file versions | `202.0.0.365`; product versions `202.0.0.365+7e3ccedd28` |

The separate Project Server provenance capture names both
`C:\Program Files\Siemens\Automation\Project Server\Bin\Siemens.Automation.Portal.Server.exe`
and the same directory's `Siemens.Automation.Portal.Project.Server.exe`. These are observed
service/file versions; no product marketing version is inferred from TIA Portal's version.

| Artifact | SHA-256 |
| --- | --- |
| Installed host | `DCC191601A57537B1949AE281CB1366B86287611DA50A4070366BEFAE9D0F637` |
| Installed Contracts DLL | `751092CE3F879671A2DAC7C1D2956EBEDBBD7E8AD0470F03C0144CFF5AB93529` |
| Installed worker | `A9DF8743FA7AE75C7F10470C9A470C6B68DC5E31F50C2F1055480C8088874030` |
| Initial tool capture | `2ACBD7B0F055A4661A4953D1B9E4E4DF0D454CCE303FB5345BB7D6425F956524` |
| Standalone tool capture | `022CD01FFF87A6FDDD63E726BBF2651877FBE9C7532CAC86D26EF487C306EB47` |
| Project Server provenance capture | `957053E5C51954455773A44FB5E74066934188D3A2830DBEF47C5DD278FAB29F` |
| Session-file metadata capture | `9802CB0EF870BDD6DBE35E7084A779264B8B538229A6DF7F2207B125BF2FF0D2` |
| Lifecycle audit correspondence capture | `8410AD15AC26C750BEAA5BEB85D95B2D41F61418C89F4473A8662932972A0263` |
| `Siemens.Automation.Portal.Server.exe` | `268242CEE99CECFFD7B8A336F9AC2ECC80882AC2374EB8583E8CAF35AF836CBA` |
| `Siemens.Automation.Portal.Project.Server.exe` | `DFEFB604E4B878292212D714E5B25AFFABA3C131D6B42B6A257B686E4015B830` |

Retained local evidence is under `TestResults/pr3-live-installed-20261006/`:
`tool-capture.json` contains 29 initial calls, and `ap21-tool-capture.json` contains 44 standalone
calls, with arguments, complete responses and derived checks. Companion files are
`project-server-provenance.json`, `session-file-metadata.json` and `lifecycle-audit-correspondence.json`.
All **73/73** decoded text documents equal `structuredContent`. Maximum response text was
**2,456 characters**; no budget-stress/omission claim is made. The
[offline report](2026-10-06-multiuser-pr3-offline-validation.md) retains the separate build,
5,556-test, coverage, reference and package evidence; those suites were not rerun for these docs.

## Initial phase: opened local sessions

Fresh Portal discovery returned PID **25092** at
`C:\Users\LCZ\Documents\Automation\Sessions\SimpleProject_ES1\SV\SimpleProject.amc21`
and PID **27540** at
`C:\Users\LCZ\Documents\Automation\Sessions\SimpleProject_LS_1\LS\SimpleProject.amc21`.
Both were unbound. Omitting PID before attachment returned `target_ambiguous`; an explicit
PID 27540 subsequently established persistent Portal-only attachment without project adoption.

| Action | Initial installed-tool result |
| --- | --- |
| `list_portals` | Both fresh candidates listed; initial/final phase inventories had the same two PIDs and project paths, with `isBound:false`. |
| `list_server_connections` | `Local Project Server`, host `net.tcp://localhost/`, port `9337`; `MultiuserTests`, host `https://s1614w/`, port `8735`. These are configuration rows, not connectivity proof. |
| `list_server_groups` | `MultiuserTests` succeeded with `groups:[]`. The configured `Local Project Server` endpoint returned sanitized `worker_operation_failed`, with no fabricated inventory. |
| `list_server_projects` | Exact `MultiuserTests` root group (`isRoot:true`, `name:null`) listed `SimpleProject_copy` and `SimpleProject`. |
| `list_local_sessions` | `SimpleProject` returned IDs 1 and 2; `SimpleProject_copy` returned ID 1. All three paths were under the authorized root; scope was `currentMachineCurrentUser`. |
| `get_lock_state` | `SimpleProject` was locked with owner evidence naming `LCZ`, `S1614W` and `SimpleProject_ES1`; `SimpleProject_copy` was unlocked with `owner:null`. |

The session rows were:

| Exact server project | Session ID | Returned `projectPath` |
| --- | --- | --- |
| `SimpleProject` | 1 | `C:\Users\LCZ\Documents\Automation\Sessions\SimpleProject_LS_1` |
| `SimpleProject` | 2 | `C:\Users\LCZ\Documents\Automation\Sessions\SimpleProject_ES1` |
| `SimpleProject_copy` | 1 | `C:\Users\LCZ\Documents\Automation\Sessions\SimpleProject_copy_LS_1` |

These are normalized `LocalSessionInfo.ProjectFileInfo.FullName` values returned by Siemens.
Here they are directories, not `.als21` opening/binding selectors. Session IDs are scoped to their
exact server project: ID 1 occurs in two different projects. No claim is made about duplicate IDs
within one session list, zero/negative IDs, or all users/machines.

The retained `.als21` file metadata independently agrees with those three directory/ID mappings:
`WorkspaceAddress.id` was respectively 1, 2 and 1, `projectName` matched each exact project,
and `serverUri` was `musrv:MultiuserTests`. The ES1 entry had `isExclusive:true`; the two LS entries
had `isExclusive:false`. These attributes were observed before remote inventory and saved in
after-run metadata. File hashes in that capture are after-run snapshots only, not before/after
filesystem preservation evidence.

The locked project's owner text was returned verbatim:

```text
User "LCZ" on device "S1614W" has locked the server project for the local session "C:\Users\LCZ\Documents\Automation\Sessions\SimpleProject_ES1".
```

These two separate project observations demonstrate locked and unlocked results, not a lock
transition, lock acquisition/release or an atomic/race-free state. Successful explicit reads of
the same `MultiuserTests` endpoint first reported `state:"connected"`, then
`previousState:"connected"` with `transition:false`. Later successful groups/session reads
recovered after the other configured endpoint's failure. That failure does not establish a
specific connectivity/authentication cause, nor was genuine disconnect/reconnect injected.

## Initial refusals and state observations

| Input or boundary | Observed result |
| --- | --- |
| Omitted PID with two unattached candidates | `target_ambiguous`; no adoption. |
| PID 25092 after Portal-only attachment to PID 27540 | `binding_conflict`; later no-PID inspection recovered on PID 27540. The host was unbound, so this is not evidence of host-local `NotSent` refusal or verified cursor preservation. |
| Wrong-case server alias, absent named group, missing project | `target_not_found`. |
| Wrong-case action, PID 0, incoherent root group, missing group `name` key, supplied inspection `forceRebind:false` | `validation_error`. |
| Explicit `.als21` binding | `validation_error`; no session adoption. |
| Default and null-action binding with two opened sessions | `target_ambiguous`; no verified standalone binding established. |
| `get_project_status` | `isOpen:false`, metadata and other project fields null. |
| `browse_project_tree` | `worker_operation_failed` with no-open-project diagnostic; session content remained unavailable. |

All **29/29** captured initial text documents decoded to the same document as `structuredContent`.
This compares decoded documents, not literal text against JavaScript `JSON.stringify`; valid
JSON escaping can differ. The **21** returned binding values all had unbound before/after
snapshots, `transition:"none"` and `project:null`. Maximum response text was **1,194 characters**;
this is no budget-stress or omission evidence.

No lifecycle elicitation surfaced in these calls; no elicitation fault/instrumentation was
injected. Audit aggregate metadata before/after matched: 27 files, 1,762,896 total bytes,
latest file `writes-2026-10-05.jsonl`, length 541,722 and last write
`2026-10-05T23:20:25.0738546+02:00`. No full audit-stream hash comparison was made.
Final initial-phase session IDs/paths and Portal candidates matched their initial reads.
This does not establish byte preservation of the whole fixture filesystem or later user changes.

## Standalone phase: prerequisites and healthy binding

After the user's fixture change, fresh discovery found the sole Portal **25092** with the exact
authorized project:

```text
C:\Users\LCZ\Desktop\RnD\plc-prompt-injections\_mcp_test\SimpleProject_copy\SimpleProject_copy.ap21
```

No `.als21` session was opened in this phase. The installed process was observed in `full` mode.
All five inventories succeeded before standalone adoption while the host was unbound. Default
`bind_project` then adopted the sole already-open project; explicit and null-action binding
returned `transition:"unchanged"`. All six inspections succeeded against the verified same PID.

A verified foreign PID 27540 returned root `binding_conflict` with `result:null`, unlike the
initial unbound worker-completed refusal. A `Local Project Server` groups failure retained
verified binding. A previously issued 138-node tree cursor continued from offset 0 to 1 with
the same snapshot ID after those inspections/refusals. This is live healthy standalone
binding/cursor preservation evidence; public responses did not establish lifecycle ownership
(`effects.sourceOpenedByWorker` was null).

The agent previewed `close_project` with `dryRun:true` and `saveBeforeClose:true`, then executed
the authorized save-and-close against that exact project. Fresh discovery still found the sole
Portal, now with `projectPath:null`. **All five inventories succeeded with zero open projects**,
including groups, root projects, current-user sessions and lock state. Default binding returned
`target_not_found` without opening a project; the old tree cursor returned
`cursor_binding_mismatch`. This demonstrates Task 5's zero-project versus already-open `.ap21`
prerequisite comparison for the exact fixture; neither remote read required an opened `.als21`.

The agent reopened the exact original `.ap21` using `open_project`. Status returned
`isOpen:true` and `isModified:false`; an old pre-close cursor remained invalid. The reported
project file size changed from **30,992 to 30,997 bytes** after saving/reopening, so this run
does not claim byte-for-byte fixture restoration.

## Genuine worker loss and explicit recovery

The agent checked the executable path and parent PID before stopping only worker **38848**,
whose parent was installed host **32012**. The host restarted the worker as **22956**.
The next `list_portals` returned a typed `binding_conflict` and changed the previously verified
binding to `invalidated`, retaining its asserted project path and clearing the binding PID.
Fresh Portal 25092 was listed with `isBound:false`. The cursor issued after reopening was
rejected as `cursor_binding_mismatch`.

A separate no-PID `list_server_connections` then succeeded with Portal-only attachment at
25092 while the host binding remained invalidated with a null binding PID. Successful inventory
did not promote the retained path. A subsequent explicit bind to the authorized path recovered
verified binding. This is genuine worker-loss evidence, not an external project-close race or
injected server disconnect/reconnect. Recovery used explicit subsequent calls; the first failed
discovery was not reported as an automatically replayed success.

The restarted worker's first successful endpoint observation had `previousState:null`; its
later lock read had `previousState:"connected"`, `transition:false`. The final bound tree cursor
again continued from offset 0 to 1 with the same 138-node snapshot after groups/lock reads.
Final state was verified PID 25092 at the exact authorized `.ap21`, open and unmodified, with
reported size 30,997 bytes. The capture completed at **21:24:09 Europe/Warsaw on October 6**.

## Inspection audit separation and lifecycle correspondence

Initial inspections and all subsequent inspection/binding/cursor calls before fixture lifecycle
left aggregate audit metadata unchanged at 27 files / 1,762,896 bytes. The authorized close
preview, actual save-and-close and reopen added exactly **three audit-v2 records**. Final totals
were 28 files / 1,776,052 bytes; `writes-2026-10-06.jsonl` contained those three records and
13,156 bytes. Its SHA-256 was
`F13B3B02DBC6E3AEEBF7EF3844FD32F079BAEFF2A802C36EC3DA12D7F1ED2A73`.

The retained correspondence capture shows each audit record contains the exact returned response
text and a matching `sha256:` response hash. Preview confirmation was `none`; both actual `full`
calls used `policy`. No lifecycle elicitation surfaced; read-only/read-write live hosts and
elicitation fault instrumentation were not exercised. The audit was not unchanged across the
whole run: the three authorized lifecycle records are intentional.

## Remaining Task 5 gates

The [accepted plan](../../plans/2026-10-05-multiuser-pr3-read-only-inventory.md#task-5-frozen-candidate-live-acceptance)
checks Step 5 for this exact prerequisite comparison. Other composite steps remain partial:

| Task 5 step | Current evidence and remaining scope |
| --- | --- |
| 1: frozen candidate/provenance | Reviewed head, installed hashes, runtime/service file versions, exact authorized fixtures/endpoints and current-machine/user scope recorded. Full binding-regression matrix remains incomplete because of the outstanding cases in Steps 2 and 4. |
| 2: discovery and binding | Multiple and sole Portal discovery, default/null/explicit binding, healthy cursor continuation and stale-cursor rejection observed. Zero Portal processes and guarded switching to a second standalone target remain untested. |
| 3: remote inventories | All five succeeded with opened sessions, with a standalone `.ap21` and with zero open projects; exact root, session ID/directory mapping and separate locked/unlocked results observed. Actual named/duplicate groups or duplicate project names, empty current-user session lists, duplicate IDs within one project, zero/negative IDs and dedicated/multiple-client tier cases remain untested. |
| 4: continuity and adverse state | Verified same-PID inspection, foreign refusal, endpoint-failure preservation, worker-loss invalidation and explicit recovery observed. Read-only/read-write live hosts, configured-unverified startup assertions, public ownership proof and external project-close races remain untested. |
| 5: open-project prerequisite | Complete for the exact sole-PID fixture: all five inventories succeeded with `.ap21` open and with zero open projects, with no opened `.als21` and no implicit open/bind. |
| 6: contract/audit/state/recovery | All 73 decoded documents match, inspection audit separation and three exact lifecycle audit correspondences observed; final standalone project open/unmodified. Lock-race and genuine connectivity/authentication/disconnect injections remain untested; response budgets were not stressed. |

No production fix was identified in the exercised cases. `.als21` adoption, session
content/state/markings/mutations and Issue #65 completion remain undelivered. These observations
do not waive the remaining operation-specific acceptance gates or claim full Task 5 acceptance.
