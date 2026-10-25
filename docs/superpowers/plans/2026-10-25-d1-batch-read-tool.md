# D-1 Shared Batch Read Tool Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace four repeated read-tool flows and read response/error types with one host implementation without changing public behavior.

**Architecture:** Add a static `BatchReadTool` using catalog delegates and a per-item projection callback. Reuse the existing read execution engine, budget algorithm and canonical result gate; retain thin PLC, Network, HMI and VCI entry points.

**Tech Stack:** C# / .NET 10 host, existing MCP SDK and canonical JSON, xUnit and FakeWorker; no new dependencies or Siemens worker changes.

**Spec:** [Approved D-1 design](../specs/2026-10-25-d1-batch-read-tool-design.md), implementing [D-1 in the authoritative review](2026-10-17-codebase-simplification-review.md#workstream-d-host-structural-consolidation-no-wire-change).

**Status:** Planned against `main` at `cfdda59`, 2026-10-25. Scope approved; implementation and execution method remain pending. No product tests, build or live TIA calls were run during planning.

## Global Constraints

- Deliver one host-only refactor PR; keep D-4 and D-5 separate. Preserve all existing tool parameters, descriptions, access checks, binding rules, worker calls and public JSON/schema shapes.
- Preserve HMI contract version `1.1`; PLC, Network and VCI remain `1.0`. Use existing `AccessPolicy.Mode`; add no effective-mode accessor.
- Preserve validation -> access -> binding -> execution -> budget -> canonical delivery, exact error/guidance strings, explicit nulls, status/count truth and request immutability.
- Rejected calls retain `isError:true`; executed batches retain `isError:false`, including failed/omitted items. Keep sequential independent reads and existing cancellation behavior; never replay requests.
- Preserve HMI's post-worker binding capture and Network's registered/cached pagination executor. No worker/native, write/lifecycle, dependency, policy or CLI changes.
- Use effective `MaxItemChars`/`MaxDocumentChars` through the existing budget; defaults remain `60,000`/`180,000`. Measure the exact response envelope; retain whole-value omissions and per-domain retry guidance.
- Keep `Tools/Snapshots/output-schemas.json` unchanged. Reuse the existing default/`2025-11-25` schema comparison, not a second schema snapshot framework.
- Commit each completed implementation/fix step. Builds and qualification runs are serialized. No push, PR creation or live TIA activity is authorized by this plan.

## Review Focus

1. Invalid/null items on an unbound client must preserve validation precedence, while valid unbound reads fail binding before execution (Tasks 1-2).
2. Worker failure followed by protocol failure and success must retain all items in caller order, accurate final counts and MCP success semantics (Tasks 1-4).
3. Replaced CLR types must preserve actual advertised schemas, domain versions and canonical text/structured equality, including root rejection nulls (Tasks 1-4).
4. Escaped/non-ASCII IDs, warning pressure and equal-size results must preserve exact envelope sizing, omission order, effective limits and domain guidance (Tasks 2-4).
5. Pre-cancellation and cancellation between items must stop at existing boundaries; HMI and Network cursor continuations must retain existing binding/security behavior (Tasks 2-4).

## Files and interfaces

| Responsibility | Files |
| --- | --- |
| Shared runner and envelope | Create `TiaMcpServer/Tools/BatchReadTool.cs` and `TiaMcpServer/OperationBatches/OperationBatchReadResponse.cs` |
| Four entry-point adapters | Modify `TiaMcpServer/{Plc,Network,Hmi,Vci}/*ReadTools.cs` for the four named tools only |
| Superseded read models | Modify `Plc/PlcToolResponses.cs` and `Network/NetworkToolResponses.cs`; delete `Hmi/HmiToolResponses.cs` and `Vci/VciToolResponses.cs` once empty |
| Characterization/helper tests | Create `TiaMcpServer.Tests/Tools/BatchReadCompatibilityTests.cs`, `Tools/BatchReadToolTests.cs` and `Tools/Snapshots/batch-read-responses.json` |
| Existing consumers | Update old response constructors in `OperationBatches/StructuredOperationBatchPayloadBudgetTests.cs` and `Hmi/HmiReadToolsTests.cs`; preserve existing domain, cancellation, cursor and schema suites |
| Test compilation/resources | Modify `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`: explicitly link both new host files, copy the new response fixture, remove the deleted HMI response link; retain PLC/Network links and VCI glob |
| Final documentation | Update `docs/ARCHITECTURE.md` sections 7/7a, this plan and D-1's entry in the authoritative review after evidence exists; keep both new documents listed in `docs/README.md` |

Place both public records in `TiaMcpServer.OperationBatches`:

```csharp
public sealed record OperationBatchReadError(string Category, string Message);
public sealed record OperationBatchReadResponse(
    string Tool, string ContractVersion, bool Success,
    OperationBatchReadError? Error, IReadOnlyList<string> Warnings,
    StructuredOperationBatch? Batch);
```

`BatchReadTool` is internal in `TiaMcpServer.Tools`. Its implementation interfaces are:

```csharp
internal static Task<CallToolResult> RunAsync<TItem>(
    OpennessWorkerClient workerClient, IReadOnlyList<TItem>? operations,
    string toolName, string contractVersion,
    Func<IReadOnlyList<TItem>?, OperationValidation> validateRead,
    Func<IReadOnlyList<TItem>, McpAccessMode, IReadOnlyList<string>> validateAccessMode,
    Func<TItem, Task<StructuredOperationItem>> execute,
    Func<StructuredOperationItem, string> retryGuidance,
    CancellationToken cancellationToken = default)
    where TItem : IOperationBatchItem;

internal static StructuredOperationBatch ApplyBudget(
    StructuredOperationBatch batch, string toolName, string contractVersion,
    Func<StructuredOperationItem, string> retryGuidance,
    int? maxItemChars = null, int? maxDocumentChars = null);

internal static CallToolResult Error(
    string toolName, string contractVersion, string category, string message);
```

Use one private shared response composer for both budget measurement and delivery. Retain each domain's existing `Error(category, message)` and `ApplyBudget(batch, maxItemChars, maxDocumentChars)` signatures as small forwarding adapters for registration/tests. Do not add an executor factory, domain interface or alternate serialization path.

## Task 1: Pin existing compatibility

**Files:** New compatibility tests and response fixture; test-project fixture copying; existing schema/conformance/domain tests as references.

**Interfaces:** Consumes current domain entry points and `McpProtocolTestHarness`. Produces deterministic pre-D-1 canonical response expectations; the existing output-schema snapshot remains the schema authority.

- [ ] Add `CanonicalResponsesMatchPreD1` cases for all four tools: valid unbound rejection, empty/invalid batch rejection, and deterministic non-cursor success/mixed-failure calls using existing domain FakeWorker scenarios. Generate the fixture from unchanged production code, inspect it, then freeze it. Do not normalize away contract fields; use existing cursor suites for nondeterministic opaque tokens.
- [ ] Assert exact canonical response equality and MCP error flags, including these invariants on the corresponding rejection/executed cases:

  ```csharp
  Assert.Equal(expectedCanonicalText, actualCanonicalText);
  Assert.Equal(expectedIsError, result.IsError == true);
  Assert.Equal(actualCanonicalText, CanonicalJson.Serialize(result.StructuredContent!.Value));
  Assert.Equal(expectedVersion, result.StructuredContent.Value.GetProperty("contractVersion").GetString());
  ```

- [ ] Run the focused gate below against unchanged production code. Characterization and existing schema snapshots must pass; no artificial RED is required for pre-existing behavior. Extend assertions only where the Review Focus matrix lacks coverage.
- [ ] Inspect the fixture/source diff and commit: `test: pin batch read tool compatibility`.

## Task 2: Add shared runner and migrate PLC

**Files:** Shared runner/records, `PlcReadTools.cs`, `PlcToolResponses.cs`, test-project links, new helper tests, existing PLC read/budget tests.

**Interfaces:** Produces the three helper signatures and two records above; consumes existing catalogs, `RequireVerifiedBindingAsync`, the per-item execution-engine overload and the existing payload budget.

- [ ] Add `BatchReadToolTests` referencing the new interface before implementation. Cover validation denial bypassing access/execution, access denial bypassing binding/execution, valid unbound rejection, ordered failed/protocol-failed/succeeded items, and cancellation before/between items. Use existing FakeWorker binding support for executed cases; use local delegates/counters for gate assertions. Observe the missing-helper compile failure, then implement and require behavioral assertions to pass.
- [ ] Add budget assertions through the shared `ApplyBudget`: explicit small limits differing from defaults; escaped/non-ASCII IDs; stable equal-size omission ties; warning pressure. Assert the final canonical document fits, `presentedChars` matches its length, failure categories survive and inputs are unchanged. Reuse existing batch test fixtures and algorithms.
- [ ] Implement the shared flow without catching new exceptions or moving cancellation ahead of existing admission gates. Validate nullable operations with the domain catalog before dereferencing; join catalog access errors with the existing newline separator. Call `ExecuteReadsAsync(operations, execute, cancellationToken)` and retain exact failure/omission semantics.
- [ ] Migrate PLC: its callback awaits `PlcWorkerInvoker.InvokeReadAsync` then calls `PlcPayloadContract.Project`. Switch its `OutputSchemaType`, keep forwarding `Error`/`ApplyBudget` adapters and existing retry strings, and remove only `PlcReadResponse`/`PlcToolError`. Link new host files in the test project.
- [ ] Run helper/PLC/OperationBatches/schema/conformance gates. Require the frozen PLC response expectations and unchanged full schema snapshot to pass. Inspect the diff and commit: `refactor: share batch read orchestration for PLC`.

## Task 3: Migrate VCI, then HMI

**Files:** `VciReadTools.cs`, `VciToolResponses.cs`, `HmiReadTools.cs`, `HmiToolResponses.cs`, test-project HMI link, `HmiReadToolsTests.cs`, relevant domain tests.

**Interfaces:** Consumes Task 2's helper. VCI supplies `ReadVciAsync` plus `VciPayloadContract.Project`; HMI supplies `HmiWorkerInvoker.InvokeReadAsync` plus `HmiPayloadContract.Project`.

- [ ] Migrate VCI with unchanged method mapping, query creation and retry guidance. Preserve its current cancellation behavior: the token reaches the engine, not a newly cancellable native/worker call. Change `OutputSchemaType` and forwarding adapters; delete the now-empty VCI response file (the test glob needs no individual removal).
- [ ] Run VCI/helper/schema/conformance/cancellation cases, including the frozen VCI responses, and commit: `refactor: use shared batch reads for VCI`.
- [ ] Migrate HMI with the existing cursor codec. Its async callback must first await the worker invocation and then evaluate `workerClient.BindingSnapshot` in the projector call; never capture it before the await. Keep contract `1.1`, catalog diagnostics and paged/unpaged retry strings. Replace the HMI response constructor in its existing test and remove the empty response file and its explicit test link.
- [ ] Run HMI/helper/schema/conformance/cancellation and HMI cursor cases. Retain assertions that issued cursors continue under the same binding and reject changed binding/query; require the frozen HMI responses and unchanged schema snapshot. Commit: `refactor: use shared batch reads for HMI`.

## Task 4: Migrate Network and qualify the whole change

**Files:** `NetworkReadTools.cs`, `NetworkToolResponses.cs`, old Network response constructors in payload-budget tests, Network pagination/protocol tests and final documentation.

**Interfaces:** Consumes Task 2's helper and the existing `NetworkReadOperationExecutor.ExecuteAsync`; retains `RegisterExecutor`, compatibility codecs and `ConditionalWeakTable` until D-5.

- [ ] Supply a deferred callback that resolves `CompatibilityExecutors.GetValue(workerClient, CreateCompatibilityExecutor)` and invokes `ExecuteAsync` for the item. Keep lookup inside the callback so rejected calls never initialize the executor; the existing table reuses it across items. Do not introduce DI/factory changes to preserve allocation timing.
- [ ] Change `OutputSchemaType` and forwarding adapters; remove only the Network read envelope/error declarations. Update old test constructors to the shared record. Preserve all hardware-page detection and per-operation retry guidance.
- [ ] Run the complete focused gate: mixed worker/protocol failures continue, both pagination paths retain cursors, budget omissions retain page-safe guidance, and the existing executor registration path remains used. Require frozen Network responses and the unchanged schema snapshot for both protocol versions.
- [ ] Inspect all four wrappers and search for remaining old response/error type references. Confirm there is one orchestration implementation and one read envelope/error pair, with no changes to excluded behavior. Run final qualification below; obtain an independent full-diff review covering all five Review Focus rows and fix any actionable finding with its own verification/commit.
- [ ] Update architecture sections 7/7a to describe the shared host runner/envelope. Record actual commands, results, coverage and limits here and in the authoritative D-1 entry. Do not mark implementation complete from planning or static inspection. Commit: `refactor: use shared batch reads for Network` (commit review fixes separately when needed).

## Verification and handoff

Run from the repository root. Build serially before any `--no-build` tests and rebuild after source/fixture changes. Use one configuration throughout:

```powershell
dotnet build TiaMcpServer.slnx -m:1 -c Release /p:UseTiaPortalReferenceStubs=true
$d1Filter = @(
  'FullyQualifiedName~BatchRead', 'FullyQualifiedName~PlcRead',
  'FullyQualifiedName~HmiRead', 'FullyQualifiedName~VciRead',
  'FullyQualifiedName~TiaMcpServer.Tests.OperationBatches',
  'FullyQualifiedName~NetworkToolsTests', 'FullyQualifiedName~NetworkStructuredProtocolTests',
  'FullyQualifiedName~HardwarePaginationFakeWorkerTests',
  'FullyQualifiedName~NetworkObjectPaginationCoordinatorTests',
  'FullyQualifiedName~Cursor', 'FullyQualifiedName~McpToolSchemaTests',
  'FullyQualifiedName~ToolOutputContractConformanceTests'
) -join '|'
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Release --no-build --no-restore --filter $d1Filter
```

For intermediate commits, select the task's classes from this gate. Final qualification runs the full non-packaging suite once with existing coverage settings:

```powershell
$d1Results = Join-Path 'TestResults' ('d1-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Release --no-build --no-restore --collect:"XPlat Code Coverage" --settings TiaMcpServer.Tests/coverage.runsettings --results-directory $d1Results --filter "Category!=Packaging"
$d1Reports = @(Get-ChildItem -LiteralPath $d1Results -Recurse -Filter coverage.cobertura.xml)
if ($d1Reports.Count -ne 1) { throw 'Expected exactly one Cobertura report.' }
./scripts/verify-coverage-threshold.ps1 -CoveragePath $d1Reports[0].FullName -MinimumLineRate 0.80
git diff --check
```

Require successful command exits, no failed/skipped required cases, an unchanged schema baseline and no unintended canonical fixture differences. Verify nonzero coverage and inspect materially changed helper branches; the aggregate `80%` gate alone does not prove they ran. If Windows sandbox temp permissions interfere, use process-local `TEMP`/`TMP` under ignored `TestResults/d1/temp`, restore the original values afterward, and reject empty/zero-hit reports as evidence.

Only test-project link/resource changes are planned, so packaging is not required. Run `Category=Packaging` if execution actually changes a product `.csproj` or another trigger listed in `docs/development/building.md`/`.github/workflows/packaging.yml`.

No live TIA test is required while the approved host-only scope holds. Choose the execution method before implementation; native execution with one final independent review is the suggested fit because the four migrations depend on one small shared interface. No implementation is authorized merely by saving this plan.
