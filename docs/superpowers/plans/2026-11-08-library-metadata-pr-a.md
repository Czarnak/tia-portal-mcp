# Library Metadata PR A Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add opt-in global-library metadata reads and optional author-at-creation, with metadata returned
for every successful `create_global`.

**Architecture:** Extend the existing typed library contracts and guarded pipeline. One worker metadata
reader serves global `inspect_library` and `create_global`; existing routing, capability gates and lifecycle
confirmation remain in place. No new tool, operation, transport method or write framework is needed.

**Tech Stack:** C#, .NET 10 host/tests, .NET Framework 4.8 worker, Siemens TIA Portal V21 Openness, xUnit.

**Spec:** [2026-11-08-library-metadata-design.md](../specs/2026-11-08-library-metadata-design.md), sections
3, 5-7, PR A parts of 10-12; [native results](../acceptance/reports/2026-11-08-library-metadata-native-probes.md).

**Status:** Task 1's decision gate is resolved and committed; Tasks 2-6 remain unchecked. The owner
reports a 255-character TIA UI maximum and approved host fail-fast validation without another cap probe.
The agreed creation-author admission rule is nonblank and at most 255 UTF-16 code units (`string.Length`),
with the effective item budget as an additional constraint. The latest owner instruction also applies
the length cap to `release_type_version` now through a reusable helper in `TiaMcpServer.Contracts/Shared`,
without otherwise changing release text/blank handling. This is owner evidence/approval, not a
Siemens-documented or independently probed native maximum or Unicode-counting rule.

## Global Constraints

- Every library call needs a verified binding; global libraries are resolved in the bound Portal
  (ADR-0015). Only libraries already open in that Portal can be targeted.
- `library_write` keeps 1-50 items in caller order, stop at first failure, no batch rollback
  (`Safety/Pipeline`). Read-write prompts only for global-library lifecycle (ADR-0016).
- Worker payloads use explicit nulls; members that are omitted when null are declared
  `JsonIgnore(WhenWritingNull)` and registered in `ConditionalMemberRegisterTests`.
- Budgets read `StructuredOperationBatchPayloadBudget.MaxItemChars` / `MaxDocumentChars`, never a literal.
- Siemens property reads use `OpennessExceptions.IsRecoverablePropertyFailure`; anything else escapes
  for normal worker failure/Portal-loss classification.
- No implicit save, close, reopen or retry; metadata collection performs getters only.
- PR A adds no operations: library reads remain contract `1.0`, writes `2.0`; tool/operation counts stay
  unchanged. No PR B setters, language mutation, `metadataChange`, verification keys or ADR-0019.
- **Owner override:** retain this plan and the complete shared spec through PR A. No documentation-pruning
  task or `docs: prune working files` commit; PR B needs the existing spec.

## Review Focus

- A dangerous author must fail before worker/native creation; test the 10,000-character regression and
  255-/256-code-unit boundary in Task 4 for creation and release. Share the length cap without extending
  creation's nonblank requirement to release beyond its existing validation.
- Omitted/false metadata must not trigger native metadata getters or expand global inventory reads;
  test this in Task 3, including explicit false on other operations.
- A recoverable getter failure must preserve the rest of the metadata; security/disposal/fatal failures
  must escape. Exercise all of these in Task 2.
- Metadata can fail after a library already exists; preserve its captured identity and real failure
  category, and do not retry or clean up automatically. Exercise this in Task 4.
- Large existing values and malformed nested payloads must retain truthful execution status and reject
  invalid evidence. Exercise omission, strict decoding and conditional-member rules in Task 5.

---

### Task 0: Native qualification (completed)

- [x] Run A1-A4 in disposable Portal sessions and preserve raw direct-call evidence.
- [x] Select `typeof(UserGlobalLibrary)` for explicit-author creation; omitted author was empty here.
- [x] Verify fresh/read-only/system-library getters and exact multiline author round trip.
- [x] Record the long-author crash, exclude it from reruns, and bring the contradiction to the owner.
- [x] Record the owner's decision to keep author creation behind the Task 1 gate.

