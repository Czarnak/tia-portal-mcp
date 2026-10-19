# Worker Portal-loss Handling (B-16 and Q3) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `portal_lost` the shared worker-boundary category for `NonRecoverableException`, and stop ordinary Network recovery paths from absorbing that exception.

**Architecture:** Keep exception-to-response mapping in `Program.Execute` and reuse the host's existing binding invalidation. Add one small exception filter for broad recovery catches, preserving each domain's existing ordinary-failure policy and typed mutation outcomes. Remove the redundant VCI read/plan mappings; retain the VCI writer's outcome-bearing mapping.

**Tech Stack:** C#, net48 Openness worker, net10.0 host/FakeWorker/xUnit tests, source-linked Siemens test doubles and compile-time reference stubs. No new dependency.

**Spec:** [Agreed simplification review](2026-10-17-codebase-simplification-review.md), decision Q3 and workstream B item 16; [findings appendix](2026-10-17-codebase-simplification-review-findings.md), WORKERCORE-4, WORKERNET-7 and the verifier-corrected WORKERDOMAINS-16.

**Status:** Initial offline implementation, independent review and qualification completed on 2026-10-19, on `fix/worker-portal-loss-b16-q3` from `6e7791889a3bb9e9b8b4ba46853136b46fd3d0ba`. Authorized interrupted reads of installed candidate `08f4d4a` exposed a disposal-aware mapping gap. The follow-up `e804e6f` is implemented, reviewed and qualified offline. Its separately authorized manual-close Network retest passed `portal_lost` and immediate cursor invalidation on 2026-10-19. Broader live acceptance remains pending with the limits recorded below.

## Implementation Record

The approved plan and index entries were committed as `f6c4f6c` before implementation.
All implementation used native execution in the existing checkout, on the requested
new branch; builds and tests were serialized. Task-local `TEMP`/`TMP` and ignored
logs under `TestResults/portal-loss` kept test artifacts inside the workspace.

| Task | Commit | Fresh evidence |
| --- | --- | --- |
| 1: shared policy and mapping | `da27972` | Baseline 194/194; VCI regression red 9/194, green 196/196; serial stub solution build passed |
| 2: Network propagation | `5b6e8ab` | All 33 new fatal-fault cases red; focused green 351/351; serial stub build passed |
| 3: inventory and read propagation | `d8df0ec` | Eight new cases red; focused and typed-outcome preservation green 192/192 |
| Review fix: interface owner traversal | `27630db` | Four fatal cases red with four ordinary controls passing; focused green 110/110; serial stub build passed |
| 4: host recovery and documentation | This record's commit | Focused host/FakeWorker gate 99/99; full bounded suite 7,043/7,043; worker coverage gate 445/445; stub and installed-V21 solution builds passed |

Implementation adjustments and their limits:

- FakeWorker also source-links two changed Network helpers. It now source-links
  the shared filter and has a minimal exception stand-in, with no Siemens runtime
  assembly dependency. Its existing loss fixture accepts an absolute `.ap21` path
  and uses the existing typed status builder so public explicit-bind recovery can
  be tested.
- The initial 33 Network fatal-fault cases share one `NetworkPortalLossTests` fixture,
  alongside unchanged ordinary-failure suites. Full `HardwareConfigReader`,
  `NetworkDeviceCreator`, `NetworkMutationVerifier`, `NetworkObjectIndexReader` and the net48 `Program` entry
  point remain compile-checked rather than executed by those offline tests.
- Independent review found one P2 defect: the shared interface-path matcher could
  absorb fatal owner traversal before a worker filter saw it. An additive overload
  accepts the worker recovery predicate at all three live callers; the original
  six-argument API and DTO recovery remain intact. Contracts keeps no Siemens
  dependency or JSON schema change. Eight live-selector/control cases and eight
  policy fault cases cover the fix; there were no minor review findings.
- Native PowerShell and an ignored ledger replaced optional Bash workflow helpers
  because of known Windows path/permission limits. This changes bookkeeping only;
  task commands and results are retained in the ledger/logs during execution.
- Existing typed write outcomes, cleanup/stamping boundaries and identity-loss
  `binding_conflict` behavior remain as approved. The installed Portal's fatal-loss
  behavior and restoration are unverified until a separately authorized live run.

### Initial offline qualification

The final production-code commit is `27630db`; the host/FakeWorker regressions
and this documentation are included in this record's commit. The SHA-256 of the
LF-normalized C# and project-file patch against the baseline is
`0484849a129b25e245e178033f1b63f17ed9ba9dbd9052dc9f28774ed3124aab`.
These runs qualify the initial candidate, before the disposal-aware follow-up below.

