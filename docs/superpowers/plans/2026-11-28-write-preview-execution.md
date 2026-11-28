# All mutation tools: direct apply and reusable previews - implementation plan

Status: planned for documentation review; no implementation task, test or live acceptance below has run.
Date: 2026-11-28
Source baseline: `5cdc4fd2`; branch at design time: `feat/plc-write-preview-execution`.
Spec: [All mutation tools design](../specs/2026-11-28-write-preview-execution-design.md).
Supersedes the [PLC-only implementation plan](2026-11-12-plc-write-preview-execution.md).

## Goal

Standardize nonprompting apply across all 63 mutation/lifecycle operations before adding immutable preview references to their existing 11 tools.

## Architecture

The existing guarded pipeline keeps validation, binding, ordering, confirmation, verification, response budgeting and audit ownership. Each domain separates current execution preparation and required native-adjacent checks from descriptive preview planning, deriving verification identities from execution rather than presented effects. After the common apply milestone, one host-session store supplies exact original requests through the same execution path, with exclusive attempts, exact source-context checks and conservative retirement.

## Constraints

- Keep the .NET 10 host / .NET Framework 4.8 worker split; Siemens calls run in the worker.
- Reuse `Safety/Pipeline`, canonical structured results, the binding lease, operation/capability catalogs and audit v2. Do not create a parallel write pipeline or revive safety tokens.
- Preserve access modes, local-session/container eligibility, native exception classification, zero elicitation for content writes and current lifecycle elicitation rules.
- Preserve caller ordering, 1-50 unique-ID items for batch tools, 256-character IDs, stop at the first failed item, no batch rollback and no automatic replay. Lifecycle stays one command per call.
- Retain fresh request/access, exact binding/owner/target, concurrency/hash, crash-prevention, silent-failure, cleanup and transaction protections. Remove only work whose sole purpose is descriptive inventory, simulation or presentation.
- Previews never mutate. Nonprompting apply never calls a descriptive planner or re-planner. Prompting calls retain fresh consequence planning and post-acceptance comparison.
- Use effective `MaxItemChars` / `MaxDocumentChars`; canonical text, structured content and audited delivered document/hash remain identical.
- Preserve all 63 operations. Compilation remains direct without planning. A test must detect an added writable tool or operation that is neither covered nor explicitly classified as compilation.
- Builds, tests, fixture mutation and Git commits run serially. Preserve unrelated work and commit each implementation/fix step using a conventional message. Remote writes require explicit approval.

This plan authorizes no live TIA call, installation change, push or PR publication. Obtain exact disposable targets and an agreed fixture/cleanup list before live mutations. A missing native environment remains an unqualified acceptance row.

Use current jCodemunch navigation and verify source freshness before edits. Read each exact file before changing it, preserve intervening work, and register edits/reindex as directed by the current guide. New host source outside the pipeline glob and worker source linked into tests require explicit entries in `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`. Add conditional wire members to `ConditionalMemberRegisterTests` and strictly validate raw evidence before permissive envelope decoding.

## Operation inventory and release sequence

The operation matrix uses tool plus operation as its key: names shared by PLC and HMI are distinct rows.

| Domain | Count | Exact operations | Owning source |
| --- | --- | --- | --- |
| PLC | 15 | update_block_logic, patch_block_logic, update_type_content, create_tag_table, delete_tag_table, create_tag, update_tag, delete_tag, create_user_constant, update_user_constant, delete_user_constant, create_block, delete_block, create_block_group, delete_block_group | `TiaMcpServer/Plc/PlcOperationCatalog.cs` |
| Network | 5 | add_network_device, configure_network_device, create_subnet, update_subnet, delete_subnet | `TiaMcpServer/Network/NetworkOperationCatalog.cs` |
| HMI | 9 | create_tag_table, update_tag_table, delete_tag_table, create_tag, update_tag, delete_tag, create_alarm, update_alarm, delete_alarm | `TiaMcpServer/Hmi/HmiWriteCatalog.cs` |
| VCI | 6 | create_workspace, delete_workspace, export, sync_to_workspace, sync_to_project, delete_mapping | `TiaMcpServer/Vci/VciWriteCatalog.cs` |
| Library | 22 | create_global, open_global, retrieve_global, save_global, close_global, archive_global, create_folder, delete_folder, create_master_copy, copy_master_copy, instantiate_master_copy, delete_master_copy, create_type, create_type_version, release_type_version, set_default_type_version, synchronize_type, instantiate_type_version, delete_type, delete_type_version, set_comments, set_languages | `TiaMcpServer.Contracts/Library/LibraryWriteMethods.cs` |
| Project lifecycle | 6 | open_project, create_project, save_project, save_project_as, archive_project, close_project | `TiaMcpServer/Tools/ProjectWriteTools.cs` |

`compile_check` is an explicit exclusion, not a 64th mutation or reference-enabled tool. Keep its direct API, access checks, binding, output schema, contract version and lack of planning/confirmation unchanged. PLC's automatic shared compilation is separate and remains supported.

| PR | Tasks | Deliverable | Write versions after PR |
| --- | --- | --- | --- |
| 1 | 1 | PLC linked-instance execution guards | Existing versions |
| 2 | 2 | PLC execution evidence and nonprompting direct apply | PLC 3.0 |
| 3 | 3 | Network current execution preparation and direct apply | Network 3.0 |
| 4 | 4 | HMI current owner/evidence and direct apply | HMI 3.0 |
| 5 | 5 | VCI required checks outside descriptive planning | VCI 3.0 |
| 6 | 6 | Nonprompting lifecycle direct apply; Library and all-63 consistency milestone | Lifecycle 2.0; Library remains 2.0 |
| 7 | 7-10 | Shared references on all 11 mutation/lifecycle tools | PLC/Network/HMI/VCI 3.1; lifecycle 2.1; Library 2.1 |

PRs 1-6 contain no preview store, preview ID input, receipt, reference audit field or reference schema. Task 6's complete apply matrix and required qualification gate Task 7. Do not partially deliver references to PLC first.

## Verification and PR gates inherited by every task

Every behavioral task first adds compiling tests and observes the stated failing assertion. Minimal declarations needed to compile a new interface may precede that assertion; missing compilation or a zero-test filter is not evidence that the intended behavior was exercised. Record selected test counts, the initial failure reason, passing rerun, candidate commit and warnings.

For each PR, run focused checks first, then these commands serially:

```powershell
dotnet restore TiaMcpServer.slnx
dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"
dotnet build TiaMcpServer.slnx -m:1 /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"
python -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30
git diff --check
```

Run `dotnet test TiaMcpServer.Tests --filter "Category=Packaging"` whenever the PR changes a product project file, `Directory.Build.props`, `global.json`, `scripts/verify-doctor-package.ps1`, `scripts/verify-reference-stubs.ps1` or `reference-stubs/`. Installed-reference compilation does not establish live behavior. Regenerate stubs only through the documented workflow; never commit Siemens DLLs.

