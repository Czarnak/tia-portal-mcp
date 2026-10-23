# C-2 Shared Batch Rules Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** All six batch catalogs and the write pipeline enforce the 50-item and 256-character rules through one helper with one wording, never echo an oversized `operationId`, and return one validation-result type.

**Architecture:** A new `OperationBatchRules` in `TiaMcpServer/OperationBatches` owns the count and per-item envelope checks. Each catalog calls it, then runs only its domain rules. `WriteRun.ValidateInput` calls it as the pipeline's entry guard. One `OperationValidation` record replaces `NetworkValidationResult` and the PLC/HMI/VCI tuples.

**Tech Stack:** C#, net10.0 host, xUnit.

**Spec:** Review plan item C-2 ([review](2026-10-17-codebase-simplification-review.md); findings TOOLS-3, HOSTDOMAINS-2, TOOLS-4) and the [Workstream C plan](2026-10-21-workstream-c-plan.md), wave 3.

**Branch:** `refactor/c2-shared-batch-rules`, worktree `.worktrees/C-2`, cut from `main` @ `3e48de7` or later.

## Global Constraints

Copied verbatim from the Workstream C plan:

- **Shell:** run `dotnet` from PowerShell. Bash mangles `/p:` flags.
- **Build:** `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true`. The baseline is the seven existing test-assembly nullable warnings and no new warnings.
- **Full suite:**
  - Run serialized, outside the sandbox.
  - Use process-local `TEMP`/`TMP` under ignored `TestResults/<id>/temp`.
  - Collect coverage with `TiaMcpServer.Tests/coverage.runsettings` and the single-thread xUnit settings from `.github/workflows/ci.yml`.
  - `scripts/verify-coverage-threshold.ps1 -MinimumLineRate 0.80` must pass.
  - Zero failures and zero skips.
- **Test-first:** observe a runtime failure before implementing, not just a compile error, wherever the plan names one. A pure refactor records a green baseline with the named pinning tests.
- **Test linking:** `TiaMcpServer.Tests` links host sources through `<Compile Include>`. `Safety/Pipeline`, `Vci`, `Openness/Hmi` and `Openness/Vci` use globs, and Contracts is a `ProjectReference`. Check the csproj before adding a host file.
- **Commits:** commit after each task, as short one-line conventional commits with no body.
- **Docs come last.** Each PR also adds an evidence sub-bullet under its item in the review plan, in the B-item style.
- **No TIA project names or paths** in the repo, including disposable copies.
- **Live runs:** use the installed candidate, which is separate from the dev build. Never dispose `TiaPortalProcess`.
- **jCodemunch:** `find_references` returned false empties for some C# consumers during planning. Confirm consumers with `search_text` or `git grep` before deleting a member.

C-2 rules (maintainer decisions, 2026-10-23):

- **No single-project rule.** C-0 removed every item `projectPath`, so the review's `WriteProjectPaths.Normalize` check has nothing to compare.
- **The shared wording** is the Network/PLC/HMI-read variant, exactly:
  - count below 1: `Batch must contain at least one operation.`
  - count above 50: `Batch exceeds the maximum of 50 operations (received {count}).`
  - null item: `Batch contains a null operation.`
  - blank `operationId`: `Each operation requires a unique operationId.`
  - `operationId` over 256 characters: `Each operationId must be at most 256 characters.` (never echoes the ID)
  - duplicate: `Duplicate operationId '{id}'.`
- **Per-item order is null, blank, length, duplicate.** An item that fails any of them produces no further message, so its ID is never echoed.
- **Stay per domain:** blank-operation, unknown-operation, wrong-category and access-mode messages.
- **`OperationValidation` keeps the member name `Error`**, so existing `.IsValid`/`.Error`/`.Category` reads keep compiling.
- **`WriteRun.ValidateInput` stays** as the defensive entry guard (it also covers lifecycle) and uses the shared rules.
- **Out of scope:** operation names are still echoed unbounded in unknown-operation and wrong-category messages. Task 5 logs this as a follow-up.
- **Tests:** run only each task's focused filter. The orchestrator runs the full suite once per branch.

## Review Focus

