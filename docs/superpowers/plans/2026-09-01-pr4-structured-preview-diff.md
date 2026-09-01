# PR 4 Structured Preview Diff Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add bounded, structured current-versus-requested preview evidence to `preview_write_batch` for `update_block_logic` and `update_type_content`, without changing token binding, canonical-network behavior, or any non-content write preview.

**Architecture:** Keep the worker and the current-state hash exactly as they are: the host already reads the exact-format current text that the token binds to, so PR 4 only adds a response-only comparison layer on top of those strings. Build one pure host-side diff composer with explicit per-line, per-side, and per-batch budgets; thread its typed output through the existing text preview envelope by widening `diff` from `string?` to a structured response-only value; and leave canonical JSON tools and lifecycle/network previews untouched.

**Tech Stack:** C# 12, .NET 8 host/tests, .NET Standard 2.0 contracts, .NET Framework 4.8 worker, xUnit, System.Text.Json, PowerShell 7 live harness, NDJSON MCP host protocol.

**Spec:** [docs/superpowers/specs/2026-09-01-write-safety-hardening-design.md](../specs/2026-09-01-write-safety-hardening-design.md)

## Global Constraints

- PR 4 starts only after the PR 2 wrapper-delegation baseline is present on the working branch. Implement against `WriteBatchTools`; do not fork a second preview-diff path in compatibility wrappers.
- The worker read that feeds the token is already authoritative. Do not add a second worker read for preview evidence, and do not predict post-import or post-compile Siemens state.
- Structured preview evidence is response-only. It must never participate in `IssueToken`, `ValidateEnvelope`, `ValidateAndConsume`, audit hashing, or any current-state hash.
- Only `update_block_logic` and `update_type_content` emit structured preview evidence. Lifecycle previews, `network_write`, and every non-content generic batch write continue to emit `diff: null`.
- Preserve the canonical structured-network seam exactly as shipped. `CanonicalWriteSafety.cs`, `StructuredToolResult.cs`, `NetworkReadTools.cs`, `NetworkWriteTools.cs`, and `StructuredOperationBatchPayloadBudget.cs` do not change behavior in this PR.
- Preserve exact current-state binding semantics: raw hashes and equality use the original text; line-window comparison alone normalizes CRLF and CR to LF and reports `lineEndingOnly`.
- The display budgets are fixed and exact: 40 excerpt lines and 8,192 excerpt characters per side per operation; first 20 plus last 20 lines when a changed span exceeds 40 lines; 512 characters max per displayed line; 320 excerpt lines and 32,768 excerpt characters across the whole batch, allocated strictly in request order.
- Every eligible operation keeps raw SHA-256, raw character count, raw line count, and equality flags even when the batch excerpt budget is exhausted.
- `offline` and `FakeWorker` evidence are necessary but insufficient. Completion requires the separately authorized live TIA Portal V21 host-level preview/apply/restore/compile gate plus a dated acceptance report.
- Run all Windows .NET verification serially: `dotnet build ... -m:1 /p:UseTiaPortalReferenceStubs=true`, `dotnet test ... --no-restore -m:1 --disable-build-servers`, then `git diff --check` and `git status --short`.
- Do not commit, push, or run the live apply mode without fresh explicit authorization for the exact disposable project target.

---

## File And Interface Map

| Path | Role |
| --- | --- |
| `TiaMcpServer/Batch/BatchPreviewDiff.cs` | New pure host-side diff models, constants, line normalization/comparison, truncation accounting, and request-order batch budgeting for eligible content writes only. |
| `TiaMcpServer/Batch/WriteBatchTools.cs` | Registered preview path; read ordered current-state payloads once, build the structured diff from those already-bound strings, and pass it into `WriteSafetyService.CreatePreview`. |
| `TiaMcpServer/Safety/WriteSafetyService.cs` | Widen the generic text-preview `diff` seam from `string?` to a structured response-only value, still outside token issuance and validation. |
| `TiaMcpServer/Safety/WriteSafetyTooling.cs` | Match the widened `diff` seam and retire the unused string-only `CreateLineDiff` helper. |
| `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj` | Link the new host file into the test assembly. |
| `TiaMcpServer.Tests/Batch/BatchPreviewDiffTests.cs` | Pure RED/GREEN coverage for eligibility, hashes/stats, normalized equality, line-ending-only reporting, per-line truncation, per-side truncation, and request-order batch-budget exhaustion. |
| `TiaMcpServer.Tests/Batch/BatchSafetyTokenTests.cs` | Prove display evidence does not affect token validation when target, request, state, and binding stay unchanged. |
| `TiaMcpServer.Tests/Batch/WriteBatchPreviewDiffIntegrationTests.cs` | Registered-class end-to-end preview tests through `WriteBatchTools` and `FakeWorker`, asserting real preview JSON shape and `diff: null` for ineligible writes. |
| `TiaMcpServer.Tests/Diagnostics/CiWorkflowTests.cs` | Pin the documentation authority text that describes the new preview evidence bounds. |
| `scripts/live-test-preview-write-diff.ps1` | New host-level MCP live harness for preview-only evidence checks and explicitly authorized apply/restore/compile verification on a disposable project copy. |
| `TiaMcpServer.Tests/Batch/WritePreviewDiffLiveHarnessContractTests.cs` | Execution-free tests that inspect the live harness source for safety gates, host-level protocol use, and restore/compile/report requirements. |
| `docs/ARCHITECTURE.md` | Update write-safety documentation to describe the response-only structured diff seam and its non-participation in token hashing. |
| `docs/SupportedOperations/IMPORT_EXPORT_OPTIONS_SUMMARY.md` | Document additive preview evidence for block/type replacement previews, the eligible operations, and the exact budgets/flags. |
| `docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md` | Document that `preview_write_batch` can now return bounded structured diff evidence for the two content-replacement writes only. |
| `docs/IMPROVEMENT_LOG.md` | Record PR 4 completion and any explicitly retained follow-up. |
| `docs/superpowers/acceptance/reports/2026-09-01-pr4-structured-preview-diff-live.md` | Durable live acceptance report with project copy, preview evidence, apply/restore/compile outcome, and explicit evidence boundary. |
| `docs/README.md` | Index the new acceptance report so the `docs/` tree stays reachable. |
| `docs/superpowers/README.md` | Index the new acceptance report under historical process evidence. |

### Task 1: Capture The Registered Runtime RED Through `WriteBatchTools`

**Files:**
- Create: `TiaMcpServer.Tests/Batch/WriteBatchPreviewDiffIntegrationTests.cs`

**Interfaces:**
- Consume: `WriteBatchTools.PreviewWriteBatch(OpennessWorkerClient workerClient, WriteSafetyService safety, BatchOperationRequest[] operations)`
- Consume: `FakeWorkerBinding.BindVerifiedAsync(...)`
- Preserve current behavior: `preview_write_batch` still returns `diff: null`
- Consume the existing FakeWorker scenarios: `block-source-roundtrip`, `type-content-roundtrip`, and `echo`

- [ ] **Step 1: Add the registered runtime RED tests**

Create `TiaMcpServer.Tests/Batch/WriteBatchPreviewDiffIntegrationTests.cs`:

