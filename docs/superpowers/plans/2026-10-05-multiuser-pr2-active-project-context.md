# Multiuser PR 2 Active Project Context Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the worker's concrete project/ownership fields with a typed active engineering
context and lifecycle owners, preserving all delivered standalone selection, binding, and lifecycle
behavior without enabling a Multiuser operation.

**Architecture:** `TiaPortalSession` remains the single authority for worker identity, generation,
Portal attachment, and selection. It owns one `ActiveProjectContext` with a `ProjectBase` engineering
root and a typed standalone-project or local-session owner. Existing content services retain a
read-only standalone `Project` compatibility accessor; public Multiuser integration and individual
content-service migrations stay in their designated successor PRs.

**Tech Stack:** C#; .NET SDK 10.0.400; net48 worker; netstandard2.0 contracts; net10.0 host/tests;
System.Text.Json; xUnit; PowerShell 7; installed TIA Portal V21 Openness references.

**Spec:** [Multiuser Engineering design](../specs/2026-09-28-multiuser-engineering-design.md),
especially “Active project context,” “Lifecycle ownership,” and “PR 2 — Active project context.”

**Planning baseline:** 2026-10-05, merge commit
`478f5ca1bc8cb36e729c1b0954252e2e69f0fb6d` (PR #106, Multiuser PR 1).
The checkout was clean when inspected. The user requested PR 2 planning; this document does not
authorize implementation, GitHub writes, or live execution. The spec's older “PR 1 detailed
planning authorized” status describes its original handoff, not this new planning request.

## Global Constraints

- “Migrate the central worker session seam without exposing Multiuser.” Deliver PR 2 only.
- “Preserve `bind_project`, configured-selector promotion, detach ownership, and binding ID/revision continuity.”
- “Host and contract projects remain Siemens-free.” Keep Siemens runtime calls in the net48 worker.
- “Capabilities are descriptive, not authorization.” Do not advertise `.als21` compatibility.
- “Ownership does not survive detach or reattachment.” Adoption does not grant lifecycle ownership.
- “The worker never calls `LocalSession.Close()` as unconditional cleanup.” Add no local-session save,
  discard, commit, opener, inventory, connectivity probe, or remote mutation call in this PR.
- Preserve `unbound`, `configured_unverified`, `verified`, and `invalidated`; old project-tree
  cursors still fail with `cursor_binding_mismatch` after binding ID/revision changes.
- Preserve lifecycle phases `preview`, `blocked`, `applied`, and `error`, typed results/verification,
  one canonical document, mode-derived confirmation, one pinned lease, and one audit v2 record per call.
- “No new snapshot reader, `SafetyRead` catalog entry, or token-bound write tool is introduced for Multiuser.”
  Preserve the current transitional Network/batch flows without introducing a parallel mechanism.
- Preserve tool counts 5/15/15 in read-only/read-write/full, public schemas, startup selection,
  access policies, worker protocol version, and failure-category vocabulary.
- Use stable SDK 10.0.400 and serial builds (`-m:1`). Run the full suite with collection/assembly
  parallelism disabled and enforce the existing 0.80 line-coverage threshold.
- Generated references remain compile-only, source-verified, and excluded from packages/runtime
  payloads; stub, test-double, real-reference, and live evidence remain distinct.
- PR 2 requires fresh live `.ap21` selection/switching and guarded lifecycle regression on its own
  frozen candidate. Historical acceptance and offline success cannot qualify that gate.

## Review Focus

1. A new context wrapper around the same engineering handle must not create a new identity generation
   or host binding epoch; a different handle at the same path must invalidate old evidence (Task 2).
2. An unreadable/blank live path or UI-side SaveAs must not leave a verified identity for the old path
   or silently select another project (Task 2).
3. Failed closing, failed attachment, and headless last-client detachment must preserve the source
   where continuity is provable and invalidate it when detachment has occurred (Task 2).
4. A local-session owner must never inherit generic standalone save/close/archive or cleanup
   behavior, even if a future caller constructs an internal context (Tasks 1 and 3).
5. Rebinding or modified-state changes during confirmation must block stale dispatch, preserve typed
   failure semantics, and produce exactly one audit record (Task 4).

---

## Baseline and Scope Decisions

PR 1 now provides source-owned strong-named references, `ProjectBase`/`MultiuserProject`/`LocalSession`
signatures, a locked compile probe, drift verification, and passive contract vocabularies.
`docs/IMPROVEMENT_LOG.md` still makes accepted PR 1 qualification/review an execution dependency:
the merge commit is present, but this planning session did not independently rerun that evidence.

Current production seams:

- `Openness/TiaPortalSession.cs` stores `_project`, `_projectOpenedByWorker`, `_selectedProjectPath`,
  Portal handle/PID, worker ID, and generation. Selection uses exact inventory and shared selectors.
- `Program.cs` validates expected identity before and after connection/project resolution and stamps
  successful responses with the live path and worker identity.
- `ProjectLifecycleService` operates on concrete `Project`, revalidates immediately before each
  mutation, and explicitly records create, SaveAs identity changes, and close.
- Host `ProjectSessionBinding`, `LifecycleBindingStrategy`, `WriteExecution`, and cursors consume
  the existing four-member worker identity. These production contracts do not need modification.
- Tests already source-link selected worker files against repository-owned Siemens boundary doubles
  in `TestUtilities/TagSafetySiemensDoubles.cs`; they never execute the generated reference assemblies.

Use a narrow context migration. A service-wide `Project` → `ProjectBase` migration would couple
this PR to content compatibility work assigned to PRs 5/6. A broad engineering facade or duplicated
Multiuser services would contradict the selected architecture. The compatibility accessor is
temporary and explicitly standalone-only; it does not create a second stored project handle.

The design's logical `BindingIdentity` remains a projection produced by
`TiaPortalSession.GetSessionIdentity()` from the active context plus worker/Portal/generation state.
Do not store a separately mutable copy in the context. Wire identity/binding fields remain unchanged
until PR 4 plans their complete extension, strict decoding, comparison, and invalidation rules.

`CapabilitySet` is an internal, read-only empty collection in PR 2, explicitly meaning compatibility
classification has not been populated. It grants nothing, drives no dispatch, and is not serialized.
Remote identity and connection observation are null. PR 4 populates these fields and delivers the
public capability/binding contract; PRs 5/6 enable individually verified content operations.

### File Responsibilities

All paths below are relative to the repository root.

| File | Responsibility |
| --- | --- |
| `TiaMcpServer.OpennessWorker/Openness/ActiveProjectContext.cs` | Engineering root, typed owner, internal context metadata |
| `TiaMcpServer.OpennessWorker/Openness/ProjectLifecycleOwner.cs` | Minimal owner base: typed engineering root and worker-open provenance |
| `TiaMcpServer.OpennessWorker/Openness/StandaloneProjectOwner.cs` | Retain the concrete standalone `Project` handle |
| `TiaMcpServer.OpennessWorker/Openness/LocalSessionOwner.cs` | Retain `LocalSession` and its `MultiuserProject`; expose no mutation methods |
| `TiaMcpServer.OpennessWorker/Openness/TiaPortalSession.cs` | Context transitions, identity, exact adoption, detach, and ownership |
| `TiaMcpServer.OpennessWorker/Openness/ProjectLifecycleService.cs` | Explicit standalone-owner resolution at lifecycle/status boundaries |
| `TiaMcpServer.OpennessWorker/Program.cs` | Preserve authorization/identity gates and use explicit standalone resolution |
| `TiaMcpServer.Tests/TestUtilities/PortalSessionSiemensDoubles.cs` | Executable in-memory Portal/session boundary doubles and call observations |
| `TiaMcpServer.Tests/TestUtilities/TagSafetySiemensDoubles.cs` | Extend the existing project double without duplicate Siemens type names |
| `TiaMcpServer.Tests/Worker/ActiveProjectContextTests.cs` | Owner/root consistency and passive metadata tests |
| `TiaMcpServer.Tests/Worker/ActiveProjectContextSessionTests.cs` | Execute the real source-linked session transitions against boundary doubles |
| `TiaMcpServer.Tests/Worker/ActiveProjectContextSourceTests.cs` | Wiring, standalone-only dispatch, and cleanup prohibition checks |
| `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj` | Link context/session/inventory production source into the existing offline tests |

Existing source-contract and host/FakeWorker tests are adjusted only as listed in their owning tasks.
Do not edit reference source/artifacts, host production code, public contracts, operation catalogs,
or content-service signatures to complete this migration. A demonstrated need requires replanning
the affected boundary first.

## Task 1: Add the Typed Context and Passive Lifecycle Owners

**Files:** Create the four context/owner production files and `ActiveProjectContextTests.cs` from
the table. Create `TestUtilities/PortalSessionSiemensDoubles.cs`; modify
`TestUtilities/TagSafetySiemensDoubles.cs` and `TiaMcpServer.Tests.csproj`.

**Interfaces:**

- `internal abstract class ProjectLifecycleOwner`: `ProjectBase EngineeringRoot { get; }` and
  `bool OpenedByWorker { get; }`; no `Dispose`, save, close, discard, or commit abstraction.
- `internal sealed class StandaloneProjectOwner : ProjectLifecycleOwner`:
  constructor `(Project project, bool openedByWorker)`, `Project Project { get; }`.
- `internal sealed class LocalSessionOwner : ProjectLifecycleOwner`:
  constructor `(LocalSession localSession, bool openedByWorker)`,
  `LocalSession LocalSession { get; }`, `MultiuserProject Project { get; }`.
- `internal sealed class ActiveProjectContext`: factories
  `ForStandalone(Project project, bool openedByWorker)` and
  `ForLocalSession(LocalSession localSession, bool openedByWorker, string containerKind, string sessionMode)`.
  Read-only `EngineeringRoot`, `Owner`, `ContainerKind`, `SessionMode`,
  `IReadOnlyList<ProjectCapabilityInfo> CapabilitySet`, `MultiuserRemoteIdentity? RemoteIdentity`, and
  `ProjectServerConnectionObservation? ConnectionObservation`.

- [ ] **Step 1: Write the failing owner/context tests.**

  `StandaloneContext_PreservesRootAndOwner` asserts `Assert.Same(project, context.EngineeringRoot)`,
  the same concrete owner handle, `standaloneProject`, `notApplicable`, and the supplied ownership.
  `LocalContext_PreservesSessionOwnerWithoutMutation` asserts the same `localSession.Project` root,
  `localSession`/`unknown`, supplied ownership, and zero Save/Close/CloseAndCommit calls.
  `ServerContext_RequiresLocalOwner` asserts `serverProject` uses `LocalSessionOwner`.
  Both factories expose an empty capability set and null remote identity/observation.
  Null owner/root inputs and invalid container/mode combinations throw `ArgumentException` (or
  its `ArgumentNullException` subtype), without a mutation call. Valid local/server modes are
  `multiuser`, `exclusive`, or `unknown`; `notApplicable` is standalone-only. Names ending `_LS`
  or `_ES` do not turn an explicitly unknown mode into another mode.

  A minimal assertion example (using the existing test-double `Project`) is:

  ```csharp
  var project = new Siemens.Engineering.Project();
  var context = ActiveProjectContext.ForStandalone(project, openedByWorker: false);
  Assert.Same(project, context.EngineeringRoot);
  Assert.Same(project, Assert.IsType<StandaloneProjectOwner>(context.Owner).Project);
  Assert.Equal(ProjectContainerKinds.StandaloneProject, context.ContainerKind);
  Assert.Equal(MultiuserSessionModes.NotApplicable, context.SessionMode);
  Assert.Empty(context.CapabilitySet);
  Assert.Null(context.ConnectionObservation);
  ```

  Extend the existing double to `public sealed partial class Project : ProjectBase`; retain its
  existing device/group behavior. Add a minimal `ProjectBase` with configurable `Path`, `IsModified`,
  and read failures, plus `MultiuserProject` and `LocalSession` boundary doubles. Never add a Siemens
  assembly reference to the test project or execute `ref/` DLLs. Source-link only the four new
  production files in this task.

- [ ] **Step 2: Run the focused test and observe RED.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter "FullyQualifiedName~ActiveProjectContextTests" -- xUnit.ParallelizeTestCollections=false
  ```

  Expected: compilation fails because the production context/owner types are absent.

- [ ] **Step 3: Implement the four internal types with the interfaces above.**

  Use PR 1 vocabulary constants. Derive the engineering root from the typed owner so independent
  root and owner arguments cannot disagree. Retain handles; do not open, enumerate server sessions,
  infer modes, observe connectivity, or invoke lifecycle methods. Document the capability field's
  unpopulated status and that these types provide no authorization.

- [ ] **Step 4: Run context tests, linked-reader regressions, and the worker stub build.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter "FullyQualifiedName~ActiveProjectContextTests|FullyQualifiedName~ProjectDeviceEnumeratorTests|FullyQualifiedName~ProjectTreeWorkerProducerContractTests|FullyQualifiedName~TagOperationSafetyReaderTests" -- xUnit.ParallelizeTestCollections=false
  dotnet build TiaMcpServer.OpennessWorker/TiaMcpServer.OpennessWorker.csproj -m:1 -c Release /p:UseTiaPortalReferenceStubs=true
  ```

  Expected: PASS without changing reference source/artifacts or invoking a local-session API.

- [ ] **Step 5: Commit this independently testable foundation.**

  Stage only this task's files, run `git diff --cached --check`, and commit
  `refactor(worker): add typed active project context`.

## Task 2: Move the Central Session Seam to the Context

**Files:** Modify `Openness/TiaPortalSession.cs`, both doubles files, and the test project; create
`Worker/ActiveProjectContextSessionTests.cs`. Update
`Worker/PortalSelectionSourceTests.cs` and `Worker/TiaPortalSessionBindingGuidanceSourceTests.cs`
where their assertions name the replaced private fields.

**Interfaces:** Consumes Task 1 factories/owners. Produces:

- `internal ActiveProjectContext? ActiveContext { get; }` and
  `internal ProjectBase? EngineeringRoot { get; }` on `TiaPortalSession`.
- Existing `public Project? Project { get; }` becomes a read-only compatibility projection from
  `StandaloneProjectOwner`; it has no setter or separate backing field.
- `internal void AdoptContext(ActiveProjectContext context, string? expectedProjectPath)` validates
  the actual root path before selecting the context; it performs no lifecycle operation.
- Existing `AdoptProject(Project, bool, string?)` delegates to the standalone factory and
  `AdoptContext`. Existing identity/open/create/close/SaveAs entry-point signatures remain unchanged.

- [ ] **Step 1: Link and test the real session before changing its implementation.**

  Source-link `TiaPortalSession.cs` and `TiaPortalProcessInventory.cs` (the selector, authorization,
  detach guard, close guard, and exception files are already linked; do not add duplicates).
  Extend doubles only for API members those files call: Portal processes/attachment, Projects
  composition/open, process mode/PID/attached clients, lifecycle counters, events, and disposal.
  Use a test collection with `DisableParallelization=true`, restore the static process registry in
  `finally`, and use real temporary absolute `.ap21` files for session opening tests.

  Add these named behavioral tests:

  | Test | Required assertions |
  | --- | --- |
  | `SameRootAdoption_DoesNotAdvanceGeneration` | Fresh wrappers for the same root preserve generation; adoption sets `OpenedByWorker=false` |
  | `DifferentRootAtSamePath_AdvancesGeneration` | Replacement root at the identical canonical path changes generation and rejects the old expected identity |
  | `SaveAsOnSameRoot_AdvancesGenerationOnce` | `AcceptCurrentProjectIdentity` adopts the changed path once; another acceptance does not advance it |
  | `UnreadableOrBlankPath_ClearsContextWithoutFallback` | Engineering exception/null/blank path returns null live identity, clears ownership, advances generation once, and does not select another sole project |
  | `ExternalPathChange_RejectsOldIdentity` | `EnsureConnected` rejects the changed path with `binding_conflict` before the operation body |
  | `ExactSelection_RejectsMissingAndAmbiguousTargetsBeforeDetach` | `target_not_found`/`target_ambiguous`; original context, PID, generation, and lifecycle counters unchanged |
  | `CleanWorkerOwnedOpen_ClosesThenOpens` | Call order is modified-state read → Close → Open; replacement ownership is true |
  | `DirtyOrUnreadableWorkerOwnedOpen_DoesNotCloseOrOpen` | Dirty source gives existing `state_changed`; read failure gives `worker_operation_failed`; source context remains |
  | `CloseFailure_DoesNotOpenReplacement` | Existing failure behavior preserved; source handle/ownership remain until live identity can be inspected |
  | `UiOwnedOpen_LeavesSourceOpen` | No source Close/Save; destination is opened and worker-owned |
  | `SwitchAndReattach_DoesNotRestoreOwnership` | Exact bind performs no Open/Save/Close, former UI project remains open, destination ownership is false |
  | `HeadlessLastClient_UnknownOrModifiedStateBlocksDetach` | All attached projects are inspected; no Dispose/Attach when any state is dirty/unreadable |
  | `AttachFailureAfterDetach_CannotReturnFormerIdentity` | No stale source context/PID is certified after Dispose/Attach failure |
  | `DisconnectAndDispose_ReleaseWithoutLifecycleCalls` | Standalone and synthetic local/server contexts: events removed before Portal disposal; context cleared; no explicit Save/Close/CloseAndCommit; repeated Dispose is harmless |

  Record generation before/after each transition. Assert exact increments for same-handle path
  transitions and handle replacement; retain existing multi-transition attach/detach increments
  rather than inventing a new numerical convention.

- [ ] **Step 2: Run the new tests and observe RED.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter "FullyQualifiedName~ActiveProjectContextSessionTests" -- xUnit.ParallelizeTestCollections=false
  ```

  Expected: missing `ActiveContext`/`AdoptContext` compilation failures, then meaningful behavioral
  failures while the old fields still own session state.

- [ ] **Step 3: Replace `_project` and `_projectOpenedByWorker` with one context field.**

  Migrate live path reads, modified-state reads, source-rebind probes, explicit adoption,
  worker-open tracking, identity acceptance, close marking, invalidation, disconnect, and disposed
  callbacks. Compare engineering-root reference identity, not context-wrapper identity, for
  generation changes. Preserve `_selectedProjectPath` as the retained exact-selection assertion:
  stale live handles cannot certify it, but explicit rescans still use it instead of another project.
  Keep worker ID/PID/generation in the session and project ownership in the context's owner.

  Only standalone factories are reachable from existing production selection/open/create paths.
  Preserve lookup-before-transition, read-only confirmations, all-project detach inspection, and
  event unsubscription order. Update source assertions to test these boundaries using the new field
  names; do not remove their behavioral protections.

- [ ] **Step 4: Run session/selection regressions and rebuild the worker.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter "FullyQualifiedName~ActiveProjectContext|FullyQualifiedName~PortalSelectionSourceTests|FullyQualifiedName~TiaPortalSessionBindingGuidanceSourceTests|FullyQualifiedName~TiaPortalTargetSelectorTests|FullyQualifiedName~PortalDetachGuardTests|FullyQualifiedName~ProjectRebindCloseGuardTests" -- xUnit.ParallelizeTestCollections=false
  dotnet build TiaMcpServer.OpennessWorker/TiaMcpServer.OpennessWorker.csproj -m:1 -c Release /p:UseTiaPortalReferenceStubs=true
  ```

  Expected: PASS; deterministic doubles verify production session code, not a copied state machine.

- [ ] **Step 5: Commit.**

  Stage this task's files, check the staged diff, and commit
  `refactor(worker): migrate portal session to active context`.

## Task 3: Make Standalone Dispatch and Lifecycle Ownership Explicit

**Files:** Modify `Openness/TiaPortalSession.cs`, `Openness/ProjectLifecycleService.cs`, and
`Program.cs`; create `Worker/ActiveProjectContextSourceTests.cs`; extend context session tests.
Update `Worker/LifecycleSecondIdentityValidationContractTests.cs`,
`Worker/WorkerOpenPolicySourceTests.cs`, and `Worker/PortalSelectionSourceTests.cs` only for wiring
changes that their assertions must follow.

**Interfaces:** `internal StandaloneProjectOwner RequireStandaloneOwner()` on `TiaPortalSession`
returns the active standalone owner. An absent context retains existing no-project behavior;
a local/server context throws `WorkerOperationException` with existing `target_kind_unsupported`.
No new failure category is registered. `WithProject` remains `Func<Project, WorkerResponse>`.

- [ ] **Step 1: Write the failing owner-boundary and source-wiring tests.**

  In executable session tests, internally adopt a local/server context and assert
  `RequireStandaloneOwner` rejects it with `target_kind_unsupported`, while the root remains bound
  and local-session Save/Close/CloseAndCommit counters stay zero. Standalone resolution returns the
  exact owner handle without a generation change.

  `LocalOwner_GenericReplacementIsRefused` asserts that generic open and source-rebind planning
  reject a synthetic local/server source with `target_kind_unsupported`, before any standalone
  Open/Close, preserve the original context, and leave local-session lifecycle counters at zero.
  Create's source check is pinned before the first Siemens Create call by the source-wiring test.

  Source tests assert status/lifecycle resolution and `Program` project dispatch resolve the
  standalone owner, identity validation still precedes the engineering body, and only the existing
  explicit standalone open/create routes obtain handles from `TiaPortal.Projects`. No production
  caller invokes the local-session context factory. No new code calls `LocalSessions.Open`,
  `OpenServerProject`, `LocalSession.Save`, `LocalSession.Close`, or `CloseAndCommit`.

- [ ] **Step 2: Run the focused tests and observe RED.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter "FullyQualifiedName~ActiveProjectContext" -- xUnit.ParallelizeTestCollections=false
  ```

  Expected: missing owner resolver or failed standalone wiring assertions.

- [ ] **Step 3: Route current lifecycle/content boundaries through the standalone owner.**

  Keep `ProjectLifecycleService.EnsureProject` returning concrete `Project`, but obtain it from
  `RequireStandaloneOwner().Project` after the existing connection/path policy. Status reads return
  unopened status only when there is no context; reject a local owner instead of treating it as an
  absent standalone project. Preserve full vs basic status metadata behavior.

  In `Program.WithProject` and direct project consumers (including subnet/probe paths), resolve the
  standalone owner before calling existing `Project` services. Preserve both expected-identity
  checks around connection/resolution and each lifecycle check immediately before Save, SaveAs,
  Archive, and Close. SaveAs still accepts the new path; close still marks the context closed only
  after successful `Project.Close`. Rebind closing still uses `ProjectRebindCloseGuard` and only a
  standalone owner. `TiaPortalSession.OpenProject` and `ReadProjectRebindState` reject an active
  local/server owner before planning or replacing it, including a clean worker-owned local source.
  `CreateProject` likewise checks any existing source owner before calling Siemens Create. These
  internal defensive checks prevent future context activation from acquiring implicit terminal
  authority. Do not add lifecycle methods to `LocalSessionOwner`.

- [ ] **Step 4: Run the wiring and lifecycle regressions.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter "FullyQualifiedName~ActiveProjectContext|FullyQualifiedName~LifecycleSecondIdentityValidationContractTests|FullyQualifiedName~WorkerOpenPolicySourceTests|FullyQualifiedName~PortalSelectionSourceTests|FullyQualifiedName~LifecycleIdentityContinuityTests|FullyQualifiedName~ProjectRebindStatePayloadContractTests" -- xUnit.ParallelizeTestCollections=false
  dotnet build TiaMcpServer.OpennessWorker/TiaMcpServer.OpennessWorker.csproj -m:1 -c Release /p:UseTiaPortalReferenceStubs=true
  ```

  Expected: PASS with unchanged worker/public payload shapes and unchanged content-service signatures.

- [ ] **Step 5: Commit.**

  Stage this task's files, check the staged diff, and commit
  `refactor(worker): resolve standalone lifecycle owners explicitly`.

## Task 4: Pin Host Binding, Protocol, Confirmation, and Audit Continuity

**Files:** Extend `TiaMcpServer.Tests/Tools/BindProjectToolProtocolTests.cs`,
`TiaMcpServer.Tests/Tools/LifecycleMcpProtocolTests.cs`,
`TiaMcpServer.Tests/Project/ProjectSessionBindingTests.cs`,
`TiaMcpServer.Tests/Project/LifecycleBindingStrategyTests.cs`, and
`TiaMcpServer.Tests/Safety/Pipeline/WriteConfirmationPipelineTests.cs` only where the cases below
are not already covered.

**Interfaces:** Consumes unchanged production `WorkerSessionIdentity`, `ProjectBindingSnapshot`,
`LifecycleBindingStrategy`, `LifecycleWriteResponse`, and the existing MCP/FakeWorker fixtures.
Produces regression evidence only; no new host production interface or FakeWorker Multiuser path.

- [ ] **Step 1: Check the existing coverage and add only missing assertions.**

  Retain these existing tests and extend them where needed:

  - `TryAdoptVerified_SameIdentity_NoRevisionBump`: same identity preserves ID/revision; changed
    generation at the same path produces a fresh epoch and invalidates old cursors.
  - `BindProject_RejectsRelativeOrNonAp21Path`: include `C:/Projects/A.als21`; rejection is
    `validation_error` before transport. Startup-selector behavior remains unchanged.
  - Explicit bind in all three modes: non-null previous/current binding and candidate inventory,
    matching configured-selector promotion only, no Open/Save/Close dispatch, conflict without
    force, exact selection with force, and source preservation on pre-detach refusal.
  - `EveryLifecycleDryRun_PreservesSourceAndFilesystem`: each of the six tools reports `preview`,
    causes no mutation/elicitation, and emits exactly one audit v2 record.
  - Mode confirmation: actual read-write lifecycle calls ask exactly once even with no acknowledge
    guard; decline/cancel/missing form/false confirm deny with `access_denied`; full uses policy.
  - Confirmation-time state changes: retain
    `StateChangedWhilePromptWasOpen_IsRefusedWithoutMutation` and
    `MutablePlanStateChangedDuringPrompt_IsComparedToTheOriginalSnapshot` in
    `WriteConfirmationPipelineTests`. Their existing `duringPrompt` callback changes binding,
    effects, preconditions, or guards before acceptance returns. Assert `binding_conflict`, zero
    mutations, one prompt, and exactly one audit record; Task 2 separately exercises live-handle
    generation changes in the actual session source. Do not invent a new FakeWorker control API.
  - Typed outcomes: rejected calls have top-level error/null result/`isError:true`; attempted
    mutation or verification failures have `success:false`, `error:null`, `isError:false`, and
    typed failure evidence. Text JSON equals structured content; audit response hash matches.

  Run existing focused tests first. Add a failing assertion only for a demonstrated coverage gap;
  do not manufacture RED by removing working behavior or duplicate the existing protocol suite.

- [ ] **Step 2: Run the affected regression groups.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter "FullyQualifiedName~BindProjectToolProtocolTests|FullyQualifiedName~BindOpenProjectIntegrationTests|FullyQualifiedName~ProjectSessionBindingTests|FullyQualifiedName~ProjectBindingPayloadContractTests|FullyQualifiedName~LifecycleBindingStrategyTests|FullyQualifiedName~LifecycleMcpProtocolTests|FullyQualifiedName~ModeDerivedConfirmationTests|FullyQualifiedName~WriteConfirmationPipelineTests|FullyQualifiedName~ToolOutputContractConformanceTests|FullyQualifiedName~WriteSafetyLeaseConcurrencyTests" -- xUnit.ParallelizeTestCollections=false
  ```

  Expected: PASS with 5/15/15 discovery counts, unchanged schemas/null policy, and no host production
  changes. Source-linked session tests from Task 2 establish the worker half that FakeWorker cannot.

- [ ] **Step 3: Commit any added regression coverage.**

  Stage only changed test files, check the staged diff, and commit
  `test: pin active context binding and lifecycle continuity`. If no coverage gap exists, record the
  focused evidence without an empty commit.

## Task 5: Document the Internal Boundary and Run Offline/Reference Gates

**Files:** Modify `docs/ARCHITECTURE.md`, `docs/IMPROVEMENT_LOG.md`, and `docs/README.md`.
Create `docs/SupportedOperations/MULTIUSER_OPERATIONS_SUMMARY.md`; index it in `docs/README.md`.
No README/public startup/tool-list change is required.

**Interfaces:** Documentation describes the delivered internal context and explicitly undelivered
Multiuser surface. Qualification commands produce separate offline, package, and real-reference evidence.

- [ ] **Step 1: Update maintained documentation for the actual PR 2 result.**

  Architecture: common root/typed owners, authoritative session identity, standalone bridge,
  ownership transitions, and cleanup prohibition. Multiuser summary: no public Multiuser tool,
  `.als21` selection/open, content compatibility, local save, discard, or commit is delivered;
  internal metadata is not an advertised capability matrix. Improvement log: record the context
  migration and keep the current-PR live gate open until Task 6 passes. Link the summary and report
  when created; distinguish internal preparation from Issue #65 completion.

- [ ] **Step 2: Restore, verify unchanged references, and build serially.**

  ```powershell
  dotnet restore TiaMcpServer.slnx -m:1
  dotnet restore reference-stubs/TiaMcpServer.ReferenceStubs.sln -m:1
  pwsh -NoProfile -File scripts/verify-reference-stubs.ps1 -Configuration Release -NoRestore
  dotnet build TiaMcpServer.slnx -m:1 --no-restore -c Release /p:UseTiaPortalReferenceStubs=true
  ```

  Expected: PASS; no tracked reference drift or reference update.

- [ ] **Step 3: Run the full offline suite once with the coverage gate.**

  Use a fresh task-specific results directory so historical reports cannot satisfy this run.

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build -c Release --collect:"XPlat Code Coverage" --settings TiaMcpServer.Tests/coverage.runsettings --results-directory TestResults/multiuser-pr2 -- xUnit.ParallelizeTestCollections=false xUnit.ParallelizeAssembly=false xUnit.MaxParallelThreads=1 RunConfiguration.MaxCpuCount=1
  $pr2CoverageReports = @(Get-ChildItem -Path TestResults/multiuser-pr2 -Recurse -Filter coverage.cobertura.xml)
  if ($pr2CoverageReports.Count -ne 1) { throw "Expected exactly one PR 2 coverage report." }
  pwsh -NoProfile -File scripts/verify-coverage-threshold.ps1 -CoveragePath $pr2CoverageReports[0].FullName -MinimumLineRate 0.80
  ```

  Expected: full suite PASS and line coverage ≥ 0.80. Do not weaken serialization or coverage to
  bypass a failure. Repeat only after a relevant code change or unresolved failure.

- [ ] **Step 4: Compile against installed V21 references on the Windows qualification machine.**

  ```powershell
  dotnet build TiaMcpServer.slnx -m:1 --no-restore -c Release /p:UseTiaPortalReferenceStubs=false /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"
  dotnet build reference-stubs/TiaMcpServer.OpennessReferenceProbe/TiaMcpServer.OpennessReferenceProbe.csproj -m:1 -c Release /p:UseTiaPortalReferenceStubs=false /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"
  ```

  Expected: PASS; retain logs showing `UseTiaPortalReferenceStubs=false`. A compile failure or
  unavailable installed-reference build leaves qualification open; do not substitute stub success.

- [ ] **Step 5: Pack and verify Siemens exclusion.**

  ```powershell
  dotnet pack TiaMcpServer/TiaMcpServer.csproj -c Release -m:1 --no-restore /p:UseTiaPortalReferenceStubs=true -o artifacts/multiuser-pr2
  $pr2Packages = @(Get-ChildItem -LiteralPath artifacts/multiuser-pr2 -Filter '*.nupkg')
  if ($pr2Packages.Count -ne 1) { throw "Expected exactly one PR 2 package." }
  pwsh -NoProfile -File scripts/verify-doctor-package.ps1 -PackagePath $pr2Packages[0].FullName
  ```

  Expected: PASS; no `Siemens.Engineering*.dll` in the package/staged worker payload. Task 6 must
  separately identify and hash the exact real-reference host/worker artifacts it actually runs.

- [ ] **Step 6: Review scope, indexes, and commit documentation.**

  `git diff --check` passes; changed files fit this plan; no public contract/catalog or reference
  artifact changed. Verify every new document has a working relative link from `docs/README.md`.
  Commit `docs: describe internal active project context` after the offline gates pass, leaving
  live acceptance explicitly pending.

## Task 6: Qualify the Frozen Candidate Live on Standalone Projects

**Files:** Create `docs/superpowers/acceptance/reports/2026-10-05-multiuser-pr2-active-project-context-live.md`
on execution (use the actual run date if later); update `docs/README.md`,
`docs/superpowers/README.md`, and the maintained acceptance boundary in the Multiuser summary/log.

**Interfaces:** Use the existing six lifecycle tools and `bind_project`; no new test operation.
Follow the evidence structure of the
[October 3 lifecycle/binding report](../acceptance/reports/2026-10-03-lifecycle-tiers-bind-project-live-validation.md).
The exact requests below form the reviewable live scope; running them requires fresh fixture authorization.

- [ ] **Step 1: Freeze and record the candidate and proposed fixtures before requesting live authorization.**

  Record candidate SHA, clean source status, real-reference build logs, installed host/worker hashes,
  TIA Portal V21 version, two disposable UI `.ap21` projects A/B in distinct Portals, an isolated
  headless `.ap21` fixture H, exact scratch destinations for create/SaveAs/archive, and permitted
  temporary edit/save/discard/cleanup actions. Project Server is not required for PR 2. Prepare the
  report/matrix and exact fixture scope first; do not acquire authorization for unnamed projects.

- [ ] **Step 2: Run the read-only selection group serially.**

  No-path bind with A/B is ambiguous. Exact A bind succeeds, repeat preserves the epoch, B without
  force conflicts, B with force succeeds, return to A does not revive an old tree cursor. Capture
  previous/current binding and candidates. A/B remain open, with no saves or lifecycle operations.
  Matching configured startup assertions can be verified; divergent ordinary reads cannot select
  B or implicitly open a file. All six lifecycle tools remain unavailable in read-only. Record
  Openness access-dialog observation separately from lifecycle elicitation.

- [ ] **Step 3: Run read-write guarded lifecycle and ownership cases serially.**

  Each of open/create/save/SaveAs/archive/close receives a dry run and an authorized actual call on
  scratch fixtures. Dry runs preserve source/destinations and elicit nothing. Actual calls ask
  once, including a clean save/open without an acknowledge guard. Decline/cancel preserve state.
  Opening alongside a UI-owned source preserves it; switching from a clean worker-owned source
  closes only that source; a dirty worker-owned source blocks the switch. SaveAs verifies the new
  path/epoch; an old cursor fails; close verifies no active project. Explicitly rebind after detach
  and prove prior worker ownership is not regained. Do not infer ownership from a matching path.

- [ ] **Step 4: Run full-mode and adverse-state cases serially.**

  Repeat the six lifecycle previews/applies in full without server elicitation; policy satisfies
  acknowledge guards, while blocks remain effective. Include exact-target dirty close/discard,
  destination collision, archive-inside-project block, and archive without save on a dirty fixture
  to capture its actual typed outcome. Use H to show a modified last-client headless detach is
  blocked; then clean/save it explicitly under authorized scope before releasing it. UI close/reopen
  at the same path and UI SaveAs invalidate earlier binding evidence. Inspect after any unexpected
  failure; never force a crash or replay a possibly executed mutation to obtain a passing capture.

- [ ] **Step 5: Match response contracts, audits, and restoration evidence.**

  For each lifecycle call, decoded text equals structured content, typed result/verification and
  phase match actual state, and exactly one audit v2 record matches the response hash and mode.
  Confirmation/guard satisfaction is `user`, `policy`, or `none`/null as applicable. Record frozen
  SHA/versions, exact authorized fixture paths, sanitized request/response, before/after inventories,
  expected/observed postconditions, persistence/file evidence, and final cleanup/recovery state.
  Preserve failed attempts and limitations. No Project Server revision/session evidence is claimed.

- [ ] **Step 6: Close only the qualified PR 2 gate and obtain independent review.**

  Mark live acceptance complete only if every required case passed on the identified candidate.
  Link the report from both indexes and maintained docs. Any source fix creates a new candidate;
  rerun affected qualification and record which earlier evidence still applies. Commit
  `docs: record active context standalone live acceptance`. Request independent review focused on
  the five risks above and the full production diff before proposing integration. PR 3 planning
  follows merged, accepted PR 2; Issue #65 remains open because `.als21` is still undelivered.

## Execution Preflight and Stop Conditions

- [ ] Read this plan, the spec, `AGENTS.md`, and current maintained architecture together.
- [ ] Reconcile against the latest merged baseline and confirm PR 1 qualification/review evidence
  was accepted. Do not mechanically execute this plan if predecessor assumptions changed.
- [ ] Use an isolated execution worktree when implementation is authorized; do not create one just
  to write/review this document. Preserve the user's chosen execution method.
- Stop and revise the plan if a step requires public identity/schema changes, `.als21` adoption/open,
  a new operation/failure category, an advertised compatibility matrix, or content-service migration.
- Stop if a local-session owner gains an implicit terminal action or worker shutdown is reported as
  a successful save/discard/commit. Detaching may release Portal resources; it cannot claim a session
  lifecycle postcondition without the later explicit operation and its verification.
- Stop if preserving standalone identity/ownership requires weakening fresh checks, pinned leases,
  confirmation, typed payload readers, audit, cursor validation, or existing safety guards.
- Unavailable Windows/V21 fixtures or missing headless coverage leave the live gate open; they do
  not prevent preparing/reviewing the internal implementation but do prevent merge qualification.
- A timeout, crash, disconnect, or possible mutation stops the live run for inspection. Resume only
  after restoration or a fresh exact-target authorization; never automatically retry.

## Completion Evidence and Handoff

The PR 2 requirements map to the tasks as follows:

| Spec requirement | Owning tasks |
| --- | --- |
| Typed common root and lifecycle owners | 1–3 |
| Preserve standalone selection, configured promotion, and ownership | 2–4 |
| Preserve worker identity and host binding/cursor continuity | 2–4 |
| No new Multiuser operation or implicit terminal lifecycle action | 1, 3, 5 |
| Preserve guarded lifecycle, typed outcomes, and audit | 3–4, 6 |
| Full offline suite and real-reference compilation | 5 |
| Current-PR live `.ap21` selection/switching/lifecycle evidence | 6 |
| Maintained documentation and accepted serial handoff | 5–6 |

PR 2 is merge-ready only with the internal context/owner migration, preserved standalone protocol
and binding behavior, passing full offline/coverage/package/reference gates, current-candidate live
`.ap21` evidence, indexed maintained documentation, and accepted independent review. The public
Multiuser surface remains undelivered.

Before implementation, review this plan and select **Native** or **Subagent-driven** execution.
Native is recommended for this PR because Tasks 1–3 change the same session/owner interfaces and
share a tightly coupled boundary-double fixture; one implementer can preserve that continuity,
followed by independent whole-branch review.