Each PR includes a dated `docs/superpowers/acceptance/reports/` report, updates to its existing supported-operations reference and `docs/IMPROVEMENT_LOG.md`, the literal release-note sentence below, and independent review of its changed safety/identity/protocol paths. Put durable architecture decisions in `docs/ARCHITECTURE.md` and the next unused ADR number in `docs/adr/`; update the ADR index. Do not assume ADR 0020 is still unused. Working traces/timing files stay under task-owned transient evidence or `scripts/timing/`, following existing retention policy. Record passed, failed and unexecuted rows by operation, mode, format and container; do not generalize sampled native results to all 63 paths.

## Task 1: PR 1 - protect PLC linked library instances at native boundaries

Files:
- Create `TiaMcpServer.Contracts/Plc/PlcWriteGuardInfo.cs` and `TiaMcpServer.OpennessWorker/Openness/Plc/PlcLibraryInstanceGuard.cs` for shared typed inspection.
- Modify `TiaMcpServer.OpennessWorker/Openness/Block/BlockImporter.cs`, `BlockMutationService.cs` and `BlockTargetResolver.cs` in that directory, and `TiaMcpServer.OpennessWorker/Openness/Plc/PlcTypeImporter.cs` for initial/adjacent guards and shared group resolution.
- Modify `TiaMcpServer.OpennessWorker/Program.PlcDispatch.cs`, `Program.cs`, `TiaMcpServer.Contracts/Worker/WorkerRequest.cs`, `TiaMcpServer.Contracts/Safety/OperationPolicyCatalog.cs`, `TiaMcpServer.Contracts/Project/ProjectCapabilityCatalog.cs` and `TiaMcpServer/Worker/OpennessWorkerClient.Plc.cs` for one bound inspection operation.
- Modify `TiaMcpServer/Plc/PlcWritePlanner.cs` and `PlcGuardDefinitions.cs` for preview inspection.
- Create `TiaMcpServer.Tests/Worker/PlcLibraryInstanceGuardTests.cs`; extend existing PLC precondition/importer tests and `TiaMcpServer.Tests/TestSupport/LibrarySiemensDoubles.cs` / `TagSafetySiemensDoubles.cs`; update test source links.
- Update `docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md`, `docs/IMPROVEMENT_LOG.md` and the dated PR 1 acceptance report; correct singleton historical wording only where supported by the original reports.

Interfaces: `PlcWriteGuardInfo` has required `Blocked`, nullable `GuardId` and `Message` with explicit nulls when clear. `PlcLibraryInstanceGuard.InspectContentUpdate(IEngineeringServiceProvider target)`, `InspectDelete(IEngineeringServiceProvider target)`, descendant inspection and `RequireAllowed(PlcWriteGuardInfo result)` serve preview and mutation. Extract `ResolveDeleteBlockGroup(ProjectBase project, string blockPath)` into the current resolver. The bound internal `inspect_plc_write_target` request uses closed `PlcWriteGuardOperation` values for update_block_logic, update_type_content, delete_block and delete_block_group; it is Observe and maps through `ProjectCapabilityCatalog.OperationFor` to plc_write. Public patch preview inspects its normalized block-update target without adding a worker patch mutation. Stable guards are `plc_library_instance_edit` and `plc_library_test_instance_delete`; unreadable required evidence uses narrowed `plc_state_unverifiable`.

- [ ] Read retained November 7 probes and installed V21 XML for LibraryTypeInstanceInfo and LibraryTypeVersionState; verify InWork/Committed spelling and existing service-acquisition patterns. Record the limited original modal/unlink evidence.
- [ ] Add `TestPlcLinkedInstanceGuardsNeverMutate` cases for linked FB/FC/UDT, XML/SimaticSD/supported source paths, both states, null service, service/version/state failures, unknown enum, direct deletion, nested groups and unreadable descendants. Count import/generation/delete calls; include plain-object and committed-deletion controls and fatal-loss propagation.
- [ ] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~PlcLibraryInstanceGuardTests|FullyQualifiedName~PlcWritePreconditionsTests"`; expect FAIL with linked replacements/deletions reaching a native mutation counter.
- [ ] Implement content refusal for every linked instance and deletion refusal for in-work linked instances. Inspect every group descendant before group deletion, fail closed on incomplete required evidence, and avoid unnecessary version-state reads for content refusal.
- [ ] Check before temporary external-source registration and recheck immediately before every target import/generation/delete, including unchanged resolved handles. Retain exact import-not-started outcomes, preparation/cleanup evidence and portal_lost precedence.
- [ ] Use the same inspector for preview, deferring targets simulated as created earlier. Re-run the focused command to PASS and review source links, bound/local-session routing and absence of new public tools.
- [ ] Run the inherited PR gates and independent review. With scoped disposable-target authorization, qualify linked/plain controls without entering the old unguarded modal/unlink paths; mark unavailable FB/FC/group/container cases unqualified.
- [ ] Add release-note sentence: "PLC content writes now refuse linked library instances before replacement, and direct or group deletion protects in-work test instances."
- [ ] Review the diff and commit `fix: guard PLC library instances before replacement and deletion`. Commit any subsequent fix with its failing-then-passing regression evidence; do not create references in this PR.

## Task 2: PR 2 - derive PLC direct execution and verification without a descriptive plan

Files:
- Create `TiaMcpServer.Contracts/Plc/PlcWriteExecutionInfo.cs`, `PlcWriteTargetInfo.cs` and `TiaMcpServer.OpennessWorker/Openness/Plc/PlcWriteExecutionCapture.cs`.
- Modify `TiaMcpServer.Contracts/Worker/WorkerResponse.cs`, `TiaMcpServer.OpennessWorker/Worker/WorkerOperationException.cs`, `TiaMcpServer/Worker/WorkerCallResult.cs`, `PersistentWorkerTransport.cs`, `OpennessWorkerClient.Invocation.cs` and `OpennessWorkerClient.Binding.cs` for strict evidence carriage.
- Modify `TiaMcpServer.OpennessWorker/Program.PlcDispatch.cs`, `Program.cs`, `Openness/Plc/PlcSoftwareLocator.cs`, `PlcTypeTargetResolver.cs`, `TagMutationService.cs`, `PlcWritePreconditions.cs` and `Openness/Block/BlockTargetResolver.cs` / `BlockMutationService.cs` / `BlockImporter.cs` plus `Openness/Plc/PlcTypeImporter.cs` for request-local capture and retained checks.
- Create `TiaMcpServer/Plc/PlcWriteExecutionContract.cs` and `PlcPatchExecutionPreparation.cs`; modify `PlcWriteDomain.cs`, `PlcWritePlanner.cs`, `PlcWorkingState.cs`, `PlcVerificationPolicy.cs`, `PlcSharedCompilation.cs` and `PlcContractVersion.cs`.
- Modify `TiaMcpServer/Safety/Pipeline/WriteVerification.cs` and `TrustedWriteVerifier.cs` for execution-aware supersession.
- Create `TiaMcpServer.Tests/Plc/PlcWriteExecutionEvidenceTests.cs` and `PlcDirectApplyTests.cs`; extend existing patch, resolved-audit, shared-compilation, read-back, verifier, raw worker-envelope and conditional-member tests, fake-worker cases and explicit test source links.
- Update `TiaMcpServer.Tests/Tools/Snapshots/output-schemas.json` for plc_write only, PLC reference, architecture/ADR, improvement log and PR 2 acceptance report.

