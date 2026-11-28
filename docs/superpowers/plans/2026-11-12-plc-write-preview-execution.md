# PLC writes: preview references and execution checks — implementation plan

Status: superseded on 2026-11-28 by the [all-mutation-tools phased plan](2026-11-28-write-preview-execution.md). Retained for review history; do not execute this PLC-only plan.
Spec: [PLC write design](../specs/2026-11-12-plc-write-preview-execution-design.md)
Branch: `feat/plc-write-preview-execution`

## Goal

Deliver one PLC PR with reusable `previewId` infrastructure, apply without descriptive planning, and mandatory live protections against unsafe or silently incorrect PLC mutations.

## Architecture

The host saves immutable validated requests in a bounded session store and resolves them through the existing guarded write pipeline. PLC preview retains its descriptive planner, while apply obtains canonical identity and mutation provenance from the worker's existing target-resolution/mutation paths. A typed library-instance inspector serves both preview and mandatory native-adjacent checks; other tools keep their current public surface and behavior.

## Constraints

- Keep the .NET 10 host / .NET Framework 4.8 worker split; all Siemens calls run in the worker.
- Reuse `Safety/Pipeline`, canonical structured results, the verified binding lease, operation/capability catalogs and audit v2. No parallel write pipeline or token-bound surface.
- A preview never mutates TIA. Apply never runs the descriptive PLC planner, computes content diffs, or simulates the complete batch.
- Mandatory identity, concurrency, crash-prevention and silent-failure protections run against current state beside the affected native action; a prior preview cannot waive them.
- Keep 1-50 ordered unique-ID operations, the 256-character ID limit, stop at first failure, no batch rollback and no automatic replay.
- Keep `plc_read` at `1.0`, read-write/full eligibility, zero PLC elicitation, and standalone/local-session eligibility through `ProjectBase`.
- Use effective `MaxItemChars` / `MaxDocumentChars`; canonical text, structured content and the audited delivered document must remain identical.
- Preserve unrelated work. Other tools retain their current contracts and planning hooks until their separate PRs.

Task order is sequential where a later task consumes an earlier interface. Independent test-case preparation/review may run in parallel; builds, tests, shared fixture mutations and Git commits run serially. Before each edit, read its exact current file and preserve intervening work. Use current jCodemunch navigation; verify stale results against owning files. Every new behavioral test must be observed failing for its stated reason before the production change, then passing; a zero-test filter is not a pass.

## Task 1: Add the shared immutable preview request store

Files:
- Create `TiaMcpServer/Safety/Pipeline/WritePreviewStore.cs`: typed immutable storage, reservations, lookup, exact-binding claim and cleanup.
- Create `TiaMcpServer/Safety/Pipeline/WritePreviewModels.cs`: receipt/reservation/lookup models.
- Modify `TiaMcpServer.Contracts/Worker/WorkerFailureCategories.cs`: `preview_unavailable` and `preview_store_full` constants and closed-category registration.
- Create `TiaMcpServer.Tests/Safety/Pipeline/WritePreviewStoreTests.cs`: lifecycle, binding, concurrency and byte-accounting tests.

Interfaces: introduce `WritePreviewMetadata(string PreviewId, DateTimeOffset ExpiresAt)`. `WritePreviewStore(TimeProvider time)` owns one private session. Expose typed `TryReserve<TItem>(string toolName, IReadOnlyList<TItem> items, out WritePreviewReservation? reservation, out WriteToolError? error)`, `Resolve<TItem>(string toolName, string previewId)` returning restored items/binding or a typed error, and `TryClaim<TItem>(string toolName, string previewId, ProjectBindingSnapshot binding, out IReadOnlyList<TItem>? items, out WriteToolError? error)`. A reservation restores fresh snapshot items, prepares metadata against the pinned binding, activates once, and releases pending quota on disposal. Request type is a host type discriminator, never caller-supplied CLR metadata.