| Gate | Result and limits |
| --- | --- |
| Serial reference-stub solution build | Passed; seven existing nullable test warnings, zero errors |
| Full Debug suite, `--no-build --no-restore`, `xUnit.MaxParallelThreads=4` | **7,043/7,043 passed**; 7.65 minutes |
| Full suite with repository coverage settings | 7,042 passed, one unchanged snapshot timing test failed at its five-second background-task wait; the full bounded rerun above passed that test in 114 ms |
| Configured host/contract coverage | Nonzero report: 16,004/16,969 lines (94.31%), 8,406/9,689 branches (86.75%). The repository settings exclude worker code |
| Supplemental source-linked worker coverage | **445/445 focused tests passed**. Combined with contract coverage, 55/65 instrumented changed production lines were covered (84.62%). This measures changed lines present in the reports, not all native worker logic |
| Installed V21 references, compile only | Serial solution build with `UseTiaPortalReferenceStubs=false` and the installed V21 `net48` reference directory passed; seven existing nullable test warnings, zero errors. No Portal attachment or operation was performed |
| Documentation and scope | Local Markdown targets and `git diff --check` passed; B-16/Q3 and both documentation indexes link to this record |

The five changed worker files absent from runtime instrumentation are `Program`,
`HardwareConfigReader`, `NetworkDeviceCreator`, `NetworkMutationVerifier` and
`NetworkObjectIndexReader`. Other uncovered changed lines remain in the connection
reader, selector and VCI-dispatch branches. Compilation and doubles do not establish
the native exception mapping, interrupted-read timing or restoration behavior.

An earlier full covered attempt was interrupted after more than eight minutes
without visible progress in minimal logging; a hang was suspected but not proved.
Only that verified offline test process tree was stopped. Its incomplete output is
not passing evidence. The subsequent complete covered run and bounded rerun above
retain the actual results. Logs, TRX files and coverage reports are ignored under
`TestResults/portal-loss`.

All six implementation rulings retain their cost if wrong: use the requested branch
in the existing checkout (shared build outputs); use native PowerShell bookkeeping
(manual ledger maintenance); keep the net48 execution-boundary mapper without an
extracted test seam (native boundary remains unverified); source-link the shared
filter into FakeWorker with an exception stand-in (fixture/type parity); consolidate
Network fault tests while retaining compile-only boundaries (runtime-only faults);
and repair the existing loss fixture's absolute-path/status contract (synthetic
recovery evidence). The independent review's one P2 defect was fixed through a
failing regression and the green whole suite; there were no deferred minor findings.

### Disposal-aware follow-up after installed-candidate testing

The maintainer installed candidate `08f4d4a` and authorized read-only interruption
of disposable standalone fixture A. Interrupted PLC and Network reads, including
a manual Portal close, returned `worker_operation_failed` with a disposal warning
while the worker survived. The first interrupted PLC and Network cases still
served a cached tree continuation before a later native read invalidated binding.
A separate force-stop attempt missed the read window and is not interrupted-read
evidence. Explicit recovery and preservation checks succeeded before the final
manual close; that manual test ended with Portal closed. Direct native
`NonRecoverableException` to `portal_lost` was not observed. This is a failed
live gate for the initial candidate, not acceptance of the follow-up. Raw paths,
process identities, responses and preservation evidence remain ignored under
`TestResults/portal-loss/live-2026-10-19`.

The approved bounded fix records unexpected disposal of the current Portal
attachment and maps an escaping `EngineeringException` to `portal_lost` only if
that observation advances during the execution. The exception boundary moves to
source-linked `WorkerExecution`; `Program.Execute` retains the existing completed
operation stamp. Deliberate detach, stale callbacks, previous disposal and failed
liveness probes do not provide disposal evidence. Each attachment has a distinct
callback subscription, including when a handle is reused or the event sender is
null. The revision read synchronizes with attachment cleanup before publishing
disposal evidence to the exception mapper. No localized message matching,
new native probe, host replay or broader recovery-filter change is added. Returned
typed write outcomes, cleanup warnings and completed results retain their contracts.
An event arriving only after exception classification cannot retrospectively change
that response; live retesting must verify the native event/exception ordering.