Interfaces: conditional worker `plcWriteExecution` contains required `Target`, `MutationStarted` and `Guard`; target is a nullable canonical device/software/kind/folder/name/block/type DTO, guard is nullable `PlcWriteGuardInfo`. Capture identity during existing traversal and stamp immediately before the first target-changing native call. `PlcWriteExecutionContract` strictly validates raw evidence and maps its target to `PlcTargetIdentity` in one place. Add default `bool MayHaveChanged(TItem item, StructuredOperationItem outcome)` to `IWriteVerificationPolicy<TItem>`, preserving the current behavior for unchanged domains; PLC supplies execution evidence. `PlcPatchExecutionPreparation.ResolveAsync(PlcOperationRequest requested)` returns one exact internal update request or a typed pre-dispatch failure using fresh export/hash/anchor checks, without effects, simulation or diff. `PlcWriteDomain.PlansBeforeApply(items) => false`; PLC version becomes 3.0.

- [ ] Add `TestAllPlcApplyPathsSkipPlanner` with one case per listed 15 operations, making both planner methods throw on apply. Add `TestExecutionEvidenceDrivesVerification` and `TestPatchSharedCompileReadBackSurviveDirectApply`. Assert effects:[], zero elicitation, ordered create/use, two singleton creates, late failure after earlier success, skipped remainder and existing create-then-patch refusal.
- [ ] Add raw-envelope tests for missing, duplicate, wrong-case, unknown and wrong-type execution members, success without canonical target, impossible started/successful refusal guards, failed NotSent and possibly-sent unknown evidence. Include a non-PLC control with no new field.
- [ ] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~PlcDirectApplyTests|FullyQualifiedName~PlcWriteExecutionEvidenceTests|FullyQualifiedName~PlcWriteVerifierTests|FullyQualifiedName~WriteExecutionResolvedAuditTests"`; expect FAIL with a descriptive planner call, absent execution identity or accepted malformed execution evidence.
- [ ] Capture canonical aliases, device/software distinction, Software Units, nested folders, short paths and pre/post-rename keys without a second inventory/lookup RPC. Preserve capture through success, typed partial/native failure, fatal loss and every host copy path.
- [ ] Initialize _items and execution tracking during validation. Keep per-item live target, hash, format/declaration, singleton selection, ignored tag rename and Task 1 guards. Remove old constant getter prerequisites whose sole purpose is presenting the old value; the setter must remain reachable.
- [ ] Extract mandatory patch resolution from the planner. Validate/authorize the public patch before normalization; resolve original anchors once against the fresh served export, preserving whole-export hash, XML-only document edits, overlap/newline/delimiter rules and exact resolvedOperation audit timing. Never execute a preview's resolved replacement.
- [ ] Derive verification identity from execution, not _effects. False stamps and NotSent refusals do not supersede; started mutations use canonical keys; possibly-sent missing/rejected evidence leaves prior item execution statuses intact but marks affected earlier verification not_verified/false with inspection guidance. Do not invent identity or add a failed non-content verification entry.
- [ ] Preserve deferred source compilation including committed-then-failed imports, compile before re-export, and compare the same submitted/resolved content with existing verification exports. Keep informational compilation/read-back semantics, reusable full-export hashes and one per-call excerpt allowance; suppress unsafe follow-up after binding loss.
- [ ] Re-run the focused command to PASS, then `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~PlcSharedCompilation|FullyQualifiedName~PlcReadBack|FullyQualifiedName~PlcPatch|FullyQualifiedName~TrustedWriteVerifierTests"` to PASS. Confirm relevant discovered names and extend the filter to an actual existing class if a substring selects zero tests.
- [ ] Capture plc_write's real production output schema, replace only its snapshot node and run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~McpToolSchemaTests"` to PASS for default and 2025-11-25 protocol.
- [ ] Run inherited PR gates and independent review. Qualify authorized direct source/XML/patch operations, partial commit/shared compilation, hash/rename/singleton refusals and standalone/local-session checks; separate installed-reference from live evidence.
- [ ] Add release-note sentence: "PLC writes execute ordered requests without descriptive apply planning while preserving fresh hashes, patches, shared compilation and read-back diagnostics."
- [ ] Review the diff and commit `feat: apply PLC writes from current execution evidence`. No preview-reference surface is delivered.

## Task 3: PR 3 - prepare and verify Network writes from current execution

Files:
- Create `TiaMcpServer/Network/NetworkExecutionPreparation.cs` and `NetworkWriteExecutionContract.cs`; modify `NetworkWriteDomain.cs`, `NetworkWritePlanner.cs`, `NetworkIdentityResolver.cs`, `NetworkVerificationPolicy.cs`, `NetworkWritePayloadBudget.cs`, `NetworkGuardedWriteModels.cs`, `NetworkToolResponses.cs` and `NetworkContractVersion.cs`.
- Create `TiaMcpServer.Contracts/Network/NetworkWriteExecutionInfo.cs` and `NetworkWriteTargetInfo.cs`; modify worker response/result carriage and strict transport validation in the same files named in Task 2.
- Modify `TiaMcpServer.OpennessWorker/Openness/Network/NetworkConfigurationTargetBinding.cs`, `NetworkDeviceConfigurator.cs`, `NetworkDeviceCreator.cs`, `SubnetLifecycleService.cs` and `NetworkPostconditionChecks.cs` plus `TiaMcpServer.OpennessWorker/Program.NetworkDispatch.cs` to capture current canonical identity/provenance.
- Create `TiaMcpServer.Tests/Network/NetworkDirectApplyTests.cs` and `NetworkWriteExecutionEvidenceTests.cs`; extend guarded budget/ordering/identity, subnet lifecycle, sparse configure outcome and strict protocol tests, fake-worker cases, conditional-member registration and test source links.
- Update only network_write's output-schema snapshot node, Network reference, architecture/ADR, improvement log and PR 3 acceptance report.

Interfaces: `NetworkExecutionPreparation.PrepareAsync(NetworkOperationRequest item)` returns a typed current prepared item plus canonical target and required-check evidence or a typed pre-dispatch refusal. It does not return a descriptive `NetworkWriteEffect`. A conditional `networkWriteExecution` DTO carries canonical current target and target-mutation provenance; a strict `NetworkWriteExecutionContract` maps it to verification keys. Required prepared recovery-result admission occurs before that item's mutation using effective budgets, with aggregate reservations retained for the remainder of the call. `NetworkWriteDomain.PlansBeforeApply(items) => false` and Network write version becomes 3.0. Preserve existing strict `configureNetworkDeviceOutcome` validation independently.

