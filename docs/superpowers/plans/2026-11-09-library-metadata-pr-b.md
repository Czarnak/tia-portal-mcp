# Library metadata PR B Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add global-library comment/language edits, finish the agreed library contribution scope, and prepare PR B to close [#218](https://github.com/Czarnak/tia-portal-mcp/issues/218).

**Architecture:** Extend the existing `library_write` pipeline with two global-only operations. Each mutation resolves current native state and performs its setters and one end-state check inside a library transaction; the existing host pipeline supplies admission, optional planning, typed results, verification and audit.

**Tech Stack:** C#; net10.0 host/tests, net48 Siemens worker, netstandard2.0 contracts; installed TIA Portal V21; existing xunit, FakeWorker and reference stubs.

**Spec:** [Shared metadata design](../specs/2026-11-08-library-metadata-design.md), sections 8-12, with the owner decisions below. PR A is merged in PR #230 (`315f1683`). Continue on `feat/library-metadata-pr-b`; do not restart or replay Task 0.

## Global constraints and owner decisions

- This session prepares and commits the plan only. Implementation, more live work and remote writes are separate work.
- Owner direction, 2026-11-09: TIA remains the refusal authority. No read-only/write-protection guard, native support catalogue precheck, 32767-character cap, collision guard, implicit save/reopen/retry, fingerprint or extra verification RPC.
- B6 resolves the former acceptance mismatch: read-only comments can be `worker_operation_failed` (`EngineeringNotSupportedException`); read-only language activation can be `engineering_rejected` (`EngineeringTargetInvocationException`). Keep `WorkerExecution.Classify` unchanged; `portal_lost` still wins. Categories follow types, never messages.
- B8 safely rejected 100,000 characters. Its message mentions 32767; no boundary maximum was independently tested. Preserve the effective item budget, and let Portal refuse admitted oversized comments. Existing author caps from PR A remain unchanged.
- Keep the designed end-state check for ignored/altered text or unintended side effects. It does not duplicate a proved native refusal. No read after a failed transaction or separate post-commit verification read.
- `set_comments` and `set_languages` require `scope:"global"` and an already-open library in the bound Portal. Exact selector resolution and null native lookups remain necessary to identify targets; unsupported/missing language/comment targets use `target_not_found`.
- Keep write contract `2.0`, read contract `1.0`, 1-50 ordered unique-ID items, stop at first failure, no batch rollback/replay. Metadata-only calls do not prompt; lifecycle-containing read-write calls still prompt once and plan first. Full/content-only apply stays unplanned.
- Shared host/worker validation owns request shape, culture syntax and canonical uniqueness. The host checks aggregate comment text against `StructuredOperationBatchPayloadBudget.MaxItemChars`; no new worker budget setting or lower fixed cap. Delivery uses effective `MaxItemChars`/`MaxDocumentChars` and existing whole-value omission.
- Last writer wins. No language removal, project-library metadata, post-creation author/name/version/family/copyright setters, or additional maintenance operations. Close #218 with the delivered scope and explicit follow-ups, without claiming the contributor's whole bundle was ported.
- Final implementation sequence: live acceptance, permanent docs, pruning including PR A handoff files, then an empty contributor-credit commit. No implementation/doc commit follows that credit commit.

## Review focus

1. Case/alias cultures must canonicalize before duplicate checks; malformed text shapes must fail before worker activity (Task 1).
2. Unrelated **nonempty** comments and unrequested language settings must survive edits; the fresh native fixtures weakly covered that case (Task 2).
3. Commit or scope-disposal failure must not claim rollback or publish a known final state, even if a setter failed first (Task 2).
4. A missing-comment producer must match both library and culture; unsupported cultures cannot become producible (Task 3).
5. Omitted large receipts and failed metadata attempts must not turn execution into failure or supersede unrelated library assets (Tasks 3-4).

## File map