Regression evidence before the fix: **8/20 failed**, comprising the actual
session-disposal/engineering-exception case, six IPC invalidation cases and a
cached-cursor case; the 12 existing controls passed. Offline follow-up qualification
also exposed a same-handle late callback false positive (**1/3 failed** before
subscription identity was added). Independent review caught a stale source-contract
test and a cross-thread disposal-publication race; both were corrected, and the
reviewer reported no remaining findings.

| Follow-up gate | Result and limits |
| --- | --- |
| Focused disposal, IPC, cursor and outcome-preservation tests | **175/175 passed** |
| Updated existing source-contract groups | **18/18 passed**; retain guard-before-clear and unsubscribe-before-native-dispose checks |
| Final full Debug suite with source-linked worker coverage, `xUnit.MaxParallelThreads=4` | **7,065/7,065 passed**, zero skipped; 6.80 minutes |
| Changed instrumented worker lines | **72/74 covered (97.30%)**: session 49/51, mapper 23/23. Mapper branch coverage is 100%; the `Program.Execute` bridge remains compile-checked |
| Serial reference-stub solution build | Passed; seven existing nullable test warnings, zero errors |
| Installed V21 references, compile only | Serial solution build passed; seven existing nullable test warnings, zero errors. No Portal attachment or operation |
| Documentation and scope | All 43 local Markdown targets in changed documents resolve; `git diff --check` passed. No new dependency, public schema, tool or host-production change |

A first full follow-up run passed 7,064 tests and failed one existing source-layout
assertion. That assertion now follows `ClearPortal`, with its safety checks retained;
the final whole-suite result above supersedes that run. Logs, TRX files and coverage
reports remain ignored under `TestResults/portal-loss`. Coverage executes source-linked
worker code against doubles in net10.0, with generated code excluded; it does not
establish native event timing or installed-candidate acceptance.

The implementation follow-up stopped before live testing as requested. The
maintainer then installed the update and separately authorized the retest below.

### Scoped live retest of installed candidate `e804e6f`

The maintainer reopened disposable standalone fixture A and explicitly requested
large reads with manual Portal closure to check `portal_lost` and immediate cursor
invalidation. This run issued one interruption read; the maintainer closed Portal
through the UI. No force-termination or project-write call was issued.

| Observation | Evidence |
| --- | --- |
| Installed provenance | Running host and worker both reported `4.1.1-local.14.ge804e6f`; installed DLL/executable hashes were captured privately |
| Healthy baseline | Explicit binding to A, `isModified:false`, matching AP21 baseline hash; tree snapshot had 96 nodes, hardware snapshot had 93 devices and 3 subnets. The saved tree continuation returned sequence 1 before interruption |
| Large read | One `list_network_objects` operation requested all six kinds (`deviceItem`, `networkInterface`, `node`, `subnet`, `ioSystem`, `communicationConnection`) with `pageSize:1`; the scan ran from 20:52:58.603 to 20:53:11.534 UTC (12.931 seconds) |
| Interrupted response | Item failed with `portal_lost`, a disposed native enumerator diagnostic and the attached-Portal disposal warning; the worker survived |
| Immediate tree continuation | The first TIA call after that response rejected the previously healthy cursor with `cursor_binding_mismatch`. No discovery, binding, status or native content read occurred between them |
| Hardware continuation | The next call also rejected the old hardware cursor with `cursor_binding_mismatch` |
| Later binding/status checks | Discovery reported binding already invalidated in both previous/current values and zero Portals; status was refused with `binding_conflict` carrying the Portal-loss reason |
| Process and file checks | Same host/worker PIDs and creation times survived; the user-closed Portal processes were absent; AP21 SHA-256 still matched baseline |
| Response contract | Parsed text matched structured content for all 10 captured documents |

This focused retest **passed** the observed Network interruption and immediate
invalidation behavior. It does not establish complete B-16/Q3 live acceptance.
No native IPC request log was available, so the one submitted read is not
request-log proof of no replay. The exception's precise CLR type was not captured.
Recovery/reopen/rebind and healthy post-recovery PLC/Network reads were not executed
on this installed candidate. Interrupted PLC, HMI, VCI and Multiuser reads, and
post-mutation outcomes, were not tested. Final state is Portal closed and binding
invalidated, with no automatic recovery. The file hash is AP21 preservation evidence,
not a complete comparison of every project-directory file.