- [ ] Add `TestAllNetworkApplyPathsSkipPlanner` for all five operations, with both planner methods throwing, and `TestExecutionEvidenceDrivesVerification` for owner aliases, qualified nodes/subnets, rename, partial configure, NotSent and possibly-sent failures. Add a prepare-budget refusal that proves zero mutation dispatch.
- [ ] Add required-deletion-evidence tests: incomplete current consequence protection still blocks, complete connected deletion executes, and descriptive connection/effect snapshots are not queried merely to present apply output. Assert precise effects:[] and truthful partial outcomes.
- [ ] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~NetworkDirectApplyTests|FullyQualifiedName~NetworkWriteExecutionEvidenceTests|FullyQualifiedName~NetworkGuardedWriteBudgetTests|FullyQualifiedName~NetworkSubnetRenameTests"`; expect FAIL with a descriptive planner call or planner-owned _prepared/_effects dependence.
- [ ] Separate minimal current owner/target/settings/deletion checks from preview presentation. Keep an explicit check inventory explaining every retained read and every preview-only read; do not replace required deletion evidence with an empty inventory.
- [ ] Initialize item tracking before either path; prepare one item immediately before its mutation, preserve canonical configured target and required recovery admission, and emit no descriptive effects. Permit later valid native references to earlier creations without simulating the whole batch.
- [ ] Populate execution verification keys from actual resolved identities; use Task 2's shared verifier seam for nonmutating and unknown cases. Preserve setting order, stop-at-first-native-failure, sparse appliedSettings, partial engineeringError/outcome and ignored subnet rename detection.
- [ ] Reject malformed execution/recovery evidence without publishing it; do not convert missing outcome into apparent empty applied state. Re-run the focused command to PASS and run existing `NetworkStructuredProtocolTests` / `NetworkPortalLossTests` to PASS.
- [ ] Capture the production network_write schema and run `McpToolSchemaTests` to PASS. Run inherited PR gates and independent review, then authorized installed native direct configure/subnet/connected-delete/partial-failure controls; mark unavailable modes/containers unqualified.
- [ ] Add release-note sentence: "Network writes prepare each current target at execution time and omit descriptive apply effects while retaining deletion protection and recovery evidence."
- [ ] Review the diff and commit `feat: execute Network writes without descriptive planning`.

## Task 4: PR 4 - resolve HMI owners and verification during execution

Files:
- Create `TiaMcpServer/Hmi/HmiExecutionPreparation.cs` and `HmiWriteExecutionContract.cs`; modify `HmiWriteDomain.cs`, `HmiWritePlanner.cs`, `HmiVerificationPolicy.cs`, `HmiWriteModels.cs`, `HmiWritePayloadBudget.cs` and `HmiWriteCatalog.cs`.
- Create `TiaMcpServer.Contracts/Hmi/HmiWriteExecutionInfo.cs` and `HmiWriteTargetInfo.cs`; extend conditional worker response, typed failure/result carriage and strict host transport validation.
- Modify `TiaMcpServer.OpennessWorker/Openness/Hmi/HmiWriteDispatch.cs`, `HmiWriteRun.cs`, `HmiSoftwareLocator.cs`, `HmiTagTableWriter.cs`, `HmiTagWriter.cs` and `HmiAlarmWriter.cs` for current owner/target evidence and native-adjacent capture.
- Create `TiaMcpServer.Tests/Hmi/HmiDirectApplyTests.cs` and `HmiWriteExecutionEvidenceTests.cs`; extend existing guarded HMI ordering, verification, failure/cleanup, protocol and budget tests, conditional-member registration and explicit test links.
- Update hmi_write's schema snapshot node, HMI reference, architecture/ADR, improvement log and PR 4 acceptance report.

Interfaces: `HmiExecutionPreparation.PrepareAsync(HmiWriteOperationRequest item)` resolves the unique current owner and required target/deletion/reference evidence without producing descriptive table/tag/alarm effects. Current owner identity reaches `HmiWriteCatalog.ToWorkerRequest` without `_planner.ResolvedHmiName`. Conditional `hmiWriteExecution` carries canonical owner/object identity, old/new keys and target-mutation provenance; `HmiWriteExecutionContract` validates it before permissive decoding. The deletion target's containment identity must supersede affected table members without borrowing an old preview list. `HmiWriteDomain.PlansBeforeApply(items) => false`; HMI write version becomes 3.0.

- [ ] Add `TestAllHmiApplyPathsSkipPlanner` with all nine operations and discrete/analog alarm controls, omitted/explicit owners, duplicate owner names, rename chains, create/use and a late failure. Make planner methods throw and assert no descriptive inventory/read-back/automatic Validate.
- [ ] Add retained deletion/reference-protection tests for complete and unreadable required evidence, table/member supersession, identity-checked create cleanup, failures after a possibly changed culture, native connection refusal and fatal loss. Assert exact appliedProperties meaning and result statuses.
- [ ] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~HmiDirectApplyTests|FullyQualifiedName~HmiWriteExecutionEvidenceTests|FullyQualifiedName~HmiGuardedWriteDomainTests|FullyQualifiedName~HmiWriteVerifierTests"`; expect FAIL with planner owner lookup/effect dependence or absent execution identity.
- [ ] Initialize request tracking before either path. Resolve one current owner/target for each item, keep current required uniqueness/deletion evidence and capture canonical identities during the same traversal. Remove only inventory whose sole use is effects/reference descriptions.
- [ ] Switch verification to typed execution identity, including table containment, both rename keys and partial attempts. Preserve no replay/rollback, failed-create cleanup, failed-update retained changes, typed engineering failures, locator uniqueness and cache invalidation.
- [ ] Validate malformed/unknown execution evidence strictly; preserve unknown supersession guidance instead of inventing identities. Re-run the focused command to PASS and all existing HMI guarded protocol/partial-outcome tests to PASS.
- [ ] Capture the production schema and run `McpToolSchemaTests` to PASS. Run inherited PR gates and independent review; qualify authorized standalone/local-session table/tag/alarm operations and partial failures separately from Validate/UI compile.
- [ ] Add release-note sentence: "HMI writes resolve their current owner and affected objects during execution while retaining required deletion checks and partial-change reporting."
- [ ] Review the diff and commit `feat: execute HMI writes without descriptive planning`.

## Task 5: PR 5 - retain VCI required checks without descriptive scope planning

Files:
- Modify `TiaMcpServer/Vci/VciWriteDomain.cs`, `VciWritePlanner.cs`, `VciWriteCatalog.cs`, `VciGuardDefinitions.cs`, `VciVerificationPolicy.cs`, `VciWritePayloadBudget.cs` and `VciWriteContractVersion.cs`; create `VciExecutionPreparation.cs`.
- Modify `TiaMcpServer.OpennessWorker/Openness/Vci/VciDispatch.cs`, `VciPlanner.cs`, `VciScopeResolver.cs` and `VciWriter.cs` to share retained native-adjacent checks rather than duplicate scope inventory.
- Create `TiaMcpServer.Tests/Vci/VciDirectApplyTests.cs`; extend existing VCI root/path/conflict, worker, breadcrumb, scope, guarded protocol/budget and transaction tests and explicit test links.
- Update vci_write's schema snapshot node, VCI reference, improvement log, architecture/ADR and PR 5 acceptance report.