```csharp
using System.Text.Json;
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Tests.Worker;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public sealed class WriteBatchPreviewDiffIntegrationTests
{
    private const string SourceBlockCurrent =
        "DATA_BLOCK \"Recipe\"\r\nSTRUCT\r\nEND_STRUCT;\r\nBEGIN\r\nEND_DATA_BLOCK\r\n";
    private const string TypeCurrent =
        "TYPE AnalogInputSettings STRUCT Value : Real; END_STRUCT END_TYPE";

    private static OpennessWorkerClient CreateClient(ProjectSessionBinding binding)
        => new(binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate());

    private static WriteSafetyService CreateSafety(TempAuditDirectory audit, ProjectSessionBinding binding)
        => new(binding, () => DateTimeOffset.UtcNow, WriteSafetyService.DefaultTokenLifetime, audit.Path);

    private static async Task<(OpennessWorkerClient Client, WriteSafetyService Safety, ProjectSessionBinding Binding)> BoundAsync(
        TempAuditDirectory audit,
        string projectPath)
    {
        var binding = new ProjectSessionBinding(null);
        var client = CreateClient(binding);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, projectPath);
        return (client, CreateSafety(audit, binding), binding);
    }

    [Fact]
    public async Task PreviewWriteBatch_UpdateBlockLogicWithSourceFormat_EmitsStructuredDiff()
    {
        using var audit = new TempAuditDirectory();
        var (client, safety, _) = await BoundAsync(audit, "block-source-roundtrip");
        using (client)
        {
            var result = await WriteBatchTools.PreviewWriteBatch(
                client,
                safety,
                new[]
                {
                    new BatchOperationRequest
                    {
                        OperationId = "block-source",
                        Operation = "update_block_logic",
                        ProjectPath = "block-source-roundtrip",
                        BlockPath = "PLC_1/Blocks/Recipe_DB",
                        Format = SourceFormatNames.Source,
                        YamlContent = "DATA_BLOCK \"Recipe\"\r\nSTRUCT\r\n  Value : Int;\r\nEND_STRUCT;\r\nBEGIN\r\nEND_DATA_BLOCK\r\n"
                    }
                });

            using var doc = JsonDocument.Parse(result);
            var entry = doc.RootElement.GetProperty("diff").GetProperty("operations")[0];
            Assert.Equal("block-source", entry.GetProperty("operationId").GetString());
            Assert.Equal(SourceFormatNames.Source, entry.GetProperty("format").GetString());
            Assert.True(entry.GetProperty("requested").GetProperty("excerpt").GetProperty("lines").GetArrayLength() > 0);
        }
    }

    [Fact]
    public async Task PreviewWriteBatch_UpdateBlockLogicWithoutFormat_DefaultsToXmlAndEmitsStructuredDiff()
    {
        using var audit = new TempAuditDirectory();
        var (client, safety, _) = await BoundAsync(audit, "echo");
        using (client)
        {
            var result = await WriteBatchTools.PreviewWriteBatch(
                client,
                safety,
                new[]
                {
                    new BatchOperationRequest
                    {
                        OperationId = "block-default",
                        Operation = "update_block_logic",
                        ProjectPath = "echo",
                        BlockPath = "PLC_1/Blocks/Main",
                        YamlContent = "--- FILE: Main.xml\r\n<Document />\r\n"
                    }
                });

            using var doc = JsonDocument.Parse(result);
            var entry = doc.RootElement.GetProperty("diff").GetProperty("operations")[0];
            Assert.Equal("block-default", entry.GetProperty("operationId").GetString());
            Assert.Equal(SourceFormatNames.Xml, entry.GetProperty("format").GetString());
            Assert.True(entry.GetProperty("current").GetProperty("characterCount").GetInt32() > 0);
        }
    }

    [Fact]
    public async Task PreviewWriteBatch_UpdateBlockLogicWithExplicitXmlFormat_EmitsStructuredDiff()
    {
        using var audit = new TempAuditDirectory();
        var (client, safety, _) = await BoundAsync(audit, "echo");
        using (client)
        {
            var result = await WriteBatchTools.PreviewWriteBatch(
                client,
                safety,
                new[]
                {
                    new BatchOperationRequest
                    {
                        OperationId = "block-xml",
                        Operation = "update_block_logic",
                        ProjectPath = "echo",
                        BlockPath = "PLC_1/Blocks/Main",
                        Format = SourceFormatNames.Xml,
                        YamlContent = "--- FILE: Main.xml\r\n<Document Id=\"next\" />\r\n"
                    }
                });

            using var doc = JsonDocument.Parse(result);
            var entry = doc.RootElement.GetProperty("diff").GetProperty("operations")[0];
            Assert.Equal("block-xml", entry.GetProperty("operationId").GetString());
            Assert.Equal(SourceFormatNames.Xml, entry.GetProperty("format").GetString());
            Assert.True(entry.GetProperty("requested").GetProperty("characterCount").GetInt32() > 0);
        }
    }

    [Fact]
    public async Task PreviewWriteBatch_UpdateTypeContent_EmitsStructuredDiff()
    {
        using var audit = new TempAuditDirectory();
        var (client, safety, _) = await BoundAsync(audit, "type-content-roundtrip");
        using (client)
        {
            var result = await WriteBatchTools.PreviewWriteBatch(
                client,
                safety,
                new[]
                {
                    new BatchOperationRequest
                    {
                        OperationId = "type-source",
                        Operation = "update_type_content",
                        ProjectPath = "type-content-roundtrip",
                        TypePath = "PLC_1/Types/AnalogInputSettings",
                        SourceContent = "TYPE AnalogInputSettings STRUCT Value : Int; END_STRUCT END_TYPE"
                    }
                });

            using var doc = JsonDocument.Parse(result);
            var entry = doc.RootElement.GetProperty("diff").GetProperty("operations")[0];
            Assert.Equal("type-source", entry.GetProperty("operationId").GetString());
            Assert.True(entry.GetProperty("current").GetProperty("characterCount").GetInt32() > 0);
            Assert.True(entry.GetProperty("requested").GetProperty("excerpt").GetProperty("lines").GetArrayLength() > 0);
        }
    }

    [Fact]
    public async Task PreviewWriteBatch_MixedEligibleAndIneligibleWrites_PreservesEligibleRequestOrder()
    {
        using var audit = new TempAuditDirectory();
        var (client, safety, _) = await BoundAsync(audit, "echo");
        using (client)
        {
            var result = await WriteBatchTools.PreviewWriteBatch(
                client,
                safety,
                new[]
                {
                    new BatchOperationRequest
                    {
                        OperationId = "block-1",
                        Operation = "update_block_logic",
                        ProjectPath = "echo",
                        BlockPath = "PLC_1/Blocks/Main",
                        YamlContent = "--- FILE: Main.xml\r\n<Document />\r\n"
                    },
                    new BatchOperationRequest
                    {
                        OperationId = "tag-1",
                        Operation = "create_tag_table",
                        ProjectPath = "echo",
                        TableName = "Inputs"
                    },
                    new BatchOperationRequest
                    {
                        OperationId = "type-3",
                        Operation = "update_type_content",
                        ProjectPath = "echo",
                        TypePath = "PLC_1/Types/AnalogInputSettings",
                        SourceContent = "TYPE AnalogInputSettings STRUCT Value : Int; END_STRUCT END_TYPE"
                    }
                });

            using var doc = JsonDocument.Parse(result);
            var operations = doc.RootElement.GetProperty("diff").GetProperty("operations");
            Assert.Equal(new[] { "block-1", "type-3" }, operations.EnumerateArray().Select(x => x.GetProperty("operationId").GetString()).ToArray());
        }
    }

    [Fact]
    public async Task PreviewWriteBatch_IdenticalContent_ReportsRawAndNormalizedEqualityWithNoChangedExcerpt()
    {
        using var audit = new TempAuditDirectory();
        var (client, safety, _) = await BoundAsync(audit, "block-source-roundtrip");
        using (client)
        {
            var result = await WriteBatchTools.PreviewWriteBatch(
                client,
                safety,
                new[]
                {
                    new BatchOperationRequest
                    {
                        OperationId = "block-same",
                        Operation = "update_block_logic",
                        ProjectPath = "block-source-roundtrip",
                        BlockPath = "PLC_1/Blocks/Recipe_DB",
                        Format = SourceFormatNames.Source,
                        YamlContent = SourceBlockCurrent
                    }
                });

            using var doc = JsonDocument.Parse(result);
            var entry = doc.RootElement.GetProperty("diff").GetProperty("operations")[0];
            Assert.True(entry.GetProperty("rawTextEqual").GetBoolean());
            Assert.True(entry.GetProperty("normalizedLinesEqual").GetBoolean());
            Assert.False(entry.GetProperty("lineEndingOnly").GetBoolean());
            Assert.Equal(0, entry.GetProperty("currentChangedLineCount").GetInt32());
            Assert.Equal(0, entry.GetProperty("requestedChangedLineCount").GetInt32());
            Assert.Equal(0, entry.GetProperty("current").GetProperty("excerpt").GetProperty("lines").GetArrayLength());
            Assert.Equal(0, entry.GetProperty("requested").GetProperty("excerpt").GetProperty("lines").GetArrayLength());
        }
    }

    [Fact]
    public async Task PreviewWriteBatch_LineEndingOnlyChange_ReportsNormalizedEqualityButNotRawEquality()
    {
        using var audit = new TempAuditDirectory();
        var (client, safety, _) = await BoundAsync(audit, "block-source-roundtrip");
        using (client)
        {
            var requested = SourceBlockCurrent.Replace("\r\n", "\n");
            var result = await WriteBatchTools.PreviewWriteBatch(
                client,
                safety,
                new[]
                {
                    new BatchOperationRequest
                    {
                        OperationId = "block-eol",
                        Operation = "update_block_logic",
                        ProjectPath = "block-source-roundtrip",
                        BlockPath = "PLC_1/Blocks/Recipe_DB",
                        Format = SourceFormatNames.Source,
                        YamlContent = requested
                    }
                });

            using var doc = JsonDocument.Parse(result);
            var entry = doc.RootElement.GetProperty("diff").GetProperty("operations")[0];
            Assert.False(entry.GetProperty("rawTextEqual").GetBoolean());
            Assert.True(entry.GetProperty("normalizedLinesEqual").GetBoolean());
            Assert.True(entry.GetProperty("lineEndingOnly").GetBoolean());
        }
    }

    [Fact]
    public async Task PreviewWriteBatch_TruncatesOversizedEntriesAndExhaustsTheBatchBudget()
    {
        using var audit = new TempAuditDirectory();
        var (client, safety, _) = await BoundAsync(audit, "type-content-roundtrip");
        using (client)
        {
            const int retainedLineLength = 80;
            var oversizedLine = new string('x', retainedLineLength);
            var requested = string.Join(
                "\r\n",
                Enumerable.Range(1, 60).Select(_ => oversizedLine)) + "\r\n";
            var operations = Enumerable.Range(1, 9).Select(i => new BatchOperationRequest
            {
                OperationId = $"type-{i}",
                Operation = "update_type_content",
                ProjectPath = "type-content-roundtrip",
                TypePath = "PLC_1/Types/AnalogInputSettings",
                SourceContent = requested
            }).ToArray();

            var result = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);

            using var doc = JsonDocument.Parse(result);
            var diffOperations = doc.RootElement.GetProperty("diff").GetProperty("operations").EnumerateArray().ToArray();
            Assert.Equal(40, diffOperations[0].GetProperty("requested").GetProperty("excerpt").GetProperty("lines").GetArrayLength());
            Assert.Equal(
                retainedLineLength,
                diffOperations[0].GetProperty("requested").GetProperty("excerpt").GetProperty("lines")[0].GetProperty("text").GetString()!.Length);
            Assert.False(diffOperations[6].GetProperty("batchBudgetExhausted").GetBoolean());
            Assert.True(diffOperations[7].GetProperty("batchBudgetExhausted").GetBoolean());
            Assert.Empty(diffOperations[7].GetProperty("current").GetProperty("excerpt").GetProperty("lines").EnumerateArray());
            Assert.Empty(diffOperations[7].GetProperty("requested").GetProperty("excerpt").GetProperty("lines").EnumerateArray());
            Assert.True(diffOperations[8].GetProperty("batchBudgetExhausted").GetBoolean());
            Assert.Empty(diffOperations[8].GetProperty("current").GetProperty("excerpt").GetProperty("lines").EnumerateArray());
            Assert.Empty(diffOperations[8].GetProperty("requested").GetProperty("excerpt").GetProperty("lines").EnumerateArray());
            Assert.NotEmpty(diffOperations[8].GetProperty("requested").GetProperty("sha256").GetString());
            Assert.True(diffOperations[8].GetProperty("requested").GetProperty("lineCount").GetInt32() > 0);
            Assert.True(diffOperations[8].GetProperty("requested").GetProperty("characterCount").GetInt32() > 0);
        }
    }

    [Fact]
    public async Task PreviewWriteBatch_AllIneligibleWrites_ReturnsNullDiff()
    {
        using var audit = new TempAuditDirectory();
        var (client, safety, _) = await BoundAsync(audit, "echo");
        using (client)
        {
            var result = await WriteBatchTools.PreviewWriteBatch(
                client,
                safety,
                new[]
                {
                    new BatchOperationRequest
                    {
                        OperationId = "tag-1",
                        Operation = "create_tag_table",
                        ProjectPath = "echo",
                        TableName = "Inputs"
                    }
                });

            using var doc = JsonDocument.Parse(result);
            Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("diff").ValueKind);
        }
    }
}
```

