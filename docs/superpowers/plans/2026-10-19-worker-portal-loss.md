# Worker Portal-loss Handling (B-16 and Q3) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `portal_lost` the shared worker-boundary category for `NonRecoverableException`, and stop ordinary Network recovery paths from absorbing that exception.

**Architecture:** Keep exception-to-response mapping in `Program.Execute` and reuse the host's existing binding invalidation. Add one small exception filter for broad recovery catches, preserving each domain's existing ordinary-failure policy and typed mutation outcomes. Remove the redundant VCI read/plan mappings; retain the VCI writer's outcome-bearing mapping.

**Tech Stack:** C#, net48 Openness worker, net10.0 host/FakeWorker/xUnit tests, source-linked Siemens test doubles and compile-time reference stubs. No new dependency.

**Spec:** [Agreed simplification review](2026-10-17-codebase-simplification-review.md), decision Q3 and workstream B item 16; [findings appendix](2026-10-17-codebase-simplification-review-findings.md), WORKERCORE-4, WORKERNET-7 and the verifier-corrected WORKERDOMAINS-16.

**Status:** Implementation proposal, prepared on 2026-10-19 against clean `main` at `6e7791889a3bb9e9b8b4ba46853136b46fd3d0ba`. No implementation, build, test or live TIA operation was performed while writing this plan. Review the plan before execution. Recommended execution: native, sequential implementation; the tasks share exception policy and test doubles.

## Global Constraints

- Windows and TIA Portal V21; Siemens calls stay in the net48 worker.
- Build the solution with `-m:1`; run build/test processes sequentially.
- Preserve the 8/19/19 tool surface, request/response schemas, failure-category constants and access-mode behavior.
- Preserve ordered execution, stop-at-first-failure, no batch rollback and no automatic replay.
- Preserve `VciWriteOutcome`, `BlockImportOutcome`, `HmiWriteOutcome`, existing mutation warnings and diagnostic sanitization wherever a handler already carries them.
- A returned `portal_lost` invalidates a verified host binding through the existing client path. Recovery requires explicit selection/grounding; no implicit project open or adoption is added.
- Keep B-4's exact selector/evidence rules and its existing strict Network property-read policy. Its completed acceptance does not qualify this change.
- Commit each implementation/fix task after relevant checks. Push, PR creation and live interruption require separate authorization.
- Retain existing postcondition/partial-outcome handling in this scope, as confirmed by the maintainer on 2026-10-19. A broader redesign of write outcomes requires an explicit scope change.

## Design Decisions

1. **One default mapping.** In `Program.Execute`, change the `NonRecoverableException` catch from `WorkerOperationFailed` to `PortalLost`. Keep its other exception categories unchanged. Its guidance must say that the Portal connection was lost, recovery needs an explicit `bind_project`, and an attempted mutation needs inspection before retrying. Do not claim that a failed call made no changes.
2. **A filter is an exclusion, not a new universal recovery policy.** Add `Openness/Infrastructure/OpennessExceptions.cs`, namespace `TiaMcpServer.OpennessWorker.Openness.Infrastructure`, with `internal static bool IsRecoverable(Exception exception) => exception is not NonRecoverableException`. Use it on previously broad recovery catches. Keep the stricter `NetworkOpennessRead.IsRecoverable` as `exception is EngineeringException && OpennessExceptions.IsRecoverable(exception)`. Existing broad metadata/iteration catches must still handle their ordinary `InvalidOperationException` fixtures; HMI's narrower unsupported-property policy stays narrow.
3. **VCI retains write evidence.** Remove the `NonRecoverableException` translation wrappers from `VciDispatch.Plan` and `Read`, allowing the same exception to reach `Execute`. Keep `VciWriter`'s `portal_lost` catch: it supplies counts, `LastAttempted`, persistence notes and recovery guidance that the generic boundary cannot reconstruct.
4. **Fatal inventory failure wins over final validation.** `MultiuserInventoryService.Connections` and `Read` must let `NonRecoverableException` escape. In `MultiuserPortalReadDispatch.Run`, an original fatal body exception must survive the final identity checks. Run the existing final checks for success and ordinary failures, retaining foreign-PID refusal, disposal-only `binding_conflict`, no adoption and no reattachment/replay.
5. **Outcome boundaries are deliberate exceptions.** The shared mapping covers exceptions that reach the execution boundary. Existing typed post-mutation failures, cleanup warnings, completed-operation diagnostic stamping and observation-only probes retain their established contracts. This plan does not claim that every observed disconnect in those paths becomes `portal_lost`.