### Task 1: Resolve the author-length blocker before implementation (completed)

**Files:** update sections 6.1/7.3 of the shared spec, this task, and the native report/evidence index for
the previously authorized attached repeat. This is a decision/evidence task, not a production-code task.

**Consumes:** A4's fatal 10,000-character input, the successful 21-/34-character samples, the owner's
reported 255-character UI maximum and approval, and the retained attached-repeat evidence.
**Produces:** an exact, evidenced creation-author admission rule approved by the owner, including how
length is counted and where the host and worker enforce it.

- [x] Record the owner's direct TIA UI test: maximum accepted author is 255 characters; longer input
  shows an error. The owner explicitly approved host fail-fast validation and no further cap probe.
  This supersedes the pending request for a rule/approval; it is not independent native qualification.
- [x] Correct sections 6.1/7.3 with the exact creation admission rule: nonblank, at most 255 UTF-16 code units
  (`string.Length`). Per the owner's latest instruction, a reusable author metadata length helper in
  `TiaMcpServer.Contracts/Shared` serves `LibraryWriteMethods` validation for both `create_global` and
  `release_type_version`, on host before worker dispatch and worker before native calls. Overlength
  errors identify author, maximum 255 and actual length, without echoing text. Preserve supplied text,
  including LF/CRLF, and the existing omitted-author path. Release text/blank handling stays unchanged
  apart from the shared length cap. The effective item budget is an additional constraint; raising budgets
  never weakens the cap. Task 4 must test 255, 256, 10,000, non-BMP counting and LF/CRLF
  preservation for creation and release, including rejection before dispatch and independent worker
  validation. Broader author-field adoption remains an open Improvement Log follow-up.
- [x] Record the already authorized attached repeat: both original Portal processes exited -1 before
  helper detach. Retain sanitized native events and a derived process-exit summary in the existing
  evidence structure/index. Do not change the old probe to rerun the dangerous case; no further live test.
- [x] Commit the agreed design correction, resolving the Task 1 gate for Tasks 2-6. Leave those tasks
  unchecked; this decision correction implements no production behavior.

### Task 2: Shared metadata model and native reader

**Files:**
- Modify `TiaMcpServer.Contracts/Library/LibraryReadModels.cs`.
- Create `TiaMcpServer.OpennessWorker/Openness/Library/LibraryMetadataReader.cs` and
  `TiaMcpServer.Tests/Library/LibraryMetadataReaderTests.cs`.
- Modify `TiaMcpServer.Tests/TestSupport/LibrarySiemensDoubles.cs`,
  `TiaMcpServer.Tests/TestSupport/HmiUnifiedSiemensDoubles.cs`, and `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`.
- Modify `reference-stubs/Siemens.Engineering.Base/Library.cs` and the existing language/text stub files
  only where the read API is absent; regenerate tracked `ref/` assemblies with the repository script.

**Interfaces:** `LibraryMetadataReader.Read(GlobalLibrary library) -> LibraryMetadataInfo`.
Define `LibraryMetadataInfo`, `LibraryCommentInfo`, `LibraryUsedProductInfo`, `LibraryHistoryEntryInfo`
in the shared library namespace. Metadata has exactly the section 7.2 members; use nullable scalar values
(`long?` for KB size), nullable collections, `Complete` and a non-null `Diagnostics` list. Date/time values
are ISO 8601 strings, not JSON-serialized native DateTime/proxy objects.

- [ ] Write reader tests before implementation: `Read_PreservesEmptyValuesAndNullLanguages`,
  `Read_SortsCommentsAndLanguagesOrdinally`, `Read_CapsProductsAt100AndHistoryAt50`,
  `Read_DoesNotCapCommentsOrLanguages`, `Read_RecoverableGetterFailurePreservesOtherMembers`, and
  `Read_SecurityDisposedAndFatalFailuresEscape`. Assert original native order for capped lists,
  `Complete=false` plus a cap diagnostic only when a list actually exceeds its cap; exactly 100/50 is
  complete. Keep empty comments. Legitimate null languages do not generate failure diagnostics.
