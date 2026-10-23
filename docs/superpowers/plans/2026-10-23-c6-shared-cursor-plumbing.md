# C-6 Shared Cursor Plumbing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The hardware-page, project-tree and HMI cursor codecs seal and open through one protector helper, throw one `CursorException`, and share one set of state rules. Every documented category and message stays the same, and the unreachable `preserveUnboundEpoch` flag is gone.

**Architecture:** `TiaMcpServer/Cursors` gains a `CursorException`, a `CursorKind` descriptor (purpose, message, foreign-process category), a `CursorStateRules` helper class, and `Seal`/`Open` on `AuthenticatedCursorProtector`. `ProjectBindingCursorState` moves there with one validity rule: unbound states keep their epoch everywhere. The three codecs keep only their state shape and validator.

**Tech Stack:** C#, net10.0 host, xUnit.

**Spec:** Review plan item C-6 ([review](2026-10-17-codebase-simplification-review.md); findings HOSTCORE-12, HOSTDOMAINS-9) and the [Workstream C plan](2026-10-21-workstream-c-plan.md), wave 3. The improvement-log entry "Unreachable `preserveUnboundEpoch` branches" is folded in.

**Branch:** `refactor/c6-shared-cursor-plumbing`, worktree `.worktrees/C-6`, cut from `main` @ `3e48de7` or later.

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

C-6 rules (maintainer decisions, 2026-10-23):

- **Categories and messages are byte-identical.**
  - A foreign-process cursor is `snapshot_unavailable` for the project tree, and `invalid_cursor` for HMI and hardware pages.
  - The messages stay `The supplied hardware page cursor is invalid.`, `The supplied project-tree cursor is invalid.` and `The supplied hmi_read cursor is invalid.`
  - `HmiWorkerInvoker` keeps its own "issued by an earlier server process" message, and an HMI encode failure stays `protocol_error` in `HmiPayloadContract`.
- **Unbound states keep their epoch everywhere.** `FromSnapshot` always keeps `BindingId` and `Revision` for an unbound snapshot. An unbound cursor matches only the same epoch. Hardware pages adopt the tree rule; after C-0 no hardware cursor can be issued unbound.
- **Oversized cursors keep today's behaviour.**
  - Hardware still returns its cursor-limit omission.
  - Tree and HMI still fail as `invalid_cursor` and `protocol_error`.
  - `Seal` keeps the size data as the inner exception, so the separate fix can use it.
- **Leave alone:**
  - the worker's `NetworkObjectCursorCodec` regex (net48, cannot share host code);
  - `ProjectSessionBinding.IsSameProject`;
  - the second protector in `NetworkReadTools` (C-5 territory).
- **Do not catch `ObjectDisposedException`.** No codec catches it today.
- **C-5 reuses this.** `CursorKind`, `Seal`, `Open`, `CursorStateRules` and `ProjectBindingCursorState.IsValid()` must not be private or tied to one domain.
- **Tests:** run only each task's focused filter. The orchestrator runs the full suite once per branch.

## Review Focus

1. **Foreign-process category per tool.** The tree must keep `snapshot_unavailable` while HMI and hardware keep `invalid_cursor`. Pinned by `ProjectTreeCursorCodecTests.Decode_ForeignProcessMapsToSnapshotUnavailable`, `HardwarePageCursorCodecTests.Decode_ForeignProcessStillMapsToHardwareInvalidCursor`, `HmiPageCursorCodecTests.Cursor_FromOtherProcessKey_IsInvalidCursor` and `ProjectTreeBrowseCoordinatorTests.MalformedForeignMismatchedAndOutOfRangeCursorsMapToClosedFailures`, run in Task 3.
2. **Oversized hardware cursor.** It must still become an omission carrying `CursorChars`/`LimitChars`, not an error. No test covers this today. Pinned by the new baseline `HardwarePageProjectorTests.Project_OversizedCursor_ReturnsCursorLimitOmission` in Task 3.
3. **An unbound cursor from an earlier unbound epoch** must be rejected. Pinned by `ProjectTreeBrowseCoordinatorTests.Cursor_UnboundThenBoundAndCleared_RejectsBeforeStoreAccessOrRenewal` and the new hardware tests in Task 2.
4. **A hash with a trailing newline** must be rejected everywhere, not only by the tree. Pinned by the new hardware codec and payload tests in Task 3.
5. **The messages a client sees** must stay the same. Pinned by the `InvalidCursor_KeepsDomainMessage` baseline tests in Task 3, which are green before and after.

**Contract change:** none. Hardware now rejects a null-shaped unbound binding and a hash with a trailing newline; neither can be issued.