## Review Focus

| Input or failure mode | Required behavior | Owning task |
| --- | --- | --- |
| Fatal exception during lazy enumeration, `Current`, property access or disposal | Escape broad recovery; stop subsequent work instead of returning partial success | 2 |
| Ordinary metadata/iterator failure or unavailable optional property | Preserve current degraded-read/evidence behavior; do not broaden strict property recovery | 1, 2 |
| Fatal inventory body failure followed by attachment loss | Preserve the original fatal exception and perform no attach/replay; healthy ordinary failures retain their category | 3 |
| VCI or another existing typed mutation failure | Retain partial outcomes, attempted-target evidence, sanitized diagnostics and no-retry warnings | 1, 3, 4 |
| Verified binding and an existing cursor when `portal_lost` arrives | Invalidate binding, reject continuation and require explicit recovery without replaying the failed request | 4 |

## File Map and Scope

Paths below are repository-relative. Tests compile linked production sources; adding a production file does not automatically add it to the test assembly.

| Responsibility | Files |
| --- | --- |
| Shared filter and default failure category | Create `TiaMcpServer.OpennessWorker/Openness/Infrastructure/OpennessExceptions.cs`; modify `TiaMcpServer.OpennessWorker/Program.cs`, `Openness/Network/NetworkOpennessRead.cs`, `Openness/Vci/VciDispatch.cs` |
| Network propagation | Worker `Openness/Network/`: `HardwareDiscoveryEvidenceCapture.cs`, `HardwareConfigReader.cs`, `NetworkObjectSelectorResolver.cs`, `NetworkNodeReadSelectorBuilder.cs`, `NetworkConnectionEvidenceCapture.cs`, `CommunicationConnectionReader.cs`, `NetworkAttributeMetadataProcessor.cs`, `NetworkAttributeResultBuilder.cs`, `NetworkMutationVerifier.cs`, `NetworkDeviceCreator.cs` |
| Other best-effort reads and inventory | Worker `Openness/Multiuser/MultiuserInventoryService.cs`, `MultiuserPortalReadDispatch.cs`; `Openness/Block/BlockExporterXmlBundle.cs`; `Openness/Hmi/HmiSoftwareLocator.cs` |
| Offline test wiring and doubles | `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`; `TestUtilities/TagSafetySiemensDoubles.cs`, `NetworkSelectorSiemensDoubles.cs`, `MultiuserInventorySiemensDoubles.cs`, `PortalSessionSiemensDoubles.cs` as required by the listed tests |
| Host regression fixtures | `TiaMcpServer.Tests/Worker/OpennessWorkerClientIntegrationTests.cs`, `Multiuser/ProjectBindingInspectionClientTests.cs`; reuse `TiaMcpServer.FakeWorker/Program.cs`'s `portal-lost` scenario and request logging |
| Documentation after implementation | `AGENTS.md`, `docs/ARCHITECTURE.md`, `docs/guides/troubleshooting.md`, `docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md`, `MULTIUSER_OPERATIONS_SUMMARY.md`, `VCI_OPERATIONS_SUMMARY.md`, the original review and documentation indexes |

The host's `OpennessWorkerClient.cs` already invalidates on `BindingConflict` or `PortalLost`; no host production edit is planned. B-5's binding-gate category flattening, probe retirement, HMI cache retention, transport lifetime and unrelated simplification items remain separate. Do not turn this into a worker-wide catch rewrite.

## Task 1: Establish the Exception Policy and Central Mapping

**Files:** Shared-filter/default-category row above; `TiaMcpServer.Tests/TestUtilities/TagSafetySiemensDoubles.cs`; test csproj; create `TiaMcpServer.Tests/Worker/OpennessExceptionsTests.cs`; modify `Network/NetworkOpennessReadTests.cs`, `Vci/VciPlannerTests.cs`, `Vci/VciReaderTests.cs`.

**Interfaces:** Consumes `WorkerFailureCategories.PortalLost` and `NonRecoverableException`. Produces `OpennessExceptions.IsRecoverable(Exception) : bool`; retains the existing stricter `NetworkOpennessRead.IsRecoverable(Exception) : bool` signature.