| Responsibility | Existing files / focused additions |
| --- | --- |
| Public/shared admission | `TiaMcpServer/Library/LibraryWriteItem.cs`, `LibraryWriteCatalog.cs`; `TiaMcpServer.Contracts/Library/LibraryWriteMethods.cs`, `LibraryWriteModels.cs`; new `TiaMcpServer.Contracts/Shared/CultureNameRules.cs`; existing HMI culture validation |
| Native transaction and comparison | New `TiaMcpServer.OpennessWorker/Openness/Library/LibraryMetadataWriter.cs` and `LibraryMetadataState.cs`; existing `LibraryWriter.cs`, `LibraryWritePlanReader.cs` |
| Host presentation and verification | `TiaMcpServer/Library/LibraryWriteModels.cs`, `LibraryWritePlanner.cs`, `LibraryWriteDomain.cs`, `LibraryWritePayloadContract.cs`, `LibraryVerificationPolicy.cs`; existing budget code only if tests demonstrate a missing seam |
| Offline seams | `reference-stubs/Siemens.Engineering.Base/Core.cs`, generated `ref/`; `TiaMcpServer.Tests/TestSupport/LibrarySiemensDoubles.cs`, `HmiUnifiedSiemensDoubles.cs`, `VciSiemensDoubles.cs`; `TiaMcpServer.Tests.csproj`; existing FakeWorker library scenario and contract/schema tests |
| Permanent docs and pruning | Library operation reference, architecture, improvement log, ADR-0019/index, README, AGENTS, JSON-contract roadmap, report/evidence indexes; docs verifier and its tests |

No new pipeline, service interface, transport method family, dependency or general-purpose fixture framework is needed. Add explicit test-project links for the two new worker source files; the host Library sources are already globbed.

For Tasks 1-4, the focused RED/GREEN command is:

```powershell
dotnet test TiaMcpServer.Tests --filter "Category!=Packaging&(FullyQualifiedName~Library|FullyQualifiedName~CultureNameRulesTests|FullyQualifiedName~HmiWriteCatalogTests)"
```

Run it after writing the named failing cases, then after the implementation. A task is ready to commit only when its relevant tests pass; Task 4 additionally runs the full gates below.

## Task 0: Native evidence — complete

- [x] B1-B7/B9-B10 and B8 in its own process/Portal completed at source `76cee08d`; report/evidence committed as `c3d3e4b1`.
- [x] [Native report](../acceptance/reports/2026-11-09-library-metadata-pr-b-native-probes.md) records exact text, autoactivation, empty comment creation, rollback, both read-only exception types and recoverable B8 refusal.
- [x] Owner's latest direction resolves the B6 decision without changing classification. Do not repeat native boundary/crash searches as implementation setup.

## Task 1: Request contracts, shared culture validation and method registration

**Files:** Admission/contracts from the file map; `TiaMcpServer/Library/LibraryWriteTools.cs`, `TiaMcpServer/Hmi/HmiWriteCatalog.cs`; Base `Core.cs`, new compile-only `reference-stubs/TiaMcpServer.OpennessReferenceProbe/LibraryMetadataReferenceSurface.cs` and generated reference assemblies. Tests: `LibraryWriteMethodsTests.cs`, `LibraryWriteCatalogTests.cs`, `LibraryWriteCatalogRegistrationTests.cs`, `LibraryWorkerDispatchTests.cs`, `Hmi/HmiWriteCatalogTests.cs`; new `Shared/CultureNameRulesTests.cs`.

**Interfaces produced:**

- `CultureNameRules.CanonicalName(string? culture) -> string?`: null for blank/invalid/invariant names; otherwise `CultureInfo.GetCultureInfo(culture).Name`. Reuse it for HMI validation/duplicate checks without changing accepted HMI payload text or behavior.
- Add `Comments: List<LibraryCommentInfo>?`, `ActivateLanguages: List<string>?`, `EditingLanguage: string?`, `ReferenceLanguage: string?` to `LibraryWriteItem` and `LibraryWriteRequestInfo`. Reuse the existing `{culture,text}` DTO; validation rejects its null members.
- `LibraryMetadataChangeInfo`: nullable `Comments: List<LibraryCommentChangeInfo>`, `ActivatedLanguages: List<string>`, `EditingLanguage`/`ReferenceLanguage: LibraryLanguageChangeInfo`.
- `LibraryCommentChangeInfo`: required canonical `Culture`, nullable `Before`/`After` strings. `LibraryLanguageChangeInfo`: nullable `Before`/`After` strings. Nested members serialize explicit nulls; the enclosing `MetadataChange` member is conditional on plan/result/outcome/effect, registered in Task 4.