**Live verification:** not needed. No Siemens calls are involved.

## After the tasks

These are not implementer tasks. They run after Task 3 and before Task 4.

### Branch verification (orchestrator)

1. Run the stub build.
   - Expected: 0 errors and no new warnings.
2. Run the full suite with coverage under `TestResults/c6`, with `TEMP`/`TMP` set to `TestResults/c6/temp`.
   - Expected: 0 failed and 0 skipped.
3. Run `scripts/verify-coverage-threshold.ps1 -MinimumLineRate 0.80`.
   - Expected: it passes.
4. Record the line rates of the changed `Cursors/` files and the three codecs from the Cobertura report.

The final whole-branch review follows.

---

### Task 1: Shared cursor primitives

**Files:**
- Create: `TiaMcpServer/Cursors/CursorRules.cs`, holding `CursorException`, `CursorKind` and `CursorStateRules`. `Cursors\*.cs` is globbed, so no csproj change is needed.
- Modify: `TiaMcpServer/Cursors/AuthenticatedCursorProtector.cs` (add `Seal`/`Open` after `Unprotect`).
- Test: `TiaMcpServer.Tests/Cursors/AuthenticatedCursorProtectorTests.cs` and a new `TiaMcpServer.Tests/Cursors/CursorStateRulesTests.cs`.

**Interfaces:**
- Consumes: `Protect`, `Unprotect`, `AuthenticatedCursorStatus`, `AuthenticatedCursorException`, `AuthenticatedCursorSizeException`, and `ProjectPathNormalization.Canonicalize`.
- Produces:
  - `internal sealed class CursorException(string category, string message, Exception? innerException = null) : Exception(message, innerException)`, with `internal string Category { get; }`.
  - `internal sealed record CursorKind(string Purpose, string InvalidMessage, string ForeignProcessCategory)`.
  - `internal static class CursorStateRules`:
    - `const int MaxSnapshotIdChars = 128`;
    - `bool IsLowerSha256(string? value)`: exactly 64 characters, each `0-9` or `a-f`. No regex, so a trailing `\n` cannot slip through `$`.
    - `bool IsValidSnapshotId(string? value)`: non-blank and at most 128 characters.
    - `bool SameProject(string? first, string? second)`: both canonicalize to non-null and are equal under `OrdinalIgnoreCase`.
  - On the protector:
    - `internal string Seal<TState>(CursorKind kind, TState state, Func<TState, bool> isValid) where TState : class`. A null or invalid state throws `CursorException(invalid_cursor, kind.InvalidMessage)`. `AuthenticatedCursorSizeException` is rethrown as the same, with the size exception as `InnerException`.
    - `internal TState Open<TState>(CursorKind kind, string cursor, Func<TState, bool> isValid) where TState : class`. `ForeignProcess` throws `CursorException(kind.ForeignProcessCategory, kind.InvalidMessage)`. `AuthenticatedCursorException`, `JsonException`, a null state or an invalid state throw `CursorException(invalid_cursor, kind.InvalidMessage)`.

- [ ] **Step 1: Write the tests.**
  - In `AuthenticatedCursorProtectorTests`, with a test kind whose foreign category is `snapshot_unavailable`:
    - `SealOpen_RoundTripsAValidState`.
    - `Open_ForeignProcess_UsesTheKindsCategoryAndMessage`: the cursor is sealed by a protector with another process ID. `Category` is `snapshot_unavailable`, and `Message` equals `kind.InvalidMessage`.
    - `Open_TamperedOrInvalidState_IsInvalidCursor`, a `[Theory]`: one flipped payload byte, and a valid seal whose `isValid` returns false at open.
    - `Seal_InvalidState_IsInvalidCursorWithKindMessage`.
    - `Seal_Oversized_KeepsSizeDataAsInnerException`: `InnerException` is `AuthenticatedCursorSizeException` with `LimitChars == 4096` and `CursorChars > 4096`.
  - In `CursorStateRulesTests`:
    - `IsLowerSha256`: true for 64 lowercase hex characters. False for null, `""`, 63 and 65 characters, uppercase, a `g`, the hash plus `"\n"`, and `"\n"` plus 63 characters.
    - `IsValidSnapshotId`: 128 characters true; 129 characters, blank and null false.
    - `SameProject`: false when either side is null; true for the same path in different case.
- [ ] **Step 2: Add the members as stubs.** `Seal` returns `""`, `Open` returns `default!`, and the rules return false.
  - Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~AuthenticatedCursorProtectorTests|FullyQualifiedName~CursorStateRulesTests"`
  - Expected: the new tests FAIL, except the false rows; the existing protector tests PASS.