- [ ] **Step 1: Align the executable double and add failing policy tests.** The reference stub declares `NonRecoverableException : System.Exception` at `reference-stubs/Siemens.Engineering.Base/Core.cs:164`. Match that base type in `TagSafetySiemensDoubles.cs` and replace its synthetic-inheritance comment. Pin the distinction:

  ```csharp
  Assert.False(typeof(EngineeringException).IsAssignableFrom(typeof(NonRecoverableException)));
  Assert.False(OpennessExceptions.IsRecoverable(new NonRecoverableException("closed")));
  Assert.True(OpennessExceptions.IsRecoverable(new EngineeringException("ordinary")));
  Assert.True(OpennessExceptions.IsRecoverable(new InvalidOperationException("metadata failure")));
  Assert.False(NetworkOpennessRead.IsRecoverable(new InvalidOperationException("programming failure")));
  ```

  Update VCI read/plan tests to assert that the original fatal exception escapes `VciDispatch`, replacing their dispatch-local `WorkerOperationException` expectation. Retain the existing VCI writer tests unchanged as a regression gate.
- [ ] **Step 2: Observe red.** Run `dotnet test TiaMcpServer.Tests -m:1 --filter "FullyQualifiedName~OpennessExceptionsTests|FullyQualifiedName~NetworkOpennessReadTests|FullyQualifiedName~VciPlannerTests|FullyQualifiedName~VciReaderTests|FullyQualifiedName~VciWriterTests"`. Initially expect the missing helper, then the old VCI translation expectations to fail until implementation. Record any behavior changes exposed by correcting the test-double hierarchy; do not weaken assertions to hide them.
- [ ] **Step 3: Implement the small production change.** Add and source-link the filter, compose the existing strict Network predicate with it, change the central catch category/guidance and remove only the VCI read/plan translation wrappers. Leave `VciWriter`'s outcome-bearing catch intact. Keep `Program.Execute` in place; no new execution framework or exception wrapper.
- [ ] **Step 4: Observe green and compile the worker.** Re-run the focused command, then `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true`. The default mapper is a small direct change in net48 `Program`; source-linked tests and FakeWorker do not execute that entry point. Real boundary execution is an explicit live-gate requirement below.
- [ ] **Step 5: Review and commit.** `fix: centralize Portal loss failure handling`.

## Task 2: Let Fatal Exceptions Escape Network Recovery

**Files:** Network propagation row above; tests `Network/HardwareDiscoveryEvidenceTests.cs`, `NetworkConnectionEvidenceTests.cs`, `NetworkIdentityResolverTests.cs`, `NetworkQualifiedNodeIdentityTests.cs`, `NetworkAttributeMetadataProcessorTests.cs`, `NetworkAttributeResultBuilderTests.cs`, `NetworkMutationVerificationTests.cs`, `NetworkDeviceCreatorWorkerContractTests.cs`; test csproj/doubles only where executable coverage requires them.

**Interfaces:** Consumes Task 1's shared filter. Existing discovery, selector, attribute and verification return types stay unchanged.

- [ ] **Step 1: Add focused fatal-exception cases to existing executable seams.** Inject one `NonRecoverableException` instance at acquisition, `GetEnumerator`, `MoveNext`, `Current`, metadata property, attribute value and iterator-disposal boundaries. Assert `Assert.Same(failure, Assert.Throws<NonRecoverableException>(action))` and assert that a following callback/candidate was not visited. Cover node-owner/root/node traversal, connection identity capture, modeled/dynamic value reads and both XML-independent Network verification catch shapes. Preserve existing ordinary-failure tests alongside each fatal case.
- [ ] **Step 2: Observe red.** Run `dotnet test TiaMcpServer.Tests -m:1 --filter "FullyQualifiedName~HardwareDiscoveryEvidenceTests|FullyQualifiedName~NetworkConnectionEvidenceTests|FullyQualifiedName~NetworkIdentityResolverTests|FullyQualifiedName~NetworkQualifiedNodeIdentityTests|FullyQualifiedName~NetworkAttributeMetadataProcessorTests|FullyQualifiedName~NetworkAttributeResultBuilderTests|FullyQualifiedName~NetworkMutationVerificationTests|FullyQualifiedName~NetworkDeviceCreatorWorkerContractTests"`. Failures must demonstrate swallowed/reclassified fatal exceptions, rather than fixture setup failures.
- [ ] **Step 3: Filter the broad recovery catches.** Use `when (OpennessExceptions.IsRecoverable(exception))` at the listed catch sites. Fully qualify this helper where `using static NetworkOpennessRead` would otherwise select the stricter predicate. Preserve existing narrower filters. In `NetworkDeviceCreator.Create`, also exclude fatal loss from the catch that wraps non-`EngineeringException` failures in `InvalidOperationException`.

  Apply the same rule to both `NetworkMutationVerifier` catches: actual Portal loss must escape instead of becoming an unverified check. Ordinary verification failures retain their current evidence behavior. A mutation may already have happened; prior batch-item results remain valid, and the current item's completion must not be inferred or replayed.