1. **VCI scope categories still pass through.** `invalid_selector` and `target_kind_unsupported` from a malformed scope must survive the result-type swap. Pinned by `VciWriteCatalogTests` (the category assertion near :112) and the VCI read scope tests, run in Task 4.
2. **Envelope before budget.** An ID over 256 characters must be rejected before `MeasureProtectedCore` measures it. Pinned by `NetworkGuardedWriteBudgetTests` and `HmiWriteBudgetTests`, run in Task 2.
3. **Lifecycle is unaffected.** The single host-made item with ID `lifecycle` must still pass `ValidateInput`. Pinned by the lifecycle tests in Task 3's filter.
4. **No echo after an envelope failure.** HmiWrite and VciWrite prefix every field error with the ID, and PLC's cross-item check echoes it. Pinned by the `OversizedOperationId_IsNeverEchoed` tests in Task 2.
5. **A null batch** on any catalog returns the count-below-1 message, not a crash. Pinned by the `null` rows in Task 2.

**Contract change:** message text, plus one narrow category effect.
- HmiWrite, VciRead and VciWrite envelope messages change wording and lose the `Operation '…' (operationId '…'):` prefix on envelope errors.
- Every write's entry-guard messages change. The null-item message no longer names an index.
- An item that fails the envelope check no longer adds field errors. It also no longer contributes a VCI scope category, so a batch whose only scope error sits on such an item reports `validation_error` instead of `invalid_selector`/`target_kind_unsupported`.

**Live verification:** not needed. No Siemens calls are involved.

## After the tasks

These are not implementer tasks. They run after Task 4 and before Task 5.

### Branch verification (orchestrator)

1. Run the stub build.
   - Expected: 0 errors and no new warnings.
2. Run the full suite with coverage under `TestResults/c2`, with `TEMP`/`TMP` set to `TestResults/c2/temp`.
   - Expected: 0 failed and 0 skipped.
3. Run `scripts/verify-coverage-threshold.ps1 -MinimumLineRate 0.80`.
   - Expected: it passes.
4. Record the line rates of `OperationBatchRules.cs`, the six catalogs and `WriteRun.cs` from the Cobertura report.

The final whole-branch review follows.

---

### Task 1: Shared batch rules