Raw paths, cursors, process identities, hashes, timestamps and canonical responses
are ignored under `TestResults/portal-loss/live-e804e6f-2026-10-19` in
`baseline.json` and `manual-close.json`. The earlier 7,065-test offline qualification
still applies; this record update changes documentation only.

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
| Reviewed owner traversal fix | `TiaMcpServer.Contracts/Network/NetworkInterfacePathMatcher.cs`; worker `NetworkObjectSelectorResolver.cs`, `NetworkObjectIndexReader.cs`; `TiaMcpServer.Tests/Network/NetworkPortalLossTests.cs` |
| Other best-effort reads and inventory | Worker `Openness/Multiuser/MultiuserInventoryService.cs`, `MultiuserPortalReadDispatch.cs`; `Openness/Block/BlockExporterXmlBundle.cs`; `Openness/Hmi/HmiSoftwareLocator.cs` |
| Offline test wiring and doubles | `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`; `TestUtilities/TagSafetySiemensDoubles.cs`, `NetworkSelectorSiemensDoubles.cs`, `MultiuserInventorySiemensDoubles.cs`, `PortalSessionSiemensDoubles.cs` as required by the listed tests |
| Host regression fixtures | `TiaMcpServer.Tests/Worker/OpennessWorkerClientIntegrationTests.cs`, `Multiuser/ProjectBindingInspectionClientTests.cs`; reuse `TiaMcpServer.FakeWorker/Program.cs`'s `portal-lost` scenario and request logging |
| Documentation after implementation | `AGENTS.md`, `docs/ARCHITECTURE.md`, `docs/guides/troubleshooting.md`, `docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md`, `MULTIUSER_OPERATIONS_SUMMARY.md`, `VCI_OPERATIONS_SUMMARY.md`, the original review and documentation indexes |

The host's `OpennessWorkerClient.cs` already invalidates on `BindingConflict` or `PortalLost`; no host production edit is planned. B-5's binding-gate category flattening, probe retirement, HMI cache retention, transport lifetime and unrelated simplification items remain separate. Do not turn this into a worker-wide catch rewrite.

## Task 1: Establish the Exception Policy and Central Mapping

**Files:** Shared-filter/default-category row above; `TiaMcpServer.Tests/TestUtilities/TagSafetySiemensDoubles.cs`; test csproj; create `TiaMcpServer.Tests/Worker/OpennessExceptionsTests.cs`; modify `Network/NetworkOpennessReadTests.cs`, `Vci/VciPlannerTests.cs`, `Vci/VciReaderTests.cs`.

**Interfaces:** Consumes `WorkerFailureCategories.PortalLost` and `NonRecoverableException`. Produces `OpennessExceptions.IsRecoverable(Exception) : bool`; retains the existing stricter `NetworkOpennessRead.IsRecoverable(Exception) : bool` signature.

- [x] **Step 1: Align the executable double and add failing policy tests.** The reference stub declares `NonRecoverableException : System.Exception` at `reference-stubs/Siemens.Engineering.Base/Core.cs:164`. Match that base type in `TagSafetySiemensDoubles.cs` and replace its synthetic-inheritance comment. Pin the distinction:

  ```csharp
  Assert.False(typeof(EngineeringException).IsAssignableFrom(typeof(NonRecoverableException)));
  Assert.False(OpennessExceptions.IsRecoverable(new NonRecoverableException("closed")));
  Assert.True(OpennessExceptions.IsRecoverable(new EngineeringException("ordinary")));
  Assert.True(OpennessExceptions.IsRecoverable(new InvalidOperationException("metadata failure")));
  Assert.False(NetworkOpennessRead.IsRecoverable(new InvalidOperationException("programming failure")));
  ```

  Update VCI read/plan tests to assert that the original fatal exception escapes `VciDispatch`, replacing their dispatch-local `WorkerOperationException` expectation. Retain the existing VCI writer tests unchanged as a regression gate.
- [x] **Step 2: Observe red.** Run `dotnet test TiaMcpServer.Tests -m:1 --filter "FullyQualifiedName~OpennessExceptionsTests|FullyQualifiedName~NetworkOpennessReadTests|FullyQualifiedName~VciPlannerTests|FullyQualifiedName~VciReaderTests|FullyQualifiedName~VciWriterTests"`. Initially expect the missing helper, then the old VCI translation expectations to fail until implementation. Record any behavior changes exposed by correcting the test-double hierarchy; do not weaken assertions to hide them.
- [x] **Step 3: Implement the small production change.** Add and source-link the filter, compose the existing strict Network predicate with it, change the central catch category/guidance and remove only the VCI read/plan translation wrappers. Leave `VciWriter`'s outcome-bearing catch intact. Keep `Program.Execute` in place; no new execution framework or exception wrapper.
- [x] **Step 4: Observe green and compile the worker.** Re-run the focused command, then `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true`. The default mapper is a small direct change in net48 `Program`; source-linked tests and FakeWorker do not execute that entry point. Real boundary execution is an explicit live-gate requirement below.
- [x] **Step 5: Review and commit.** `fix: centralize Portal loss failure handling`.

