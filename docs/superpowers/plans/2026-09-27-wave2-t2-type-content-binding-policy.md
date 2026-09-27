# Wave 2 T2 Type-Content Binding Policy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Classify `get_type_content` consistently with `get_block_content` so it is allowed in read-only mode, uses the intended optional expected-session identity policy, and receives accurate transport/binding guidance.

**Architecture:** Add the missing operation capability to the shared `OperationPolicyCatalog`, which is already the single authority consumed by host access checks, worker authorization, expected-identity policy, and transport guidance. Exercise those consumers and the registered FakeWorker path without changing schemas or dispatch. Separately correct the worker's missing-identity text so it no longer claims that `get_project_status` by itself establishes a verified binding.

**Tech Stack:** C#; .NET 10 host/tests/FakeWorker; .NET Framework 4.8 Openness worker; netstandard2.0 contracts; xUnit; TIA Portal V21 references for compatibility build only.

**Spec:** [Open Bug Parallel Pull-Request Delivery Design](../specs/2026-09-26-open-bug-parallel-pr-delivery-design.md), lane T2. Issue slice: [#82.1](https://github.com/Czarnak/tia-portal-mcp/issues/82).

**Future branch:** `fix/type-content-binding-policy`, created in its own worktree from the then-current `origin/main`; its pull request targets `main` directly.

## Global Constraints

- L1 is merged at the planning baseline `9b0fb50f`. Refresh `origin/main`, issue/PR state, and the working tree before implementation. If current `main` changed the policy/session seams, stop and revalidate this plan.
- This plan authorizes implementation only after user review and selection of an execution method. It does not authorize push, PR creation, merge, issue edits, or live TIA access.
- Classify only `get_type_content` as `OperationCapability.TemporaryExport`. Keep `update_type_content` a write and keep unknown operations fail-closed.
- Optional expected identity must match `get_block_content`: an unbound request may use an explicit or configured project path that the host can resolve, while an already bound request still carries and enforces its verified identity. A request with neither a usable path nor an available project remains a failure. Do not weaken binding conflicts or supplied-identity mismatch checks.
- Preserve operation schemas, worker dispatch, batch envelopes, safety tokens, audit behavior, pagination/budgets, and all write behavior.
- The worker guidance may name `open_project`, `create_project`, or a configured `--project` that the host verifies. It must not imply that calling `get_project_status` silently binds a session.
- The maintained import/export operation table already identifies `get_type_content` as a read. T2 aligns code to that current documentation and therefore owns no maintained user-documentation file; the implementation PR records a documentation audit instead of editing unrelated prose.
- Run builds and tests serially (`-m:1`). Stub, FakeWorker, static-source, and package checks are not live V21 evidence.
- Use one conventional commit per task after focused tests and scoped diff review. Preserve unrelated user work.

## File Ownership

T2 implementation allowlist:

| Responsibility | Files |
| --- | --- |
| Shared policy | Modify `TiaMcpServer.Contracts/OperationPolicyCatalog.cs` |
| Worker binding guidance | Modify `TiaMcpServer.OpennessWorker/Openness/TiaPortalSession.cs` |
| Policy/access/transport tests | Modify `TiaMcpServer.Tests/Safety/ReadOnlyModeTests.cs`, `TiaMcpServer.Tests/Worker/WorkerTransportFailureGuidanceTests.cs`, and `TiaMcpServer.Tests/Worker/FakeWorkerIdentityEnforcementTests.cs` |
| Registered read-only regression | Modify `TiaMcpServer.Tests/Batch/TypeOperationFakeWorkerTests.cs` |
| Guidance source contract | Create `TiaMcpServer.Tests/Worker/TiaPortalSessionBindingGuidanceSourceTests.cs` |

Reserve these files while T2 is active. I2's allowlist is disjoint. Do not edit worker `Program.cs`, `WorkerRequest.cs`, `OpennessWorkerClient.cs`, `BatchWorkerInvoker.cs`, FakeWorker `Program.cs`, `ProjectSessionBinding.cs`, safety/token/audit classes, any operation schema, `README.md`, `docs/ARCHITECTURE.md`, or a SupportedOperations file. If a reserved file appears necessary, pause and obtain a reviewed whole-file plan amendment.

## Review Focus

1. `get_type_content` in read-only mode: host policy, worker policy, expected-session identity policy, transport guidance, and the registered FakeWorker path must agree.
2. `update_type_content` and unknown names: both remain fail-closed and must not inherit read capability through prefix/name similarity.
3. Unbound execution: an explicit/configured path or one uniquely attachable open project follows `get_block_content` parity, while a request with neither a usable path nor an available project still fails worker-side.
4. Bound execution with a mismatched supplied identity: the existing identity conflict remains enforced even though omission is allowed for the unbound path.
5. Guidance regression: no message may say `get_project_status` alone establishes binding; the static test is wording evidence only, not live behavior proof.

---

### Task 1: Align the shared operation policy and all consumers

**Files:** Modify `TiaMcpServer.Contracts/OperationPolicyCatalog.cs`, `TiaMcpServer.Tests/Safety/ReadOnlyModeTests.cs`, `TiaMcpServer.Tests/Worker/WorkerTransportFailureGuidanceTests.cs`, `TiaMcpServer.Tests/Worker/FakeWorkerIdentityEnforcementTests.cs`, and `TiaMcpServer.Tests/Batch/TypeOperationFakeWorkerTests.cs`.

**Interfaces:** Add exactly `['get_type_content'] = OperationCapability.TemporaryExport` beside `get_block_content` in the catalog. Do not introduce a second allowlist. Existing `OperationPolicyCatalog.RequiresExpectedSessionIdentity`, `OperationAccessPolicy`, `WorkerOperationAuthorization`, and `WorkerTransportFailureGuidance` consume the new entry unchanged.

- [ ] **Step 1: Add policy-parity RED rows.** In `ExpectedSessionIdentityPolicy_IsFailClosed`, add `get_type_content` with expected result false. Add it to the approved host and worker read-only cases. In `SafetyClassificationComesFromOperationPolicy`, assert safe-read guidance true. Add negative assertions for `update_type_content` and an unknown lookalike.
- [ ] **Step 2: Add host-path and worker-identity parity REDs.** In `TypeOperationFakeWorkerTests`, add bound success, unbound explicit-path success, unbound configured-path success, `ExecuteReadBatch_GetTypeContent_ReadOnlyMode_UnboundWithoutPath_MatchesBlockContent`, and `ExecuteReadBatch_GetTypeContent_ReadOnlyMode_BoundProjectPathConflictFailsBeforeDispatch`. Reuse identical `get_block_content` arrangements. For no path, assert the two operations have the same dispatch count and scripted result/category; do not claim unconditional or pre-dispatch failure because a real worker may attach one already-open project. The path-conflict case separately proves the host rejects a requested path that conflicts with its binding. In `FakeWorkerIdentityEnforcementTests`, add `GetTypeContent_SuppliedMismatchedExpectedIdentity_ReturnsBindingConflict`: send the optional-identity operation directly with a supplied mismatched identity, prove dispatch reaches the worker identity guard, and assert `binding_conflict`. These are distinct boundaries; do not claim the public batch path accepts a caller-supplied identity. Do not modify FakeWorker dispatch or weaken either check.
- [ ] **Step 3: Run RED.**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -m:1 /p:UseTiaPortalReferenceStubs=true --filter "FullyQualifiedName~ReadOnlyModeTests|FullyQualifiedName~WorkerTransportFailureGuidanceTests|FullyQualifiedName~FakeWorkerIdentityEnforcementTests|FullyQualifiedName~TypeOperationFakeWorkerTests"
```

Expected: current catalog omission makes identity required, denies read-only access, selects mutation-risk transport guidance, or blocks the registered read.

- [ ] **Step 4: Add the single catalog entry.** Do not change consumer code, dispatch, schemas, or binding classes.
- [ ] **Step 5: Run the Step 3 filter to GREEN.** Confirm bound, unbound-explicit, unbound-configured, no-path, `get_block_content`, write-denial, unknown-operation, and identity-mismatch cases all have parity.
- [ ] **Step 6: Review and commit.** Run `git diff --check`; inspect only the five Task 1 files; stage them explicitly and commit `fix: align type content read policy`.

### Task 2: Correct verified-binding guidance and run T2 gates

**Files:** Modify `TiaMcpServer.OpennessWorker/Openness/TiaPortalSession.cs`; create `TiaMcpServer.Tests/Worker/TiaPortalSessionBindingGuidanceSourceTests.cs`.

**Interfaces:** In `TiaPortalSession.ValidateExpectedSessionIdentity`, replace the missing-identity remedy with one bounded actionable message: establish a verified project binding by `open_project` or `create_project`, or configure `--project` and let the host verify it before the operation. Keep the same exception type/category and all actual identity comparisons. The source-contract test locates this method and asserts the remedy contains `verified project binding` plus an approved route and does not present `get_project_status` as a binding action.

- [ ] **Step 1: Write the guidance RED.** Add `MissingIdentityGuidance_RequiresVerifiedBindingAndDoesNotClaimStatusBinds`. This is a source-contract guard because the net48 session class is not linked into the net10 test assembly; label the limitation in the test name/comment.
- [ ] **Step 2: Run RED.**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -m:1 /p:UseTiaPortalReferenceStubs=true --filter "FullyQualifiedName~TiaPortalSessionBindingGuidanceSourceTests"
```

Expected: the current remedy names `get_project_status` as if it establishes binding.

- [ ] **Step 3: Change only the guidance text.** Do not alter `allowMissingExpected`, expected/actual identity fields, failure category, or session state.
- [ ] **Step 4: Run the guidance test and Task 1 filter to GREEN.**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -m:1 /p:UseTiaPortalReferenceStubs=true --filter "FullyQualifiedName~TiaPortalSessionBindingGuidanceSourceTests|FullyQualifiedName~ReadOnlyModeTests|FullyQualifiedName~WorkerTransportFailureGuidanceTests|FullyQualifiedName~FakeWorkerIdentityEnforcementTests|FullyQualifiedName~TypeOperationFakeWorkerTests"
```

- [ ] **Step 5: Audit maintained documentation.** Confirm `docs/SupportedOperations/IMPORT_EXPORT_OPTIONS_SUMMARY.md` still describes `get_type_content` as a read and does not promise projectless execution. Record the no-change conclusion in the PR; do not edit the file merely to touch documentation.
- [ ] **Step 6: Run complete serial offline/reference/package gates.**

```powershell
dotnet restore TiaMcpServer.sln
dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -m:1 /p:UseTiaPortalReferenceStubs=true
dotnet build TiaMcpServer.sln -m:1 /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"
$t2PackageDir = Join-Path 'artifacts' ('t2-package-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $t2PackageDir | Out-Null
dotnet pack TiaMcpServer/TiaMcpServer.csproj -c Release -m:1 /p:UseTiaPortalReferenceStubs=true -o $t2PackageDir
$t2Packages = @(Get-ChildItem -LiteralPath $t2PackageDir -Filter '*.nupkg')
if ($t2Packages.Count -ne 1) { throw "Expected exactly one T2 package, found $($t2Packages.Count)." }
pwsh -NoProfile -File scripts/verify-doctor-package.ps1 -PackagePath $t2Packages[0].FullName
git diff --check
```

If installed V21 references are unavailable, record that limitation; do not relabel the stub build as Siemens API proof.

- [ ] **Step 7: Self-review, independent review, and commit.** Check all five Review Focus cases, exact allowlist, no schema/access-write/safety/audit/budget changes, and no hidden documentation drift. Resolve findings, rerun affected gates, stage only Task 2 files, and commit `fix: clarify verified project binding guidance`.

## Live-Evidence Boundary

T2 requires no live V21 run for merge: the repair is a deterministic shared-policy entry plus guidance text, exercised through bound, unbound explicit/configured/no-path parity, host path-conflict, supplied worker-identity mismatch, host policy, and worker policy. The real-reference build is still required when installed references are available; FakeWorker no-path parity is not evidence of real Portal attachment behavior.

This plan authorizes no optional live check. Any later request for one needs a separately reviewed frozen-head, target, binding, evidence, and serialization gate; it must not be improvised from the I2 mutation gate.

## Pull-Request and Integration Gate

- PR title: `fix(policy): align type content read binding`.
- PR body uses `Refs #82` and names only slice #82.1. The umbrella issue must not be closed while other checklist slices remain.
- Base branch is `main`; T2 is not stacked on I2 or another feature branch.
- After focused/full/reference/package gates and independent review, T2 may merge independently because its plan requires no live barrier and its allowlist is disjoint from I2.
- Before PR or merge readiness, incorporate current `main`, rerun the focused/full stub/real-reference/package/documentation/diff gates, and repeat independent review on the exact candidate head. Verify that head does not change afterward. PR creation, push, and merge each require explicit authority.
- After merge, pull current `main`, rerun the serial stub build/full tests and focused T2 filter, inspect the merge result, and notify the I2 owner to refresh before I2's live freeze.
