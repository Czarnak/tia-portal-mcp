# Issue #223: implementation plan

Status: implemented and qualified offline; [scoped live qualification](../acceptance/reports/2026-11-27-issue-223-readback-diff-live-verification.md) completed on 2026-11-27

Spec: [approved design](../specs/2026-11-27-issue-223-readback-diff-design.md)

Issue: <https://github.com/Czarnak/tia-portal-mcp/issues/223>

Branch: `feat/issue-223-readback-diff`; baseline: `e19e46599681`.

Release baseline: [v4.4.0](https://github.com/Czarnak/tia-portal-mcp/releases/tag/v4.4.0) published PLC write contract `2.0`; earlier, still-unreleased PRs already changed it to `2.1`. This issue reuses `2.1` and leaves PLC read contract `1.0` unchanged.

## Goal

Report bounded, observational evidence of submitted versus freshly observed PLC content without changing write outcomes or concurrency hashes.

## Architecture

Add PLC-only comparison evidence after the existing content read-back, collect it in the policy by operation ID, and attach it in PLC response projection without extending shared verification or the worker protocol. Reuse the existing scope selector and bounded diff algorithm, with one excerpt allowance passed through planning, late replanning, and verification. Extend PLC response budgeting so comparison diagnostics yield before existing verification/hash evidence.

## Constraints

- Host and worker remain separate processes; this change adds no worker method, worker capability, or Siemens API call.
- Reuse existing unreleased PLC write contract `2.1`; issue #223 adds no further contract-version bump. Keep PLC read contract `1.0` and existing version assertions unchanged.
- Compare the resolved execution content, including resolved patches, with the existing nonempty read-back export under the existing binding lease.
- A mismatch alone never changes mutation status, verification success, `expected`, `actual`, or `contentHash`.
- Both equality flags describe the selected comparison scope; do not add a second whole-export equality flag.
- `normalizedLinesEqual` normalizes CRLF and lone CR to LF only; it makes no XML, whitespace, or semantic-equivalence claim.
- Use one call-owned `PlcContentDiffBudget`: 40 excerpt lines and 8,192 raw characters per side, at most 512 characters per line, and 320 excerpt lines and 32,768 raw excerpt characters across the call.
- Use effective item/document limits owned by `TiaMcpServer/OperationBatches/StructuredOperationBatchPayloadBudget.cs` for canonical encoded delivery size; defaults remain 60,000 and 180,000 characters.
- Keep shared `WriteOperationVerification`, `WriteVerification`, and `TrustedWriteVerifier` contracts unchanged.
- Text and `structuredContent` continue to derive from one canonical serialization; audit retains the exact delivered document and execution truth.
- The implementation is Windows/.NET 10 host code. Use existing Siemens-free FakeWorker tests; serialize solution builds with `-m:1`.

Scope and the complete acceptance criteria are in the linked approved design. This plan authorizes no live TIA operation, package installation, push, issue comment, or pull request creation. Local commits are part of each implementation task; preparing this plan does not execute those tasks.

## Repository evidence and starting checks

| Owning source | Established behavior the implementation must preserve |
| --- | --- |
| `TiaMcpServer/Plc/PlcVerificationPolicy.cs:56` | Existing block/type re-export, empty/failure handling, and full-export hash calculation. |
| `TiaMcpServer/Plc/PlcWriteDomain.cs:95` | Patches are resolved before verification; shared compilation precedes verification. |
| `TiaMcpServer/Safety/Pipeline/TrustedWriteVerifier.cs:19` | Successful superseded items skip read-back; failed content attempts can still be observed. |
| `TiaMcpServer/Plc/PlcContentDiffSides.cs:14` | Selector argument order is `(format, observedExport, submitted)`; XML-only comparison excludes companions. |
| `TiaMcpServer/Plc/PlcContentDiff.cs:22` | Bounded comparison, changed-span counts, newline normalization, and generation-budget accounting. |
| `TiaMcpServer/Plc/PlcWritePayloadBudget.cs:68` | Existing fallback can drop the complete verification operations array. New diagnostics must yield first. |
| `TiaMcpServer.Tests/Json/ConditionalMemberRegisterTests.cs:118` | Selected host types are explicitly appended; the new conditional property's owning type must be included. |
| `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj` | Host PLC source files are explicitly linked into tests; a new helper needs a source link. |

At implementation start run these commands sequentially, inspect changes, and preserve unrelated work:

```powershell
git status --short --branch
git log -1 --format='%h %s'
dotnet test TiaMcpServer.Tests --filter "Category!=Packaging&(FullyQualifiedName~Plc|FullyQualifiedName~McpToolSchemaTests|FullyQualifiedName~ConditionalMemberRegisterTests|FullyQualifiedName~ToolOutputContractConformanceTests)"
```

Record baseline failures separately from regressions. Refresh jCodemunch for any changed/stale file before relying on cached source. Source line numbers above describe the inspected baseline, not future edited files.

## Task 1: implement scoped comparison primitives

Files:

- Create `TiaMcpServer/Plc/PlcReadBackComparison.cs`: pure submitted/observed evidence builder.
- Modify `TiaMcpServer/Plc/PlcContentDiffSides.cs`: expose the selected scope while preserving the existing two-value `Select` interface and behavior.
- Modify `TiaMcpServer/Plc/PlcWriteModels.cs`: host-only evidence record, not yet attached to response entries.
- Create `TiaMcpServer.Tests/Plc/PlcReadBackComparisonTests.cs`: pure comparison regression cases.
- Modify `TiaMcpServer.Tests/Plc/PlcContentDiffSidesTests.cs`: assert scope selection without weakening existing behavior assertions.
- Modify `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`: link the new host helper with `Compile Include="..\TiaMcpServer\Plc\PlcReadBackComparison.cs" Link="Plc\PlcReadBackComparison.cs"` beside existing PLC links.

Interfaces:

```csharp
// PlcContentDiffSides: preserve Select(format, currentExport, submitted).
public static (string Current, string Requested, string ComparisonScope) SelectWithScope(
    string format, string currentExport, string submitted);

// PlcWriteModels.cs: all members required; Omission writes explicit null.
public sealed record PlcReadBackEvidence(
    string ComparisonScope,
    bool RawTextEqual, bool NormalizedLinesEqual, bool LineEndingOnly,
    PlcContentDiffSide Submitted, PlcContentDiffSide Observed,
    int SubmittedChangedSpanLineCount, int ObservedChangedSpanLineCount,
    bool BudgetExhausted, StructuredOperationOmission? Omission);

internal static class PlcReadBackComparison
{
    public static PlcReadBackEvidence Build(
        string format, string submitted, string observed, PlcContentDiffBudget budget);
}
```

Implement `SelectWithScope` by moving the existing selection branches into it, returning `xmlDocument` only for selected XML-document sides and `wholeText` for every unchanged/fallback whole-text branch. Have existing `Select` delegate and return only `(Current, Requested)`. In `Build`, call `SelectWithScope(format, observed, submitted)`, then `PlcContentDiff.Build(sides.Requested, sides.Current, budget)`. Map its `Current` side to `Submitted` and `Requested` side to `Observed`, including the corresponding changed-span counts; initialize `Omission` to null. Do not copy or modify the existing diff algorithm or sanitize differences.

- [x] Add the new source link, record, and builder signature, initially throwing `NotImplementedException` in `Build`, so the tests compile and have an intentional red state.
- [x] Write these literal tests in `PlcReadBackComparisonTests` with `using TiaMcpServer.Plc;` and `using Xunit;`:

```csharp
[Fact]
public void NormalizedConstantReportsSubmittedAndObservedText()
{
    const string submitted = "<ConstantType>LReal</ConstantType>";
    const string observed = "<ConstantType>Real</ConstantType>";
    var result = PlcReadBackComparison.Build("xml", submitted, observed, new());
    Assert.Equal("xmlDocument", result.ComparisonScope);
    Assert.False(result.RawTextEqual);
    Assert.False(result.NormalizedLinesEqual);
    Assert.False(result.LineEndingOnly);
    Assert.Equal(submitted, Assert.Single(result.Submitted.Excerpt.Lines).Text);
    Assert.Equal(observed, Assert.Single(result.Observed.Excerpt.Lines).Text);
    Assert.Null(result.Omission);
}

[Fact]
public void NewlineOnlyDifferenceKeepsTheExactTextDistinction()
{
    var result = PlcReadBackComparison.Build("source", "A\r\nB\r", "A\nB\n", new());
    Assert.Equal("wholeText", result.ComparisonScope);
    Assert.False(result.RawTextEqual);
    Assert.True(result.NormalizedLinesEqual);
    Assert.True(result.LineEndingOnly);
    Assert.Empty(result.Submitted.Excerpt.Lines);
    Assert.Empty(result.Observed.Excerpt.Lines);
}

[Fact]
public void GenerationExhaustionPreservesComparisonSummary()
{
    var result = PlcReadBackComparison.Build("source", "old", "new",
        new PlcContentDiffBudget { RemainingLines = 0, RemainingChars = 0 });
    Assert.False(result.RawTextEqual);
    Assert.True(result.BudgetExhausted);
    Assert.Equal(1, result.SubmittedChangedSpanLineCount);
    Assert.Equal(1, result.ObservedChangedSpanLineCount);
    Assert.Empty(result.Submitted.Excerpt.Lines);
    Assert.True(result.Submitted.Excerpt.BudgetExhausted);
    Assert.Null(result.Omission);
}
```

- [x] Add explicit cases named `IdenticalSelectedTextHasEmptyExcerpts`, `XmlOnlySubmissionIgnoresObservedCompanions`, `SingleXmlBundleUsesXmlDocumentScope`, `MultipleDocumentSubmissionUsesWholeTextScope`, `CompanionOnlyObservedExportUsesWholeTextFallback`, `MalformedBundleUsesWholeTextFallback`, and `DisjointDifferencesCountTheWholeChangedSpan`. Reuse `BlockBundleText.Compose` and the existing selector fixtures. For disjoint differences, compare `a\nkeep\nb` with `x\nkeep\ny` and assert both changed-span counts are 3, not 2. For equal XML plus companions, assert both equality flags true despite unequal full input strings. Add `GenerationExhaustionOnEqualTextOmitsNothing`: use an already-exhausted budget and identical text; assert equal flags, `BudgetExhausted:true`, empty excerpts, and zero omitted lines/characters. This preserves the existing helper's state semantics without implying that equal text lost evidence.
- [x] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~PlcReadBackComparisonTests"`; expect FAIL with `NotImplementedException` before the builder implementation.
- [x] Implement the selector/builder mapping exactly as above; remove the intentional throw.
- [x] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~PlcReadBackComparisonTests|FullyQualifiedName~PlcContentDiffSidesTests|FullyQualifiedName~PlcContentDiffTests"`; expect all selected tests to pass. Existing raw/per-side/long-line bounds remain covered by `PlcContentDiffTests`.
- [x] Review the diff for scope and source-link correctness, update the code index for edited files, and commit `feat: add scoped PLC read-back comparisons`.

## Task 2: attach evidence to the PLC verification contract

Files:

- Modify `TiaMcpServer/Plc/PlcVerificationPolicy.cs`: collect comparisons after successful nonempty exports, project them after shared verification, and accept the common allowance.
- Modify `TiaMcpServer/Plc/PlcWriteModels.cs`: conditional init-only property and PLC-specific evidence projection.
- Modify `TiaMcpServer/Plc/PlcWritePlanner.cs`: optional allowance parameters on initial planning and replanning.
- Modify `TiaMcpServer/Plc/PlcWriteDomain.cs`: own and pass the per-call allowance.
- Modify `TiaMcpServer.FakeWorker/Plc/PlcWriteRoundtripScenario.cs` and `TiaMcpServer.FakeWorker/Program.cs`: isolated import-normalization and compile-transformation scenarios.
- Modify `TiaMcpServer.Tests/Plc/PlcGuardedWriteDomainTests.cs`: successful/failed observations, patches, types, and absence cases.
- Modify `TiaMcpServer.Tests/Plc/PlcSharedCompilationPipelineTests.cs`: observation after a compile-time text transformation.
- Modify `TiaMcpServer.Tests/Plc/PlcGuardedWriteMcpTests.cs`: canonical text/structured/audit response equality; preserve emitted version assertions at `2.1`.
- Modify `TiaMcpServer.Tests/Tools/McpToolSchemaTests.cs`, `TiaMcpServer.Tests/Json/ConditionalMemberRegisterTests.cs`, and `TiaMcpServer.Tests/Tools/Snapshots/output-schemas.json`: conditional schema, register, and PLC schema snapshot.
- Modify `docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md`, `docs/SupportedOperations/IMPORT_EXPORT_OPTIONS_SUMMARY.md`, `docs/ARCHITECTURE.md`, and `AGENTS.md`: observational semantics only; preserve current contract numbers and historical reports.

Interfaces:

```csharp
// Add to the body of PlcOperationVerification; preserve its positional constructor.
[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
[Description("Present only when written PLC content was successfully re-exported and compared with the submitted execution content.")]
public PlcReadBackEvidence? ReadBack { get; init; }

// Keep existing callers compatible with the optional map.
public static PlcWriteVerification From(WriteVerification shared,
    IReadOnlyDictionary<string, PlcReadBackEvidence>? readBack = null);

// PlcWritePlanner: optional parameters; use supplied instance or one new local instance.
public Task<PlcWritePlanResult> PlanAsync(IReadOnlyList<PlcOperationRequest> items,
    PlcContentDiffBudget? budget = null);
public Task<PlcWriteReplanResult> ReplanAsync(PlcOperationRequest item,
    PlcContentDiffBudget? budget = null);

// PlcVerificationPolicy: add optional budget to its constructor and this static entry point.
public static Task<PlcWriteVerification> VerifyAsync(OpennessWorkerClient client,
    IReadOnlyList<PlcOperationRequest> items, IReadOnlyDictionary<string, PlcWriteEffect> effects,
    StructuredOperationBatch batch, PlcContentDiffBudget? budget = null);
```

`PlcWriteDomain` creates `_contentDiffBudget = new PlcContentDiffBudget()` once per domain instance and passes it to `_planner.PlanAsync`, `_planner.ReplanAsync`, and `PlcVerificationPolicy.VerifyAsync`. Retain its existing primary constructor; passing the budget as a method argument avoids referencing another instance field from a field initializer. The planner uses `budget ??= new PlcContentDiffBudget()` once at method entry and passes it through existing resolution. Remove the separate local/new replanning allowances.

The policy owns an ordinal `Dictionary<string, PlcReadBackEvidence>`. After a nonempty export, populate it with `Build(format, item.Content!, export.Payload, budget)` and retain the existing full-export hash calculation and generic verification result. Static verification creates the policy, awaits the unchanged shared verifier, then joins the dictionary through `PlcWriteVerification.From`. Join only to matching operation IDs; missing evidence leaves `ReadBack` null. Do not compare in the response composer or perform another export.

- [x] Add failing integration assertions for the test matrix below before changing policy/projection behavior. Add the DTO property and optional method signatures first if needed to compile assertions.

| Test name / existing anchor | Exact setup and required assertion |
| --- | --- |
| `ReadBackMismatchDoesNotFailSuccessfulImport` | Add dedicated FakeWorker scenario `plc-write-readback-normalizes`. Transform only `<ConstantType>LReal</ConstantType>` to `<ConstantType>Real</ConstantType>` when storing a block update. Submit XML containing the former; assert `RawTextEqual:false`, distinct correctly oriented excerpt text, and all three success values true. Assert `Expected` remains null and `Actual == ContentHash ==` the next `plc_read` hash. |
| `ReadBackEqualContentKeepsHashAndEmptyExcerpts` | Default round-trip scenario echoes a submitted source exactly. Assert both equality flags true, no excerpt lines, unchanged hash/check, and no omission. |
| `PatchExecutesExistingImportAndVerifiesResolvedFormat` | Extend existing XML/source theory: `ReadBack.RawTextEqual` is true for the resolved patched export, public operation remains `patch_block_logic`, public request `Content` remains null, and worker dispatch remains `update_block_logic` only. |
| `TypeReadBackUsesSubmittedExecutionContent` | Theory over XML/source using `Station_1/Types/UDT_Settings` and existing FakeWorker type inventory. Read its matching hash, submit a matching-name type declaration, assert populated equality evidence, correct scope and fresh hash. |
| `FailedContentAttemptReportsObservedComparison` | Extend an existing failed-content scenario that still returns a nonempty export. Assert its original failed status/category and root failure remain, while comparison and current hash describe the observed export. |
| Existing superseded-patch, export-lost, preview and non-content tests | Assert `ReadBack` is null and canonical `readBack` is absent. Preserve existing superseded/failure/check assertions. |
| `ReadBackObservesSharedCompileTransformation` | Add dedicated `plc-write-readback-compile-transform` scenario. Store submitted source unchanged; only `Compile` replaces `READBACK_BEFORE_COMPILE` with `READBACK_AFTER_COMPILE`. Submit at least two source block updates to activate shared compilation. Assert observed excerpts contain the latter marker and no new export/compile requests were added beyond existing verification. |
| `ReadBackSharesPlanningAndReplanningExcerptAllowance` | Use the same explicit budget for planner initial planning, one dependent replan, and policy verification. Seed small remaining limits so planning/replanning spend the last allowance; verify subsequent read-back excerpts are exhausted while flags, counts, hashes and item status remain available. Also verify a new call has a fresh budget. |
| Populated MCP response/audit case | Assert text equals canonical structured content and the audited delivered document/hash with `readBack` present. The audit still retains requested operations and exact resolved patch input independently. |

- [x] Register the two scenario names in FakeWorker dispatch. Gate transformations on those scenario names and literal markers so existing default scenario behavior remains identical. Keep these transformations Siemens-free.
- [x] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~ReadBack|FullyQualifiedName~PatchExecutesExistingImportAndVerifiesResolvedFormat"`; expect FAIL on missing/null `ReadBack` or absent expected observed marker before policy wiring.
- [x] Implement policy collection, PLC-only projection and shared allowance propagation as specified. Keep shared compilation before verification and preserve the shared verifier's supersession/failure logic.
- [x] Add `typeof(PlcOperationVerification)` to the host-type scan and register `PlcOperationVerification.ReadBack` with its appearance condition. Keep `PlcReadBackEvidence.Omission` explicit nullable, requiring no conditional-register entry.
- [x] Keep `PlcWriteContractVersion.Current` at `2.1`, PLC read version at `1.0`, and emitted-version assertions unchanged. `PlcContractVersion.cs` and `ToolOutputContractConformanceTests.cs` need no changes for versioning; retain the conformance suite in regression validation. Preserve historical text and literal budget fixtures.
- [x] Add schema assertions using the existing current/`2025-11-25` protocol theory: `readBack` is optional, its present members are typed/required according to the records, nullable omission remains represented, and no unrelated tool schema gains the field.
- [x] Refresh only the generated `plc_write` subtree using the temporary capture procedure below; the repository currently has a snapshot reader/comparer, not an updater. Remove the temporary capture method before committing and inspect that only the `plc_write` subtree changed. The permanent schema theory and `OutputSchemasMatchMain` must pass for both protocols afterward.

Temporary capture method inside the existing `McpToolSchemaTests` class:

```csharp
[Fact]
public async Task CaptureIssue223PlcWriteSchema()
{
    var target = Environment.GetEnvironmentVariable("TIA_ISSUE223_SCHEMA_PATH");
    Assert.False(string.IsNullOrWhiteSpace(target));
    await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full);
    var tools = await harness.Client.ListToolsAsync();
    var schema = tools.Single(tool => tool.Name == "plc_write").ProtocolTool.OutputSchema!.Value;
    await File.WriteAllTextAsync(target!, schema.GetRawText());
}
```

Run this capture and replace the subtree without reformatting other tool schemas:

```powershell
$env:TIA_ISSUE223_SCHEMA_PATH = Join-Path ([System.IO.Path]::GetTempPath()) 'issue-223-plc-write-schema.json'
dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~CaptureIssue223PlcWriteSchema"
@'
import json
import os
from pathlib import Path

path = Path('TiaMcpServer.Tests/Tools/Snapshots/output-schemas.json')
original = path.read_bytes().decode('utf-8')
line_ending = '\r\n' if '\r\n' in original else '\n'
marker = '  "plc_write": '
start = original.index(marker) + len(marker)
_, consumed = json.JSONDecoder().raw_decode(original[start:])
captured = json.loads(Path(os.environ['TIA_ISSUE223_SCHEMA_PATH']).read_text(encoding='utf-8'))
rendered = json.dumps(captured, ensure_ascii=False, indent=2).replace('\n', line_ending + '  ')
updated = original[:start] + rendered + original[start + consumed:]
assert json.loads(updated)['plc_write'] == captured
before = json.loads(original)
after = json.loads(updated)
assert all(before[key] == after[key] for key in before if key != 'plc_write')
path.write_bytes(updated.encode('utf-8'))
'@ | python -I -
Remove-Item Env:\TIA_ISSUE223_SCHEMA_PATH
```

- [x] Remove `CaptureIssue223PlcWriteSchema` from `McpToolSchemaTests.cs`, retain the permanent assertions, and run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~McpToolSchemaTests"`; expect both protocol snapshot comparisons to pass.
- [x] Document the comparison scope and newline rules under existing unreleased PLC write contract `2.1`, distinguish side `sha256` from the reusable full-export `contentHash`, and state that failed/after-compile evidence is observational. Update the compact current behavior rule in `AGENTS.md` without changing its contract number or copying acceptance claims.
- [x] Run `dotnet test TiaMcpServer.Tests --filter "Category!=Packaging&(FullyQualifiedName~Plc|FullyQualifiedName~McpToolSchemaTests|FullyQualifiedName~ConditionalMemberRegisterTests|FullyQualifiedName~ToolOutputContractConformanceTests)"`; expect all selected tests to pass.
- [x] Review source, schema and FakeWorker changes, update the code index, and commit `feat: report PLC submitted versus observed content`.

## Task 3: preserve evidence through response budgeting and qualify delivery

Files:

- Modify `TiaMcpServer/Plc/PlcWritePayloadBudget.cs`: comparison-aware item/document omission, nested omission accounting, and immutable presentation copies.
- Modify `TiaMcpServer.Tests/Plc/PlcWriteBudgetTests.cs`: encoded limits, aggregate overflow, omission order and execution-truth regressions.
- Modify `TiaMcpServer.Tests/Plc/PlcGuardedWriteMcpTests.cs`: delivered/audited omission case.
- Modify `docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md`, `docs/SupportedOperations/IMPORT_EXPORT_OPTIONS_SUMMARY.md`, and `docs/ARCHITECTURE.md`: omission order, delivery-success implications and common generation allowance.

Interfaces:

Keep `PlcWritePayloadBudget.Apply(response, maxItemChars = null, maxDocumentChars = null)` unchanged. Add private comparison omission helpers inside that file; they operate on the local `verification` copy and replace record/array values rather than mutating shared objects. `PlcReadBackEvidence.Omission` is `StructuredOperationOmission?`; comparison removal uses existing `PlcWriteVerification.Omission` while retaining operations and their hashes.

For each side whose retained excerpt is removed, replace its excerpt with:

```csharp
new PlcContentDiffExcerpt(
    Array.Empty<PlcContentDiffLine>(),
    old.OmittedLineCount + old.Lines.Count,
    old.OmittedCharacterCount + old.Lines.Sum(line => line.Text.Length),
    old.BudgetExhausted)
```

This counts already-truncated characters once, adds only the retained text removed now, preserves side SHA/counts and changed-span counts, and leaves generation `BudgetExhausted` unchanged. Set `ReadBack.Omission = Budget.Omission(reason, effectiveLimit, Size(originalReadBack))` for outer response-budget removal. Do not repeatedly omit an excerpt with zero retained lines or replace an existing omission without newly removed data.

Item-limit handling must inspect the serialized aggregate `verification.Operations` size after diagnostic compaction. If over limit, remove retained comparison excerpts in descending encoded size, operation order as the tie-break, remeasuring the aggregate after each removal. If still over, remove whole `ReadBack` comparisons in the same size/order policy and record verification-level omission, preserving operations/hashes. Only if the original compact checks still exceed the limit may the existing `OmitChecks` fallback run. Checking each comparison individually is insufficient because many small entries can overflow the aggregate root.

For document-limit removal, retain the existing domain order for old roots while inserting comparison excerpts and then whole comparisons immediately before aggregate verification checks: preview content diffs, shared compilation reports, failure import outcomes, comparison excerpts, comparison summaries, verification checks, effects, and existing batch fallback. Measure candidate sizes using `CanonicalJson`, recheck the current document after each omission, and keep the existing largest-first/stable-order convention inside each class. Call `Present` with nested read-back omissions included in `HasOmission` alongside existing compilation omissions. Comparison removal may set root delivery `Success` false; it never changes batch item status, `verification.Success`, `Actual`, or `ContentHash` while operations remain deliverable.

- [x] Add budget tests named `ReadBackExcerptsYieldBeforeContentHashes`, `AggregateReadBackOverflowCompactsExcerpts`, `ReadBackSummariesYieldBeforeChecks`, `NestedReadBackOmissionChangesOnlyDeliverySuccess`, `ReadBackBudgetingDoesNotMutateExecutionResponse`, `ReadBackOmissionAccountsForRetainedAndAlreadyOmittedText`, and `ReadBackUsesEncodedSizesAndEffectiveLimits`.
- [x] Build fixtures with successful `contentHash` entries containing actual format-tagged hashes and populated `ReadBack` from `PlcReadBackComparison.Build`. For the aggregate case, include at least 10 entries, prove each comparison alone is below the explicit item limit, and prove the combined operations array is over it. Choose explicit limits between measured original and compact forms, including omission-record overhead.
- [x] In the excerpt-removal cases assert these literal invariants, using `source` for the original response and `bounded` for `Apply(source, ...)`:

```csharp
Assert.True(source.Success);
Assert.False(bounded.Success);
Assert.True(bounded.Verification!.Success);
Assert.Equal(source.Verification!.Operations.Select(o => o.ContentHash),
    bounded.Verification.Operations.Select(o => o.ContentHash));
Assert.Equal(source.Verification.Operations.Select(o => o.Actual),
    bounded.Verification.Operations.Select(o => o.Actual));
Assert.All(bounded.Verification.Operations, o => Assert.True(o.Success));
Assert.Contains(bounded.Verification.Operations, o => o.ReadBack?.Omission is not null);
```

- [x] For summary removal, use an item limit below summary-only size and above measured original compact-check size; assert operations/hashes survive and `verification.Omission` is nonnull. For immutability, compare `CanonicalJson.Serialize(source)` before/after and assert the source still contains its excerpts and null omissions.
- [x] For omission counts, start with a line longer than the per-line cap so characters were already omitted, then remove its retained excerpt and assert the new omitted-character count equals the complete original changed-span text length without duplication. Preserve the original generation-budget flag.
- [x] For encoded-size coverage, include control characters requiring JSON escapes and 256-character IDs. Test both explicit small limits and an isolated effective-limit configuration above defaults using the repository's existing serialized configuration test pattern; restore that configuration afterward. Assert actual canonical sizes fit their applicable limits and an already-fitting response is unchanged.
- [x] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~PlcWriteBudgetTests"`; expect FAIL because the baseline aggregate fallback removes hashes before comparison diagnostics or because nested omission is not counted.
- [x] Implement comparison-aware omission and immutable copies with the ordering above; keep the protected-core admission behavior and original no-comparison fallback intact. Read-back comparisons are optional diagnostics and do not need admission reservation because they can be omitted before original protected checks.
- [x] Extend the canonical audit test with a budgeted response: delivered text, structured content, delivered audit document/hash and omission fields agree, while audited item statuses and attempted input remain truthful.
- [x] Update documentation to distinguish intrinsic generation truncation from outer encoded delivery omission, explain conservative spending across replans, and retain inspection guidance without recommending write replay.
- [x] Run focused tests, then the full non-packaging suite, stub build and documentation checks sequentially:

```powershell
dotnet test TiaMcpServer.Tests --filter "Category!=Packaging&(FullyQualifiedName~Plc|FullyQualifiedName~McpToolSchemaTests|FullyQualifiedName~ConditionalMemberRegisterTests|FullyQualifiedName~ToolOutputContractConformanceTests)"
dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"
dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true
python -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30
git diff --check
```

- [x] Request independent review of the implementation diff, especially selected-scope equality, map/projection lifetime, aggregate budget handling, omission counts, schema registration and hash preservation. Resolve actionable findings, rerun the checks affected by fixes, and record exact verification results and remaining live limits.
- [x] Review the final diff, update the code index and commit `fix: preserve PLC hashes when read-back evidence is budgeted`.

Only the test `.csproj` is planned to change, so Packaging is not triggered. If implementation expands to a product `.csproj`, `Directory.Build.props`, `global.json`, packaging verification scripts or reference stubs, also run `dotnet test TiaMcpServer.Tests --filter "Category=Packaging"` before committing that change. Do not run builds/tests concurrently.

## Offline execution record (2026-11-27)

- Task 1 was committed as `081badab`; its intentional `NotImplementedException` red phase was observed, then all 33 comparison/selector/diff tests passed.
- Verification, shared allowance wiring, schemas and durable behavior documentation were committed as `8feeccf5`. The focused acceptance suite passed all 764 tests. Both MCP protocol schema paths passed; only the `plc_write` snapshot subtree changed.
- The full non-packaging suite passed 9,086 tests with no failures. Independent review then prompted one additional domain-level regression, `ReadBackDomainSharesDefaultAllowanceThroughLateReplanAndResetsPerCall`; it passed separately. Three deliberate mutations resetting the allowance at planning, replanning or verification each failed that regression at the intended assertion. Production source was restored byte-for-byte, and all 57 read-back/patch/audit regressions passed afterward. The full-suite count predates this final added test.
- The serialized stub solution build passed with zero errors and eight nullable warnings in existing Network/HMI/VCI test sources. Documentation validation passed with zero errors and the two expected transient plan/spec warnings; `git diff --check` passed.
- Independent review has no unresolved actionable findings. Budget regressions cover encoded aggregate limits, omission order/counts, repeat application, immutable copies, nested delivery success and preserved hashes; canonical MCP/audit tests retain exact delivered text/hash and attempted execution input.
- During the offline implementation phase, no live TIA operation, package installation, remote write, push or PR creation was performed. A subsequent user-authorized live follow-up verified the scoped normalization, patch, shared-compilation and preservation cases in the [live report](../acceptance/reports/2026-11-27-issue-223-readback-diff-live-verification.md). Retain the working plan/spec until the later pre-merge pruning step below.

## Acceptance mapping and delivery boundary

| Spec requirement | Implementation and evidence |
| --- | --- |
| S-1 | Tasks 1-2: typed helper, policy dictionary, PLC projection, block/patch/type pipeline tests. |
| S-2 | Task 1: scope/direction, newline and changed-span tests; Task 2: public schema and documentation. |
| S-3 | Task 2: unchanged shared verifier and hash path, failed/superseded/empty tests, post-compile marker and audit checks. |
| S-4 | Tasks 1-2: common domain-owned allowance and plan/replan/verification exhaustion regression. |
| S-5 | Task 3: encoded item/document tests, omission accounting, immutable copies and hash preservation. |
| S-6 | Tasks 2-3: existing unreleased contract 2.1, unchanged version assertions, two protocol schema paths, canonical/audit checks, documentation and final offline qualification. |

The offline implementation is complete only when all tasks and corresponding acceptance criteria pass and review has no unresolved actionable findings. Report live normalization as unverified by this branch until separately authorized and executed. A later optional live check can use the reporter's neutral FBD FC with `val: Real`, `res: Bool`, `Ne`/`SrcType=Real`, and an `LReal` literal, then compare the returned evidence and fresh hash; its outcome must not be presumed from FakeWorker tests.

This transient design/plan must stay linked only from within `docs/superpowers/`. Before merge, retain behavior in the existing architecture/operation references and remove these working files in `docs: prune working files`, following `AGENTS.md`. No push or merge is authorized by this plan.