- [ ] Add `TestPreviewRequestIsImmutable`, `TestPreviewAbsoluteExpiryAndQuota`, `TestPreviewRequiresExactBinding` and store-level `TestPreviewClaimIsSingleUse`. Use a local deterministic `TimeProvider` subclass; do not add a dependency just for fake time.
- [ ] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~WritePreviewStoreTests"`; expect FAIL because the new store is missing or the new invariants are violated, and confirm at least one test is selected.
- [ ] Implement canonical UTF-8 deep snapshots, 32 random bytes encoded as `wp1_` plus 43 base64url characters, Ordinal ID comparisons, 10-minute monotonic lifetime from receipt preparation, 16 live/reserved entries, and 16,777,216 combined request bytes. Derive display expiry/store lifetime from one timestamp pair. Count pending reservations; never evict an unexpired entry or renew lifetime on access.
- [ ] Store the exact leased binding at publication; compare with `SameBinding` at claim. Atomically remove entries on claim; absence/expiry/consumption uses `preview_unavailable`, and a known binding mismatch uses `binding_conflict`. Wrong type/tool/private session cannot restore a request. No tombstones or cached execution results.
- [ ] Re-run the focused command to PASS, including parallel claims on the isolated in-memory store, reservation rollback, exact byte boundaries and stale worker/Portal/session identity cases. Review memory accounting after every state transition.
- [ ] Commit `feat: add shared single-use preview request storage`.

## Task 2: Integrate opt-in preview lifecycle into the guarded pipeline

Files:
- Modify `TiaMcpServer/Safety/Pipeline/WriteRecords.cs`, `TiaMcpServer/Safety/Pipeline/IWriteDomain.cs`, `TiaMcpServer/Safety/Pipeline/WriteExecution.cs`, `TiaMcpServer/Safety/Pipeline/WriteRun.cs`: optional reference resolution, reservation, admission/claim and finish integration.
- Modify `TiaMcpServer/Safety/Pipeline/WriteAudit.cs`: optional correlation only, preserving audit v2 and best-effort behavior.
- Modify `TiaMcpServer/Program.cs` and `TiaMcpServer.Tests/TestSupport/McpProtocolTestHarness.cs`: session-store construction/injection through `WriteExecution`.
- Modify `TiaMcpServer.Tests/Safety/Pipeline/PipelineFakes.cs`, `TiaMcpServer.Tests/Safety/Pipeline/WriteExecutionTests.cs` and existing pipeline failure/audit tests; add `TiaMcpServer.Tests/Safety/Pipeline/WritePreviewExecutionTests.cs` for publication/consumption boundaries.
- Modify `TiaMcpServer.Tests/Json/ConditionalMemberRegisterTests.cs`: explicitly include the host audit model and its conditional property.

Interfaces: add optional `PreviewId = null` to `WriteCall<TItem>`, optional `Preview = null` metadata to `WriteReport`, and default-off `SupportsPreviewReferences` plus `IsPublishablePreview(TResponse response)` on `IWriteDomain`. Keep the existing `RunAsync` call signature usable. Inject `WritePreviewStore` through `WriteExecution`; pass a private preparation context to `WriteRun`. Add conditional `WriteAuditRecord.PreviewId`. New constructor dependencies must be supplied in existing manual fixtures and harnesses, without making a service a public tool parameter.

- [ ] Add `TestPreviewClaimIsSingleUse` at pipeline level, `TestOnlyCompletePreviewPublishesReceipt` with an opted-in fake domain, and `TestReferenceApplyAuditsRestoredOperations`. Inject cancellation immediately before claim and after claim; also inject compose failure, normal audit-sink append failure, per-item guard failure, native failure, partial completion and transport loss.
- [ ] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~Safety.Pipeline"`; expect FAIL on the new assertions. Preserve an explicit control where a non-opted-in fake still plans on apply.
- [ ] Resolve stored items before constructing item-sized `WriteRun` arrays. Snapshot/reserve preview input before asynchronous work and use restored immutable items for the preview itself. Route unavailable/capacity errors through the canonical entered-call rejection/audit path, allowing an empty audit item list only when no request was recoverable.
- [ ] Under the existing binding lease, after input/access/capability/binding admission, recheck expiry/binding and atomically claim before beginning the execution loop. Do not claim a full-input call. Pre-claim denial releases no available entry; every post-claim outcome consumes it. No catch path reinstates it.
- [ ] Finish a preview by preparing candidate receipt metadata, composing/budgeting it, asking the domain whether that delivered response is complete, and discarding receipt/reservation otherwise. Strip receipt fields from the already-budgeted failure while preserving failure/omission; a payload that fits only without receipt must not become complete success. Test that exact boundary. Before final serialization, atomically check expiry/cancellation and activate a complete reservation inside the lease; an expired candidate becomes canonical `preview_unavailable` without receipt. Clean up a newly activated entry if final serialization throws. Attempt existing best-effort audit, then return; never change receipt fields after canonical serialization and never promise disk persistence/client receipt.
- [ ] Add audit correlation for both publication and ID application while retaining actual restored IDs/statuses and the exact delivered document/hash. Keep source/XML out of extra audit fields. Verify pending reservations are disposed on every outer/leased error path.
- [ ] Re-run the focused command to PASS and assert no other domain acquires references or changes its planning/confirmation defaults.
- [ ] Commit `feat: integrate opt-in preview references with guarded execution`.