## Task 2: Let Fatal Exceptions Escape Network Recovery

**Files:** Network propagation row above; tests `Network/HardwareDiscoveryEvidenceTests.cs`, `NetworkConnectionEvidenceTests.cs`, `NetworkIdentityResolverTests.cs`, `NetworkQualifiedNodeIdentityTests.cs`, `NetworkAttributeMetadataProcessorTests.cs`, `NetworkAttributeResultBuilderTests.cs`, `NetworkMutationVerificationTests.cs`, `NetworkDeviceCreatorWorkerContractTests.cs`; test csproj/doubles only where executable coverage requires them.

**Interfaces:** Consumes Task 1's shared filter. Existing discovery, selector, attribute and verification return types stay unchanged.

- [x] **Step 1: Add focused fatal-exception cases to existing executable seams.** Inject one `NonRecoverableException` instance at acquisition, `GetEnumerator`, `MoveNext`, `Current`, metadata property, attribute value and iterator-disposal boundaries. Assert `Assert.Same(failure, Assert.Throws<NonRecoverableException>(action))` and assert that a following callback/candidate was not visited. Cover node-owner/root/node traversal, connection identity capture, modeled/dynamic value reads and both XML-independent Network verification catch shapes. Preserve existing ordinary-failure tests alongside each fatal case.
- [x] **Step 2: Observe red.** Run `dotnet test TiaMcpServer.Tests -m:1 --filter "FullyQualifiedName~HardwareDiscoveryEvidenceTests|FullyQualifiedName~NetworkConnectionEvidenceTests|FullyQualifiedName~NetworkIdentityResolverTests|FullyQualifiedName~NetworkQualifiedNodeIdentityTests|FullyQualifiedName~NetworkAttributeMetadataProcessorTests|FullyQualifiedName~NetworkAttributeResultBuilderTests|FullyQualifiedName~NetworkMutationVerificationTests|FullyQualifiedName~NetworkDeviceCreatorWorkerContractTests"`. Failures must demonstrate swallowed/reclassified fatal exceptions, rather than fixture setup failures.
- [x] **Step 3: Filter the broad recovery catches.** Use `when (OpennessExceptions.IsRecoverable(exception))` at the listed catch sites. Fully qualify this helper where `using static NetworkOpennessRead` would otherwise select the stricter predicate. Preserve existing narrower filters. In `NetworkDeviceCreator.Create`, also exclude fatal loss from the catch that wraps non-`EngineeringException` failures in `InvalidOperationException`.

  Apply the same rule to both `NetworkMutationVerifier` catches: actual Portal loss must escape instead of becoming an unverified check. Ordinary verification failures retain their current evidence behavior. A mutation may already have happened; prior batch-item results remain valid, and the current item's completion must not be inferred or replayed.
- [x] **Step 4: Verify the production/test wiring and observe green.** Re-run the focused command and serial stub build. `NetworkMutationVerifier`, `HardwareConfigReader` and `NetworkDeviceCreator` are not currently source-linked into the test project. Exercise their already linked dependencies/callback seams and compile their production call sites; do not duplicate their bodies in tests or claim those tests executed the full classes. If a new branch needs a seam, move only that small callback-based operation to an existing linked helper. Retain the full-class runtime limitation in the validation record.
- [x] **Step 5: Review and commit.** `fix: propagate Portal loss through Network recovery paths`.

## Task 3: Preserve Fatal Inventory and Read Failures

**Files:** Other best-effort reads/inventory row above; tests `Multiuser/MultiuserInventoryWorkerTests.cs`, `Multiuser/MultiuserPortalReadDispatchTests.cs`, `Block/BlockExporterSafetyTests.cs`, `Hmi/HmiSoftwareLocatorTests.cs`; existing doubles as needed.

**Interfaces:** Consumes Task 1's filter. Produces unchanged inventory/export/locator results on ordinary failures and propagates the original fatal exception to `Program.Execute`.