- [ ] **Step 2: Run the registered runtime RED**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~WriteBatchPreviewDiffIntegrationTests"
```

Expected RED: every eligible-case assertion fails because `WriteBatchTools.PreviewWriteBatch` still serializes `diff: null`. This runtime RED is the justification for the first production edit in PR 4.

- [ ] **Step 3: Review checkpoint**

Confirm the failing tests all route through registered `WriteBatchTools`, not `BatchPreviewDiff` or a widened preview signature alone. Do not edit production before this runtime RED is observed.

Suggested commit if separately authorized: `test: add registered preview diff regressions`

---

### Task 2: Implement The Smallest End-To-End Registered Preview Diff

**Files:**
- Create: `TiaMcpServer/Batch/BatchPreviewDiff.cs`
- Modify: `TiaMcpServer/Batch/WriteBatchTools.cs`
- Modify: `TiaMcpServer/Safety/WriteSafetyService.cs`
- Modify: `TiaMcpServer/Safety/WriteSafetyTooling.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`

**Interfaces:**
- Add: `public static class BatchPreviewDiff`
- Add: `public const int MaxExcerptLinesPerSide = 40`
- Add: `public const int MaxExcerptCharsPerSide = 8_192`
- Add: `public const int MaxExcerptCharsPerLine = 512`
- Add: `public const int MaxBatchExcerptLines = 320`
- Add: `public const int MaxBatchExcerptChars = 32_768`
- Add: `public static BatchPreviewDiffDocument? Build(IReadOnlyList<BatchOperationRequest> operations, IReadOnlyList<OperationBatchCurrentState> states)`
- Add: `public sealed record BatchPreviewDiffDocument(IReadOnlyList<BatchPreviewDiffEntry> Operations)`
- Add: `public sealed record BatchPreviewDiffEntry(string OperationId, string Operation, string Format, BatchPreviewDiffSide Current, BatchPreviewDiffSide Requested, bool RawTextEqual, bool NormalizedLinesEqual, bool LineEndingOnly, int UnchangedPrefixLineCount, int UnchangedSuffixLineCount, int CurrentChangedLineCount, int RequestedChangedLineCount, bool BatchBudgetExhausted)`
- Add: `public sealed record BatchPreviewDiffSide(string Sha256, int CharacterCount, int LineCount, BatchPreviewDiffExcerpt Excerpt)`
- Add: `public sealed record BatchPreviewDiffExcerpt(IReadOnlyList<BatchPreviewDiffLine> Lines, int OmittedLineCount, int OmittedCharacterCount, bool BudgetExhausted)`
- Add: `public sealed record BatchPreviewDiffLine(int LineNumber, string Text, int OmittedCharacterCount)`
- Change: `WriteSafetyService.CreatePreview(..., object? diff = null, string? instructions = null) -> string`
- Change: `WriteSafetyTooling.CreatePreview(..., object? diff = null, string? instructions = null) -> string`
- Change: `private static async Task<(IReadOnlyList<OperationBatchCurrentState> States, string CombinedState, string? Error)> ReadCombinedCurrentStateAsync(...)`
- Preserve: `WriteSafetyService.CreateCanonicalPreview<TTarget, TInput, TState>(..., JsonElement? diff = null)` unchanged

- [ ] **Step 1: Implement the end-to-end production path**

Create `TiaMcpServer/Batch/BatchPreviewDiff.cs` and wire the registered preview path to it. Use this exact high-level structure:

```csharp
public static BatchPreviewDiffDocument? Build(
    IReadOnlyList<BatchOperationRequest> operations,
    IReadOnlyList<OperationBatchCurrentState> states)
{
    if (operations.Count != states.Count)
    {
        throw new ArgumentException("Operations and current-state rows must align by index.");
    }

    var remainingBatchLines = MaxBatchExcerptLines;
    var remainingBatchChars = MaxBatchExcerptChars;
    var entries = new List<BatchPreviewDiffEntry>();

    for (var index = 0; index < operations.Count; index++)
    {
        var op = operations[index];
        if (!IsEligible(op, out var requestedText, out var normalizedFormat))
        {
            continue;
        }

        var compared = Compare(states[index].CurrentState, requestedText);
        var currentExcerpt = BuildExcerpt(
            compared.CurrentLines,
            compared.FirstChangedCurrentLineIndex,
            compared.LastChangedCurrentLineIndex,
            ref remainingBatchLines,
            ref remainingBatchChars);
        var requestedExcerpt = BuildExcerpt(
            compared.RequestedLines,
            compared.FirstChangedRequestedLineIndex,
            compared.LastChangedRequestedLineIndex,
            ref remainingBatchLines,
            ref remainingBatchChars);

        entries.Add(new BatchPreviewDiffEntry(
            op.OperationId,
            op.Operation,
            normalizedFormat,
            new BatchPreviewDiffSide(Sha256(states[index].CurrentState), states[index].CurrentState.Length, CountLines(states[index].CurrentState), currentExcerpt),
            new BatchPreviewDiffSide(Sha256(requestedText), requestedText.Length, CountLines(requestedText), requestedExcerpt),
            compared.RawTextEqual,
            compared.NormalizedLinesEqual,
            compared.LineEndingOnly,
            compared.UnchangedPrefixLineCount,
            compared.UnchangedSuffixLineCount,
            compared.CurrentChangedLineCount,
            compared.RequestedChangedLineCount,
            currentExcerpt.BudgetExhausted || requestedExcerpt.BudgetExhausted));
    }

    return entries.Count == 0 ? null : new BatchPreviewDiffDocument(entries);
}
```

Implementation rules:

- `IsEligible(...)` returns true only for `update_block_logic` and `update_type_content`, pulling `YamlContent` or `SourceContent` and normalizing `format` with the existing defaults: `xml` for blocks, `source` for types.
- `Compare(...)` computes raw equality from the original strings, then computes normalized-line equality by replacing CRLF and CR with LF only for line comparison.
- Identical normalized lines produce zero changed-line counts and empty excerpts on both sides.
- The excerpt builder enforces the exact limits from the spec: 40 lines and 8,192 characters per side, first 20 plus last 20 lines for oversized spans, 512 characters per displayed line, and 320 lines plus 32,768 characters across the whole batch in request order.
- When the batch budget is exhausted, later eligible entries keep hashes, line counts, character counts, and omission totals but return empty `excerpt.lines`.

Change `ReadCombinedCurrentStateAsync` in `WriteBatchTools.cs` so preview retains the already-read per-operation strings:

```csharp
private static async Task<(IReadOnlyList<OperationBatchCurrentState> States, string CombinedState, string? Error)>
    ReadCombinedCurrentStateAsync(
        OpennessWorkerClient workerClient,
        BatchOperationRequest[] operations)
{
    var states = new List<OperationBatchCurrentState>(operations.Length);
    foreach (var op in operations)
    {
        var state = await BatchWorkerInvoker.ReadCurrentStateAsync(workerClient, op).ConfigureAwait(false);
        if (!state.Success)
        {
            return (Array.Empty<OperationBatchCurrentState>(), string.Empty,
                $"Could not read current state for operationId '{op.OperationId}' ({op.Operation}). Error: {state.Error}");
        }

        states.Add(new OperationBatchCurrentState(op.OperationId, op.Operation, state.Payload));
    }

    return (states, BatchSafetySnapshot.CombineCurrentState(states), null);
}
```

Then replace the preview call site's `diff: null` with:

```csharp
var previewDiff = BatchPreviewDiff.Build(operations, snapshot.States);