## Task 3: Add native library-instance protection and preview inspection

Files:
- Create `TiaMcpServer.Contracts/Plc/PlcWriteGuardInfo.cs`: required `Blocked`, nullable `GuardId` and nullable `Message`, explicit nulls when clear.
- Create `TiaMcpServer.OpennessWorker/Openness/Plc/PlcLibraryInstanceGuard.cs`: typed inspection shared by preview/mutation.
- Modify `TiaMcpServer.OpennessWorker/Openness/Block/BlockImporter.cs`, `TiaMcpServer.OpennessWorker/Openness/Block/BlockMutationService.cs`, `TiaMcpServer.OpennessWorker/Openness/Block/BlockTargetResolver.cs` and `TiaMcpServer.OpennessWorker/Openness/Plc/PlcTypeImporter.cs`: guards before replacement/direct/group deletion on all format paths, with shared group resolution.
- Modify `TiaMcpServer.OpennessWorker/Program.PlcDispatch.cs`, `TiaMcpServer.OpennessWorker/Program.cs`, `TiaMcpServer.Contracts/Worker/WorkerRequest.cs`, `TiaMcpServer.Contracts/Safety/OperationPolicyCatalog.cs`, `TiaMcpServer.Contracts/Project/ProjectCapabilityCatalog.cs`, and `TiaMcpServer/Worker/OpennessWorkerClient.Plc.cs`: one internal bound inspection method.
- Modify `TiaMcpServer/Plc/PlcWritePlanner.cs`, `TiaMcpServer/Plc/PlcGuardDefinitions.cs` and relevant working-state resolution: preview inspection/deferred targets.
- Create `TiaMcpServer.Tests/Worker/PlcLibraryInstanceGuardTests.cs`; extend `TiaMcpServer.Tests/TestSupport/TagSafetySiemensDoubles.cs`, `TiaMcpServer.Tests/TestSupport/LibrarySiemensDoubles.cs` and existing importer/precondition tests only where needed.
- Modify `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`: explicit links for new worker code and host files outside the pipeline glob.

Interfaces: expose worker-native `InspectContentUpdate(IEngineeringServiceProvider target)`, `InspectDelete(IEngineeringServiceProvider target)`, group descendant inspection and `RequireAllowed(PlcWriteGuardInfo result)`. Extract `ResolveDeleteBlockGroup(ProjectBase project, string blockPath)` from current private group-resolution logic into the existing block resolver; its typed result carries resolved software/device, user group and canonical path and is used by both preview and mutation. Add host client `InspectPlcWriteTargetAsync(string operation, string? blockPath, string? typePath)`. The internal `inspect_plc_write_target` request has closed `PlcWriteGuardOperation` values for the four affected public operations, is `Observe`, maps to `plc_write` and runs under `WithEngineeringRoot`. It returns one typed check payload, decoded canonically. Names `plc_library_instance_edit` / `plc_library_test_instance_delete` are stable guard IDs; unavailable evidence uses narrowed `plc_state_unverifiable`.