Interfaces: `VciExecutionPreparation.ValidateAsync(VciOperationRequest item)` performs only current mandatory project-directory/root conflict/path/required-evidence admission. It returns a typed refusal or completion, never counts/effects. Worker scope expansion retains per-leaf path/owner/status checks beside native actions. `VciWriteDomain.PlansBeforeApply(items) => false`; initialize _items before either path. `VciVerificationPolicy` retains case-insensitive workspace keys and no native read-back; VCI write version becomes 3.0. Preview retains first-item-per-workspace inventory and dependent-preview warnings; direct apply does not present stale preview dependency/count warnings.

- [ ] Add `TestAllVciApplyPathsSkipPlanner` for all six operations, create/use, repeated workspace operations, late partial failure, first-item native refusal and timeout breadcrumb. Make descriptive planner calls throw.
- [ ] Add explicit root-inside-project, representability/path-boundary, exact-root-conflict, required evidence unreadable, nested-root allowed and native owner/status/transaction tests. Include controls preserving export overwrite and native failure ordering.
- [ ] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~VciDirectApplyTests|FullyQualifiedName~VciGuardedWrite|FullyQualifiedName~VciPath|FullyQualifiedName~VciScope"`; expect FAIL with a descriptive plan call or a mandatory path check existing only in planning.
- [ ] Extract `ProjectFolderError` and required worker checks into execution preparation; execute one item at a time without initial leaf/mapping counts or simulated effects. Keep native scope expansion and fresh owner checks, import prefilter-before-transaction ordering and existing failure aggregation.
- [ ] Preserve files-remain/import-unvalidated guidance where truthful without making effect descriptions a prerequisite; retain exact path/ADS/containment/length semantics and native overwrite behavior.
- [ ] Keep fatal timeout/crash/portal_lost attempted-leaf context, invalidated binding and inspect-before-retry instructions. Re-run the focused command to PASS, then existing VCI transaction/breadcrumb/strict-envelope cases to PASS.
- [ ] Capture the production schema and run `McpToolSchemaTests` to PASS. Run inherited PR gates and independent review; qualify authorized disposable workspace/mapping export/import/delete controls without deliberately repeating known SimaticSD crash paths.
- [ ] Add release-note sentence: "VCI writes run current path and owner checks per operation without descriptive scope planning or apply-time count inventories."
- [ ] Review the diff and commit `feat: execute VCI writes without descriptive planning`.

## Task 6: PR 6 - separate lifecycle preparation and complete the common apply milestone

Files:
- Create `TiaMcpServer/ProjectLifecycle/LifecycleExecutionPreparation.cs`; modify `LifecycleWriteDomain.cs`, `LifecycleBindingStrategy.cs`, `LifecycleEffects.cs`, `LifecycleWriteResponse.cs` and `TiaMcpServer/Worker/OpennessWorkerClient.Lifecycle.cs`.
- Modify `TiaMcpServer/Safety/Pipeline/WriteRun.cs` only where preparation/confirmation integration requires it; preserve other domains' confirmation counts and post-acceptance comparison.
- Modify `TiaMcpServer/Library/LibraryWriteDomain.cs` / `LibraryVerificationPolicy.cs` only for demonstrated consistency defects, preserving existing worker guards, metadata transactions and native result identities.
- Create `TiaMcpServer.Tests/Project/LifecycleDirectApplyTests.cs` and `TiaMcpServer.Tests/Safety/Pipeline/AllMutationApplyPolicyTests.cs`; extend lifecycle, local-session, Library guarded/type/metadata, prompt and MCP tests.
- Add the explicit 63-row matrix and retained-check inventory under the dated acceptance report; update affected existing test links and six lifecycle output-schema snapshot nodes.
- Update `TiaMcpServer/Tools/ProjectWriteTools.cs` descriptions, `docs/ARCHITECTURE.md`, project lifecycle/Library references, improvement log, ADR/index and PR 6 acceptance report. Do not modify `ProjectEngineeringTools` or compile_check schema/version/access.

Interfaces: `LifecycleExecutionPreparation.PrepareAsync(LifecycleWriteItem item)` yields required current source/destination/ownership/dirty-state/preservation context without a presented `LifecycleEffects`. `LifecycleWriteDomain.PlansBeforeApply(items) => false` while `ConfirmsEveryCall=true` remains; shared runner still plans read-write prompting calls and compares fresh consequences/guards after acceptance. Nonprompting lifecycle effects are null; batch effects remain empty. Actual open/create/save-as/close verification follows the resulting binding. Lifecycle version becomes 2.0; Library stays 2.0.

- [ ] Add `TestAllMutationOperationsFollowApplyPolicy` enumerating production catalogs/discovery and asserting the exact listed 63 tool/operation pairs, all 11 mutation tools, and an explicit direct compile exclusion. Any new unclassified writable tool/operation fails the test.
- [ ] Add `TestPromptingCallsKeepFreshConsequences` for all six project lifecycle tools and all six Library global lifecycle operations in read-write, including mixed Library batches, decline/cancel/timeout, changed consequences after acceptance and zero elicitation in full.
- [ ] Add `TestExecutionCheckInventoryIsComplete`: classify every current planner guard/read in PLC, Network, HMI, VCI, Library and lifecycle as required execution or preview-only description; pair retained checks with native-call counters and omitted descriptions with forbidden-read counters.
- [ ] Add lifecycle full-mode no-plan cases for all six commands: verified/configured/invalidated/unbound preparation, unbound open/create, modified worker-owned source, borrowed/local-session preservation, existing target, archive containment, save/discard, same-owner reuse and post-mutation binding transitions. Cover all 22 Library operations on their existing nonprompting path, including collision/link/version/hash/end-state checks.
- [ ] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~LifecycleDirectApplyTests|FullyQualifiedName~AllMutationApplyPolicyTests|FullyQualifiedName~ProjectLifecycleToolTests|FullyQualifiedName~LibraryGuardedWriteDomainTests"`; expect FAIL with full-mode descriptive planning or an incomplete operation/check matrix.
- [ ] Extract required lifecycle preparation without omitting source ownership/dirty state, destination, discard/preservation or local-session protections. Preserve exact source binding lease and native-adjacent rechecks. Keep detailed read-write effects/prompt/replan; full-mode effects:null never substitutes old preview effects.
- [ ] Verify Library remains the existing no-plan baseline: no new descriptive scans in full/content apply and no removal of master-copy/type/link/hash/version/transaction protections for cosmetic uniformity.
- [ ] Add direct compile regression controls proving `compile_check` accepts its unchanged arguments, never invokes plan/replan, has no dryRun/reference field or new prompt, and preserves access/binding/output contracts. Keep source shared compilation covered independently.
- [ ] Re-run the focused command to PASS and the full all-63 mode/container matrix to PASS. Capture only six lifecycle schema nodes and run `McpToolSchemaTests` to PASS; assert Library and compile schema/version controls remain unchanged.
- [ ] Run inherited PR gates and independent review. Obtain required scoped native targets and record all rows as passed/failed/unexecuted, including unavailable local-session/hardware cases. The common apply milestone is unmet while a required gate is missing; references do not begin before that gate is accepted.
- [ ] Measure representative direct and preview-then-direct workloads: argument bytes, canonical response bytes, worker calls and elapsed time. Retain actual candidate/input/measurement conditions; do not infer model tokens or client latency from byte counts.
- [ ] Add release-note sentence: "Every mutation tool now separates descriptive preview from nonprompting apply, while lifecycle confirmation and required execution protections remain intact."
- [ ] Review the diff and commit `feat: standardize lifecycle and all mutation apply paths`. Record the milestone's candidate commit and review evidence before Task 7.