return safety.CreatePreview(
    ApplyToolName,
    projectPath,
    targets,
    summary,
    operations,
    snapshot.CombinedState,
    diff: previewDiff,
    instructions: "Preview only — nothing was changed. To apply, call apply_write_batch with the identical operations list, confirm=true, and this safetyToken.");
```

Do not call `BatchPreviewDiff.Build(...)` from `ApplyWriteBatch`. Widen the generic preview seam in `WriteSafetyService` and `WriteSafetyTooling` to `object? diff`, and remove `WriteSafetyTooling.CreateLineDiff`.

Link the new host file into `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`:

```xml
<Compile Include="..\TiaMcpServer\Batch\BatchPreviewDiff.cs"
  Link="Host\Batch\BatchPreviewDiff.cs" />
```

- [ ] **Step 2: Run the registered runtime suite to GREEN**

Run the Task 1 command again.

Expected GREEN: the registered preview JSON now emits structured diff entries for source blocks, default/xml blocks, types, mixed eligible/ineligible batches, identical content, line-ending-only changes, and truncation cases, while an all-ineligible batch still returns `diff: null`.

- [ ] **Step 3: Review checkpoint**

Inspect `TiaMcpServer/Batch/BatchPreviewDiff.cs`, `TiaMcpServer/Batch/WriteBatchTools.cs`, `TiaMcpServer/Safety/WriteSafetyService.cs`, `TiaMcpServer/Safety/WriteSafetyTooling.cs`, and `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`. Confirm:

- the first runtime seam that emits structured diff data is `WriteBatchTools`;
- the only token inputs remain tool name, project path, target, requested input, and current state;
- canonical network preview paths remain unchanged; and
- no worker file changed.

Suggested commit if separately authorized: `feat: wire structured preview diff into registered batch preview`

---

### Task 3: Refine The Pure Builder And Token-Independence Coverage

**Files:**
- Create: `TiaMcpServer.Tests/Batch/BatchPreviewDiffTests.cs`
- Modify: `TiaMcpServer.Tests/Batch/BatchSafetyTokenTests.cs`

**Interfaces:**
- Consume: `BatchPreviewDiff.Build(IReadOnlyList<BatchOperationRequest> operations, IReadOnlyList<OperationBatchCurrentState> states)`
- Consume: `WriteSafetyService.CreatePreview(..., object? diff = null, string? instructions = null)`
- Preserve: all runtime behavior added in Task 2

- [ ] **Step 1: Add the pure builder and token-independence tests**

Create `TiaMcpServer.Tests/Batch/BatchPreviewDiffTests.cs`:

```csharp
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using TiaMcpServer.OperationBatches;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public sealed class BatchPreviewDiffTests
{
    private static BatchOperationRequest BlockOp(string id, string path, string requested, string? format = null)
        => new()
        {
            OperationId = id,
            Operation = "update_block_logic",
            BlockPath = path,
            YamlContent = requested,
            Format = format,
        };

    private static BatchOperationRequest TypeOp(string id, string path, string requested, string? format = null)
        => new()
        {
            OperationId = id,
            Operation = "update_type_content",
            TypePath = path,
            SourceContent = requested,
            Format = format,
        };

    private static OperationBatchCurrentState State(BatchOperationRequest op, string current)
        => new(op.OperationId, op.Operation, current);

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [Fact]
    public void Build_ReturnsNullWhenBatchHasNoEligibleTextReplacement()
    {
        var operations = new[]
        {
            new BatchOperationRequest
            {
                OperationId = "tag-1",
                Operation = "create_tag_table",
                TableName = "Inputs",
            }
        };

        var states = new[]
        {
            new OperationBatchCurrentState("tag-1", "create_tag_table", "{\"table\":\"Inputs\"}")
        };

        Assert.Null(BatchPreviewDiff.Build(operations, states));
    }

    [Fact]
    public void Build_UsesNormalizedWriteFormatAndRawHashesForEligibleOperations()
    {
        const string current = "DATA_BLOCK \"Recipe\"\r\nBEGIN\r\nEND_DATA_BLOCK\r\n";
        const string requested = "DATA_BLOCK \"Recipe\"\r\nBEGIN\r\n  Value := 1;\r\nEND_DATA_BLOCK\r\n";
        var op = BlockOp("block-1", "PLC_1/Blocks/Recipe_DB", requested, SourceFormatNames.Source);

        var diff = BatchPreviewDiff.Build(new[] { op }, new[] { State(op, current) })!;
        var entry = Assert.Single(diff.Operations);

        Assert.Equal(SourceFormatNames.Source, entry.Format);
        Assert.Equal(Sha256(current), entry.Current.Sha256);
        Assert.Equal(Sha256(requested), entry.Requested.Sha256);
    }

    [Fact]
    public void Build_IdenticalContent_ProducesEmptyExcerptsAndZeroChangedSpans()
    {
        const string content = "TYPE \"A\"\r\nSTRUCT\r\nEND_STRUCT;\r\nEND_TYPE\r\n";
        var op = TypeOp("type-1", "PLC_1/Types/A", content);

        var diff = BatchPreviewDiff.Build(new[] { op }, new[] { State(op, content) })!;
        var entry = Assert.Single(diff.Operations);

        Assert.True(entry.RawTextEqual);
        Assert.True(entry.NormalizedLinesEqual);
        Assert.False(entry.LineEndingOnly);
        Assert.Equal(0, entry.CurrentChangedLineCount);
        Assert.Equal(0, entry.RequestedChangedLineCount);
        Assert.Empty(entry.Current.Excerpt.Lines);
        Assert.Empty(entry.Requested.Excerpt.Lines);
    }

    [Fact]
    public void Build_DetectsLineEndingOnlyDifference()
    {
        const string current = "TYPE \"A\"\r\nSTRUCT\r\nEND_STRUCT;\r\nEND_TYPE\r\n";
        const string requested = "TYPE \"A\"\nSTRUCT\nEND_STRUCT;\nEND_TYPE\n";
        var op = TypeOp("type-1", "PLC_1/Types/A", requested);

        var diff = BatchPreviewDiff.Build(new[] { op }, new[] { State(op, current) })!;
        var entry = Assert.Single(diff.Operations);

        Assert.False(entry.RawTextEqual);
        Assert.True(entry.NormalizedLinesEqual);
        Assert.True(entry.LineEndingOnly);
    }

    [Fact]
    public void Build_TruncatesChangedSpanToFirstAndLastTwentyLinesPerSide()
    {
        var current = string.Join("\r\n", Enumerable.Range(1, 60).Select(i => $"OLD {i}")) + "\r\n";
        var requested = string.Join("\r\n", Enumerable.Range(1, 60).Select(i => $"NEW {i}")) + "\r\n";
        var op = TypeOp("type-1", "PLC_1/Types/A", requested);

        var diff = BatchPreviewDiff.Build(new[] { op }, new[] { State(op, current) })!;
        var entry = Assert.Single(diff.Operations);

        Assert.Equal(40, entry.Current.Excerpt.Lines.Count);
        Assert.Equal(40, entry.Requested.Excerpt.Lines.Count);
        Assert.Equal("OLD 1", entry.Current.Excerpt.Lines[0].Text);
        Assert.Equal("OLD 60", entry.Current.Excerpt.Lines[^1].Text);
        Assert.Equal("NEW 1", entry.Requested.Excerpt.Lines[0].Text);
        Assert.Equal("NEW 60", entry.Requested.Excerpt.Lines[^1].Text);
        Assert.Equal(20, entry.Current.Excerpt.OmittedLineCount);
        Assert.Equal(20, entry.Requested.Excerpt.OmittedLineCount);
    }

    [Fact]
    public void Build_TruncatesLongLinesAndReportsOmittedCharacters()
    {
        var longLine = new string('x', BatchPreviewDiff.MaxExcerptCharsPerLine + 37);
        var current = "TYPE \"A\"\r\nEND_TYPE\r\n";
        var requested = $"TYPE \"A\"\r\n{longLine}\r\nEND_TYPE\r\n";
        var op = TypeOp("type-1", "PLC_1/Types/A", requested);

        var diff = BatchPreviewDiff.Build(new[] { op }, new[] { State(op, current) })!;
        var line = diff.Operations[0].Requested.Excerpt.Lines.Single(x => x.LineNumber == 2);

        Assert.Equal(BatchPreviewDiff.MaxExcerptCharsPerLine, line.Text.Length);
        Assert.Equal(37, line.OmittedCharacterCount);
        Assert.Equal(37, diff.Operations[0].Requested.Excerpt.OmittedCharacterCount);
    }

    [Fact]
    public void Build_ExhaustsTheWholeBatchLineBudgetAtTheFifthEligibleOperation_AndLaterEntriesKeepHashesAndCounts()
    {
        var current = string.Join("\r\n", Enumerable.Range(1, 60).Select(i => $"OLD {i}")) + "\r\n";
        var requested = string.Join("\r\n", Enumerable.Range(1, 60).Select(i => $"NEW {i}")) + "\r\n";
        var operations = Enumerable.Range(1, 6)
            .Select(i => TypeOp($"type-{i}", $"PLC_1/Types/T{i}", requested))
            .ToArray();
        var states = operations.Select(op => State(op, current)).ToArray();

        var diff = BatchPreviewDiff.Build(operations, states)!;

        Assert.All(diff.Operations.Take(4), entry => Assert.NotEmpty(entry.Requested.Excerpt.Lines));
        Assert.True(diff.Operations[4].BatchBudgetExhausted);
        Assert.Empty(diff.Operations[4].Current.Excerpt.Lines);
        Assert.Empty(diff.Operations[4].Requested.Excerpt.Lines);
        Assert.True(diff.Operations[5].BatchBudgetExhausted);
        Assert.Empty(diff.Operations[5].Current.Excerpt.Lines);
        Assert.Empty(diff.Operations[5].Requested.Excerpt.Lines);
        Assert.NotEmpty(diff.Operations[5].Requested.Sha256);
        Assert.True(diff.Operations[5].Requested.LineCount > 0);
        Assert.True(diff.Operations[5].Requested.CharacterCount > 0);
    }
}
```

Extend `TiaMcpServer.Tests/Batch/BatchSafetyTokenTests.cs` with:

```csharp
[Fact]
public void DisplayDiff_IsNotRequiredForTokenValidation()
{
    using var audit = new TempAuditDirectory();
    var service = audit.CreateSafety();
    var (ops, states) = TwoItemBatch();
    var targets = BatchSafetySnapshot.BuildTargets(ops);
    var combined = BatchSafetySnapshot.CombineCurrentState(
        ops.Select((o, i) => new OperationBatchCurrentState(o.OperationId, o.Operation, states[i])).ToList());
    var project = BatchSafetySnapshot.ResolveProjectPath(ops);

    var preview = service.CreatePreview(
        ApplyToolName,
        project,
        targets,
        "summary",
        ops,
        combined,
        diff: new { operations = new[] { new { operationId = "b", operation = "update_block_logic" } } });
    var token = JsonDocument.Parse(preview).RootElement.GetProperty("safetyToken").GetString()!;

    Assert.True(service.ValidateAndConsume(token, ApplyToolName, project, targets, ops, combined).IsValid);
}