- [ ] Read the retained November 7 probes and installed V21 XML for `GetService<LibraryTypeInstanceInfo>` and `LibraryTypeVersionState`; confirm the enum values are `InWork` and `Committed`. Reuse existing library writer service-acquisition patterns; do not scan arbitrary getters or enumerate project libraries.
- [ ] Add `TestLinkedContentWritesNeverMutate`, `TestLibraryInstanceDeletionChecks` and `TestLibraryGuardPreviewAndApplyShareInspector`. Exercise null service; linked FB/FC/UDT; both states; service/version/state failures; unknown enum; nested group and unreadable descendant; plain and committed-delete controls. Assert zero import/delete calls on refusals and unchanged `portal_lost` propagation.
- [ ] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~PlcLibraryInstanceGuardTests|FullyQualifiedName~PlcWritePreconditionsTests"`; expect FAIL because linked mutations currently reach the native doubles.
- [ ] Implement content blocking for every linked instance, without reading state unnecessarily. Block direct/group deletion only for in-work linked instances, preserving deliberate committed deletion. Inspect all descendants before deleting a group and fail closed on incomplete required inspection.
- [ ] Install an initial check on XML, SimaticSD and supported source importer routes after resolution and before external-source registration. Recheck immediately before every target `Import`, `ImportFromDocuments`, `GenerateBlocksFromSource` or `Delete`, even without re-resolution; inspect the freshly resolved object if resolution changed. Preserve import-not-started target outcomes, preparation/cleanup evidence and fatal classification. Share group-target resolution between preview and deletion rather than implementing a second selector traversal.
- [ ] Implement the internal preview read and typed payload validation. Do not query a live object that the preview only simulates creating earlier in the call; mark its protection as deferred to actual execution. Guarded apply always checks live state again.
- [ ] Re-run focused tests to PASS. Verify source linkage and that this inspection adds no public tool, implicit bind/open or standalone-only library access.
- [ ] Commit `fix: guard PLC library instances before replacement and deletion`.

## Task 4: Carry typed execution identities and mutation provenance

Files:
- Create `TiaMcpServer.Contracts/Plc/PlcWriteExecutionInfo.cs` and `TiaMcpServer.Contracts/Plc/PlcWriteTargetInfo.cs`: required typed execution/check evidence and canonical identity DTO.
- Modify `TiaMcpServer.Contracts/Worker/WorkerResponse.cs`, `TiaMcpServer/Worker/WorkerCallResult.cs`, and `TiaMcpServer.OpennessWorker/Worker/WorkerOperationException.cs`: conditional evidence carriage.
- Create `TiaMcpServer.OpennessWorker/Openness/Plc/PlcWriteExecutionCapture.cs`: per-request capture, no static state.
- Modify `TiaMcpServer.OpennessWorker/Program.PlcDispatch.cs`, `TiaMcpServer.OpennessWorker/Program.cs`, `TiaMcpServer.OpennessWorker/Openness/Plc/PlcSoftwareLocator.cs`, `TiaMcpServer.OpennessWorker/Openness/Block/BlockTargetResolver.cs`, `TiaMcpServer.OpennessWorker/Openness/Plc/PlcTypeTargetResolver.cs`, `TiaMcpServer.OpennessWorker/Openness/Plc/TagMutationService.cs`, `TiaMcpServer.OpennessWorker/Openness/Block/BlockMutationService.cs`, `TiaMcpServer.OpennessWorker/Openness/Block/BlockImporter.cs` and `TiaMcpServer.OpennessWorker/Openness/Plc/PlcTypeImporter.cs`: retain resolved owner/path and stamp native boundaries.
- Create `TiaMcpServer/Plc/PlcWriteExecutionContract.cs`; modify `TiaMcpServer/Worker/PersistentWorkerTransport.cs`, `TiaMcpServer/Worker/OpennessWorkerClient.Invocation.cs` and `TiaMcpServer/Worker/OpennessWorkerClient.Binding.cs`: strict envelope validation and all result-copy paths.
- Modify `TiaMcpServer/Safety/Pipeline/WriteVerification.cs`, `TiaMcpServer/Safety/Pipeline/TrustedWriteVerifier.cs`, `TiaMcpServer/Plc/PlcVerificationPolicy.cs`, `TiaMcpServer/Plc/PlcWriteDomain.cs`: execution-backed supersession/verification.
- Add `TiaMcpServer.Tests/Plc/PlcWriteExecutionEvidenceTests.cs`; extend `TiaMcpServer.Tests/Plc/PlcWriteVerifierTests.cs`, worker response/result/resolver tests, fake-worker protocol scenarios, conditional-member tests and explicit test source links.

