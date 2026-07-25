# Phase 5 Plan 4: Certification and Documentation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Certify the complete Phase 5 implementation with automated and real TIA Portal V21 evidence, align repository and source-skill documentation, refresh the architecture graph, and close the phase only when all 45 approved criteria pass.

**Architecture:** Treat certification as a release gate, not a documentation exercise. First pin automated regression and documentation contracts. Then execute live acceptance on a user-authorized disposable project copy. Update source documentation only in its owning checkout after explicit authorization. Finally refresh graphify, run one whole-branch review and security review, rerun the full acceptance suite, and record a durable report.

**Tech Stack:** xUnit, .NET build/test/coverage, TIA Portal V21 + Openness, MCP lifecycle and batch tools, PowerShell, graphify, Markdown acceptance reports.

## Global Constraints

- This plan starts only after Plans 1–3 pass their exit gates.
- Authoritative criteria: `docs/superpowers/acceptance/2026-07-23-phase5-reliability-lifecycle-integrity.md` with Status `Approved`.
- Live tests use a disposable project copy explicitly authorized by the user. Never use an irreplaceable engineering project.
- Record exact TIA Portal and Siemens Openness assembly versions, project-copy provenance, commands, outputs, and audit effects.
- Do not remove known-issue wording until the matching live tests pass.
- Do not edit `C:\Users\LCZ\.codex\plugins\cache\...`. AC-039 requires the owning source checkout and explicit authorization.
- No push, PR, merge, publication, credential change, or third-party modification is authorized by this local plan.
- One consolidated whole-branch review occurs after all implementation tasks, followed by fresh full acceptance. Do not rely on earlier task evidence after a final fix.

## Acceptance Ownership

| Area | Criteria |
|---|---|
| CI and coverage | AC-001–AC-003 |
| Architecture and tool surface | AC-004–AC-005, AC-009, AC-044 |
| Status/probe behavior | AC-006–AC-010, AC-012, AC-045 |
| Binding and SaveAs | AC-011, AC-013–AC-018 |
| Warnings, categories, safety, recovery | AC-019–AC-022, AC-032–AC-035, AC-043 |
| PLC block writes | AC-023–AC-031 |
| Regression, docs, source skill, graph | AC-036–AC-040 |
| Security and boundary validation | AC-041–AC-042 |

---

## Task 1: Pin automated regression and documentation contracts

**Files:**

- Modify: `TiaMcpServer.Tests/McpToolSchemaTests.cs`
- Modify: `TiaMcpServer.Tests/WriteToolSafetyTokenTests.cs`
- Modify: `TiaMcpServer.Tests/Diagnostics/CiWorkflowTests.cs`
- Modify: `README.md`
- Modify: `docs/IMPROVEMENT_PLAN.md`

**Acceptance:** AC-001–AC-005, AC-036, AC-038, AC-044; preparatory evidence for AC-039.

- [ ] **Step 1: Add/confirm RED documentation and surface tests before prose changes.**

  Pin:

  - exactly ten MCP tools;
  - exactly seven lifecycle methods: `get_project_status`, `open_project`, `create_project`, `save_project`, `save_project_as`, `archive_project`, `close_project`;
  - no MCP-visible `probe_project_status_for_lifecycle`;
  - lifecycle writes remain self-previewing with the existing token/confirm contract;
  - README build command contains `-m:1`;
  - README no longer instructs deliberate project switching through side-effecting `get_project_status`.

  Run the focused tests and observe RED for any stale README/surface expectation before editing docs.

- [ ] **Step 2: Update user-facing repository documentation conservatively.**

  In `README.md`:

  - describe `get_project_status` as read-only and non-binding;
  - describe explicit switching through `open_project`;
  - state `save_project_as` requires `rebind:true`;
  - document categorized failures and separate warning arrays;
  - document serialized build and coverage commands;
  - keep `update_block_logic` and SCL `create_block` known issues marked `pending live V21 re-verification` until Task 2 passes.

  In `docs/IMPROVEMENT_PLAN.md`:

  - mark Phase 5 implementation tasks complete only as their automated gates pass;
  - keep live acceptance and source-skill update open;
  - retain Transactions, ExclusiveAccess, Openness events, authorization expansion, and doctor enhancements in Phase 6/later scope.