- [x] **Step 1: Add failing regressions.** Cover both `Connections` enumeration and `Read` remote-call loss; a fatal body exception with concurrent Portal disposal; fatal XML export before companion export and fatal companion export after successful XML; a fatal cached-HMI liveness read. Assert original exception identity, no following read/fallback, no attachment/replay and no project/session mutation. Healthy `target_not_found`, ordinary incomplete inventory, XML companion fallback and stale-cache relocation must retain their current behavior.
- [x] **Step 2: Observe red.** Run `dotnet test TiaMcpServer.Tests -m:1 --filter "FullyQualifiedName~MultiuserInventoryWorkerTests|FullyQualifiedName~MultiuserPortalReadDispatchTests|FullyQualifiedName~BlockExporterSafetyTests|FullyQualifiedName~HmiSoftwareLocatorTests"`.
- [x] **Step 3: Apply only the propagation fixes.** Filter the inventory, XML-bundle and cached-HMI catches. Refactor `MultiuserPortalReadDispatch.Run` so an original `NonRecoverableException` is rethrown without being replaced by its final checks. Keep final validation on successful and ordinary-failure paths, including existing disposal-only `binding_conflict` behavior. Do not reconnect to validate a dead read.
- [x] **Step 4: Observe green and verify outcome preservation.** Re-run the focused command plus `dotnet test TiaMcpServer.Tests -m:1 --filter "FullyQualifiedName~VciWriterTests|FullyQualifiedName~BlockImportCoordinatorTests|FullyQualifiedName~BlockMutationPostconditionTests|FullyQualifiedName~BlockUpdateOutcomeDecoratorTests|FullyQualifiedName~BlockExporterVerificationTests"`.

  Retain these explicit boundaries: `BlockMutationService`, `PlcTypePostconditionVerifier`, `BlockImportCoordinator`, `BlockExporterVerification` and existing HMI writer cleanup preserve their typed post-mutation/primary-failure handling. `Program.Stamp` still preserves a completed result when diagnostic stamping fails. `TiaPortalSession`'s existing identity-loss refusals retain `binding_conflict`, which already invalidates host binding. Probe reports retain their diagnostic role. Record these as scope limits, not as universal `portal_lost` coverage.
- [x] **Step 5: Review and commit.** `fix: preserve fatal Portal exceptions in inventory and reads`.

## Task 4: Verify Host Recovery and Document the Scope

**Files:** Host regression fixture row above; current documentation row; this plan's checkboxes/evidence. No host production change is expected.

**Interfaces:** Uses existing `portal-lost` FakeWorker scenario, `TIA_MCP_FAKE_WORKER_REQUEST_LOG`, `ProjectBindingSnapshot.InvalidatedState`, `WorkerDispatchState.Sent` and the existing explicit-bind recovery APIs.

- [x] **Step 1: Expand the existing host integration tests.** Parameterize `PortalLost_InvalidatesBindingImmediately` over representative PLC, Network, HMI, compile/lifecycle and inventory worker methods. Reuse the existing request logger to assert exactly one failed method invocation. Check the unchanged `portal_lost` category, `Sent` dispatch state, invalidated binding/revision and continuation refusal. Extend `ProjectBindingInspectionClientTests`' cursor/loss cases rather than adding another fixture framework. Cover explicit re-grounding afterward and preserve an ordinary `worker_operation_failed` control that keeps a healthy binding.
- [x] **Step 2: Run the focused host gate.** `dotnet test TiaMcpServer.Tests -m:1 --filter "FullyQualifiedName~PortalLost_InvalidatesBindingImmediately|FullyQualifiedName~ProjectBindingInspectionClientTests|FullyQualifiedName~VciWriterTests"`. Existing host invalidation should pass; add only missing fixture behavior if a representative method cannot reach the existing scenario. This is host-contract evidence, not real Openness exception mapping.
- [x] **Step 3: Complete the offline gate once.** Run the serial stub solution build and `dotnet test TiaMcpServer.Tests -m:1 --no-build` in the same default Debug configuration. Where installed references are available, also run `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=false /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"`. Record totals, warnings, exact candidate and limitations. Do not repeat expensive suites after documentation-only edits.
- [x] **Step 4: Review the whole diff and update documentation.** Check every changed catch against its ordinary-failure control and the preservation boundaries. Update current docs with the shared mapping, explicit bind/inspection recovery and retained outcome exceptions. Link this implementation plan from B-16/Q3 in the original review and record offline qualification separately from live acceptance. Keep actual fixture paths, endpoint identities and raw diagnostics private.
- [x] **Step 5: Validate documentation and commit.** Run `git diff --check` and check changed Markdown links. Commit `test: verify Portal loss invalidation and recovery` with the host tests and accurate documentation. Leave live acceptance visibly pending until the gate below passes.

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