Interfaces: wire member `plcWriteExecution` is conditional outside PLC mutations. A non-null `PlcWriteExecutionInfo` requires `Target`, `MutationStarted`, `Guard`; target is nullable canonical device/software/kind/folder/name/block/type identity, guard is nullable typed `PlcWriteGuardInfo`. Map this DTO to existing host `PlcTargetIdentity` in one place. Add default `MayHaveChanged(TItem item, StructuredOperationItem outcome)` to the shared verification policy with today's behavior as its default; PLC uses execution evidence. Missing evidence plus possibly-sent dispatch is unknown, not a false stamp.

- [ ] Add `TestExecutionEvidenceDrivesVerification`: software/device aliases of the same target, two devices with the same software name, Software Units, nested folders, short paths, rename chains, success followed by non-mutating refusal, started native failure, postcondition failure and missing/rejected evidence after dispatch. Assert both execution statuses and verification statuses.
- [ ] Add strict raw-envelope cases for duplicate/wrong-case/unknown/missing/wrong-type members, success without target, and a started/successful refusal guard. Assert `protocol_error` with rejected data withheld. Add a non-PLC response control with no new field.
- [ ] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~PlcWriteExecutionEvidenceTests|FullyQualifiedName~PlcWriteVerifierTests|FullyQualifiedName~WorkerResponseJsonTests|FullyQualifiedName~WorkerCallResultTests"`; expect FAIL on absent identity/provenance and malformed-envelope acceptance.
- [ ] Capture the existing locator's resolved software/device and canonical path during its existing traversal; do not add a second inventory, lookup RPC or native-equality shortcut. For creates, refine identity from the actual returned object where available. Capture both pre/post-rename keys as needed.
- [ ] Pass a request-local capture into mutation services/importers. Stamp `MutationStarted` immediately before the first native action that can change the target (setter/create/delete/import/source generation), after its adjacent checks. Temporary source registration/cleanup remains independently visible in existing import outcomes and does not falsely stamp the target as changed. Propagate the snapshot through normal responses, wrapped typed exceptions, fatal responses and block outcomes without changing shared exception classification. Protected target refusals have a false stamp even when temporary preparation required cleanup.
- [ ] Validate raw execution evidence before permissive worker decoding and preserve it through every host result-copy path. Require valid evidence for successful PLC mutations; missing failure evidence remains permitted for transport loss. Conditional-member registrations must cover the new envelope field.
- [ ] Replace PLC verification's dependence on planned `_effects` with an execution dictionary. A valid false stamp or `NotSent` cannot supersede; started evidence uses actual canonical keys. For possibly-sent unknown evidence, mark earlier successful verification `not_verified`/false with inspection guidance, preserve actual succeeded statuses, add no entry for the failed non-content item, and avoid unsafe reads after binding loss. Other domains keep the old policy behavior.
- [ ] Re-run the focused command to PASS, plus existing shared verifier tests. Ensure no fake effect or old preview data is used to fill missing execution identity.
- [ ] Commit `refactor: derive PLC verification from worker execution evidence`.

## Task 5: Make PLC apply bypass the descriptive planner

Files:
- Modify `TiaMcpServer/Plc/PlcWriteDomain.cs`, `TiaMcpServer/Plc/PlcWritePlanner.cs`, `TiaMcpServer/Plc/PlcWorkingState.cs`, `TiaMcpServer/Plc/PlcVerificationPolicy.cs`: separate preview state from execution state.
- Modify `TiaMcpServer.OpennessWorker/Openness/Plc/TagMutationService.cs`, `TiaMcpServer.OpennessWorker/Openness/Plc/PlcWritePreconditions.cs`, `TiaMcpServer.OpennessWorker/Openness/Block/BlockMutationService.cs`: remove effects-only prerequisites, preserve required checks and make diagnostics independent of a presumed prior plan.
- Extend `TiaMcpServer.Tests/Plc/PlcGuardedWriteDomainTests.cs`, planner/working-state tests, worker mutation/precondition tests and fakes.

Interfaces: PLC returns `PlansBeforeApply(items) => false`; initializes request and execution tracking during validation, before either path. PLC preview retains the planner; its replan method must not be called by unplanned apply. Apply composes `effects:[]`. Mandatory block checks are per-item execution failures, not a hidden whole-batch planning pass.