[Fact]
public void DifferentDisplayDiffs_IssueTokensThatValidateAgainstTheSameState()
{
    using var audit = new TempAuditDirectory();
    var service = audit.CreateSafety();
    var (ops, states) = TwoItemBatch();
    var targets = BatchSafetySnapshot.BuildTargets(ops);
    var combined = BatchSafetySnapshot.CombineCurrentState(
        ops.Select((o, i) => new OperationBatchCurrentState(o.OperationId, o.Operation, states[i])).ToList());
    var project = BatchSafetySnapshot.ResolveProjectPath(ops);

    var first = JsonDocument.Parse(service.CreatePreview(
        ApplyToolName,
        project,
        targets,
        "summary",
        ops,
        combined,
        diff: new { version = 1 }));
    var second = JsonDocument.Parse(service.CreatePreview(
        ApplyToolName,
        project,
        targets,
        "summary",
        ops,
        combined,
        diff: new { version = 2, extra = true }));

    Assert.True(service.ValidateEnvelope(first.RootElement.GetProperty("safetyToken").GetString(), ApplyToolName, project, targets, ops).IsValid);
    Assert.True(service.ValidateEnvelope(second.RootElement.GetProperty("safetyToken").GetString(), ApplyToolName, project, targets, ops).IsValid);
}
```

- [ ] **Step 2: Run the refinement RED**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~BatchPreviewDiffTests|FullyQualifiedName~BatchSafetyTokenTests.DisplayDiff_|FullyQualifiedName~BatchSafetyTokenTests.DifferentDisplayDiffs_"
```