- [ ] **Step 3: Implement the members.**
  - Run: the same filter.
  - Expected: all PASS.
- [ ] **Step 4: Commit.** Message: `feat: add shared cursor seal, open and state rules`

**Task test command:** the Step 2 filter.

### Task 2: One binding-state rule

**Files:**
- Create: `TiaMcpServer/Cursors/ProjectBindingCursorState.cs`. Move the record out of `TiaMcpServer/Network/HardwarePageCursorState.cs` (:16-59) into namespace `TiaMcpServer.Cursors`.
- Modify: `TiaMcpServer/Network/HardwarePageCursorCodec.cs`. Delete the private `IsValidHostBinding` (:86-100), and use `state.HostBinding.IsValid()`.
- Modify: `TiaMcpServer/ProjectTree/ProjectTreeCursorCodec.cs` (delete `IsValidHostBinding` :100-114) and `TiaMcpServer/Hmi/HmiPageCursorCodec.cs` (:101). Both use `.IsValid()`.
- Modify, to drop `preserveUnboundEpoch: true`: `ProjectTreePageProjector.cs` :115, `ProjectTreeBrowseCoordinator.cs` :149, `HmiPayloadContract.cs` :151 and `HmiWorkerInvoker.cs` :66.
- Add `using TiaMcpServer.Cursors;` to `Network/HardwarePageCursorState.cs`, `ProjectTree/ProjectTreePageProjector.cs` and `Hmi/HmiPayloadContract.cs`, which name the moved type and lack it.
- Test: `TiaMcpServer.Tests/Network/HardwarePageCursorCodecTests.cs`. Also drop the argument in the three other test files that use it: `HmiPayloadContractDecodeTests` (:231), `HmiReadToolsTests` (:72) and `ProjectTreeBrowseCoordinatorTests` (:298, :317, :346, :354, :362). The test files that name the type already import `TiaMcpServer.Cursors`.

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces, on `internal sealed record ProjectBindingCursorState(bool IsBound, string? BindingId, long? Revision, string? NormalizedProjectPath)`:
  - `static ProjectBindingCursorState FromSnapshot(ProjectBindingSnapshot snapshot)`: an unbound snapshot gives `(false, snapshot.BindingId, snapshot.Revision, null)`, and a bound one is unchanged.
  - `bool Matches(ProjectBindingSnapshot snapshot)`: equal `IsBound`, `BindingId` (ordinal) and `Revision`, and path under `OrdinalIgnoreCase`. There is no unbound shortcut.
  - `bool IsValid()`: today's tree rule. Unbound needs a non-blank ID, `Revision >= 0` and a null path. Bound needs a non-blank ID, `Revision >= 0` and an already-canonical path.

- [ ] **Step 1: Rewrite and add the hardware tests.**
  - `UnboundNetworkCaptureAndCodec_RetainLegacyShape` becomes `UnboundCapture_KeepsEpochAndRoundTrips`. The capture of `epoch-a`/0 equals `(false, "epoch-a", 0, null)`. It matches `epoch-a`/0 and not `epoch-b`/2, and the codec round trip returns the same binding.
  - `Validate_UnboundHostCursor_RemainsValidWhileHostIsUnbound` becomes `Validate_UnboundHostCursor_FromAnotherUnboundEpoch_IsBindingMismatch`, which expects `cursor_binding_mismatch`.
  - Add `Validate_UnboundHostCursor_SameEpoch_IsValid`, which expects null.
  - Add `Decode_RejectsSignedNullShapedUnboundBinding`: a signed state with binding `(false, null, null, null)` gives `invalid_cursor`.
- [ ] **Step 2: Run them.**
  - Run: `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~HardwarePageCursorCodecTests`
  - Expected: `UnboundCapture_KeepsEpochAndRoundTrips`, `…FromAnotherUnboundEpoch_IsBindingMismatch` and `Decode_RejectsSignedNullShapedUnboundBinding` FAIL.
- [ ] **Step 3: Move the record, delete the flag and the two `IsValidHostBinding` copies,** and update every caller. A coordinator or projector test that expects a cursor from one unbound epoch to stay valid under a different unbound snapshot is testing the retired rule: issue and check against the same snapshot instead, and say so in the commit's task report.
- [ ] **Step 4: Run the cursor domains.**
  - Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~TiaMcpServer.Tests.Network|FullyQualifiedName~TiaMcpServer.Tests.Project|FullyQualifiedName~TiaMcpServer.Tests.Hmi|FullyQualifiedName~TiaMcpServer.Tests.Cursors"`
  - Expected: all PASS, including `UnboundEpochComparison_RejectsEitherIdentityOrRevisionChange` without its flag argument, and `Cursor_UnboundThenBoundAndCleared_RejectsBeforeStoreAccessOrRenewal`.