- [ ] **RED:** Add `MetadataOperations_AreGlobalOnly` asserting 22 operations, 16 global writes, 14 project writes, 32 methods including plans, no `library_project_set_*`; both global methods are `ProjectMutation` and map to `library_write` on standalone/local-session roots. Keep 9/21/21 tools.
- [ ] Add `MetadataShapeAndCanonicalCultures` cases: project scope, missing selectors, unsupported members, empty/null comment entries/text, duplicate `en-US`/`en-us`, malformed/blank/invariant cultures, empty activation list, and no language setting. Neutral `en` and a syntactically valid unsupported culture pass admission for native resolution. Empty comment text is valid.
- [ ] Add `CommentAggregateUsesEffectiveBudget`: sum text lengths across an item using an overflow-safe accumulator; reject above the configured budget before worker activity, accept admitted 100,000-character input with a raised budget. No 32767 rule. Test exact LF/CRLF/whitespace preservation and no per-comment truncation.
- [ ] Run the focused new tests; observe missing members/operations/helper failures before implementation.
- [ ] Implement the DTOs, closed member tuples and new `MetadataOperations` list in `LibraryWriteMethods`. Exclude metadata operations explicitly from `ProjectLibraryWrites`; retain lifecycle-only confirmation and existing table-driven dispatch/policy/capability registration. Copy/canonicalize accepted library request cultures in `ToWorkerRequest`; worker validation accepts only the same shapes and canonicalizes lookups too. Reject these members on old operations. Extend existing valid-request/reflective member fixtures for the new list fields, and add both operation names to the tool description now; finalize its behavior guidance in Task 6.
- [ ] Add only needed installed-API-proved stub members: `LanguageComposition.Find(CultureInfo)`, `LanguageAssociation.Add(Language)`, editing/reference setters, `MultilingualTextItemComposition.Find(Language)`. Pin their signatures with the compile-only reference surface against installed APIs and generated stubs. Do not copy assumptions from the issue bundle.
- [ ] GREEN: run focused library/culture/HMI tests, `pwsh -NoProfile -File scripts/verify-reference-stubs.ps1 -Update`, then the same command without `-Update`; build `reference-stubs/TiaMcpServer.OpennessReferenceProbe` with `dotnet build -m:1` once with `/p:UseTiaPortalReferenceStubs=true` and once with `/p:UseTiaPortalReferenceStubs=false`; then run `dotnet test TiaMcpServer.Tests --filter "Category=Packaging"` serially. Commit `feat: add library metadata write contracts`.

## Task 2: Transactional native edits and narrow end-state checking

**Files:** New writer/state files; `LibraryWriter.cs`; doubles and `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`. New tests: `LibraryMetadataWriterTests.cs`, `LibraryMetadataStateTests.cs`.

**Consumes:** Task 1 DTOs and shared validation; existing `LibraryWriteTargets.Library`, identity construction and `WorkerExecution` classification.

**Interfaces produced:**

- `LibraryMetadataWriter.Write(TiaPortal portal, ProjectBase project, LibraryWriteRequestInfo item) -> LibraryWriteResultInfo`.
- `LibraryMetadataWriter.ReadState(GlobalLibrary library) -> LibraryMetadataState`, strict native reads of all comments, active languages, editing/reference; no tolerant diagnostic substitution.
- Worker-local `LibraryMetadataState`, containing managed `Comments`, `ActiveLanguages`, `EditingLanguage`, `ReferenceLanguage`; pure `Mismatches(LibraryWriteRequestInfo item, LibraryMetadataState before, LibraryMetadataState after) -> IReadOnlyList<string>`. Keep this file Siemens-free. Do not reuse the tolerant public `LibraryMetadataReader` for mutation checks.

- [ ] **RED:** `MetadataState_OrdinalChangesAndUnrelatedText` tests exact special text; clearing with an empty or removed item; unchanged unrelated nonempty comments; unchanged active/editing/reference after comments. `MetadataState_OnlyRequestedLanguageChanges` tests retention of old languages, only requested additions (including autoactivation), unrequested setting preservation, and new empty items only for newly active cultures.
- [ ] Add writer tests proving all native lookups finish before setters, strict before-read failure makes zero setters, transaction persistence is the resolved global library, and one successful commit occurs. Already-active activation and unchanged editing/reference skip setters.
- [ ] Add failure tests: later setter throws after earlier changes; end-state mismatch; `CanCommit:false`; partial end-state read. Assert rollback and captured before/observed-after values, with unreached `after:null`, no reread and no save/reopen. All successes return `library` plus only the named `metadataChange` portions.
- [ ] Add native type tests for `EngineeringNotSupportedException` versus `EngineeringTargetInvocationException`, plus fatal/current-execution disposal classification. Add `CommitOrDisposeFailure_HasNoOutcomeAndNeverRereads` for `CommitOnDispose`, transaction disposal and access disposal, including disposal replacing an earlier setter error.
- [ ] Run the new tests RED; implement the two focused files and dispatch after existing `LibraryWriter.Write` validation/capability gates. Use one exclusive-access/library-transaction scope per item; native `Languages.Find` and `Comment.Items.Find` nulls identify missing targets. Do not inspect read-only flags or introduce a supported-language inventory guard.
- [ ] Resolve all requested targets, read before-state, run comment setters or activation/editing/reference setters in that order, read/check end-state, check `CanCommit`, then request commit. Scope disposal must finish before returning success.
- [ ] Keep setter/check failures separate from commit/disposal failures: retain a pending pre-commit exception without requesting commit; dispose transaction/access first. Only successful rollback disposal permits the typed `LibraryWriteOutcomeInfo.MetadataChange` and rollback message. If commit/disposal fails, discard the pending outcome, preserve classification/engineering evidence, and report unknown state with inspect-before-retry guidance. Never read native state after failure; use the already captured identity/state. Preserve shared Portal-loss precedence without a new liveness probe.
- [ ] GREEN: focused writer/state tests and all existing Library tests. Reuse existing transaction `RollbackAction`, `CommitAction`, `DisposeFailure` double seams; add only missing hooks. Commit `feat: implement transactional library metadata edits`.