Expected RED: any incompleteness in the minimal Task 2 implementation now shows up in exact helper-level assertions, especially the per-side omitted-line accounting, per-line character truncation, or the fifth-operation whole-batch exhaustion boundary.

- [ ] **Step 3: Tighten the pure helper behavior until the refinement suite passes**

Keep the runtime JSON property names from Task 2 unchanged. Fix only the pure helper accounting or generic preview seam details needed to satisfy the new tests; do not introduce a second response projection layer.

- [ ] **Step 4: Run the refinement suite to GREEN**

Run the Step 2 command again.

Expected GREEN: the pure builder now proves no-eligible behavior, hashes/stats, identical-content handling, line-ending-only handling, per-side truncation, per-line truncation, the exact fifth-operation batch-exhaustion boundary, and token independence from display evidence.

- [ ] **Step 5: Run the combined preview verification slice**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~WriteBatchPreviewDiffIntegrationTests|FullyQualifiedName~BatchPreviewDiffTests|FullyQualifiedName~BatchSafetyTokenTests.DisplayDiff_|FullyQualifiedName~BatchSafetyTokenTests.DifferentDisplayDiffs_"
```

Expected GREEN: the registered runtime surface and the pure helper assertions now agree on the same diff shape and the same response-only token boundary.

- [ ] **Step 6: Review checkpoint**

Confirm:

- the first production edit was justified by Task 1's runtime RED through `WriteBatchTools`;
- `BatchPreviewDiffTests` now use enough eligible operations to cross the real 320-line batch budget, with exhaustion beginning at the fifth eligible operation and later entries retaining hashes/counts but empty excerpts;
- `BatchSafetyTokenTests` prove display evidence is outside token semantics; and
- no test relies on a compile-time seam mismatch as the milestone's primary RED.

Suggested commit if separately authorized: `test: harden pure preview diff accounting`

---

### Task 4: Update The Architecture And Supported-Operation Authorities

**Files:**
- Modify: `docs/ARCHITECTURE.md`
- Modify: `docs/SupportedOperations/IMPORT_EXPORT_OPTIONS_SUMMARY.md`
- Modify: `docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md`
- Modify: `docs/IMPROVEMENT_LOG.md`
- Modify: `TiaMcpServer.Tests/Diagnostics/CiWorkflowTests.cs`

**Interfaces:**
- Document only. No code, schema, or contract changes in this task.

- [ ] **Step 1: Add the documentation RED check**

Before editing docs, add these assertions to `TiaMcpServer.Tests/Diagnostics/CiWorkflowTests.cs` so the behavior is pinned:

```csharp
[Fact]
public void ImportExportSummary_DocumentsStructuredPreviewDiffBounds()
{
    var text = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "docs", "SupportedOperations", "IMPORT_EXPORT_OPTIONS_SUMMARY.md"));

    Assert.Contains("update_block_logic", text, StringComparison.Ordinal);
    Assert.Contains("update_type_content", text, StringComparison.Ordinal);
    Assert.Contains("40 excerpt lines", text, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("8,192", text, StringComparison.Ordinal);
    Assert.Contains("32,768", text, StringComparison.Ordinal);
    Assert.Contains("line-ending-only", text, StringComparison.OrdinalIgnoreCase);
}
```

If `CiWorkflowTests` already contains a more natural documentation-authority section for import/export summaries, place this assertion there rather than inventing a second helper.

- [ ] **Step 2: Run the focused RED**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~ImportExportSummary_DocumentsStructuredPreviewDiffBounds"
```

Expected RED: the current docs do not mention structured preview diff budgets, eligible operations, or line-ending-only reporting.

- [ ] **Step 3: Update the four authority documents**

Apply these exact documentation additions:

- In `docs/ARCHITECTURE.md` §8, add one short paragraph after the preview/apply description stating that `preview_write_batch` may include a response-only structured `diff` object for `update_block_logic` and `update_type_content`, built from the already-bound exact-format current text and the submitted replacement text, and that the object is outside token issuance and validation.
- In `docs/SupportedOperations/IMPORT_EXPORT_OPTIONS_SUMMARY.md`, add one subsection named `Preview evidence` containing the exact budgets and flags from the spec:
  - 40 excerpt lines and 8,192 excerpt characters per side per eligible operation;
  - first 20 plus last 20 lines when a changed span exceeds 40 lines;
  - 512 characters per displayed line;
  - 320 excerpt lines and 32,768 excerpt characters across the whole batch in request order;
  - raw SHA-256, raw character count, raw line count, `rawTextEqual`, `normalizedLinesEqual`, and `lineEndingOnly`;
  - `diff: null` for every other operation.