- [ ] **Step 5: Sweep.**
  - Run: `git grep -n "preserveUnboundEpoch\|IsValidHostBinding" -- TiaMcpServer TiaMcpServer.Tests`
  - Expected: no hits.
- [ ] **Step 6: Commit.** Message: `refactor: keep unbound cursor epochs in one binding rule`

**Task test command:** the Step 4 filter.

### Task 3: Codecs use the shared seal and open

**Files:**
- Modify: `TiaMcpServer/Network/HardwarePageCursorCodec.cs`. Delete `HardwarePageCursorException` (:10-18), the regex (:24) and `SameProject` (:102-109); `ValidateState` becomes `private static bool IsValid(HardwarePageCursorState)`.
- Modify: `TiaMcpServer/ProjectTree/ProjectTreeCursorCodec.cs`. Delete `ProjectTreeCursorException` (:16-25), the regex (:31), `MaximumSnapshotIdChars`, `IsValidSnapshotId` and `IsValidQueryHash`.
- Modify: `TiaMcpServer/Hmi/HmiPageCursorCodec.cs`. Delete `HmiPageCursorException` (:18-27). Switch :96/:98 to `CursorStateRules.IsValidSnapshotId`/`IsLowerSha256`, and change the `<exception cref>` at :60 to `CursorException`.
- Modify: `TiaMcpServer/Network/HardwarePagePayloadContract.cs`. Replace the regex (:20-22, used at :89) with `CursorStateRules.IsLowerSha256`.
- Modify: `TiaMcpServer/ProjectTree/ProjectTreeSnapshotStore.cs` (:75, :206-207), which uses `CursorStateRules`.
- Modify the catches:
  - `HardwarePaginationCoordinator.cs` :70 catches `CursorException`.
  - `HardwarePageProjector.cs` :63 catches `CursorException { InnerException: AuthenticatedCursorSizeException size }`.
  - `ProjectTreeBrowseCoordinator.cs` :85-88 and `HmiWorkerInvoker.cs` :53 catch `CursorException`.
  - `HmiPayloadContract.cs` :88 catches `CursorException or JsonException`.
- Every file listed under Modify that lacks `using TiaMcpServer.Cursors;` gains it: at least `HardwarePaginationCoordinator.cs`, `ProjectTreeBrowseCoordinator.cs`, `HmiWorkerInvoker.cs`, `HmiPayloadContract.cs`, `ProjectTreeSnapshotStore.cs` and `HardwarePagePayloadContract.cs`.
- Test: `HardwarePageCursorCodecTests`, `HardwarePagePayloadContractTests`, `HardwarePageProjectorTests`, `ProjectTreeCursorCodecTests`, `HmiPageCursorCodecTests` and `HardwarePaginationFakeWorkerTests` (:163 names `HardwarePageCursorException`). Repoint any test that names a deleted exception type to `CursorException`.

**Interfaces:**
- Consumes (Task 1): `CursorKind`, `CursorException`, `Seal`, `Open` and `CursorStateRules`. (Task 2): `ProjectBindingCursorState.IsValid()`.
- Produces: each codec declares `private static readonly CursorKind Kind`:
  - hardware: `("hardware-page", "The supplied hardware page cursor is invalid.", invalid_cursor)`;
  - tree: `("project-tree", "The supplied project-tree cursor is invalid.", snapshot_unavailable)`;
  - HMI: `("hmi-page", "The supplied hmi_read cursor is invalid.", invalid_cursor)`.
  - `Encode` is `_protector.Seal(Kind, state, IsValid)`, and `Decode` is `_protector.Open<TState>(Kind, cursor, IsValid)`. The public signatures stay the same.

- [ ] **Step 1: Add the message baselines,** asserting through `Assert.ThrowsAny<Exception>` so that they compile before and after the change.
  - Add `InvalidCursor_KeepsDomainMessage` to each of the three codec test classes. A tampered cursor and a foreign-process cursor each give exactly the codec's message.
  - Add `HardwarePageProjectorTests.Project_OversizedCursor_ReturnsCursorLimitOmission`. Use a bound binding whose canonical project path is long enough to push the sealed cursor past 4,096 characters, with the same path as `resolvedProjectPath` and as the identity's `ProjectPath`, and a returned count below the total so that a cursor is issued. Assert that the omission reason is `HardwarePageProjector.CursorLimitReason`, `LimitChars == 4096` and `CursorChars > 4096`.
  - Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~TiaMcpServer.Tests.Network|FullyQualifiedName~TiaMcpServer.Tests.Project|FullyQualifiedName~TiaMcpServer.Tests.Hmi|FullyQualifiedName~TiaMcpServer.Tests.Cursors"`
  - Expected: PASS. Record the count.