- [ ] Add `TestAllPlcApplyPathsSkipPlanner` for all 14 operations and `TestCreateUseAndLateFailure`; make the fake planner throw if either method is called by apply. Include create table/tag, create group/block, two singleton creates and a late missing delete followed by a skipped item.
- [ ] Add `TestMandatoryChecksSurviveUnplannedApply` with stale content hash, mismatched declaration/format, import cleanup failure, occupied singleton, ignored tag rename and a plain constant whose old-value getter is unreadable. The last case must reach its requested setter without an effects-only read.
- [ ] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~PlcGuardedWriteDomainTests|FullyQualifiedName~PlcWritePreconditionsTests|FullyQualifiedName~PlcWriteVerifierTests"`; expect FAIL because apply still plans or relies on preview effects/old values.
- [ ] Opt PLC into no-plan apply, initialize `_items` outside planning and use Task 4 execution evidence. Keep the worker's fresh singleton number selection and strict content hash/declaration checks. Preserve rename-first/name-readback and required import/source-cleanup checks.
- [ ] Stop generating delete-details/address-overlap information on apply; preserve it in preview. Remove `RequireReadableValue` from ordinary constant updates. Retain live group traversal for the library test-instance deletion guard; remove reads whose only purpose was a presented effect.
- [ ] Replace diagnostics that assert a prior plan existed with current-state wording. Confirm failed current items use applied-phase item failures with root `error:null`, prior successes remain, and partial-write warnings/skipped remainder stay truthful.
- [ ] Re-run focused tests to PASS; do not broaden native rejection claims or restore removed name/default-table guards.
- [ ] Commit `feat: apply PLC writes without descriptive planning`.

## Task 6: Expose PLC preview references through the closed MCP contract

Files:
- Create `TiaMcpServer/Tools/WritePreviewArgumentsTool.cs`: shared raw source-alternative validator/schema decorator, PLC-only registration.
- Modify `TiaMcpServer/Plc/PlcWriteTools.cs`, `TiaMcpServer/Plc/PlcContractVersion.cs`, `TiaMcpServer/Plc/PlcWriteModels.cs`, `TiaMcpServer/Plc/PlcWriteDomain.cs`, `TiaMcpServer/Plc/PlcWritePayloadBudget.cs` and `TiaMcpServer/Tools/McpToolRegistration.cs`.
- Extend `TiaMcpServer.Tests/Plc/PlcGuardedWriteMcpTests.cs`, `TiaMcpServer.Tests/Plc/PlcWriteBudgetTests.cs`, `TiaMcpServer.Tests/Tools/McpToolSchemaTests.cs`, `TiaMcpServer.Tests/Tools/ToolOutputContractConformanceTests.cs`, `TiaMcpServer.Tests/Json/ConditionalMemberRegisterTests.cs` and test source links.
- Update only the PLC entry in `TiaMcpServer.Tests/Tools/Snapshots/output-schemas.json` from actual production `ListToolsAsync` output; the existing schema tests compare snapshots and do not regenerate them.

Interfaces: public method accepts optional `PlcOperationRequest[]? operations = null`, `bool dryRun = false`, `string? previewId = null`, plus existing injected services/cancellation. The raw decorator enforces exactly one input source and rejects explicit nulls before SDK binding. PLC opts into references; response adds conditional `PreviewId` and `PreviewExpiresAt` together. PLC write version is `3.0`; read stays `1.0`. `IsPublishablePreview` examines the final budgeted response, block guards and nested omissions.