- [ ] Extend only needed Siemens-shaped doubles/stubs from installed V21 reflection: metadata getters,
  language enumeration, product/history entries. Failure hooks must distinguish the two recoverable
  property exception types from other engineering exceptions. Link the new worker reader into the test
  project explicitly, as existing worker library sources are linked.
- [ ] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~LibraryMetadataReaderTests"` and
  observe the relevant missing-reader/behavior failure before adding production logic.
- [ ] Implement the single reader. Read each member independently; a recoverable failure in a collection
  or its elements nulls that collection with a diagnostic. Preserve native nulls/empty strings; cap only
  products/history, examining one extra entry to detect truncation. No setters or lifecycle calls.
- [ ] Regenerate/verify stubs with `pwsh -NoProfile -File scripts/verify-reference-stubs.ps1 -Update`, then
  the same command without `-Update`. Run the reader tests, serial reference-stub solution build and
  Packaging tests. Expected: all pass, no undocumented API approximations.
- [ ] Commit `feat: add global library metadata reader`.

### Task 3: Opt-in `inspect_library` plumbing

**Files:** `TiaMcpServer/Library/LibraryReadItem.cs`, `LibraryReadCatalog.cs`;
`TiaMcpServer.Contracts/Library/LibraryReadMethods.cs`, `LibraryReadModels.cs`;
`TiaMcpServer.OpennessWorker/Openness/Library/LibraryReader.cs`;
`TiaMcpServer.FakeWorker/Library/LibraryReadScenario.cs`;
tests `LibraryReadItemCatalogTests.cs`, `LibraryReadCatalogTests.cs`, `LibraryReaderTests.cs`,
`LibraryReadToolsTests.cs` in `TiaMcpServer.Tests/Library/`, and
`TiaMcpServer.Tests/Json/ConditionalMemberRegisterTests.cs`.

**Interfaces:** `LibraryReadItem.IncludeMetadata` and `LibraryQueryInfo.IncludeMetadata` are `bool?`;
`LibraryEntryInfo.Metadata` is conditional `LibraryMetadataInfo?`. Existing
`LibraryReadCatalog.ToQuery(LibraryReadItem)` copies the flag. Existing
`LibraryReadMethods.Validate(string, LibraryQueryInfo)` owns operation/member admission.

- [ ] Add failing tests for omitted/false/true on global inspect, true on project inspect, false on project
  inspect, explicit false and true on each other operation, nonboolean/null JSON inputs under existing
  argument-shape rules, and transport of the flag. Assert project true returns `validation_error` with
  `Metadata is available for global libraries only.` before native access.
- [ ] Run the affected catalog/tool tests; confirm behavior fails without the new plumbing.
- [ ] Add the nullable flag, description, query copy and closed validation. Only inspect accepts the
  member; project false behaves like omission. In `LibraryReader.Read`, after the existing global
  inspect row is constructed, attach `LibraryMetadataReader.Read(global)` only when the flag is true.
  Do not add it to `RowReader.LibraryEntry`, which also serves global inventory, or to cursor hashing.
- [ ] Update FakeWorker and register the conditional entry metadata member with its exact presence rule.
  Add getter-count tests proving omission/false and `list_global_libraries` never read metadata, and a
  read-only/system-shaped fixture proving inspection performs no mutation.
- [ ] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~LibraryRead|FullyQualifiedName~LibraryReader|FullyQualifiedName~ConditionalMemberRegisterTests"`;
  expected: passing new tests and existing read/cursor tests.
- [ ] Commit `feat: expose opt-in global library metadata`.

### Task 4: Shared creation/release author cap and creation metadata