## Task 7: PR 7 - add bounded immutable storage and exclusive attempt retirement

Files:
- Create `TiaMcpServer/Safety/Pipeline/WritePreviewStore.cs`, `WritePreviewModels.cs` and `WriteMutationDisposition.cs`.
- Modify `TiaMcpServer.Contracts/Worker/WorkerFailureCategories.cs` for preview_unavailable, preview_in_use and preview_store_full with closed-category registration.
- Create `TiaMcpServer.Tests/Safety/Pipeline/WritePreviewStoreTests.cs` and `WritePreviewContextTests.cs`.
- Modify `TiaMcpServer/Worker/ProjectSessionBinding.cs`, `OpennessWorkerClient.Binding.cs`, `OpennessWorkerClient.Invocation.cs`, `OpennessWorkerClient.Lifecycle.cs` and `OpennessWorkerClient.cs` plus current transport lifecycle notifications for exact source-context epoch observation.

Interfaces: `WritePreviewMetadata(string PreviewId, DateTimeOffset ExpiresAt)`; `WritePreviewContext(long Epoch, ProjectBindingSnapshot Binding)`; `WritePreviewStore(TimeProvider time)`. Expose `TryReserve<TItem>(string toolName, IReadOnlyList<TItem> items, out WritePreviewReservation<TItem>? reservation, out WriteToolError? error)` and `TryBeginApply<TItem>(string toolName, string previewId, out WritePreviewAttempt<TItem>? attempt, out WriteToolError? error)`. A reservation owns deep immutable bytes, restores fresh input, prepares metadata/context and activates once. An attempt exposes fresh items/context and `Complete(WritePreviewAttemptOutcome outcome)`, idempotent with its winning token. Outcomes: Succeeded, FailedNoChange, AbandonedNoChange, MayHaveChanged, ContextChanged. `WriteMutationDisposition` is NoChangeProven or MayHaveChanged.

- [ ] Verify Task 6's all-63 milestone report and required gates are complete. If not, stop this PR before implementation; do not add a PLC-only reference surface.
- [ ] Add `TestPreviewAttemptStateMachine`, `TestPreviewContextIncludesUnboundEpoch` and `TestPreviewQuotaExpiryAndImmutability` using a local deterministic TimeProvider subclass. Include concurrent callers, duplicate completion tokens and reservation rollback.
- [ ] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~WritePreviewStoreTests|FullyQualifiedName~WritePreviewContextTests"`; expect FAIL with mutable snapshot, duplicate in-flight winner or incorrect retirement/failure counter.
- [ ] Implement canonical UTF-8 snapshots, host-selected request type/tool/private-session checks, Ordinal IDs from 32 random bytes as wp1_ plus 43 base64url characters, monotonic 10-minute lifetime and paired UTC display expiry. Count reserved/available/in-flight entries together: 16 entries and 16,777,216 combined request bytes. Never evict live entries or renew on access.
- [ ] Implement atomic Available -> InFlight before asynchronous preparation/prompt. Success/no-op success, MayHaveChanged and ContextChanged retire. First FailedNoChange releases with counter 1; second retires. AbandonedNoChange releases without increment/reset. Concurrent losers get preview_in_use and change no counter. Expired completion never returns a usable entry.
- [ ] Ground runtime hello/session generation before publication, even while unbound, without opening a project or creating a native attachment solely for a receipt. Track context epoch across every binding state, bind/switch-away-back, worker restart, attachment/owner loss/recovery and deliberate lifecycle context transition. SameBinding plus epoch are both required; unbound restart/attachment change must invalidate even with otherwise equal unbound snapshots. No path-only fallback or configured-to-verified/recovery migration during reference admission; intended open/create/save-as/close transitions after successful source-context admission remain legal and retire the used reference.
- [ ] Keep capacity failures independent of direct apply; no tombstones, execution-result cache, automatic replay or resetting attempts after unknown outcomes. Re-run the focused command to PASS including byte-exact quota boundaries and no-renewal tests.
- [ ] Review store races, memory accounting and context ownership independently; review the diff and commit `feat: add bounded preview storage and exclusive attempts`. This internal task alone exposes no public references.

## Task 8: PR 7 - integrate reference attempts with the guarded pipeline and audit

Files:
- Modify `TiaMcpServer/Safety/Pipeline/WriteRecords.cs`, `IWriteDomain.cs`, `WriteExecution.cs`, `WriteRun.cs` and `WriteAudit.cs`.
- Modify `TiaMcpServer/Program.cs` and `TiaMcpServer.Tests/TestSupport/McpProtocolTestHarness.cs` for session-store/TimeProvider injection.
- Modify `TiaMcpServer.Tests/Safety/Pipeline/PipelineFakes.cs`, `WriteExecutionTests.cs` and existing failure/resolved-audit tests; create `WritePreviewExecutionTests.cs` and `WritePreviewAttemptFailureTests.cs`.
- Modify `TiaMcpServer.Tests/Json/ConditionalMemberRegisterTests.cs` for optional audit metadata and every host response model used by the publication seam.

Interfaces: `WriteCall<TItem>(IReadOnlyList<TItem> Items, bool DryRun, string? PreviewId = null)`; optional `WritePreviewMetadata? Preview` on WriteReport. `IWriteDomain` gains default-off `SupportsPreviewReferences => false`, `IsPublishablePreview(TResponse response) => false` and `WriteMutationDisposition MutationDisposition(TItem item, WorkerCallResult result)`. The default returns NoChangeProven only for authoritative NotSent. WriteExecution injects one WritePreviewStore per host/MCP session and uses TryBeginApply before constructing item-sized WriteRun arrays or asynchronous source preparation. Conditional `WriteAuditRecord.PreviewId` is correlation only; existing requested/resolved operation evidence remains unchanged.