- [ ] **Step 3: Run the automated baseline.**

  ```powershell
  dotnet restore TiaMcpServer.sln
  dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
  dotnet test TiaMcpServer.sln --no-restore --no-build --verbosity minimal
  ```

  This run must include all Round 4 regression tests; do not replace it with focused Phase 5 filters.

- [ ] **Step 4: Run the scoped coverage gate in a fresh directory.**

  ```powershell
  $phase5Results = Join-Path 'TestResults' ('phase5-final-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --collect:"XPlat Code Coverage" --settings TiaMcpServer.Tests/coverage.runsettings --results-directory $phase5Results
  $reports = @(Get-ChildItem -LiteralPath $phase5Results -Recurse -Filter coverage.cobertura.xml)
  if ($reports.Count -ne 1) { throw "Expected exactly one Cobertura report; found $($reports.Count)." }
  ./scripts/verify-coverage-threshold.ps1 -CoveragePath $reports[0].FullName -MinimumLineRate 0.80
  ```

- [ ] **Step 5: Commit the pre-live documentation state.**

  ```powershell
  git add README.md docs/IMPROVEMENT_PLAN.md TiaMcpServer.Tests
  git commit -m "docs: align phase 5 automated behavior"
  ```

---

## Task 2: Execute live TIA Portal V21 acceptance

**Files:**

- Create: `docs/superpowers/acceptance/reports/2026-07-23-phase5-reliability-lifecycle-integrity.md`
- Do not modify the source project; use a disposable copy.

**Acceptance:** Live/API portions of AC-006–AC-010, AC-015–AC-016, AC-026–AC-028, AC-031, AC-035, AC-037, plus live confirmation of AC-041–AC-043.

- [ ] **Step 1: Establish an explicit safe environment gate.**

  Before any write, require the user to identify and authorize:

  - disposable source project copy A;
  - empty target directory for SaveAs copy B;
  - existing PLC/block path suitable for export/no-op/edit tests;
  - new unique SCL block path for create-block testing.

  Record the canonical paths in the report. Abort if any target resolves to the original/non-disposable project or if target B already contains user data.

- [ ] **Step 2: Record runtime identity and baseline state.**

  Record:

  - TIA Portal product/version shown by the running installation;
  - file versions of `Siemens.Engineering.Base.dll` and `Siemens.Engineering.Step7.dll` used by the worker;
  - worker build commit and host build commit;
  - initial open project canonical path;
  - audit directory and pre-test audit file/entry count.

- [ ] **Step 3: Prove side-effect-free status and private probe behavior.**

  On real TIA V21:

  1. with no project open, call direct status and record `isOpen:false`;
  2. open disposable A explicitly;
  3. call status with no path and with canonical A; both report A without binding mutation;
  4. call direct status requesting different B; it fails `binding_conflict`, tells the caller to use `open_project`, and A remains open;
  5. run one lifecycle preview/apply path that needs current state and record that the private probe, not public status switching, supplied it.

  Capture evidence for AC-006–AC-010, AC-012, and AC-045.

- [ ] **Step 4: Prove SaveAs rejection and supported rebinding.**

  1. call `save_project_as` with `rebind:false`; verify `validation_error`, no token, no audit addition, no Siemens write, and A remains open;
  2. call tokenless `save_project_as` with `rebind:true` to obtain preview/token;
  3. apply once with `confirm:true` and the exact token/input;
  4. verify exactly one Siemens SaveAs, copied project B exists, worker open path is B, host binding is B, and the token cannot be reused;
  5. execute a subsequent status/read call in the same session.

  Capture AC-015–AC-017, AC-022, AC-035, AC-042, AC-043.

- [ ] **Step 5: Prove block-update round trips.**

  Using the disposable B project:

  1. export the selected block as documents and retain the exact original bundle;
  2. apply byte-identical `update_block_logic`; verify one import, compile success, and successful non-empty re-export;
  3. make one controlled logic edit, apply it once, compile, re-export, and prove the intended change is present;
  4. submit one malformed/unsafe bundle; verify `validation_error` before import and prove the previously exported block is unchanged;
  5. force or safely reproduce an import/postcondition failure if the disposable fixture supports it; verify failure category/warning and a subsequent same-session read.

  Capture AC-023–AC-029, AC-033–AC-035, AC-042–AC-043.