- In `docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md`, add one bullet under `Update and write behavior` stating that block/type replacement previews can include bounded structured evidence and that it is current-versus-requested only, not predicted post-write state.
- In `docs/IMPROVEMENT_LOG.md`, record PR 4 as complete only after live acceptance is finished. Until then, keep it in the open follow-up section as "PR 4 structured preview diff live gate pending."

- [ ] **Step 4: Run the documentation test to GREEN**

Run the Step 2 command again.

Expected GREEN: the documentation-authority test now passes against the updated wording.

- [ ] **Step 5: Review checkpoint**

Inspect the docs diff and confirm it does all of the following:

- documents the exact budgets and line-ending-only semantics;
- says the evidence is response-only and outside token hashing;
- limits the feature to `update_block_logic` and `update_type_content`; and
- does not claim lifecycle, network, or predicted post-state diff support.

Suggested commit if separately authorized: `docs: describe structured preview diff bounds`

---

### Task 5: Add The Guarded Host-Level Live Harness And Durable Acceptance Report

**Files:**
- Create: `scripts/live-test-preview-write-diff.ps1`
- Create: `TiaMcpServer.Tests/Batch/WritePreviewDiffLiveHarnessContractTests.cs`
- Create: `docs/superpowers/acceptance/reports/2026-09-01-pr4-structured-preview-diff-live.md`
- Modify: `docs/README.md`
- Modify: `docs/superpowers/README.md`

**Interfaces:**
- Add script modes: `[ValidateSet('Preview', 'Apply')] [string] $Mode = 'Preview'`
- Add explicit apply gate: `[switch] $AllowApply`
- Add explicit CI bypass gate for authorized runs only: `[switch] $ConfirmApplyForCi`
- Add mandatory live-target parameters: `[Parameter(Mandatory)] [string] $ProjectPath`, `[Parameter(Mandatory)] [string] $BlockPath`, `[Parameter(Mandatory)] [string] $TypePath`
- Add optional host artifact parameter: `[string] $HostDllPath = "TiaMcpServer/bin/Debug/net8.0/TiaMcpServer.dll"`

- [ ] **Step 1: Add the execution-free contract RED tests**

Create `TiaMcpServer.Tests/Batch/WritePreviewDiffLiveHarnessContractTests.cs`:

```csharp
using System.Text.RegularExpressions;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public sealed class WritePreviewDiffLiveHarnessContractTests
{
    private static readonly string ScriptPath = Path.GetFullPath(
        Path.Combine(GetRepositoryRoot(), "scripts", "live-test-preview-write-diff.ps1"));

    [Fact]
    public void Script_ExistsAndRequiresPowerShell7()
    {
        var text = ReadScript();
        Assert.Matches(new Regex(@"^\s*#Requires\s+-Version\s+7(\.\d+)?\s*$", RegexOptions.Multiline), text);
        Assert.Contains("$ErrorActionPreference = 'Stop'", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_DefaultsToPreviewAndGuardsApplyBehindAnExplicitSwitch()
    {
        var text = ReadScript();
        Assert.Matches(new Regex(@"\[ValidateSet\(\s*'Preview'\s*,\s*'Apply'\s*\)\]"), text);
        Assert.Matches(new Regex(@"\[string\]\s*\$Mode\s*=\s*'Preview'"), text);
        Assert.Matches(new Regex(@"\[switch\]\s*\$AllowApply"), text);
        Assert.Matches(new Regex(@"if\s*\(\s*\$Mode\s*-eq\s*'Apply'\s*-and\s*-not\s*\$AllowApply\s*\)"), text);
    }

    [Fact]
    public void Script_UsesTheRealHostLevelMcpProtocolRatherThanDirectWorkerIpc()
    {
        var text = ReadScript();
        Assert.Contains("TiaMcpServer.dll", text, StringComparison.Ordinal);
        Assert.Contains("--project", text, StringComparison.Ordinal);
        Assert.Contains("'initialize'", text, StringComparison.Ordinal);
        Assert.Contains("notifications/initialized", text, StringComparison.Ordinal);
        Assert.Contains("'tools/call'", text, StringComparison.Ordinal);
        Assert.Contains("get_project_status", text, StringComparison.Ordinal);
        Assert.Contains("preview_write_batch", text, StringComparison.Ordinal);
        Assert.Contains("apply_write_batch", text, StringComparison.Ordinal);
        Assert.DoesNotContain("OpennessWorker.exe", text, StringComparison.Ordinal);
        Assert.DoesNotContain("open_project", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_ApplyPathRestoresOriginalBytesAndCompilesTheDisposableProject()
    {
        var text = ReadScript();
        Assert.Contains("compile_check", text, StringComparison.Ordinal);
        Assert.Contains("byte-identical", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("restore", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Read-Host", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_DocumentsDisposableProjectScopeAndWritesTheAcceptanceReportPath()
    {
        var text = ReadScript();
        Assert.Matches(new Regex(@"disposable", RegexOptions.IgnoreCase), text);
        Assert.Contains("2026-09-01-pr4-structured-preview-diff-live.md", text, StringComparison.Ordinal);
    }

    private static string ReadScript()
    {
        Assert.True(File.Exists(ScriptPath), $"Expected the live harness at '{ScriptPath}'.");
        return File.ReadAllText(ScriptPath);
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            ".."));
    }
}
```

- [ ] **Step 2: Run the contract RED**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~WritePreviewDiffLiveHarnessContractTests"
```

Expected RED: the script file does not exist yet.

- [ ] **Step 3: Create the live harness**

Create `scripts/live-test-preview-write-diff.ps1` with the same static safety shape as the network live harnesses:

```powershell
#Requires -Version 7
[CmdletBinding()]
param(
    [ValidateSet('Preview', 'Apply')]
    [string] $Mode = 'Preview',
    [Parameter(Mandatory)] [string] $ProjectPath,
    [Parameter(Mandatory)] [string] $BlockPath,
    [Parameter(Mandatory)] [string] $TypePath,
    [switch] $AllowApply,
    [switch] $ConfirmApplyForCi,
    [string] $HostDllPath = "TiaMcpServer/bin/Debug/net8.0/TiaMcpServer.dll"
)

$ErrorActionPreference = 'Stop'

if ($Mode -eq 'Apply' -and -not $AllowApply) {
    throw "Apply mode is disabled by default. Re-run with -AllowApply only for an explicitly authorized disposable project copy."
}
```

Harness requirements:

- Launch the real host (`TiaMcpServer.dll`) in read-write mode with the exact disposable `--project` path, speak NDJSON MCP, and verify the active binding through the public read-only `get_project_status` path before any batch preview/apply call.
- In both modes, read the current block/type text first through `execute_read_batch` so the harness owns the exact original bytes it later compares and, in `Apply` mode, restores.
- In `Preview` mode, run three non-mutating checks and write their findings into the report body:
  - block source preview with a real content change and structured diff present;
  - type source preview with a real content change and structured diff present;
  - line-ending-only preview where the submitted replacement changes only CRLF vs LF and the preview reports `rawTextEqual = false`, `normalizedLinesEqual = true`, and `lineEndingOnly = true`.
- Also in `Preview` mode, generate enough oversized requested replacements with 512-character retained lines so the returned preview proves per-line truncation, per-side truncation, and deterministic request-order whole-batch exhaustion without applying anything.
- In `Apply` mode, after an interactive confirmation unless `-ConfirmApplyForCi` is supplied:
  - preview a two-item batch (`update_block_logic` and `update_type_content`);
  - apply it with the unchanged operation list and issued token;
  - preview and apply a second batch restoring the original bytes exactly;
  - re-read both objects and assert byte-identical restoration;
  - run `compile_check` and require zero errors;
  - record the final clean state in the acceptance report.

- [ ] **Step 4: Create the dated acceptance report template and index entries**

Create `docs/superpowers/acceptance/reports/2026-09-01-pr4-structured-preview-diff-live.md` with these fixed sections:

```markdown
# PR 4 Structured Preview Diff Live Acceptance