- [ ] Add `TestPreviewAttemptStateMachine` pipeline cases for first/second failed zero-change apply, intervening decline/cancel, success/no-op success, late item failure after earlier success, native refusal, possibly-sent timeout, malformed outcome, compose/verification/serialization exception and best-effort audit failure.
- [ ] Add context checks before preparation, after preparation and immediately before first mutation, including configured promotion, explicit recovery, unbound open/create and expiry during confirmation. Concurrent requests with the same ID must yield one attempt/prompt winner.
- [ ] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~WritePreviewExecutionTests|FullyQualifiedName~WritePreviewAttemptFailureTests|FullyQualifiedName~WriteExecutionFailureAuditTests"`; expect FAIL with an unguarded duplicate attempt, incorrectly reusable sent failure or lost restored audit operations.
- [ ] Begin reference exclusivity atomically before async work, restore the original items/context and perform current validation/access/binding/capability/lease/mandatory checks. Preserve detailed prompting and fresh consequence comparison. Never substitute an empty request for lookup failure.
- [ ] Accumulate whole-attempt disposition across all items and operation-owned side effects, including output files and temporary source preparation/cleanup; exclude independent audit/diagnostic bookkeeping. Initially release failed attempts only with authoritative host/transport proof that no mutation request was sent; target-only MutationStarted:false, categories, empty applied lists, missing outcomes and successful cleanup do not independently prove no change.
- [ ] Finalize the winning attempt on every return, exception, cancellation, verification, budgeting and serialization path before releasing exclusivity. Earlier mutation or unknown dispatch always retires; pre-dispatch decline/cancel is AbandonedNoChange. Expiry is checked before first dispatch after a prompt; it does not abort a batch already executing.
- [ ] Reserve immutable preview input before asynchronous work. Under the pinned lease, prepare metadata, budget candidate output, check explicit domain publishability, then atomically recheck context/expiry/cancellation and activate before single canonical serialization. Failed publication strips receipt fields from the already-budgeted failure without rebuilding an unbudgeted success.
- [ ] Remove a newly activated entry if final serialization fails. Audit append is best effort; failure does not falsify an already delivered receipt or reinstate a used reference. Preserve original requestedOperations, exact attempted patch resolvedOperation, item statuses and delivered canonical document/hash without another source/XML copy.
- [ ] Re-run the focused command to PASS and existing shared pipeline tests to PASS, including non-opted-in controls, exact admission/quota/metadata boundaries and all outer/leased cleanup paths.
- [ ] Review attempt-finalization and publication races independently; review the diff and commit `feat: integrate preview attempts with guarded execution`.

## Task 9: PR 7 - expose exact reference sources on all 11 mutation tools

Files:
- Create `TiaMcpServer/Tools/WritePreviewArgumentsTool.cs` for shared raw presence validation and schema alternatives; modify `McpToolRegistration.cs` and `ProjectWriteTools.cs`.
- Modify `TiaMcpServer/Plc/PlcWriteTools.cs`, `Network/NetworkWriteTools.cs`, `Hmi/HmiWriteTools.cs`, `Vci/VciWriteTools.cs` and `Library/LibraryWriteTools.cs`.
- Modify all five write-domain response/budget files and `LifecycleWriteResponse.cs` / `LifecycleWriteDomain.cs` for conditional receipt metadata and explicit publishability; update the five domain version owners and lifecycle version.
- Extend production-protocol tests in each domain and lifecycle; create `TiaMcpServer.Tests/Tools/AllMutationPreviewReferenceTests.cs`, `WritePreviewArgumentTests.cs` and `WritePreviewSchemaTests.cs`.
- Modify `TiaMcpServer.Tests/Tools/ToolOutputContractConformanceTests.cs`, `McpToolSchemaTests.cs`, `Snapshots/output-schemas.json`, conditional-member registration and explicit test source links.

Interfaces: each batch method accepts `TItem[]? operations = null, bool dryRun = false, string? previewId = null` plus its existing injected services/cancellation. Each scalar lifecycle method adds `string? previewId = null` and makes previously required scalar CLR strings nullable/optional solely to support the reference branch; the raw direct-command branch still enforces the existing required arguments. Its internal ExecuteAsync passes optional previewId through WriteCall, and restored LifecycleWriteItem.Operation remains fixed by the called tool. All domains opt into SupportsPreviewReferences and explicit IsPublishablePreview. Receipt fields previewId/previewExpiresAt occur together only on publishable previews. Final versions: PLC/Network/HMI/VCI 3.1; lifecycle 2.1; Library 2.1.

- [ ] Add `TestEveryMutationOperationSupportsReferences` over all 63 operation rows through production MCP tools: direct, dry preview and ID apply, with canonical text/structured/audit comparisons and no reference fields on compile/read/bind tools.
- [ ] Add raw source-shape cases: both/neither batch sources, explicit null/empty/invalid IDs, operations:null, ID plus dryRun:true, unknown/wrong-case/duplicate roots, retired arguments and malformed booleans. Reject before SDK binding with canonical validation_error and no audit/worker activity; keep entered semantic/admission denials audited.
- [ ] For lifecycle, accept `save_project({})` and `close_project({})` with existing defaults, reject an ID plus any scalar command argument even null/default-valued, and preserve existing legal direct null/default semantics. Direct required command sets are open: projectPath; create: projectDirectory/projectName; save-as: targetDirectory/targetName; archive: archiveDirectory/archiveName. Save and close require no scalar argument. Reference input never invents operations[].
- [ ] Add `TestBoundedDiagnosticsStillPublishPreview`: a replacement changing over 40 lines or a long XML line still receives a receipt when the immutable request/mandatory evidence fit. Missing mandatory identity/guard evidence, omitted request/value or failed protected-summary delivery withhold it. Test receipt-overhead exact boundaries and all response budget omission layers.
- [ ] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~AllMutationPreviewReferenceTests|FullyQualifiedName~WritePreviewArgumentTests|FullyQualifiedName~WritePreviewSchemaTests|FullyQualifiedName~ToolOutputContractConformanceTests|FullyQualifiedName~ConditionalMemberRegisterTests"`; expect FAIL with missing source alternatives, absent all-tool receipts or a bounded excerpt wrongly preventing publication.
- [ ] Use the existing closed-tool decorate seam so presence-sensitive validation precedes SDK binding. Advertise batch oneOf operations/reference branches; advertise lifecycle direct/reference alternatives while retaining legal empty direct commands. Keep injected services out of public schemas and keep operation-member validation strict.
- [ ] Store effective original defaults/IDs/selectors/hashes/content/order and deserialize fresh inputs. Direct and ID apply share all execution checks and effects behavior; reference lookup never binds, recovers, opens or migrates context.
- [ ] Implement per-domain publication predicates that distinguish intentional diagnostic shortening from incomplete required evidence. Preserve semantic eligibility separately during budgeting: optional diff/excerpt truncation or compaction alone must not turn preview root success false; required request/evidence/protected-summary delivery failure still prevents a receipt. Include encoded receipt metadata in protected-core/final budgeting and conditional-member registration; never echo stale preview effects during apply.
- [ ] Capture output schemas from `McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full)` and real `Client.ListToolsAsync()`. Replace only the 11 changed write-tool nodes; remove temporary exporters. Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~McpToolSchemaTests"` to PASS under default and 2025-11-25 protocols and inspect the snapshot diff for compile/read/bind changes.
- [ ] Re-run the focused command to PASS, including store saturation with direct-apply controls, all-source immutability, unbound context restart, stale content after preview, wrong-tool IDs and preserved lifecycle prompts.
- [ ] Review schemas/source validation/publication budgeting independently; review the diff and commit `feat: expose preview references across all mutation tools`.