- [ ] **Step 6: Prove SCL block creation.**

  1. preview and apply `create_block` for a unique SCL FC or FB path in disposable B;
  2. record generated XML evidence showing a non-empty compile unit;
  3. verify the block resolves in TIA Portal and compiles without error;
  4. re-export or read it to prove it exists at the requested path;
  5. verify one write request and normal same-session follow-up read.

  Capture AC-030, AC-031, AC-035, AC-042, AC-043.

- [ ] **Step 7: Write the live report without claiming blocked criteria.**

  Use the established report structure:

  - `# Acceptance Test Report`
  - commit/dependency/runtime checks;
  - 45-row `ID | Description | Test Type | Result | Evidence` table;
  - focused commands and observed output;
  - summary counts;
  - failed/blocked criteria detail;
  - overall verdict.

  If any live criterion fails, set the verdict to failed/blocked, fix through TDD, and rerun the entire acceptance suite after the fix.

---

## Task 3: Update the source TIA Portal MCP skill under explicit authorization

**Files:**

- Modify only the owning source-repository `skills/tia-portal-mcp/SKILL.md` after its exact checkout is supplied and authorized.
- Never modify: `C:\Users\LCZ\.codex\plugins\cache\totally-integrated-claude\...`

**Acceptance:** AC-039.

- [ ] **Step 1: Stop and obtain the external boundary prerequisites.**

  Ask the user for the owning `totally-integrated-claude` source checkout and explicit permission to edit it. If it is unavailable or not authorized, record AC-039 as blocked and do not certify Phase 5 complete.

- [ ] **Step 2: Add a failing source-skill validation test or validator assertion in that repository.**

  The test must reject stale preview-per-tool names such as `preview_update_block_logic` and require the current ten-tool/self-previewing lifecycle description.

- [ ] **Step 3: Update source documentation only.**

  Document:

  - current ten-tool surface and seven lifecycle methods;
  - self-preview/token/apply flow;
  - read-only `get_project_status` and deliberate `open_project` switching;
  - `save_project_as(rebind:true)` requirement;
  - direct categorized failures and separate warnings;
  - block update/create behavior after live verification.

- [ ] **Step 4: Validate the source repository and record its commit in the Phase 5 report.**

  Do not install, publish, or overwrite cache state as part of this task unless the user separately authorizes that external action.

---

## Task 4: Finalize repository docs after live evidence

**Files:**

- Modify: `README.md`
- Modify: `docs/IMPROVEMENT_PLAN.md`
- Modify: `docs/superpowers/acceptance/reports/2026-07-23-phase5-reliability-lifecycle-integrity.md`

**Acceptance:** AC-036–AC-039, AC-044.

- [ ] **Step 1: Remove only live-disproved known issues.**

  After AC-026, AC-027, AC-031, and AC-016 pass, replace the obsolete `update_block_logic`, SCL `create_block`, and SaveAs/status caveats with the verified behavior and recovery guidance. If a criterion remains blocked, keep its known issue visible.

- [ ] **Step 2: Close Phase 5 roadmap items and preserve Phase 6 exclusions.**

  Mark only evidenced items complete. Keep Transactions/ExclusiveAccess, Openness events, authorization expansion, doctor enhancements, network reflection validation, create-tag attribute forwarding, and `deviceItemName` semantics open in their later phase.