## Task 3: Previews, same-call language dependencies and verification keys

**Files:** `LibraryMetadataWriter.cs`, `LibraryWritePlanReader.cs`; host planner/domain/effect/verification files. Tests: `LibraryWritePlanReaderTests.cs`, `LibraryWritePlannerTests.cs`, `LibraryVerificationPolicyTests.cs`, `LibraryGuardedWriteDomainTests.cs`.

**Interface produced:** `LibraryMetadataWriter.Plan(GlobalLibrary library, LibraryWriteRequestInfo item, LibraryWritePlanInfo plan) -> void`, called once the existing plan has resolved its library/identity. No transaction or setter in planning.

- [ ] **RED:** `MetadataPreview_NeverMutates` asserts requested-only changes with current before-values, predicted activations including inactive editing/reference, and no transaction/save calls. Native plan exceptions retain Observe classification.
- [ ] `MetadataCommentProducer_MatchesLibraryAndCulture` asserts a supported inactive comment yields `MissingTargets` identity `{kind:"comment",name:<canonical culture>}` on the exact global library, empty folder path/null folder kind. An earlier same-library `set_languages` activation/editing/reference supplies it; use existing last-match 1-based `createdByItem`. Multiple missing cultures must each have a producer. Other libraries/cultures/unsupported languages do not qualify.
- [ ] `MetadataCalls_KeepStagingRules` asserts preview and lifecycle-prompt calls plan all items; newly created/opened/retrieved libraries still require a later call in planned mode. Full/content-only apply does not plan; an activation then comment can succeed in one call against current native state.
- [ ] `MetadataKeys_DoNotSupersedeAssets` asserts root-plus-`metadata` keys for both own/later roles, before no-effect/no-target fallbacks. Later successful or attempted metadata edits supersede earlier metadata, including outcome-free name/path alias failures; they do not supersede folder/master-copy/type items or `create_global`. Whole-library lifecycle items still supersede metadata.
- [ ] Run RED, then implement the plan branch, missing-comment identities and `Produces` branch; call the strict metadata planner directly, outside the existing tolerant `Observe` wrapper that would invent a state-unverifiable guard. Project/validate `MetadataChange` into effects on preview and apply. Preserve resolved library identity from failed outcomes when available; retain conservative selector-alias matching when absent. Keep `HasReadBack:false`.
- [ ] GREEN: focused planning/verification/domain tests and existing Library tests. Commit `feat: integrate library metadata planning and verification`.

## Task 4: Strict payloads, budget omission and MCP regression

**Files:** `LibraryWritePayloadContract.cs`, conditional register; existing budget code if required; `TiaMcpServer.FakeWorker/Library/LibraryWriteScenario.cs`; `LibraryWriteContractDecodeTests.cs`, `LibraryWriteBudgetTests.cs`, `LibraryWriteMcpTests.cs`, `Tools/McpToolSchemaTests.cs`, `Tools/Snapshots/output-schemas.json`; reuse worker transport/outcome-copy tests.