**Files:**
- Create: `TiaMcpServer/OperationBatches/OperationBatchRules.cs` (namespace `TiaMcpServer.OperationBatches`; the repo has no global usings).
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`. `OperationBatches` is an explicit list (about :155-164), so add a `<Compile Include>` for the new file there.
- Create: `TiaMcpServer.Tests/OperationBatches/OperationBatchRulesTests.cs`.

**Interfaces:**
- Consumes: `IOperationBatchItem` (`OperationId`, `Operation`), which all six request types and `LifecycleWriteItem` implement.
- Produces, in `internal static class OperationBatchRules`:
  - `public const int MaxItems = 50;` and `public const int MaxOperationIdChars = 256;`
  - `public static string? CheckCount(int count)`: returns the count message, or null for 1–50. Callers pass `operations?.Count ?? 0`.
  - `public static string? CheckItem(IOperationBatchItem? item, ISet<string> seenIds)`: returns the first failing message in the order null, blank, length, duplicate, or null. It adds the ID to `seenIds` only after the length check passes. Copy Network's "bound it before any response construction" comment (`NetworkOperationCatalog.cs` :150-152) onto the length check; Task 2 deletes the original.

- [ ] **Step 1: Write the tests.** Use `PlcOperationRequest` as the item.
  - `CheckCount_UsesSharedWording`, a `[Theory]`: 0 gives `Batch must contain at least one operation.`; 1 and 50 give null; 51 gives `Batch exceeds the maximum of 50 operations (received 51).`
  - `CheckItem_RejectsNullBlankAndDuplicate`: null gives `Batch contains a null operation.`; `""` and `"  "` give `Each operation requires a unique operationId.`; `"x"` passes once, and the second `"x"` gives `Duplicate operationId 'x'.`
  - `CheckItem_BoundsOperationIdWithoutEcho`: a 256-character ID passes; a 257-character ID gives exactly `Each operationId must be at most 256 characters.`, and the message does not contain the ID.
  - `CheckItem_OversizedIdNeverReachesDuplicateCheck`: the same 300-character ID twice gives the length message both times and leaves `seenIds` empty.
- [ ] **Step 2: Add both methods returning null.**
  - Run: `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~OperationBatchRulesTests`
  - Expected: every row that expects a message FAILS.
- [ ] **Step 3: Implement both methods.**
  - Run: the same filter.
  - Expected: all PASS.
- [ ] **Step 4: Commit.** Message: `feat: add shared OperationBatchRules`

**Task test command:** `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~OperationBatchRulesTests`

### Task 2: Catalogs use the shared rules

**Files:**
- Modify: `TiaMcpServer/Network/NetworkOperationCatalog.cs` (constants :33-34, envelope :123-162).
- Modify: `TiaMcpServer/Plc/PlcOperationCatalog.cs` (constants :15-16, `Validate` :143-167, `ValidateItem` :170-200).
- Modify: `TiaMcpServer/Hmi/HmiOperationCatalog.cs` (constants :13-14, `ValidateRead` :138-157, `ValidateItem`).
- Modify: `TiaMcpServer/Hmi/HmiWriteCatalog.cs` (:57-70).
- Modify: `TiaMcpServer/Vci/VciReadCatalog.cs` (:17-28).
- Modify: `TiaMcpServer/Vci/VciWriteCatalog.cs` (:24-36).
- Each of the six catalogs gains `using TiaMcpServer.OperationBatches;`.
- Test: `TiaMcpServer.Tests/Hmi/HmiWriteCatalogTests.cs`, `TiaMcpServer.Tests/Vci/VciReadCatalogTests.cs`, `TiaMcpServer.Tests/Vci/VciWriteCatalogTests.cs`, `TiaMcpServer.Tests/Plc/PlcOperationCatalogTests.cs`, `TiaMcpServer.Tests/Network/NetworkOperationCatalogTests.cs` and `TiaMcpServer.Tests/Hmi/HmiOperationCatalogTests.cs`. Repoint the constant references in `NetworkOperationCatalogTests` (:125), `PlcOperationCatalogTests` (:34) and `NetworkGuardedWriteBudgetTests` (:25, :28) to `OperationBatchRules`. Leave the test-local `ExpectedMaxOperationIdLength` constants alone.

**Interfaces:**
- Consumes (Task 1): `OperationBatchRules.CheckCount`, `CheckItem`, `MaxItems`, `MaxOperationIdChars`.
- Produces: no new members. `PlcOperationCatalog.MaxBatchSize`/`MaxOperationIdLength`, `HmiOperationCatalog`'s and `NetworkOperationCatalog`'s copies are deleted.

- [ ] **Step 1: Write the tests.**
  - `HmiWriteCatalogTests.EnvelopeMessages_UseSharedWording`, `VciReadCatalogTests.EnvelopeMessages_UseSharedWording` and `VciWriteCatalogTests.EnvelopeMessages_UseSharedWording`. Each is a `[Theory]` keyed by a case string that the test switches on to build the batch, and asserts that `Error` equals the shared message exactly. The cases are: `null` batch, empty batch, 51 valid items, one null item, one blank ID, one 257-character ID, and two otherwise-valid items with ID `x`.
  - `HmiWriteCatalogTests.OversizedOperationId_IsNeverEchoed` and the same in `VciWriteCatalogTests`: one item with a known operation, a 300-character ID and one invalid field. `Error` does not contain the ID and equals the length message.
  - `PlcOperationCatalogTests.CrossItemCheck_DoesNotEchoOversizedOperationId`: `create_block` on a block (ID `c`), then `update_block_logic` on the same block with a 300-character ID. `Error` contains `at most 256 characters`, does not contain the ID, and does not contain `created earlier in this call`.
  - `PlcOperationCatalogTests.CrossItemCheck_StillReportedBesideAnUnrelatedFieldError`: `create_block` (ID `c`), `update_block_logic` on the same block (ID `u`), and a third item with an unrelated field error. `Error` contains both `created earlier in this call` and the field error.
  - Null batch:
    - Network: in `ValidateRead_RejectsEmptyAndOverFiftyItemBatches` (:121), also call `ValidateRead(null)` and assert `Error` equals `Batch must contain at least one operation.`
    - PLC: add `[Fact] RejectsNullOrEmptyBatch` asserting that message for `null` and for an empty array.
    - HMI: in `MoreThan50OrDuplicateIdsRejected`, change the `ValidateRead(null)` asserts (:308-309) to `Assert.Equal` on that message.
- [ ] **Step 2: Run the new tests.**
  - Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~EnvelopeMessages_UseSharedWording|FullyQualifiedName~OversizedOperationId_IsNeverEchoed|FullyQualifiedName~CrossItemCheck|FullyQualifiedName~RejectsNullOrEmptyBatch|FullyQualifiedName~ValidateRead_RejectsEmptyAndOverFiftyItemBatches|FullyQualifiedName~MoreThan50OrDuplicateIdsRejected"`
  - Expected:
    - every HmiWrite, VciRead and VciWrite wording case FAILS, except the null-item case, which already passes;
    - both `OversizedOperationId_IsNeverEchoed` FAIL;
    - `CrossItemCheck_DoesNotEchoOversizedOperationId` FAILS;
    - the null-batch asserts and `CrossItemCheck_StillReportedBesideAnUnrelatedFieldError` already PASS.