- [ ] **Step 3: Run documentation/schema tests and commit.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter "FullyQualifiedName~McpToolSchemaTests|FullyQualifiedName~ProjectLifecycleToolTests|FullyQualifiedName~CiWorkflowTests"
  git add README.md docs/IMPROVEMENT_PLAN.md docs/superpowers/acceptance/reports/2026-07-23-phase5-reliability-lifecycle-integrity.md
  git commit -m "docs: align verified phase 5 behavior"
  ```

---

## Task 5: Refresh graphify and run consolidated review

**Files:**

- Update generated graph artifacts under: `graphify-out/`
- Modify the acceptance report with graph/review evidence.

**Acceptance:** AC-040, AC-041, AC-042, AC-043.

- [ ] **Step 1: Refresh the graph after all code changes.**

  ```powershell
  graphify update .
  git add graphify-out
  git commit -m "docs: refresh phase 5 architecture graph"
  $graphCommit = git rev-parse HEAD
  ```

- [ ] **Step 2: Run one whole-branch code and security review.**

  Review the full Phase 5 diff for:

  - secrets/credentials/protected path leakage;
  - path traversal and unsafe filename handling;
  - category completeness and user-safe errors;
  - warning caps;
  - binding mutation only after verified success;
  - preview/token/state/audit ordering;
  - no same-call write retries;
  - uncertain outcomes never rendered/audited as success;
  - source/cache boundary compliance;
  - files/functions within project size/complexity limits.

  Do not implement a fix inside certification. Route every CRITICAL/HIGH finding back to its owning Plan 1–3 task, repeat that task's RED/GREEN cycle, focused review, full exit gate, and commit, then restart Plan 4 acceptance. Refresh graphify again after any code change and update `$graphCommit`.

- [ ] **Step 3: Prove graph freshness.**

  After subsequent report-only commits, run:

  ```powershell
  git diff --quiet $graphCommit HEAD -- '*.cs'
  if ($LASTEXITCODE -ne 0) { throw 'C# changed after the recorded graph commit.' }
  ```

  Record the graph commit and command result in the report.

---

## Task 6: Rerun full acceptance and issue the final verdict

**Files:**

- Finalize: `docs/superpowers/acceptance/reports/2026-07-23-phase5-reliability-lifecycle-integrity.md`

**Acceptance:** AC-001–AC-045.

- [ ] **Step 1: Rerun all automated gates from a clean worktree.**

  Commit any pending review/report evidence first. `git status --short` must be empty before this final run; do not hide or discard unrelated changes.

  ```powershell
  git status --short
  dotnet restore TiaMcpServer.sln
  dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
  dotnet test TiaMcpServer.sln --no-restore --no-build --verbosity minimal
  $phase5Results = Join-Path 'TestResults' ('phase5-acceptance-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --collect:"XPlat Code Coverage" --settings TiaMcpServer.Tests/coverage.runsettings --results-directory $phase5Results
  $reports = @(Get-ChildItem -LiteralPath $phase5Results -Recurse -Filter coverage.cobertura.xml)
  if ($reports.Count -ne 1) { throw "Expected exactly one Cobertura report; found $($reports.Count)." }
  ./scripts/verify-coverage-threshold.ps1 -CoveragePath $reports[0].FullName -MinimumLineRate 0.80
  ```

- [ ] **Step 2: Rerun the complete live/API acceptance set after the final code build.**

  Repeat every live/API scenario from Task 2 Steps 3–6: no-project/direct/conflicting status, lifecycle probe evidence, rejected and supported SaveAs, no-op/edited/malformed block updates, recoverable failure follow-up reads, and SCL create/resolve/compile. Earlier live evidence from a pre-fix build is not final evidence.

- [ ] **Step 3: Finalize the 45-row report.**

  Every criterion must be `PASS` with a concrete command/test/live evidence reference. Any missing authorization, missing live environment, failed test, graph drift, or unresolved review finding yields `BLOCKED`/`FAIL`, not a completion claim.

- [ ] **Step 4: Commit the final acceptance evidence.**

  ```powershell
  git add docs/superpowers/acceptance/reports/2026-07-23-phase5-reliability-lifecycle-integrity.md
  git commit -m "docs: record phase 5 acceptance evidence"
  ```

## Phase 5 Exit Gate

- [ ] AC-001 through AC-045 all pass with fresh evidence.
- [ ] Scoped line rate is at least 0.80.
- [ ] Full Round 4 and Phase 5 regressions pass.
- [ ] Live V21 tests identify exact runtime versions and disposable fixture provenance.
- [ ] Source skill is updated in its authorized source checkout; installed cache remains untouched.
- [ ] Graph commit matches final C# state.
- [ ] Whole-branch review has no unresolved CRITICAL/HIGH findings.
- [ ] Final audit/security checks show no secret leakage or uncertain-outcome false success.
- [ ] Worktree is clean and no push/PR/merge has occurred without separate user authorization.