- [ ] Add `TestPreviewInputAlternativesOverMcp`, `TestOnlyCompletePreviewPublishesReceipt` and `TestReferenceApplyAuditsRestoredOperations` in the real-protocol harness. Include absent/empty/invalid IDs, null sources, both sources, neither source, `dryRun:true` with ID, malformed booleans, duplicate/wrong-case/unknown roots and retired arguments; preserve the distinction between raw shape rejection and entered semantic validation.
- [ ] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~PlcGuardedWriteMcpTests|FullyQualifiedName~PlcWriteBudgetTests|FullyQualifiedName~ToolOutputContractConformanceTests|FullyQualifiedName~ConditionalMemberRegisterTests"`; expect FAIL on the missing source alternative/receipt/version contract.
- [ ] Use the existing closed-tool `decorate` seam and retain strict operation-member validation. Advertise matching `oneOf`, make operations optional at CLR binding only, and keep injected services out of input schema. Do not change the registration/schema of any other tool.
- [ ] Reserve encoded receipt overhead in protected-core admission and include actual receipt metadata in budget calculations. Publish neither field for a block guard, root/nested omission, failure or compaction. Add the host response type to conditional-member scanning and register both fields.
- [ ] Cover a successful preview, complete ID apply, cache saturation with direct apply control, content changed after preview, rebind/restart, two concurrent same-ID requests, cancellation and failure after an earlier success. Compare canonical text, structured content and audit document/hash; assert zero elicitation in both writable modes.
- [ ] Capture the production output schema with a temporary test using `await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full)`, then `await harness.Client.ListToolsAsync()` and the single tool named `plc_write`; serialize its `ProtocolTool.OutputSchema` to a task-owned temporary JSON file. Replace only the snapshot's `plc_write` JSON node from that capture, preserve other entries and remove the temporary exporter. Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~McpToolSchemaTests"`; both default and `2025-11-25` protocol comparisons must PASS. Inspect the snapshot diff and reject unrelated tool changes, then re-run the Task 6 focused command to PASS.
- [ ] Commit `feat: expose reusable PLC preview references in contract 3.0`.

## Task 7: Run integrated offline and installed-reference qualification

Files:
- Extend only implicated cases in `TiaMcpServer.FakeWorker/Program.cs`, existing worker/PLC tests and `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj` when integration exposes missing fixture wiring.
- Store private working run notes under `docs/superpowers/evidence/2026-11-12-plc-write-preview-execution/` while this branch is open; no MCP traces become durable acceptance inputs.

Interfaces: all 14 success/failure fixtures carry the new PLC execution envelope where appropriate; non-PLC responses do not. New host Tools/PLC and worker helper files must be explicitly linked into the test project; `Safety/Pipeline/*.cs` is already globbed. New Siemens API usage must compile against the installed V21 references and compile-only stubs.

- [ ] For any integration defect, first add its failing focused test, record the expected assertion failure, implement the smallest correction, re-run that focus and commit the fix with a conventional message. Do not widen unrelated fakes or relax payload contracts to obtain green tests.
- [ ] Run `dotnet restore TiaMcpServer.slnx`, then `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true`; require exit code 0 and record warnings separately.
- [ ] Run `dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"`; require all selected tests to pass. Confirm test counts and that new tests are discovered, not filtered away.
- [ ] Run `dotnet build TiaMcpServer.slnx -m:1 /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"`; require exit code 0. Treat this as installed-reference compilation, not live behavior.
- [ ] If product project/build settings, `reference-stubs/` or either required packaging verification script changed, run `dotnet test TiaMcpServer.Tests --filter "Category=Packaging"`. Regenerate stubs only through the documented verification/update workflow if required; never commit Siemens DLLs. Record any required gate that cannot run as unmet.
- [ ] Review the complete diff and coverage of materially changed production logic. Meet the repository threshold or target 80% where meaningful; do not add implementation-mirroring tests for a percentage. Check unchanged versions, schemas, mode/capability rules and planning behavior for Network/HMI/VCI/library/lifecycle.
- [ ] Have an independent reviewer inspect claim/publication races, guard placement, execution provenance, protocol rejection and local-session behavior; resolve findings before live qualification. Commit only any real fixes, each with its focused regression test.

## Task 8: Qualify guarded native behavior and publish durable documentation

Files:
- Create a dated report under `docs/superpowers/acceptance/reports/` for this implementation's actual run date, plus an indexed raw native fixture/probe input only if such evidence is generated.
- Update `docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md`, `docs/ARCHITECTURE.md`, `docs/IMPROVEMENT_LOG.md`, `AGENTS.md`, and applicable user examples describing PLC version `3.0`.
- Add `docs/adr/0020-plc-preview-references-and-execution-checks.md` and its `docs/adr/README.md` entry; confirm the number remains unused before creation.
- Correct the singleton wording in `docs/adr/0001-plc-content-exchange-and-creation.md`, `0012-portal-as-rejection-authority.md`, PLC reference and improvement log; annotate historical report context rather than rewriting its actual observed results.
- Update `docs/superpowers/README.md` with the acceptance report; remove the transient spec/plan links and working files in the final prune commit after durable content exists.