## Task 10: PR 7 - qualify complete references and publish durable contracts

Files:
- Extend only implicated existing fake-worker/protocol/budget/transport fixtures and `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj` when integration reveals missing source wiring.
- Update `docs/ARCHITECTURE.md`, the existing PLC/Network/HMI/VCI/Library/project lifecycle supported-operations references, `docs/IMPROVEMENT_LOG.md`, applicable examples and `AGENTS.md`.
- Add the next unused `docs/adr/` record for common apply/reference ownership and update its index.
- Create the dated `docs/superpowers/acceptance/reports/` PR 7 report and update `docs/superpowers/README.md`. Prune only completed owning-PR transient documents/links after durable material exists; do not delete still-needed staged plans.

Interfaces: all 11 mutation/lifecycle tools expose the same exact-input reference semantics and bounded session store. The complete 63-row matrix covers original direct and reference applies, current mode/container eligibility, exact context and retirement. Compile remains an unchanged direct exclusion. Durable docs describe semantic success, execution uncertainty and delivery budgeting separately.

- [ ] Add `TestReferenceApplyPreservesCanonicalAudit` and complete the spec's named acceptance tests with positive and rejection cases for every tool/operation. Assert original patch plus exact resolved attempted input, no rejected payload echo, no extra source copy and identical delivered text/structured/audit document/hash.
- [ ] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~AllMutationPreviewReferenceTests|FullyQualifiedName~WritePreview|FullyQualifiedName~AllMutationApplyPolicyTests|FullyQualifiedName~WriteExecutionResolvedAuditTests"`; if integration fails, record the narrow assertion, fix minimally and re-run that focus to PASS before broad gates. Commit each real fix with its own regression evidence.
- [ ] Run inherited PR gates serially, including packaging when triggered. Confirm test discovery/counts and source wiring; review materially changed logic coverage against the repository threshold or 80% where meaningful without adding implementation-mirroring tests.
- [ ] Have an independent reviewer audit source-context epoch completeness, in-flight exclusivity/finalization, whole-attempt no-change proof, first/second failure policy, prompting semantics, unbound lifecycle sources, quotas/expiry, strict envelopes and unchanged compile behavior. Resolve every required finding before native qualification.
- [ ] Prepare exact installed candidate, mode, effective budgets, disposable project/global-library/workspace/hardware/local-session targets and fixture cleanup list; obtain scoped authorization before live mutations. Never infer authorization from this plan or perform unrelated save/close/check-in/cleanup.
- [ ] Qualify representative native preview/reference and direct controls across all six domains: stale hashes, create/use, late partial failure, ID success/no-op retirement, first/second host-proven zero-change failures, decline/cancel, same-ID concurrency, context switch/restart/recovery, source linked-instance checks and safe loss handling. Mark every unexecuted operation/mode/container row explicitly rather than generalizing examples.
- [ ] Measure direct and preview/reference workloads after standardization: argument/canonical response bytes, worker-call counts, elapsed time, bounded-diff receipt eligibility and store saturation. Compare equivalent full replacements and small patches; references save retransmission only in workflows actually using preview. Claim model-input token/latency changes only if directly observed.
- [ ] Document shared 10-minute/16-entry/16-MiB limits, immutable original context, failure-count retention through decline/cancel, immediate retirement on success/partial/unknown, no replay, scalar defaults, bounded diagnostics and inspection guidance. Record native qualification limits separately from offline/stub/installed compilation.
- [ ] Add release-note sentence: "All mutation tools accept reusable preview references with exact-context checks, exclusive attempts and conservative success or failure retirement."
- [ ] Re-run docs verification and `git diff --check`, review the complete diff/status and commit `docs: document common apply and all-tool preview references`. Remote publication still requires explicit approval. Prune eligible working files in a separate reviewed `docs: prune working files` commit once durable replacements exist.

## Requirement coverage and execution boundary

| Spec requirement / named acceptance | Tasks | Required evidence |
| --- | --- | --- |
| S-1 TestAllMutationOperationsFollowApplyPolicy | 2-6, 9-10 | Exact 63-row production catalog/discovery matrix; forbidden planner/replanner counters; compile exclusion |
| S-1 TestPromptingCallsKeepFreshConsequences | 6, 8-10 | All six project and six Library lifecycle prompts; fresh consequence changes deny; full/content zero elicitation |
| S-2 TestExecutionCheckInventoryIsComplete | 1-6 | Explicit retained-versus-descriptive read/guard inventory plus native/forbidden-read counters |
| S-2 TestPlcLinkedInstanceGuardsNeverMutate | 1-2, 10 | Typed shared inspector, linked FB/FC/UDT formats/states, direct/nested-group and plain/committed controls |
| S-3 TestExecutionEvidenceDrivesVerification | 2-4, 6 | Canonical execution keys, strict envelope rejection, nonmutating/started/unknown supersession |
| S-3 TestPatchSharedCompileReadBackSurviveDirectApply | 2, 9-10 | Original patch fresh resolution, exact attempted audit, committed partial compile before re-export, diagnostic comparison/hash reuse |
| S-4 TestEveryMutationOperationSupportsReferences | 7-10, gated by 6 | All 63 source alternatives through the existing 11 production tools; no compile/read/bind references |
| S-5 TestPreviewAttemptStateMachine | 7-10 | Exclusive attempt, success/no-op retirement, two proven-zero-change failures, noncounting abandon, no replay |
| S-5 TestPreviewContextIncludesUnboundEpoch | 7-10 | Exact snapshot plus epoch, unbound attachment/restart, switch-away/back, recovery/promotion refusal |
| S-5 TestPreviewQuotaExpiryAndImmutability | 7-10 | Deep UTF-8 snapshots, all-tools quota boundaries, expiry at publication/prompt/dispatch, no eviction/renewal |
| S-6 TestBoundedDiagnosticsStillPublishPreview | 8-10 | Over-40-line/long-line receipt control versus missing mandatory evidence/protected delivery failures |
| S-6 TestReferenceApplyPreservesCanonicalAudit | 8-10 | Restored original operations, conditional ID correlation, exact canonical/audit identity and safe exception finalization |
| S-7 Qualification, independent review and measured value | Every PR | Recorded red/green checks, build/test gates, native pass/fail/unexecuted rows and measured workload conditions |

All checkboxes remain unchecked until their own evidence exists. Structural plan-check completeness does not establish implementation or runtime correctness and does not authorize executing this plan.

## Plan validation procedure

Use `procoder:source-command-plan` for structural validation. The canonical spec/plan remain under `docs/superpowers/`; the controller's hardcoded `.procoder/specs/` and `.procoder/plans/` paths may receive checksum-identical temporary mirrors only when those paths are unused. Never overwrite user mirrors or modify procoder configuration to evade project policy. Run spec check for `write-preview-execution` before plan check, use the installed native Windows launcher-resolved binary if Git Bash cannot start, then remove only task-owned mirrors. Do not seed tasks, implement runtime code, mutate TIA or publish because a controller says COMPLETE.