- [ ] **Step 2: Write the newline tests.**
  - `HardwarePageCursorCodecTests.Encode_RejectsQueryHashWithTrailingNewline`: `invalid_cursor`.
  - `HardwarePageCursorCodecTests.Decode_RejectsSignedSnapshotHashWithTrailingNewline`: `invalid_cursor`.
  - `HardwarePagePayloadContractTests.Decode_RejectsSnapshotHashWithTrailingNewline`: a protocol failure, through the file's `AssertProtocolFailure`.
  - Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~TrailingNewline"`
  - Expected: the three new tests FAIL; the two existing tree tests PASS.
- [ ] **Step 3: Move the three codecs onto `Seal`/`Open`,** delete the listed members, and switch every catch.
- [ ] **Step 4: Rerun.**
  - Run: the Step 1 filter.
  - Expected: PASS, with the baseline count plus three, including every Review Focus 1–2 pin.
- [ ] **Step 5: Sweep.**
  - Run: `git grep -nE "HardwarePageCursorException|ProjectTreeCursorException|HmiPageCursorException" -- TiaMcpServer TiaMcpServer.Tests`
  - Expected: no hits.
  - Run: `git grep -nE "0-9a-f\]\{64\}" -- TiaMcpServer`
  - Expected: no hits. Test `Assert.Matches` patterns and the worker copy under `TiaMcpServer.OpennessWorker` are outside this filter.
- [ ] **Step 6: Commit.** Message: `refactor: seal and open all host cursors through one helper`

**Task test command:** the Step 1 filter.

### Task 4: Docs and evidence (docs last)

The controller supplies the branch-verification values: the full-suite count, the skip count and the line rates. Use them as given. Do not run or invent them.

**Files:**
- Modify: `docs/ARCHITECTURE.md`. Rewrite :629-631 ("mirrors `ProjectTreeCursorCodec`") to say the three codecs share `Seal`/`Open`, and fix any other mention of the deleted exception classes or `preserveUnboundEpoch` (`git grep` them under `docs/` and `AGENTS.md`, excluding `docs/superpowers/`).
- Modify: `docs/guides/mcp-client-configuration.md` :86. A tree cursor whose binding changed returns `cursor_binding_mismatch`, not "invalid".
- Modify: `docs/IMPROVEMENT_LOG.md`, in the C-0 follow-ups section.
  - Delete the "Unreachable `preserveUnboundEpoch` branches" bullet.
  - Replace the "Tree-cursor message bug" bullet with "**Oversized tree and HMI cursors.**" A cursor above 4,096 characters, for example from a long bound project path, fails as `invalid_cursor` ("The supplied project-tree cursor is invalid.") on a browse that sent no cursor, or as an HMI `protocol_error`, while hardware pages return a cursor-limit omission. `Seal` keeps the size data as the inner exception. Fix in its own PR.
- Modify: `docs/superpowers/plans/2026-10-17-codebase-simplification-review.md`, item C-6 (:122). Amend the item text: hardware pages adopt the unbound-epoch rule (maintainer, 2026-10-23), and the shared `IsValidHostBinding` became `ProjectBindingCursorState.IsValid()`.

**Interfaces:** none.

- [ ] **Step 1: Make the doc edits above.**
- [ ] **Step 2: Add an evidence sub-bullet under C-6,** in the format of the C-3 bullets. Include:
  - the date;
  - the full-suite count with zero skips, and the line rates;
  - the shared pieces by name: `CursorKind`, `CursorException`, `Seal`/`Open` and `CursorStateRules`;
  - that categories and messages are unchanged;
  - that unbound cursors keep their epoch everywhere (maintainer, 2026-10-23) and the flag is gone;
  - that the logged tree-cursor message bug was not found as described and was re-logged as the oversized-cursor defect;
  - "No live TIA verification was performed; not needed because no Siemens calls are involved."
- [ ] **Step 3: Check.**
  - Run: `git grep -n "preserveUnboundEpoch\|Tree-cursor message bug" -- docs/IMPROVEMENT_LOG.md docs/ARCHITECTURE.md AGENTS.md`
  - Expected: no hits.
  - Run: `git grep -n "CursorKind" -- docs/superpowers/plans/2026-10-17-codebase-simplification-review.md`
  - Expected: at least one hit, in the evidence bullet.
- [ ] **Step 4: Commit.** Message: `docs: record C-6 evidence`

**Task test command:** the Step 3 greps.