- [ ] **Step 4: Verify the production/test wiring and observe green.** Re-run the focused command and serial stub build. `NetworkMutationVerifier`, `HardwareConfigReader` and `NetworkDeviceCreator` are not currently source-linked into the test project. Exercise their already linked dependencies/callback seams and compile their production call sites; do not duplicate their bodies in tests or claim those tests executed the full classes. If a new branch needs a seam, move only that small callback-based operation to an existing linked helper. Retain the full-class runtime limitation in the validation record.
- [ ] **Step 5: Review and commit.** `fix: propagate Portal loss through Network recovery paths`.

## Task 3: Preserve Fatal Inventory and Read Failures

**Files:** Other best-effort reads/inventory row above; tests `Multiuser/MultiuserInventoryWorkerTests.cs`, `Multiuser/MultiuserPortalReadDispatchTests.cs`, `Block/BlockExporterSafetyTests.cs`, `Hmi/HmiSoftwareLocatorTests.cs`; existing doubles as needed.

**Interfaces:** Consumes Task 1's filter. Produces unchanged inventory/export/locator results on ordinary failures and propagates the original fatal exception to `Program.Execute`.

- [ ] **Step 1: Add failing regressions.** Cover both `Connections` enumeration and `Read` remote-call loss; a fatal body exception with concurrent Portal disposal; fatal XML export before companion export and fatal companion export after successful XML; a fatal cached-HMI liveness read. Assert original exception identity, no following read/fallback, no attachment/replay and no project/session mutation. Healthy `target_not_found`, ordinary incomplete inventory, XML companion fallback and stale-cache relocation must retain their current behavior.
- [ ] **Step 2: Observe red.** Run `dotnet test TiaMcpServer.Tests -m:1 --filter "FullyQualifiedName~MultiuserInventoryWorkerTests|FullyQualifiedName~MultiuserPortalReadDispatchTests|FullyQualifiedName~BlockExporterSafetyTests|FullyQualifiedName~HmiSoftwareLocatorTests"`.
- [ ] **Step 3: Apply only the propagation fixes.** Filter the inventory, XML-bundle and cached-HMI catches. Refactor `MultiuserPortalReadDispatch.Run` so an original `NonRecoverableException` is rethrown without being replaced by its final checks. Keep final validation on successful and ordinary-failure paths, including existing disposal-only `binding_conflict` behavior. Do not reconnect to validate a dead read.
- [ ] **Step 4: Observe green and verify outcome preservation.** Re-run the focused command plus `dotnet test TiaMcpServer.Tests -m:1 --filter "FullyQualifiedName~VciWriterTests|FullyQualifiedName~BlockImportCoordinatorTests|FullyQualifiedName~BlockMutationPostconditionTests|FullyQualifiedName~BlockUpdateOutcomeDecoratorTests|FullyQualifiedName~BlockExporterVerificationTests"`.

  Retain these explicit boundaries: `BlockMutationService`, `PlcTypePostconditionVerifier`, `BlockImportCoordinator`, `BlockExporterVerification` and existing HMI writer cleanup preserve their typed post-mutation/primary-failure handling. `Program.Stamp` still preserves a completed result when diagnostic stamping fails. `TiaPortalSession`'s existing identity-loss refusals retain `binding_conflict`, which already invalidates host binding. Probe reports retain their diagnostic role. Record these as scope limits, not as universal `portal_lost` coverage.
- [ ] **Step 5: Review and commit.** `fix: preserve fatal Portal exceptions in inventory and reads`.