- [ ] **RED:** `MetadataChange_StrictAcrossPlanSuccessAndOutcome` covers absent versus conditional-null metadataChange, required explicit nested nulls, missing/unknown/miscased/duplicate members and null list elements. Reject malformed evidence as `protocol_error` without echo, including the raw `libraryWriteOutcome` gate before permissive envelope decoding. Failure `after:null` is valid; successful requested comment values are exact strings.
- [ ] `MetadataBudget_OmissionPreservesExecutionAndVerification` uses oversized existing before-text/JSON expansion at default and raised limits. Assert whole effect/item value omission through the existing budget layer, no partial comment arrays or truncation; applied item/verification remain trusted while delivery success becomes false with omission/read guidance. Cover outcomes as well as previews/successes.
- [ ] MCP tests pin read-only tool absence, writable unbound refusal, local-session global capability, no metadata-only elicitation, exactly one lifecycle elicitation, zero planning on actual content/full calls, dry-run dependencies, stop-at-first-failure/skipped later items, native type evidence, Portal-loss invalidation and audit semantics. Retain PR A author/read regression and canonical text/structured-content equality.
- [ ] Run RED; extend the existing fake-worker scenario and strict validators/register. Add operation-specific shape/null checks for `MetadataChange` on plan, result and outcome; do not invent manual required-member lists or an alternative envelope. Register all four new conditional wrapper members with when-present descriptions.
- [ ] Regenerate the output-schema snapshot from built production `tools/list`, using the existing harness; only `library_write` gains these response/input fields. Keep `metadataChange` out of input members and `creationLanguage` absent. Rename the PR-A-only schema test to describe both slices; verify both protocol versions in `OutputSchemasMatchMain`.
- [ ] GREEN: run the focused tests, then these gates serially:

```powershell
dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"
dotnet test TiaMcpServer.Tests --filter "Category=Packaging"
dotnet build TiaMcpServer.slnx -m:1 /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"
```

- [ ] Review failures from their actual output; record retries and uncertainty rather than silently relabeling a failed run. Commit `test: qualify library metadata write contracts` after all applicable gates pass.

## Task 5: Built-MCP live acceptance

**Files:** New `docs/superpowers/acceptance/reports/YYYY-MM-DD-library-metadata-pr-b-live-verification.md`; verified native evidence/index as appropriate. Reuse the Task 0 report; do not rerun it wholesale.

- [ ] Run the exact built candidate in a disposable Portal with a verified disposable project binding and fresh global-library fixtures. The supplied `LibProject.ap21`/`Library1.al21` paths require fresh availability/ownership checks at execution time; create fresh fixtures where needed. Record source/binary provenance. Do not attach or mutate a different project/library accidentally.
- [ ] Through MCP: set one and two comments, clear one; inactive and unsupported/neutral culture errors; activate languages and set editing/reference, including automatic activation; repeat already-active/unchanged requests. Seed an unrelated nonempty comment and verify preservation. Check exact multiline/Unicode text and dirty-library behavior.
- [ ] Test both read-only operations with native-type expectations from Task 0, including attached failure evidence. A safely isolated oversized comment may be exercised through MCP only when the effective item budget admits it; expect native refusal, not a new validation cap. No new boundary search.
- [ ] Preview each operation; preview an activation followed by its comment (`createdByItem`), then apply the pair in one content-only call. Test a lifecycle-containing read-write call's single confirmation and staging restriction, and full-mode create-then-edit where unplanned reuse is allowed.
- [ ] Mix `create_master_copy`/type or folder work with metadata and confirm unrelated verification stays trusted; repeat metadata and close-with-discard to check supersession. Save, close and reopen to verify persistence; discard an edit to verify it is gone. Inspect state before retrying a failed attempted write.
- [ ] Record exact passed/failed/unavailable cases, cleanup/remaining fixtures and native versus MCP evidence honestly. Resolve material contradictions before claiming acceptance. Commit `test: record library metadata PR B live acceptance`.

## Task 6: Permanent documentation, PR closure draft and final pruning

**Files:** `README.md`, `AGENTS.md`, `TiaMcpServer/Library/LibraryWriteTools.cs` (review `LibraryReadTools.cs` for consistency), `docs/ARCHITECTURE.md`, `docs/SupportedOperations/LIBRARY_OPERATIONS_SUMMARY.md`, `docs/IMPROVEMENT_LOG.md`, `docs/roadmap/json-contract.md`; new `docs/adr/0019-library-metadata-transactions.md` plus ADR index; reports/indexes; `scripts/verify-docs.py`, `scripts/tests/test_verify_docs.py`; transient files listed below.