- [ ] **Step 3: Use the shared rules in all six catalogs.**
  - Each catalog returns the `CheckCount` message as its whole-batch error. In its item loop, it adds the `CheckItem` message and `continue`s.
  - HmiWrite and VciWrite call their `Error(...)` prefix helper only after `CheckItem` returns null.
  - PLC passes `ValidateWriteCall` (:342) only the items whose `CheckItem` returned null.
  - Delete the three constant pairs, every `50`/`256` literal and Network's original bound-it comment.
- [ ] **Step 4: Run the domain tests.**
  - Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~TiaMcpServer.Tests.Network|FullyQualifiedName~TiaMcpServer.Tests.Plc|FullyQualifiedName~TiaMcpServer.Tests.Hmi|FullyQualifiedName~TiaMcpServer.Tests.Vci|FullyQualifiedName~OperationBatchRulesTests"`
  - Expected: all PASS, including `RejectsContentOpOnBlockCreatedEarlierInCall`, the Network `"same"` duplicate assertion and the budget tests.
- [ ] **Step 5: Sweep.**
  - Run: `git grep -nE "> 50\b|> 256\b|< 1 or > 50|1 to 50" -- TiaMcpServer/Network/NetworkOperationCatalog.cs TiaMcpServer/Plc/PlcOperationCatalog.cs TiaMcpServer/Hmi/HmiOperationCatalog.cs TiaMcpServer/Hmi/HmiWriteCatalog.cs TiaMcpServer/Vci/VciReadCatalog.cs TiaMcpServer/Vci/VciWriteCatalog.cs`
  - Expected: no hits.
  - Run: `git grep -nwE "MaxBatchSize|MaxOperationIdLength" -- TiaMcpServer TiaMcpServer.Tests`
  - Expected: no hits.
- [ ] **Step 6: Commit.** Message: `refactor: share batch envelope rules across catalogs`

**Task test command:** the Step 4 filter.

### Task 3: Write pipeline entry guard uses the shared rules

**Files:**
- Modify: `TiaMcpServer/Safety/Pipeline/WriteRun.cs`, `ValidateInput` (:167-188). The domain delegation (:190-194) is unchanged.
- Add `using TiaMcpServer.OperationBatches;` if `WriteRun.cs` lacks it.
- Test: `TiaMcpServer.Tests/Safety/Pipeline/WriteExecutionTests.cs`.

**Interfaces:**
- Consumes (Task 1): `OperationBatchRules.CheckCount(Items.Count)` and `CheckItem(item, seenIds)`, with one ordinal `HashSet<string>`.
- Produces: no new members. The first failing message is returned through the existing `Validation(...)` helper with category `validation_error`.

- [ ] **Step 1: Write the tests,** using the fixture of the existing `DuplicateOperationIds_…` test (:48).
  - `DuplicateOversizedOperationId_IsRejectedWithoutEcho`: two items with the same 300-character ID give `validation_error`. `ErrorMessage(doc)` equals the length message, and the document text does not contain the ID.
  - `MoreThanFiftyItems_IsRejectedAtEntry`: 51 items give `validation_error`, and `ErrorMessage(doc)` equals `Batch exceeds the maximum of 50 operations (received 51).` The domain never validated: `Assert.Equal(new[] { "compose" }, _domain.Calls)`, as at :44 and :55.
  - Update `NullItemOrBlankOperationId_IsRejectedBeforeTheGate`: its `Assert.Contains("index 1", …)` (:63) becomes `Assert.Equal("Batch contains a null operation.", ErrorMessage(nullDoc))`, and the blank case asserts `Each operation requires a unique operationId.`
- [ ] **Step 2: Run them.**
  - Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~DuplicateOversizedOperationId_IsRejectedWithoutEcho|FullyQualifiedName~MoreThanFiftyItems_IsRejectedAtEntry|FullyQualifiedName~NullItemOrBlankOperationId_IsRejectedBeforeTheGate"`
  - Expected: all three FAIL. The first echoes the ID, the second reaches the domain, and the third still gets the index wording.
- [ ] **Step 3: Replace the three checks** with `CheckCount`, then a `CheckItem` loop that returns the first message.
- [ ] **Step 4: Run the pipeline and lifecycle tests.**
  - Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~TiaMcpServer.Tests.Safety|FullyQualifiedName~Lifecycle|FullyQualifiedName~GuardedWriteMcpTests"`
  - Expected: all PASS, including `EmptyCall_…`, `DuplicateOperationIds_…` and `NullItemOrBlankOperationId_…`.
