# Multiuser PR 3 Binding Discovery and Inventory Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend `bind_project` with explicit Portal discovery and five Project Server inventory actions, preserving default binding and adding no public tool.

**Architecture:** Keep the existing binding response and standalone canonical-result/budget seam. One optional action separates selection from inspection. Inspection uses the serialized client and persistent Portal attachment without adopting or switching projects; every worker payload has a concrete DTO and becomes a typed conditional inspection result. Siemens calls remain in the net48 worker.

**Tech Stack:** C#, SDK 10.0.400, net10.0 host/tests, net48 worker, netstandard2.0 contracts, System.Text.Json, xUnit, PowerShell, installed V21 references.

**Spec:** [Multiuser Engineering design](../specs/2026-09-28-multiuser-engineering-design.md), amended on 2026-10-05 for the user-selected binding-only read surface.

**Status:** Accepted for local implementation, granular local commits and offline qualification. Tasks 1–3 implemented and independently reviewed; Task 4 offline qualification/documentation passed, final whole-branch review remains a controller gate. Task 5 live acceptance is pending exact fixture/scope authorization. No remote publication or installed-tool/configuration change is authorized.

**Baseline:** `main` / `origin/main` / GitHub main at `ea269c222e7415af347f2929cbc82a366aa2ee0f` (merged PR #109). Worktree `C:\Users\LCZ\.codex\worktrees\multiuser-pr3\tia-portal-mcp`, branch `feature/multiuser-pr3`. PR 1/PR 2 are present. Discovery remains **6/16/16**. The earlier separate read tool, request batching, new registration, and higher tool counts are superseded.

## Global Constraints

- User decision: “I prefer to stick to bind_project only, no new, separate tool.”
- Preserve existing `bind_project(projectPath?, forceRebind=false)` behavior/output, sole-project auto-bind, exact selection, configured-path conflicts, ownership, and binding/cursor continuity.
- Inspection never opens, adopts, switches, saves, closes, discards, commits, or changes a binding. No token, lifecycle elicitation, write audit, temporary Portal detach, or second worker is introduced.
- “Use exact typed selectors and canonical structured output.” Zero matches is `target_not_found`; multiple matches is `target_ambiguous`, never first-target fallback.
- “Host and contract projects remain Siemens-free.” Generated references stay source-verified, compile-only, and excluded from runtime/package payloads.
- “Capabilities are descriptive, not authorization.” Preserve the undelivered `.als21` boundary and PR 2's passive context.
- “The worker never calls LocalSession.Close() as unconditional cleanup.” No mutation APIs or automatic fixture cleanup.
- Every new action requires current-PR operation-specific live evidence before merge readiness. Compilation, metadata, FakeWorker, stubs, and historical acceptance are separate evidence.
- Genuine worker/Portal/context loss retains normal invalidation. Healthy-context preservation does not promise a dead worker remains bound. Never replay a failed inventory operation automatically.

## Review Focus

1. Default/no action retains binding; explicit discovery cannot trigger sole-project auto-bind (Tasks 2–3).
2. Intentional foreign-PID refusal preserves healthy binding/cursors, while genuine stale identity invalidates evidence (Tasks 2–3).
3. Missing group members, duplicate targets, and same-named projects across groups never select arbitrarily (Tasks 1–3).
4. Empty current-user session lists and failed lock/connectivity reads never imply all-user coverage, unlocked state, or proven authentication/connectivity (Tasks 1–3, 5).
5. New typed results preserve default output, strict explicit nulls, one canonical document, and honest whole-value omissions (Tasks 1, 3).

---

## Public interface

Append optional `action`, `portalProcessId`, `serverAlias`, `group`, and `serverProjectName`. Keep the existing CLR parameter order through `cancellationToken`, then append optional parameters to preserve positional callers. Omitted/null action means `bind`; explicit names are ordinal case-sensitive. One call performs one action: no operations array, IDs, batch wrapper, or separate read tool.

| Action | Allowed selectors | Worker result |
| --- | --- | --- |
| `bind` (default) | Existing `projectPath`, `forceRebind` | Existing `.ap21` selection/result, unchanged |
| `list_portals` | None | Fresh `list_tia_portal_processes`; existing `TiaPortalProcessListInfo` |
| `list_server_connections` | Optional `portalProcessId` | `MultiuserServerConnectionsInfo` |
| `list_server_groups` | Optional PID; required `serverAlias` | `MultiuserServerGroupsInfo` |
| `list_server_projects` | Optional PID; required alias and `group` | `MultiuserServerProjectsInfo` |
| `list_local_sessions` | Optional PID; required alias, group, `serverProjectName` | `MultiuserLocalSessionsInfo` |
| `get_lock_state` | Optional PID; required alias, group, project | `MultiuserLockStateInfo` |

`get_session_state`, `get_markings`, `.als21` adoption/opening, content compatibility, and mutations remain successor work. Future reads use binding inspection actions; no placeholder implementation is added. Planned mutation tools are outside this PR 3 read-routing amendment.

```json
{ "action": "list_portals" }
```

```json
{
  "action": "list_server_projects",
  "portalProcessId": 1234,
  "serverAlias": "Fixture",
  "group": { "isRoot": true, "name": null }
}
```

For `bind`, retain existing null/default normalization and `.ap21` validation; reject inspection selectors. Inspections reject supplied `projectPath`/`forceRebind`, even null/false keys. `list_portals` accepts only action. A supplied PID is a positive integer: reject null, fractions, strings, and booleans. Unknown action/member, wrong type, blank names, missing selectors, and cross-action keys fail before worker activity.

Root group is `{isRoot:true,name:null}`; named group is `{isRoot:false,name:"Exact name"}`. Define host `MultiuserGroupSelector` with `bool? IsRoot`/`string? Name`, `[JsonRequired]` on both and unmapped-member rejection; validate presence/coherence, then convert to `ProjectServerGroupIdentity`. Match aliases/groups/projects ordinal-exactly without trimming, case folding, or root fallback.

## Portal and binding policy

`list_portals` performs fresh process discovery without `BindOpenProjectAsync`, attachment, or project selection. Existing `portals` entries come from that call; `isBound` uses the current host snapshot: healthy verified identity is unchanged; observed identity loss invalidates it, including during supplemental candidate listing.

Capture host binding under `ExecuteSerializedBindingOperationAsync` for inventory:

1. With verified binding, reject an explicit different PID locally before dispatch (`binding_conflict`, NotSent). Default the PID assertion to the verified PID and copy captured `ExpectedSessionIdentity` only to verify continuity. Do not resolve/send projectPath.
2. Without verified binding, do not ground configured assertions or copy invalidated/last-bound PID. Reuse a healthy worker attachment. A supplied different PID is refused before attach/detach. Without attachment, select the exact explicit PID or require exactly one fresh candidate, then attach persistently without adopting a project.
3. No temporary attachment/detach or transient worker. To switch instances, use explicit binding and its existing guards. Inventory is deliberately limited to the attached instance once one exists.
4. Check cached expected identity before Portal activity and live identity after portal-only validation, without `EnsureConnected` project rescan. Genuine worker/Portal/project identity loss or timeout/crash retains invalidation. Ordinary server/selector failure preserves healthy context/ownership.

Existing `InvokeWorkerAsync` invalidates verified binding on worker `binding_conflict`; deliberate foreign-PID refusal must therefore be local. A worker conflict proving actual identity loss must not be globally suppressed. Authorization still runs before worker startup/Siemens APIs. On target absence/ambiguity, one extra process listing may supply candidates from this call; never replay inventory. Remote inspection success is not a prerequisite for ordinary binding.

## Results, API projection, and budgets

Keep `BindProjectResponse.Result : StandaloneToolOutcome<ProjectBindingResult>` and contract version `1.0`. Append optional `Inspection=null` to `ProjectBindingResult`, inside the outcome's budgeted `Value`, with `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`; default bind omits it and retains its old JSON shape. Do not add inspection beside `result` at the response root: whole-value omission must remove the inventory too.

`ProjectBindingInspectionInfo` has required `action`, nullable observed `portalProcessId`, and five typed nullable slots: `serverConnections`, `serverGroups`, `serverProjects`, `localSessions`, `lockState`. Every slot is explicitly written. Successful inventory fills exactly its action's slot; list_portals fills none and uses `portals`. Failed inspection fills none and uses existing outcome failure. No arbitrary JsonElement payload/new response union.

Inspection has `transition:"none"`, `project:null`, and before/after snapshots equal while healthy; report genuine invalidation in the after state. Bad input/local refusal uses current pre-dispatch rejection. Executed failure has top-level `error:null`, failed outcome, MCP `isError:false`. Normalize each worker result through its declared DTO and `CanonicalJson.NormalizeWorkerPayload<T>`; malformed payload is sanitized `protocol_error`, never echoed. Compose via `StructuredStandaloneResult` once.

Reuse 60,000-character whole-value and 180,000-character document budgets. Add an optional omission-guidance override to the existing helper; inventory guidance cannot suggest forbidden projectPath. Never slice an inventory and call it complete. Narrow by exact group/project where supported; retry of the same oversized inventory is not promised to fit.

Minimum required wire members (nullable members still appear as null):

- Connections: `connections[]` of alias (`ProjectServer.ServerName`), host, port; enumerate `TiaPortal.ProjectServers`. Configuration is not connectivity; no verified Protocol getter exists.
- Groups: exact remoteIdentity, connectionObservation, groups[] from `GetProjectServerGroups()`; no invented named root.
- Projects: exact server/group identity and observation, projects[] with name (`ProjectName`), checking returned ServerAlias; call root/named `GetServerProjects()`.
- Sessions: exact project identity/observation, scope `currentMachineCurrentUser`, sessions[] with int sessionId and canonical projectPath from ProjectFileInfo; `GetLocalSessions(actual ServerProjectInfo)`. No all-user coverage or session opening.
- Lock: exact identity/observation, isLocked, nullable owner, observedAt; get provider from the actual returned ServerProjectInfo. Skip owner lookup while unlocked; when locked read owner and recheck flag. Changed flag fails `state_changed`; no atomic lock guarantee.

Reuse PR 1 identity/group/observation DTOs. Generic MultiuserException does not prove unavailability/authentication: retain existing categories and sanitized failures. Failed/incomplete enumeration is not empty success. Keep observation history for the persistent worker/session, reusing existing storage where suitable, keyed by actual Portal attachment plus alias/host/port. Compare successive observations only for the same attachment/endpoint; reset after attachment/configuration changes and do not overwrite unrelated active-context observations. No polling service is added.

Combined annotation remains ReadOnly=false, Destructive=false, Idempotent=true; OpenWorld=true now covers remote inventory. Binding worker selection remains SessionSelection; inventory methods are Observe. All modes allow these actions without lifecycle elicitation. Discovery stays 6/16/16.

## Task 1: V21 surface and typed contracts

**Files:** Modify `reference-stubs/Siemens.Engineering.Base/Multiuser.cs`, `reference-stubs/TiaMcpServer.OpennessReferenceProbe/MultiuserReferenceSurface.cs`, `TiaMcpServer.Contracts/WorkerRequest.cs`, generated ref artifacts via verifier, and existing foundation tests. Create `TiaMcpServer.Contracts/MultiuserInventoryRequests.cs`, `MultiuserInventoryInfo.cs`, and `TiaMcpServer.Tests/Multiuser/MultiuserInventoryContractTests.cs`.

**Interfaces:** Five logical request/result pairs above. Flat IPC gains PortalProcessId:int?, MultiuserServerAlias, MultiuserGroupIsRoot:bool?, MultiuserGroupName, MultiuserServerProjectName. Protocol version is unchanged; nullable WorkerResponse.PortalProcessId is additive actual-attachment evidence for unbound inventory. ExpectedSessionIdentity is optional verified preservation evidence.

- [x] **Step 1: Add failing uncalled probe method groups** for enumeration, five paths/getters, and installed IList<T> signatures. Add contract assertions for distinct DTOs, required explicit nulls, exact identity/group coherence, scope, and Siemens-free contracts.
- [x] **Step 2: Run verifier/focused tests**, retaining relevant missing-surface failures.
- [x] **Step 3: Add only the verified surface/DTOs** and run `pwsh -NoProfile -File scripts/verify-reference-stubs.ps1 -Update`. Shared source hash refreshes both Base/Step7 artifacts. No invocation/construction in compile probe, new dependency, or unrelated API expansion.
- [x] **Step 4: Run drift verifier, contract tests, and installed compile probe** below. Check null-policy registers; add no legacy omission marker.

## Task 2: Worker inspection without selection/detach

**Files:** Modify worker `Openness/TiaPortalSession.cs`, `Program.cs`, Contracts `OperationPolicyCatalog.cs`, test `TestUtilities/TagSafetySiemensDoubles.cs`, and test csproj links. Create worker `Openness/MultiuserInventoryService.cs`, tests `TestUtilities/MultiuserInventorySiemensDoubles.cs`, `Multiuser/MultiuserInventoryWorkerTests.cs`, `Multiuser/MultiuserPortalReadDispatchTests.cs`.

**Interfaces:** `EnsurePortalConnected(int? requestedProcessId=null): void`; service constructor `(TiaPortalSession session)` and five synchronous typed request/result methods; `WithPortalRead(WorkerRequest, Func<TiaPortalSession, WorkerResponse>)`. Reuse list_tia_portal_processes; five new method names equal inventory actions and are Observe.

- [x] **Step 1: Write failing source-linked tests** for same-PID reuse, foreign-PID refusal before attach/detach, unattached zero/ambiguous/exact selection, and first attachment without adoption. Assert healthy context/generation/ownership unchanged and zero mutation counters. Test expected identity before/after checks and no configured-path promotion.
- [x] **Step 2: Run focused worker/dispatch tests** and retain failures.
- [x] **Step 3: Extract existing exact attachment/PID/event logic**, preserving Connect/EnsureConnected defaults. Inspection never invokes SelectOpenProject, AdoptContext, WithProject, or unconditional cleanup. Direct worker requests reject projectPath/write flags/inappropriate selectors; validate supplied expected identity.
- [x] **Step 4: Implement exact resolution/projection**, retaining actual ServerProjectInfo. Validate complete rows, identity, paths, lock evidence, endpoint observations, and sanitized existing categories. Test same-endpoint observation transitions across calls and absence of comparisons across attachment/endpoint changes.
- [x] **Step 5: Run focused/existing session-context tests**, default selection regressions, server-error preservation, and actual Portal-loss invalidation. Source-link service; stubs are not runtime doubles. Build against stub/installed references.

## Task 3: Extend existing bind_project inputs/output

**Files:** Modify host `Tools/ProjectBindingTools.cs`, `StandaloneToolResponses.cs`, `StructuredStandaloneResult.cs`, `Worker/OpennessWorkerClient.cs`; test csproj links, `Tools/BindProjectToolProtocolTests.cs`, `ToolOutputContractConformanceTests.cs`, `AccessModeDiscoveryTests.cs`, `Json/ConditionalMemberRegisterTests.cs`. Create host `Tools/ProjectBindingInspectionCatalog.cs`, `ProjectBindingInspectionPayloadContract.cs` and tests `Multiuser/ProjectBindingInspectionTests.cs`, `ProjectBindingInspectionClientTests.cs`, `ProjectBindingInspectionBudgetTests.cs`. No new tool registration/class.

**Interfaces:** Retain BindProject's first four CLR parameters; append string? action=null, int? portalProcessId=null, string? serverAlias=null, MultiuserGroupSelector? group=null, string? serverProjectName=null. Catalog validates raw action-specific keys and maps typed requests. Add client `InspectPortalAsync(WorkerRequest request, CancellationToken cancellationToken=default): Task<ProjectInspectionOutcome>` carrying before/after snapshots, WorkerCallResult, and candidates listed during this call; reuse existing outcome types where suitable. Payload contract normalizes the five DTOs. Add no interface/factory.

- [x] **Step 1: Write failing registered-tool tests** for old/default/null/explicit bind compatibility, list_portals never selecting a sole project, false/null forbidden binding keys on inspection, all selector/type/member failures before dispatch, and missing group members. Test the real wrapper/schema, not only CLR inputs.
- [x] **Step 2: Write failing FakeWorker tests** for unchanged healthy revisions/cursors, foreign-PID local NotSent refusal with zero dispatch, same-PID expected identity forwarding, no status/projectPath/promotion, unbound/configured behavior, server failure, and genuine stale/timeout/crash invalidation without replay.
- [x] **Step 3: Extend existing schema/raw validator**, routing bind to BindOpenProjectAsync and explicit inspection under existing client serialization. Preserve global identity-loss handling; avoid it only for local caller refusals. No second tool, operations array, transient worker, or batch engine.
- [x] **Step 4: Extend typed standalone result**, conditional Inspection=null, action/slot coherence, observed PID evidence, sanitized null/error semantics, and omission guidance override. Extend conditional-member checks to this host property; current register scans Contracts only. Default JSON shape must remain unchanged.
- [x] **Step 5: Run schema/payload/budget/conformance tests** for six inspection actions plus binding. Probe missing/null/wrong worker members, null rows, incoherent action/PID/remote identity, unknown observation states, and rejected secret sentinels: require sanitized `protocol_error` with no payload echo. Assert canonical equality, whole-value omissions, action-valid guidance, 6/16/16 counts, one registration, ReadOnly=false/OpenWorld=true, all modes, no lifecycle prompt/write audit, and SessionSelection/Observe authorization.

## Task 4: Qualification and maintained documentation

**Files:** Update delivered behavior in PROJECT_OPERATIONS_SUMMARY, MULTIUSER_OPERATIONS_SUMMARY, SupportedOperations index, architecture, necessary guides, README/AGENTS, improvement log, and both indexes. Qualification goes into the indexed [PR 3 offline report](../acceptance/reports/2026-10-06-multiuser-pr3-offline-validation.md); create a separate Task 5 live report only for actual evidence.

- [x] **Step 1: Run serial same-configuration solution build before subprocess tests**, full suite, materially changed branch coverage with existing 0.80 line threshold, stub drift, installed build/probe, and package-leak checks. Retain logs/exit codes and environmental limitations.
- [x] **Step 2: Document explicit discovery vs default auto-bind**, attached-PID restriction, real invalidation, selectors, current-user scope, lock/omission limits, unchanged count, and undelivered ALS21/Issue65 boundary.
- [ ] **Step 3: Independently review whole candidate** for default compatibility, schema, identity, secrets, runtime references, and scope; validate links/anchors/examples/indexes/whitespace. Documentation-only fixes reuse valid build evidence.

Execution-phase commands:

```powershell
dotnet restore TiaMcpServer.slnx
dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true --no-restore
dotnet test TiaMcpServer.Tests --no-build --no-restore --filter "FullyQualifiedName~Multiuser|FullyQualifiedName~BindProject" -- xUnit.ParallelizeTestCollections=false xUnit.ParallelizeAssembly=false xUnit.MaxParallelThreads=1 RunConfiguration.MaxCpuCount=1
dotnet test TiaMcpServer.Tests --no-build --no-restore -- xUnit.ParallelizeTestCollections=false xUnit.ParallelizeAssembly=false xUnit.MaxParallelThreads=1 RunConfiguration.MaxCpuCount=1
pwsh -NoProfile -File scripts/verify-reference-stubs.ps1
dotnet build reference-stubs/TiaMcpServer.OpennessReferenceProbe/TiaMcpServer.OpennessReferenceProbe.csproj -m:1 /p:UseTiaPortalReferenceStubs=false /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"
dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=false /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48" --no-restore
git diff --check
```

Coverage/package procedures: [building](../../development/building.md), [packaging](../../development/packaging.md). TLS/auth/restore errors and timeouts are not passes; do not change credentials/dependencies or equate compile with live acceptance.

## Task 5: Frozen-candidate live acceptance

**Files:** Reuse acceptance harness or add minimum action-based capture under scripts/acceptance. Create/index `docs/superpowers/acceptance/reports/2026-10-05-multiuser-pr3-read-only-inventory-live.md` only for actual evidence.

- [ ] **Step 1: Freeze reviewed offline-qualified candidate** and record SHA, hashes, TIA/server versions, exact PID/endpoint/group/project names, machine/user scope, and authorized reads/binding regression. Serialize shared live state; this plan authorizes no execution/setup mutation.
- [ ] **Step 2: Verify list_portals** with zero/sole/multiple projects/instances and no binding. Regress default/null bind, exact selection, ambiguity, re-verification, guarded switching, ownership, and stale cursors. Switching needs exact fixture authorization; inspection never cleans up by switching.
- [ ] **Step 3: Verify five inventories without opened ALS21**, including root/named/duplicate project names, current-user sessions/empty list/ID-path equality, and prearranged unlocked/locked owner evidence. Dedicated/multiple-client tiers apply where needed. No connection/session/project/lock mutation in capture.
- [ ] **Step 4: Verify same-PID healthy binding/ownership/cursors**, foreign-PID local refusal, unbound/configured non-promotion, all modes, and honest adverse/genuine identity-loss behavior. No claim that binding survives worker termination.
- [ ] **Step 5: Test zero-project vs already-open AP21 prerequisite separately.** Siemens remote inventory pages list an open-project prerequisite; never open/bind implicitly to satisfy it. If ALS21 is required, stop and revise before enabling affected action.
- [ ] **Step 6: Check canonical responses, no elicitation/write audit, before/after state, recovery, and operation-specific limitations.** Unavailable server does not clear unrelated healthy context. Fixture/connectivity changes require separate authorization; missing tier/action evidence leaves merge readiness pending.

## API evidence and verification boundary

Earlier October 5 planning used reflection-only installed V21 metadata without invocation/attachment: ServerName/Host/Port, group Name, ProjectName/ServerAlias, SessionId/ProjectFileInfo, IList<T> methods, lock provider methods. No declared Protocol getter was found. This revision checked current binding response, raw wrapper, standalone budget, serialized client, and blanket worker binding-conflict invalidation. Source-owned ProjectServer references still require the minimal expansion above.

Primary V21 sources: [projects](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/functions-support-for-multiuser/getting-server-projects), [groups](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/functions-support-for-multiuser/getting-project-server-group), [current-user sessions](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/functions-support-for-multiuser/getting-available-local-sessions), [lock provider](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/functions-support-for-multiuser/getting-lockstate-provider), [lock state](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/functions-support-for-multiuser/checking-project-locked), [owner](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/functions-support-for-multiuser/getting-lock-owner).

Initial planning history (October 5): only planning documents changed; no implementation, build/test/package suite, commit, remote mutation, or live TIA operation ran during that planning step. The plan was subsequently accepted and implemented/offline-qualified as recorded above. The remaining gates are final review and Task 5 operation-specific live acceptance on an explicitly authorized fixture.