## Environment

- Date:
- TIA Portal version:
- Host build:
- Disposable project path:
- Binding verification:
- Block target:
- Type target:

## Preview-Only Evidence

- Block preview:
- Type preview:
- Line-ending-only preview:
- Oversized batch preview:

## Apply / Restore / Compile

- Apply authorization:
- Applied changes:
- Restore result:
- Byte-identical re-read:
- Compile result:

## Evidence Boundary

- Proven:
- Not proven:
```

Then update both indexes:

- `docs/README.md`: add the new acceptance report to the `Latest process entries` sentence.
- `docs/superpowers/README.md`: add one row under `Acceptance reports` for `2026-09-01` and this file.

- [ ] **Step 5: Run the contract tests to GREEN**

Run the Step 2 command again.

Expected GREEN: the script source is now present and statically proves host-level protocol use, startup `--project` binding plus read-only `get_project_status` verification, preview-default safety, explicit apply gating, restore/compile behavior, and durable report naming.

- [ ] **Step 6: Review checkpoint**

Inspect `scripts/live-test-preview-write-diff.ps1`, `WritePreviewDiffLiveHarnessContractTests.cs`, the new acceptance report file, and the two doc indexes. Confirm:

- the script defaults to preview-only;
- the only `confirm = $true` write path is inside the explicit apply branch;
- the harness starts the host with the exact disposable `--project` path and verifies binding through `get_project_status` without any lifecycle preview/apply call;
- the harness never talks directly to `OpennessWorker.exe`;
- the report names the exact evidence boundary; and
- the docs indexes reference the new report.

Suggested commit if separately authorized: `test: add live harness for structured preview diff`

---

### Task 6: Run Full Offline Verification And The Separately Authorized Live Gate

**Files:**
- Review: every file changed by Tasks 1-5

**Verification boundary:**
- Establishes offline: pure diff behavior, registered preview JSON shape, token independence, documentation authority, host build, and full test-suite status.
- Establishes live: host-level preview evidence, startup `--project` binding plus read-only `get_project_status` verification, explicit apply/restore with unchanged inputs and issued tokens, byte-identical restoration, and clean compile on a disposable V21 project copy.
- Does not establish: lifecycle or network diff behavior, predicted post-write Siemens state, PLC start/stop correctness, plant acceptance, or physical-hardware acceptance.

- [ ] **Step 1: Run the focused offline verification set**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~BatchPreviewDiffTests|FullyQualifiedName~BatchSafetyTokenTests.DisplayDiff_|FullyQualifiedName~BatchSafetyTokenTests.DifferentDisplayDiffs_|FullyQualifiedName~WriteBatchPreviewDiffIntegrationTests|FullyQualifiedName~WritePreviewDiffLiveHarnessContractTests|FullyQualifiedName~ImportExportSummary_DocumentsStructuredPreviewDiffBounds"
```

Expected GREEN: all new pure, integration, contract, and documentation-authority tests pass together.

- [ ] **Step 2: Build the full stubbed solution**

Run:

```powershell
dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
```

Expected GREEN: the host, tests, worker, and scripts/docs-adjacent code compile without a live Siemens installation.

- [ ] **Step 3: Run the full serial test suite**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers
```

Expected GREEN: the complete offline suite passes. If an unrelated pre-existing flake appears, rerun only that exact test once, record both outcomes in the implementation notes, and do not expand PR 4 into unrelated repairs.

- [ ] **Step 4: Run scope and whitespace checks**

Run:

```powershell
git diff --check
git status --short
git diff --stat
```

Confirm:

- `CanonicalWriteSafety.cs`, `StructuredToolResult.cs`, `NetworkReadTools.cs`, `NetworkWriteTools.cs`, and `StructuredOperationBatchPayloadBudget.cs` are unchanged.
- `preview_write_batch` is the only runtime call site that now emits structured diff data.
- No worker file changed.
- No non-content write preview changed from `diff: null`.

- [ ] **Step 5: Run the live preview-only harness**

Run only after explicit authorization for the exact disposable project target:

```powershell
pwsh -NoProfile -File scripts/live-test-preview-write-diff.ps1 `
  -Mode Preview `
  -ProjectPath "C:\Path\To\Disposable\Line.ap21" `
  -BlockPath "PLC_1/Blocks/Recipe_DB" `
  -TypePath "PLC_1/Types/AnalogInputSettings"
```

Expected GREEN: the script records the startup `--project` plus `get_project_status` binding proof, emits structured preview evidence for the block and type routes, proves the line-ending-only case, proves truncation/batch-exhaustion behavior non-mutatingly, and writes the preview section of `docs/superpowers/acceptance/reports/2026-09-01-pr4-structured-preview-diff-live.md`.

- [ ] **Step 6: Run the live apply / restore / compile harness**

Run only after separate explicit authorization for mutation on that same disposable project copy:

```powershell
pwsh -NoProfile -File scripts/live-test-preview-write-diff.ps1 `
  -Mode Apply `
  -AllowApply `
  -ProjectPath "C:\Path\To\Disposable\Line.ap21" `
  -BlockPath "PLC_1/Blocks/Recipe_DB" `
  -TypePath "PLC_1/Types/AnalogInputSettings"
```

Expected GREEN: the script applies exactly one two-item batch through `preview_write_batch` then `apply_write_batch`, restores the original bytes through a second preview/apply pair, re-reads both objects byte-identically, compiles cleanly, and completes the dated acceptance report with the exact mutation/restore boundary while keeping lifecycle preview/apply and rebinding out of scope.

- [ ] **Step 7: Final review checkpoint**

Report exactly:

- which files changed;
- the focused test, full build, and full-suite results;
- the preview-only live result;
- the apply/restore/compile live result;
- the path to the acceptance report; and
- any remaining deferred work that stayed out of PR 4.

Suggested commit if separately authorized: `feat: add structured preview diff for block and type writes`

---

## Deferred / Out Of Scope

- No generalized diff engine for lifecycle tools, `network_write`, create/delete operations, tree operations, or any other non-content preview.
- No predicted post-import, post-compile, or post-write Siemens state. PR 4 compares only the exact currently bound current text and the submitted replacement text.
- No token format, token lifetime, token single-use, audit format, project binding, or pinned-lease behavior change.
- No canonical-network behavior change. This PR does not alter `CanonicalWriteSafety`, `StructuredToolResult`, network response schemas, or network payload budgeting.
- No PLC `start_plc` / `stop_plc` work. Those remain deferred exactly as the 2026-09-01 hardening design says.
- No claim that offline or FakeWorker coverage is sufficient. Live TIA Portal V21 host-level acceptance on a disposable project copy remains mandatory.
- No README landing-page update unless a reviewer explicitly decides the additive preview field materially changes the public landing-page contract.