- [ ] **Step 5: Commit.** Message: `refactor: use shared batch rules in the write entry guard`

**Task test command:** the Step 4 filter.

### Task 4: One validation-result type

**Files:**
- Modify: `TiaMcpServer/OperationBatches/OperationBatchRules.cs` (add the record).
- Modify: the six catalogs' `Validate*` return types. Delete `NetworkValidationResult` (`NetworkOperationCatalog.cs` :20-25).
- Modify: the read tools `PlcReadTools.cs` (:33), `NetworkReadTools.cs` (:39) and `HmiReadTools.cs` (:34), which pass `validation.Category`. `VciReadTools.cs` already does.
- Modify: the write domains `PlcWriteDomain.cs` (:32), `NetworkWriteDomain.cs` (:29) and `HmiWriteDomain.cs` (:32), which use `WriteValidation.Invalid(validation.Category, validation.Error)`. `VciWriteDomain.cs` already does.
- Test: `NetworkSubnetLifecycleRequestContractTests.cs` (:24 names the type; add `using TiaMcpServer.OperationBatches;`) and `OperationBatchRulesTests`.

**Interfaces:**
- Produces: `public sealed record OperationValidation(bool IsValid, string Category, string Error)`. It must be public, because the catalogs are public.
  - `public static OperationValidation Valid { get; }` is `new(true, string.Empty, string.Empty)`.
  - `public static OperationValidation Invalid(string category, string error)`. The argument order matches `WriteValidation.Invalid(category, message)`; there is no default, so a swapped pair cannot hide behind one. Network, PLC and HMI pass `WorkerFailureCategories.ValidationError` (`OperationBatchRules.cs` gains `using TiaMcpServer.Contracts.Worker;` only if it names a category).

- [ ] **Step 1: Record a green baseline.**
  - Run: the Task 2 Step 4 filter.
  - Expected: PASS. Record the count.
- [ ] **Step 2: Add `OperationValidation_InvalidCarriesCategoryAndError`** to `OperationBatchRulesTests` (`Invalid("invalid_selector", "m")` gives `IsValid` false, `Category` `invalid_selector`, `Error` `m`). Then add the record, and switch every catalog, read tool and domain listed above.
- [ ] **Step 3: Rerun.**
  - Run: the same filter.
  - Expected: PASS, with the baseline count plus one.
- [ ] **Step 4: Sweep.**
  - Run: `git grep -nE "NetworkValidationResult|\(bool IsValid, string Error" -- TiaMcpServer TiaMcpServer.Tests`
  - Expected: no hits.
- [ ] **Step 5: Commit.** Message: `refactor: add one OperationValidation result type`

**Task test command:** the Task 2 Step 4 filter.

### Task 5: Docs and evidence (docs last)

The controller supplies the branch-verification values: the full-suite count, the skip count and the line rates. Use them as given. Do not run or invent them.

**Files:**
- Modify: `docs/SupportedOperations/VCI_OPERATIONS_SUMMARY.md` :5. Delete the stale "and one project path".
- Modify: `docs/IMPROVEMENT_LOG.md`. Add an open "shared batch rules follow-ups (C-2, <date>)" section with one bullet: operation names are echoed with no length limit in the unknown-operation and wrong-category messages of all six catalogs.
- Modify: `docs/superpowers/plans/2026-10-17-codebase-simplification-review.md`, item C-2.

**Interfaces:** none.

- [ ] **Step 1: Add an evidence sub-bullet under C-2,** in the format of the C-3 bullets. Include:
  - the date;
  - the full-suite count with zero skips, and the line rates;
  - the shared wording;
  - that the single-project rule was dropped because C-0 removed `projectPath`;
  - that the three ID-echo sites were fixed (HmiWrite/VciWrite prefix, PLC cross-item check, `WriteRun` duplicate check);
  - that `WriteRun` now enforces 50/256 at entry;
  - that an item failing the envelope check no longer contributes a VCI scope category;
  - that `OperationValidation` keeps `Error` (maintainer, 2026-10-23);
  - "No live TIA verification was performed; not needed because no Siemens calls are involved."
- [ ] **Step 2: Check.**
  - Run: `git grep -n "OperationValidation" -- docs/superpowers/plans/2026-10-17-codebase-simplification-review.md`
  - Expected: at least two hits, the original C-2 line and the evidence bullet.
  - Run: `git grep -n "one project path" -- docs/SupportedOperations/VCI_OPERATIONS_SUMMARY.md`
  - Expected: no hits.
- [ ] **Step 3: Commit.** Message: `docs: record C-2 evidence`

**Task test command:** the Step 2 greps.