Interfaces: use the installed candidate's public `plc_write`/`plc_read` through one exact verified disposable binding. Native data setup/restoration and lifecycle operations require explicit scoped authorization at execution time. This plan authorizes no new live call, push or PR publication by itself.

- [ ] Before any live mutation, prepare a concrete fixture/cleanup list and obtain authorized disposable standalone and local-session targets. Record exact candidate commit, access mode, bound identity, effective budgets, initial project state and required linked/plain fixture states. If an environment is unavailable, mark that portion unqualified rather than treating a build/double as live proof.
- [ ] Exercise preview then ID apply, direct apply, create/use dependencies, a late native collision/partial stop, stale content hash, guarded second singleton and the existing tag-rename readback. Repeat ID application and prove no second mutation. Never bypass the singleton guard to repeat the old unsafe import.
- [ ] Exercise guarded FB/FC/UDT content refusals in each supported format/state, plain-object positive controls, guarded in-work direct/nested-group deletion and authorized committed-deletion controls. Compare project/library linkage before and after. Do not deliberately enter the old unguarded modal/unlink paths. Record that prior direct-delete proof was UDT-only and that new FB/FC/group conclusions require this qualification.
- [ ] Exercise the same guard/binding path on a local session when available; save only if separately authorized. Remove only the agreed disposable fixtures and verify restoration. Do not automatically close, save, check in or restore unrelated user objects.
- [ ] Write the acceptance report with pass/fail/unexecuted rows and separate offline, installed-reference and live evidence. Keep MCP traces, preflights, audit dumps and timing artifacts out of durable inputs; preserve only verified raw Openness evidence under the indexed inputs policy.
- [ ] Document shared reference limits/expiry/consumption, exact-input semantics, empty apply effects, changed failure timing, mandatory guard/postcondition rules, execution-evidence fallback and library-instance workflow. Describe other tools as unchanged. Correct the OB82 evidence discrepancy with links to both original dated reports.
- [ ] Run `python -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30` and `git diff --check`; require exit code 0. Commit `docs: document PLC preview references and execution checks`.
- [ ] Before any requested remote write, inspect current diff/status and relevant gates. Use the actual PR number/implementation commit in durable ADR provenance after PR creation; never link transient working files from outside `docs/superpowers/`. Once durable docs/evidence exist and before merge, delete this branch's transient working files and links in a final `docs: prune working files` commit.

## Requirement coverage and review gates

| Spec scope | Tasks | Principal observable |
| --- | --- | --- |
| S-1 PLC preview/apply separation | 4, 5, 6 | `TestAllPlcApplyPathsSkipPlanner`, `TestCreateUseAndLateFailure` |
| S-2 shared single-use storage | 1, 2 | immutable/expiry/quota/binding/claim tests |
| S-3 public canonical contract/audit | 2, 6 | real MCP source alternatives, budget and correlation tests |
| S-4 mandatory checks/verification | 3, 4, 5 | native-call counters, typed identities and unknown-dispatch fallback |
| S-5 library-instance guard | 3, 7, 8 | shared inspector tests and explicitly scoped native acceptance |
| S-6 qualification/docs | 7, 8 | build/test outputs, independent review and dated acceptance report |

The spec's 16 acceptance criteria are all assigned above. Tests named in the spec are planned tests, not claims that current code already passes them. Keep tasks unchecked until their own evidence and review gates pass. Do not dispatch implementation merely because the plan checker reports structural completeness; the current user request is to prepare the documents.

## Plan validation procedure

Use the requested `procoder:source-command-plan` skill. The installed native Windows binary is the executable resolved by its launcher; it is a supported fallback when Git Bash cannot start in the sandbox. Its controllers hardcode `.procoder/specs/` and `.procoder/plans/`, whereas project instructions require the canonical documents here. Validate checksum-identical temporary mirrors of these documents at the controller paths, run `procoder spec check plc-write-preview-execution` before `procoder plan check plc-write-preview-execution`, then remove only those temporary mirror files. Never overwrite existing user mirrors or modify procoder configuration to change this project's policy. Structural `COMPLETE` does not establish implementation or runtime correctness.