- [ ] After live acceptance, document the two operations, metadataChange success/failure/preview shapes, in-transaction check, no post-failure reread, uncertain commit/dispose guidance, native-authority/read-only category difference, dirty/no-implicit-save behavior, last-writer-wins, automatic activation, released-version caveat and no language removal. Preserve PR A metadata-read/author behavior and its native-risk admission rule.
- [ ] Change operation counts 20 to 22 and global methods 14 to 16; project methods remain 14 and all methods become 32 including plans. Tool counts remain 9/21/21. Remove the delivered comment/language-editing deferral; retain unrelated maintenance/type-instance/local-session follow-ups and add language removal/optional expected-text-hash/project metadata follow-ups without duplicate logs.
- [ ] ADR-0019 records native transactions/end-state checking, the tested refusal types, native authority, last writer wins and uncertain disposal outcomes. Link durable docs to the ADR, permanent reports, issue #218 and relevant PR/commit identities; no surviving transient links.
- [ ] Prepare a local final PR title/body containing `Closes #218`, links to PR #230 and the acceptance reports, contributor acknowledgement, delivered scope and retained follow-ups. Do not auto-close the issue, post a comment or push/open/merge a PR in this planning session; later remote writes require the applicable explicit authorization.
- [ ] Review the complete branch for correctness/scope and get independent review of substantial code changes; fix material findings and run their relevant checks before finalization. Commit permanent documentation as `docs: describe library metadata edits`.
- [ ] **RED for pruning:** Replace `test_library_metadata_handoff_retains_only_exact_plan_and_spec` with tests that both old retained paths, this PR B plan and arbitrary transient evidence are ordinary transient failures. Replace `test_retained_library_metadata_spec_still_checks_links` with generic transient link-check coverage. Run `python -I scripts/tests/test_verify_docs.py` and observe the old-exemption assertion failing.
- [ ] Remove `RETAINED_HANDOFF_FILES` and its exemption from `scripts/verify-docs.py`; remove the owner-retained handoff exception from `AGENTS.md` and the corresponding wording in `docs/superpowers/README.md`. Do not add an exemption for this plan.
- [ ] Before deleting working files, transfer every lasting decision/behavior/acceptance fact into the permanent locations above. Then delete **both PR A files** and this plan, plus PR B-created transient specs/evidence if any:

```text
docs/superpowers/plans/2026-11-08-library-metadata-pr-a.md
docs/superpowers/specs/2026-11-08-library-metadata-design.md
docs/superpowers/plans/2026-11-09-library-metadata-pr-b.md
```

- [ ] Preserve dated acceptance reports, verified raw gzip inputs and probe source directories from PR A and PR B. Replace the PR B native-probe README's design link with the permanent ADR/operation reference; repair other surviving links to the pruned files. The git history retains the plan/spec.
- [ ] GREEN, then commit pruning as `docs: prune library metadata working files`:

```powershell
python -I scripts/tests/test_verify_docs.py
python -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30 --fail-on-transient
git diff --check
```

Expected: tests pass, zero docs errors/warnings, no tracked files remain in the transient directories, and the PR A-only exemption is gone. Do not delete another active task's unowned files; resolve any such overlap explicitly.

## Task 7: Contributor credit — final implementation commit

**Identity verified:** issue #218 author is `doerk77` (GitHub user ID 82865479); existing empty credit commit `29258c6c` uses the exact trailer below. This is contribution credit, not a claim the old-base bundle was applied unchanged.

- [ ] Confirm all implementation, fixes, live evidence, permanent docs, pruning and required validation are committed; check a clean worktree. Finish any remaining work before credit.
- [ ] Make the **last branch commit** empty, using the established message/trailer:

```powershell
git commit --allow-empty -m "chore: credit library contribution" -m "Co-authored-by: doerk77 <82865479+doerk77@users.noreply.github.com>"
```

- [ ] Verify `git log -1 --format=full` shows the exact credit trailer and `git status --short` is empty. Do not add implementation/docs commits after it. If subsequent required changes arise, finish them and make the final credit commit again at the end.
- [ ] Report actual validation, remaining qualification limits, PR closure draft and final commit. Publishing is separate authorization; `Closes #218` takes effect when the eventual PR merges, not while preparing this plan.

## Plan self-review and authoring-session boundary

- [x] Checked spec coverage, step specificity, interface/type consistency, all five review-focus tests and plan proportion; incorporated the current source/stub/doc-governance seams.

The authoring session ends after documentation-link validation, plan/spec diff inspection and the plan commit. Do not start Task 1, run more live probes or publish anything in this session. The transient-plan warning is expected until Task 6 prunes the working files; do not add a retention exemption to hide it.