**Files:** `TiaMcpServer.Contracts/Library/LibraryWriteMethods.cs`, `LibraryWriteModels.cs`;
a reusable author metadata validation helper in `TiaMcpServer.Contracts/Shared/`;
`TiaMcpServer/Library/LibraryWriteItem.cs`, `LibraryWriteCatalog.cs`, `LibraryWriteDomain.cs`;
`TiaMcpServer.OpennessWorker/Openness/Library/LibraryWriter.cs`;
`TiaMcpServer.FakeWorker/Library/LibraryWriteScenario.cs`;
`TiaMcpServer.Tests/TestSupport/LibrarySiemensDoubles.cs`;
tests `LibraryWriteMethodsTests.cs`, `LibraryWriteCatalogTests.cs`, `LibraryWriterTests.cs`,
`LibraryWriteMcpTests.cs` and the conditional-member register.

**Interfaces:** existing `LibraryWriter.Write(TiaPortal, ProjectBase, string, LibraryWriteRequestInfo)`;
conditional `LibraryWriteResultInfo.Metadata : LibraryMetadataInfo?` and
`LibraryWriteOutcomeInfo.Library : LibraryIdentityInfo?`. `Author` already exists in the request/item
and is already copied by `ToWorkerRequest`; do not duplicate fields or transport methods.

- [ ] Add failing tests for omitted/supplied creation author, blank creation author, the exact Task 1
  boundaries (255, 256, 10,000 and non-BMP counting) for creation and release, and effective item-budget
  checking. `new string('A', 10000)` must produce `validation_error` without worker/native activity.
  Check the same shared cap at worker validation and preserve release text/blank behavior otherwise.
- [ ] Add tests for exact multiline text, confirmation text with/without author, dry run making no native
  create/metadata calls, and one lifecycle elicitation in read-write versus none in full.
- [ ] Add writer tests asserting the supplied path calls `IEngineeringComposition.Create` exactly once
  with `typeof(UserGlobalLibrary)` and exactly `Name`, `TargetDirectory`, `Author` (no `Comment`), while
  omission uses typed `Create<UserGlobalLibrary>`. Both successful paths return the shared metadata.
  Extend the composition double to implement `IEngineeringComposition` rather than using reflection.
- [ ] Run the affected tests to observe the missing behavior, then implement the common author metadata
  length helper in `TiaMcpServer.Contracts/Shared` and call it from shared library validation for
  `create_global` and `release_type_version`. Preserve creation's nonblank rule, effective budget checks
  and accepted text; update the existing Author description and confirmation formatting.
- [ ] Replace only the writer's `create_global` branch. Cast the returned object with `as GlobalLibrary`;
  an unexpected return fails `postcondition_failed` and says a library may exist on disk. Capture identity
  before metadata. Do not infer the omitted author, normalize supplied text, save or retry.
- [ ] Test recoverable metadata failures as successful creation with incomplete metadata. Test other
  metadata failures after creation as outcome-bearing failures: retain captured identity, classify by
  exception type using the saved-close pattern, preserve `engineeringError` where applicable, let
  `portal_lost` win and invalidate binding, and state that the library exists before any retry.
  Do not read the library again in a failure handler or attempt automatic deletion.
- [ ] Register result metadata and outcome library as conditional members. Run
  `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~LibraryWrite|FullyQualifiedName~LibraryWriter|FullyQualifiedName~ConditionalMemberRegisterTests"`.
  Expected: all pass, including existing lifecycle, ordering and partial-failure behavior.
- [ ] Commit `feat: add guarded library creation author and metadata`.

### Task 5: Strict payloads, budgets and schema regression

**Files:** `TiaMcpServer/Library/LibraryPayloadContract.cs`, `LibraryWritePayloadContract.cs`;
`TiaMcpServer.Tests/Library/LibraryPayloadContractTests.cs`, `LibraryWriteContractDecodeTests.cs`,
`LibraryReadBudgetTests.cs`, `LibraryWriteBudgetTests.cs`;
`TiaMcpServer.Tests/Tools/McpToolSchemaTests.cs`, `Tools/Snapshots/output-schemas.json`.

**Consumes:** Task 2 DTOs and the three conditional members from Tasks 3-4.
**Produces:** schema-pinned, strictly decoded metadata and identity evidence using existing canonical JSON.

