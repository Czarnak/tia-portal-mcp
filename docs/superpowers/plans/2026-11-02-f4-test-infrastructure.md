# F4 Test Infrastructure Implementation Plan

> **For agentic workers:** Use the executing-plans workflow and independent whole-branch review. Track completed steps here; the user has authorized implementation after preparing this temporary plan.

**Goal:** Consolidate the duplicated F4 test helpers without changing product code or weakening test assertions.

**Architecture:** Reuse `TestSupport` and `StructuredContractInspector`. Add one repository-file locator, one canonical tool-document reader, and one FakeWorker client factory; migrate callers while retaining scenario-specific setup.

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

- [ ] Add focused nested-root, missing-root, normalized-source and occurrence-count checks; use existing tests to characterize mechanical migrations.
- [ ] Implement `RepositoryFiles.Root`, `FindRoot(string startDirectory)`, `Path(params string[] parts)`, `ReadSource(params string[] parts)` and `CountOccurrences(string source, string value)` using existing semantics.
- [ ] Remove duplicate locator/count implementations and migrate callers; preserve assertions.
- [ ] Move utility files into `TestSupport`, update namespace/import/path references, retain existing FakeWorker configuration selection.
- [ ] Run focused source-contract/fixture checks and commit `refactor: consolidate repository test support`.

### Task 2: Canonical document reading and FakeWorker construction

**Files:** new `TestSupport/CanonicalToolDocument.cs`, `TestSupport/FakeWorkerClients.cs`, focused helper tests and existing document/client helper callers, including moved `LifecycleTestCalls.cs`.

- [ ] Characterize valid and invalid canonical results, including structured/text mismatch, missing content and retained JSON element lifetime.
- [ ] Implement `CanonicalToolDocument.Read(CallToolResult)` using existing canonical checks; migrate duplicate readers and remove VCI-to-Network test coupling.
- [ ] Implement one `FakeWorkerClients.Create` factory with existing access-mode, binding and timeout inputs; retain explicit per-scenario status/bind operations.
- [ ] Migrate duplicate construction without changing test setup or ownership; keep genuinely distinct process/transport tests explicit.
- [ ] Run focused helper, protocol and FakeWorker suites and commit `refactor: share canonical test documents and worker clients`.

### Task 3: Qualification and independent review

- [ ] Serial Release reference-stub solution build (`-m:1`), full non-Packaging suite with repository coverage settings and process-local temp under ignored `TestResults/f4/`.
- [ ] Enforce the repository 80% scoped line-coverage threshold. Run Packaging tests to check moved harness/generated-fixture integration.
- [ ] Independent whole-branch review, resolve actionable findings, and run the smallest relevant rechecks.
- [ ] Run `python -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30` and `git diff --check`.

### Task 4: Final cleanup and delivery

- [ ] As the last implementation task, remove this plan and `docs/superpowers/plans/2026-11-02-f1-retire-legacy-null-omission.md`; commit `docs: prune working files`.
- [ ] Re-run documentation/whitespace checks, verify final scope and clean branch, push and open a PR against `main` as authorized.
