# F4 Test Infrastructure Implementation Plan

> **For agentic workers:** Use the executing-plans workflow and independent whole-branch review. Track completed steps here; the user has authorized implementation after preparing this temporary plan.

**Goal:** Consolidate the duplicated F4 test helpers without changing product code or weakening test assertions.

**Architecture:** Reuse `TestSupport` and the existing canonical rendering rules. Add one repository-file locator, one canonical tool-document reader, and one FakeWorker client factory; migrate callers while retaining scenario-specific setup. Keep domain-specific `StructuredContractInspector` checks at their existing callers.

**Tech Stack:** C#, .NET 10, xUnit, existing MCP test harness and FakeWorker.

**Spec:** `docs/adr/records/2026-10-17-codebase-simplification-review.md`, F-4; companion findings CONTRACTS-11 through CONTRACTS-13.

## Assessment and constraints

- Medium complexity: many mechanical caller migrations, with differing document checks, binding setup and client timeouts.
- `FakeWorkerLocator` already selects the running configuration (commit `ce2c50b5`); preserve that behavior rather than add a fallback to a stale build.
- No product handlers, Siemens calls, public contracts, dependencies or live TIA operations change. Offline qualification is sufficient.
- Preserve cancellation, request timeouts, access modes, binding ownership, disposable lifetimes, source assertions and newline handling.
- Consolidate `TestUtilities` into `TestSupport`, preserving Siemens namespaces and updating harness imports and generated-project fixture paths.
- Shared document validation checks one text block, structured content and exact canonical equality. JSON-bearing PLC content is legitimate domain data; preserve existing test intent rather than silently exempt arbitrary documents from validation.

## Review focus

- Repository lookup works from nested output directories and fails clearly outside a checkout.
- Canonical reader detects missing, extra, noncanonical or mismatched delivery content and returns an element with a safe lifetime.
- Scenario setup and explicit binding in each FakeWorker test remain equivalent; custom timeout/cancellation cases retain their controls.
- Moved doubles and serialization harness still participate in compilation and generated fixture builds.
- No previous Debug artifact is selected during Release verification.

### Task 1: Repository paths and support files

**Files:** `TiaMcpServer.Tests/TestSupport/RepositoryFiles.cs`, its focused tests, existing repository/source locator callers; `TestUtilities/*`, test project import and generated-project consumers.

- [x] Add focused nested-root, missing-root, normalized-source and occurrence-count checks; use existing tests to characterize mechanical migrations.
- [x] Implement `RepositoryFiles.Root`, `FindRoot(string startDirectory)`, `Path(params string[] parts)`, `ReadSource(params string[] parts)` and `CountOccurrences(string source, string value)` using existing semantics.
- [x] Remove duplicate locator/count implementations and migrate callers; preserve assertions.
- [x] Move utility files into `TestSupport`, update namespace/import/path references, retain existing FakeWorker configuration selection.
- [x] Run focused source-contract/fixture checks; include in the shared implementation commit below.

### Task 2: Canonical document reading and FakeWorker construction

**Files:** new `TestSupport/CanonicalToolDocument.cs`, `TestSupport/FakeWorkerClients.cs`, focused helper tests and existing document/client helper callers, including moved `LifecycleTestCalls.cs`.

- [x] Characterize valid and invalid canonical results, including structured/text mismatch, missing content and retained JSON element lifetime.
- [x] Implement `CanonicalToolDocument.Read(CallToolResult)` using existing canonical checks; migrate duplicate readers and remove VCI-to-Network test coupling.
- [x] Implement one `FakeWorkerClients.Create` factory with existing access-mode, binding and timeout inputs; retain explicit per-scenario status/bind operations.
- [x] Migrate duplicate construction without changing test setup or ownership; keep genuinely distinct process/transport tests explicit.
- [x] Run focused helper, protocol and FakeWorker suites; commit both migrations as `refactor: consolidate shared test infrastructure`.

### Task 3: Qualification and independent review

- [x] Serial Release reference-stub solution build (`-m:1`), full non-Packaging suite with repository coverage settings and process-local temp under ignored `TestResults/f4/`.
- [x] Enforce the repository 80% scoped line-coverage threshold. Run Packaging tests to check moved harness/generated-fixture integration.
- [x] Independent whole-branch review, resolve actionable findings, and run the smallest relevant rechecks.
- [x] Run `python -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30` and `git diff --check`.

### Task 4: Final cleanup and delivery

- [ ] As the last implementation task, remove this plan and `docs/superpowers/plans/2026-11-02-f1-retire-legacy-null-omission.md`; commit `docs: prune working files`.
- [ ] Re-run documentation/whitespace checks, verify final scope and clean branch, push and open a PR against `main` as authorized.

## Progress and implementation decisions

- Branch: `refactor/f4-test-infrastructure`, based on freshly pulled `77fea02c`.
- Baseline Release reference-stub solution build passed with nine existing warnings and zero errors.
- Helper RED: 27 tests compiled and failed with the expected `NotImplementedException` stubs; evidence is ignored under `TestResults/f4/red/`.
- The common reader checks exact canonical text/structured equality and clones the returned element. It does not apply the inspector's blanket JSON-string rejection to valid PLC/user content. Existing caller-specific inspector assertions remain.
- The client factory retains the constructor's ReadWrite default; callers with other modes pass them explicitly. A bound-client helper shares existing scenario binding and disposes the client if binding fails.
- Path, document and client migrations are complete. The nine Siemens doubles are unchanged after newline normalization; no active `TestUtilities` references or duplicated private root/client helpers remain.
- Focused qualification passed 66 tests. Independent review found two P2 issues: lost raw structured/text equality and a missing-root fixture below the process-local temp directory. Both were reproduced with failing tests, then fixed.
- The first full coverage run passed 7,614 tests and failed only the environment-isolation source guard. The guard now distinguishes the factory's environment-changing `CreateBoundAsync` path using its existing conditional-helper pattern; final qualification passes.
- Implementation commits are combined because path and client migrations share consumer files; this preserves a buildable cohesive commit rather than split caller/helper dependencies.
- Final offline evidence: 7,615 non-Packaging tests and 39 Packaging tests passed, zero failures/skips. Scoped line coverage is 95.15% (12,189/12,810), above the 80% gate. Duplicate coverage copies have the same SHA-256; the attached report was used for threshold verification. The final solution build emitted eight existing warnings and zero errors. Documentation verification has zero errors and only the two expected plan warnings, removed by Task 4. No live TIA testing or deployment occurred.