- [ ] Add decode tests for missing required metadata fields, wrong types, duplicate/wrong-case members
  and null elements in comments/language/product/history/diagnostic lists. Apply existing reader
  conventions; do not hand-write required-member lists. Extend write outcome identity validation in both
  success/failure projection and `ValidateEnvelopeOutcome(JsonElement)`; malformed evidence is
  `protocol_error` and is never echoed. Legitimate scalar/collection nulls remain accepted.
- [ ] Add read and create-result oversized-metadata tests using effective budgets, including raised limits.
  Assert whole-value omission with truthful item execution/verification summaries and root delivery
  failure, rather than truncating comment text or claiming the create was undone.
- [ ] Run those tests red, implement the minimal extra nested/null-element checks alongside existing
  validators, and update the library parts of the output-schema snapshot from actual tool schemas.
  `McpToolSchemaTests.OutputSchemasMatchMain` has no regeneration flag; preserve both protocol variants
  and unrelated schemas.
- [ ] Run, serially: the focused library/conditional/schema tests; the reference-stub verify script;
  `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true`;
  `dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"`;
  `dotnet test TiaMcpServer.Tests --filter "Category=Packaging"`.
  Expected: all passing; inspect and resolve any failure rather than treating the native probe as coverage.
- [ ] Commit `test: pin library metadata contracts and budgets`.

### Task 6: PR A live acceptance and documentation

**Files:** `docs/SupportedOperations/LIBRARY_OPERATIONS_SUMMARY.md`, `docs/ARCHITECTURE.md`,
`docs/IMPROVEMENT_LOG.md`, relevant tool descriptions, `README.md`, `AGENTS.md`; a new PR A live report
under `docs/superpowers/acceptance/reports/` and its raw evidence/index where appropriate;
`scripts/verify-docs.py` and `scripts/tests/test_verify_docs.py` for the narrow retention exception.

- [ ] With the approved author rule implemented, run spec section 12's PR A matrix through the built MCP:
  global inspect with omission/false/true; project true and flag on another operation rejected; create
  without/with author returning metadata; accepted multiline author; read-only user and available system
  library. Verify strict unsafe-author rejection without sending the known fatal input to Siemens.
- [ ] Exercise binding/access-mode behavior, read-write confirmation/dry run and post-create read through
  MCP. The standalone native probe proves none of these host/worker contracts. Report any unavailable
  environment or untested native fault case explicitly; use offline fault injection for partial outcomes.
- [ ] After acceptance, document the metadata flag/shape, exact agreed author rule, empty omitted-author
  observation with its scope, cap/diagnostic semantics, whole-value omission and inspect-before-retry
  guidance. Keep language/comment editing deferred to PR B and operation/tool counts unchanged. Remove
  only delivered metadata-read/creation-author deferrals; retain metadata-write deferrals.
- [ ] Reconcile the owner's retention instruction with the main-branch docs check: it currently uses
  `--fail-on-transient`. Add an exact-path exception for this plan and the shared metadata spec, with
  tests proving strict mode accepts those two files and still rejects an unrelated plan/spec/evidence
  file. Do not disable transient checks generally or prune the required handoff files. Run
  `python -I scripts/tests/test_verify_docs.py` and
  `python -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30 --fail-on-transient`;
  expected: passing tests and zero documentation errors.
- [ ] Run the repository's applicable documentation validation and `git diff --check`; review links and
  source/schema descriptions. Commit the docs with `docs: describe library metadata PR A`.
- [ ] Preserve the shared spec and this plan; **do not prune documentation**. Verify the contribution
  attribution used for #218 and follow the existing credit convention without inventing an identity.
  Remote publication, pushing, merging and deployment require their own explicit authorization.

## Execution handoff

This is a plan only. Task 1 must be resolved with the owner before production implementation starts.
After that decision, native execution in the existing feature branch is the smaller approach: the tasks
share DTOs, doubles and library files. Request an independent final review before integration.