## Task 4: Verify Host Recovery and Document the Scope

**Files:** Host regression fixture row above; current documentation row; this plan's checkboxes/evidence. No host production change is expected.

**Interfaces:** Uses existing `portal-lost` FakeWorker scenario, `TIA_MCP_FAKE_WORKER_REQUEST_LOG`, `ProjectBindingSnapshot.InvalidatedState`, `WorkerDispatchState.Sent` and the existing explicit-bind recovery APIs.

- [ ] **Step 1: Expand the existing host integration tests.** Parameterize `PortalLost_InvalidatesBindingImmediately` over representative PLC, Network, HMI, compile/lifecycle and inventory worker methods. Reuse the existing request logger to assert exactly one failed method invocation. Check the unchanged `portal_lost` category, `Sent` dispatch state, invalidated binding/revision and continuation refusal. Extend `ProjectBindingInspectionClientTests`' cursor/loss cases rather than adding another fixture framework. Cover explicit re-grounding afterward and preserve an ordinary `worker_operation_failed` control that keeps a healthy binding.
- [ ] **Step 2: Run the focused host gate.** `dotnet test TiaMcpServer.Tests -m:1 --filter "FullyQualifiedName~PortalLost_InvalidatesBindingImmediately|FullyQualifiedName~ProjectBindingInspectionClientTests|FullyQualifiedName~VciWriterTests"`. Existing host invalidation should pass; add only missing fixture behavior if a representative method cannot reach the existing scenario. This is host-contract evidence, not real Openness exception mapping.
- [ ] **Step 3: Complete the offline gate once.** Run the serial stub solution build and `dotnet test TiaMcpServer.Tests -m:1 --no-build` in the same default Debug configuration. Where installed references are available, also run `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=false /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"`. Record totals, warnings, exact candidate and limitations. Do not repeat expensive suites after documentation-only edits.
- [ ] **Step 4: Review the whole diff and update documentation.** Check every changed catch against its ordinary-failure control and the preservation boundaries. Update current docs with the shared mapping, explicit bind/inspection recovery and retained outcome exceptions. Link this implementation plan from B-16/Q3 in the original review and record offline qualification separately from live acceptance. Keep actual fixture paths, endpoint identities and raw diagnostics private.
- [ ] **Step 5: Validate documentation and commit.** Run `git diff --check` and check changed Markdown links. Commit `test: verify Portal loss invalidation and recovery` with the host tests and accurate documentation. Leave live acceptance visibly pending until the gate below passes.

## Live Acceptance Gate — Separate Exact-target Authorization

The agreed Q3 recommendation requires real Portal-loss verification. Writing or approving this implementation plan does not authorize closing/killing a running Portal, opening a project or modifying an engineering session.

Before the live run, freeze the candidate and record the installed host/worker provenance, one disposable standalone fixture A, the precise Portal PID, the read request and the permitted interruption method. Capture baseline binding and a continuation cursor. Use only authorized disposable copies; no plant connection or production project is needed.

| Case | Evidence required |
| --- | --- |
| Healthy baseline | Bound A; successful PLC and Network reads; cursor captured; no fixture change |
| Interruption during an actual read | Interrupt the authorized Portal while a Siemens read is executing. The worker stays alive long enough to return `portal_lost`; host binding invalidates; request log proves no replay |
| Cursor and follow-on request | Old continuation is refused; ordinary content access does not silently rebind/open another project |
| Explicit recovery | Restart/reopen A within the authorized scope, explicitly bind it, verify a fresh identity and perform healthy PLC/Network reads |
| Restoration | Confirm the intended Portal/project state, unchanged disposable fixture contents and cleanup of any test-owned resources |

Choose a read large enough to observe the interruption window; define the bounded attempt count in the live authorization. A worker crash, timeout, an interruption before the Siemens call, or a later `binding_conflict` verifies a different failure path and does not establish the new `NonRecoverableException` mapping. If the timing cannot be reproduced, retain that case as unexecuted rather than forcing a native crash. Multiuser interruption is a separate optional live case requiring its own disposable session/Portal authorization; offline fault injection covers its original-exception preservation.

Completion means the offline gate passes and this scoped live gate is either executed successfully or explicitly dispositioned by the maintainer with its unexecuted limits retained. No claim of exhaustive Portal-loss, post-mutation or probe coverage follows from this plan.
