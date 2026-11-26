using System.Text.Json;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Plc;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

public sealed class PlcSharedCompilationTests
{
    [Fact]
    public void Eligibility_TwoSourceImports_IncludingResolvedPatch()
    {
        var patch = Import("patch", "PLC_A/Blocks/Second", operation: "patch_block_logic", format: "SOURCE");
        var ids = PlcSharedCompilationPolicy.DeferredOperationIds([Import("update"), patch]);
        Assert.Equal(["patch", "update"], ids.OrderBy(id => id).ToArray());
        // Patch resolution retains the ID and source format while changing only its operation.
        patch.Operation = "update_block_logic";
        Assert.True(ids.SetEquals(PlcSharedCompilationPolicy.DeferredOperationIds([Import("update"), patch])));
    }

    [Fact]
    public void Eligibility_OneSourceImport_IsEmpty()
        => Assert.Empty(PlcSharedCompilationPolicy.DeferredOperationIds([Import("only")]));

    [Fact]
    public void Eligibility_XmlAndTypeImports_NotCounted()
    {
        Assert.Empty(PlcSharedCompilationPolicy.DeferredOperationIds([
            Import("source"), Import("xml", format: "xml"), Import("default", format: null),
            Import("type", operation: "update_type_content"), Import("invalid", format: "invalid")
        ]));
    }

    [Fact]
    public void Eligibility_MixedWithTagsAndCreates_StillEligible()
    {
        var ids = PlcSharedCompilationPolicy.DeferredOperationIds([
            Import("first"), new() { OperationId = "tag", Operation = "update_tag" },
            Import("create", operation: "create_block"), Import("second"), Import("xml", format: "xml")
        ]);
        Assert.Equal(["first", "second"], ids.OrderBy(id => id).ToArray());
    }

    [Theory]
    [InlineData("PLC_A/Blocks/Group/First", "PLC_A")]
    [InlineData("PLC_A/Units/Unit/Blocks/First", "PLC_A")]
    [InlineData("First", null)]
    public void Selector_MatchesPerOperationBlockAddress(string blockPath, string? expected)
        => Assert.Equal(expected, PlcSharedCompilationPolicy.Selector(blockPath));

    [Fact]
    public async Task Tracker_GroupsBySelectorCaseInsensitive_FirstCommitOrder()
    {
        var tracker = new PlcSharedCompilationTracker();
        tracker.Record(Import("b1", "PLC_B/Blocks/One"), Committed());
        tracker.Record(Import("a1", "PLC_A/Blocks/One"), Committed());
        tracker.Record(Import("b2", "plc_b/Blocks/Two"), Committed());
        var selectors = new List<string?>();
        var entries = await tracker.CompileAsync(selector =>
        {
            selectors.Add(selector);
            return Task.FromResult(Report());
        }, Batch());

        Assert.Equal(["PLC_B", "PLC_A"], selectors);
        Assert.Equal(["PLC_B", "PLC_A"], entries.Select(entry => entry.PlcName));
        Assert.Equal(["b1", "b2"], entries[0].OperationIds);
        Assert.Equal(["a1"], entries[1].OperationIds);
    }

    [Fact]
    public async Task Tracker_UnqualifiedPathsShareNullSelector()
    {
        var tracker = new PlcSharedCompilationTracker();
        tracker.Record(Import("first", "First"), Committed());
        tracker.Record(Import("qualified", "PLC_A/Blocks/One"), Committed());
        tracker.Record(Import("second", "Second"), Committed());
        var selectors = new List<string?>();
        var entries = await tracker.CompileAsync(selector =>
        {
            selectors.Add(selector);
            return Task.FromResult(Report());
        }, Batch());

        Assert.Equal([null, "PLC_A"], selectors);
        Assert.Equal(["first", "second"], entries[0].OperationIds);
    }

    [Fact]
    public async Task Tracker_CommittedThenFailedItem_IsCompiled()
    {
        var tracker = new PlcSharedCompilationTracker();
        tracker.Record(Import("failed"), Committed(WorkerCallResult.Fail(
            WorkerFailureCategories.WorkerOperationFailed, "Final read failed.")));
        var entries = await tracker.CompileAsync(_ => Task.FromResult(Report()),
            Batch(Item("failed", WorkerFailureCategories.WorkerOperationFailed)));

        var entry = Assert.Single(entries);
        Assert.Equal(["failed"], entry.OperationIds);
        Assert.Equal("succeeded", entry.Stage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task Tracker_UncommittedFailure_NotCompiled(bool? committed)
    {
        var tracker = new PlcSharedCompilationTracker();
        tracker.Record(Import("failed"), WorkerCallResult.Fail(WorkerFailureCategories.WorkerOperationFailed,
            "Generation failed.") with { BlockImportOutcome = new() { TargetMutationCommitted = committed } });
        var entries = await tracker.CompileAsync(_ => throw new InvalidOperationException("Unexpected compile."),
            Batch(Item("failed", WorkerFailureCategories.WorkerOperationFailed)));
        Assert.Empty(entries);
    }

    [Fact]
    public async Task Tracker_MixedXmlAndOtherCommittedOutcomes_AreNotCompiled()
    {
        var tracker = new PlcSharedCompilationTracker();
        tracker.Record(Import("source"), Committed());
        tracker.Record(Import("xml", "PLC_XML/Blocks/One", format: "xml"), Committed());
        tracker.Record(Import("type", operation: "update_type_content"), Committed());
        tracker.Record(Import("create", operation: "create_block"), Committed());
        var entries = await tracker.CompileAsync(_ => Task.FromResult(Report()), Batch());

        Assert.Equal(["source"], Assert.Single(entries).OperationIds);
    }

    [Fact]
    public async Task Compile_WarningsOnly_IsSucceeded()
    {
        var entry = await CompileOne(Report("Warning", warnings: 2));
        Assert.Equal("succeeded", entry.Stage);
        Assert.Equal(2, entry.Report!.TotalWarningCount);
        Assert.Null(entry.Failure);
        Assert.Null(entry.Omission);
    }

    [Fact]
    public async Task Compile_Errors_IsFailed()
    {
        var entry = await CompileOne(Report("Error", errors: 2));
        Assert.Equal("failed", entry.Stage);
        Assert.Equal(2, entry.Report!.TotalErrorCount);
        Assert.Null(entry.Failure);
    }

    [Theory]
    [InlineData("InvocationFailed")]
    [InlineData("AmbiguousSelection")]
    public async Task Compile_InvocationFailureReport_IsUnavailable(string state)
    {
        var entry = await CompileOne(Report(state));
        Assert.Equal("unavailable", entry.Stage);
        Assert.Equal(state, Assert.Single(entry.Report!.Plcs).State);
        Assert.Null(entry.Failure);
    }

    [Fact]
    public async Task Compile_MalformedPayload_IsUnavailableProtocolError()
    {
        var entry = await CompileOne(WorkerCallResult.Ok("{\"PRIVATE_REPORT_MARKER\":true}"));
        Assert.Equal("unavailable", entry.Stage);
        Assert.Null(entry.Report);
        Assert.Equal(WorkerFailureCategories.ProtocolError, entry.Failure!.Category);
        Assert.Equal(StandalonePayloadContract.ProtocolFailureMessage, entry.Failure.Message);
        Assert.DoesNotContain("PRIVATE_REPORT_MARKER", JsonSerializer.Serialize(entry));
    }

    [Fact]
    public async Task Compile_WorkerFailure_KeepsCategory()
    {
        var entry = await CompileOne(WorkerCallResult.Fail(WorkerFailureCategories.WorkerTimeout, "Compile timed out."));
        Assert.Equal("unavailable", entry.Stage);
        Assert.Null(entry.Report);
        Assert.Equal(WorkerFailureCategories.WorkerTimeout, entry.Failure!.Category);
        Assert.Equal("Compile timed out.", entry.Failure.Message);
    }

    [Theory]
    [InlineData(WorkerFailureCategories.PortalLost)]
    [InlineData(WorkerFailureCategories.WorkerTimeout)]
    [InlineData(WorkerFailureCategories.WorkerCrashed)]
    [InlineData(WorkerFailureCategories.ProtocolError)]
    [InlineData(WorkerFailureCategories.BindingConflict)]
    public async Task UncertainStop_SkipsEveryCompile(string category)
    {
        var tracker = new PlcSharedCompilationTracker();
        tracker.Record(Import("first", "PLC_A/Blocks/One"), Committed());
        tracker.Record(Import("second", "PLC_B/Blocks/Two"), Committed());
        var entries = await tracker.CompileAsync(_ => throw new InvalidOperationException("Unexpected compile."),
            Batch(Item("first"), Item("second", category), Item("third", status: OperationBatchStatus.Skipped)));

        Assert.Equal(2, entries.Count);
        Assert.All(entries, entry =>
        {
            Assert.Equal("skipped", entry.Stage);
            Assert.Null(entry.Report);
            Assert.Null(entry.Failure);
            Assert.Null(entry.Omission);
        });
    }

    [Fact]
    public async Task Compile_FirstNonSucceededItemDeterminesUncertainStop()
    {
        var tracker = new PlcSharedCompilationTracker();
        tracker.Record(Import("first"), Committed());
        var entries = await tracker.CompileAsync(_ => Task.FromResult(Report()), Batch(
            Item("first", WorkerFailureCategories.WorkerOperationFailed),
            Item("later", WorkerFailureCategories.PortalLost)));
        Assert.Equal("succeeded", Assert.Single(entries).Stage);
    }

    [Fact]
    public async Task Compile_SelectorsRunSequentially()
    {
        var tracker = new PlcSharedCompilationTracker();
        tracker.Record(Import("first", "PLC_A/Blocks/One"), Committed());
        tracker.Record(Import("second", "PLC_B/Blocks/Two"), Committed());
        var first = new TaskCompletionSource<WorkerCallResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<string?>();
        var compilation = tracker.CompileAsync(selector =>
        {
            calls.Add(selector);
            return calls.Count == 1 ? first.Task : Task.FromResult(Report());
        }, Batch());

        Assert.Equal(["PLC_A"], calls);
        Assert.False(compilation.IsCompleted);
        first.SetResult(Report());
        var entries = await compilation;
        Assert.Equal(["PLC_A", "PLC_B"], calls);
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public async Task NoCommits_ReturnsEmptyList()
        => Assert.Empty(await new PlcSharedCompilationTracker().CompileAsync(
            _ => throw new InvalidOperationException("Unexpected compile."), Batch()));

    private static PlcOperationRequest Import(string id, string blockPath = "PLC_A/Blocks/One",
        string operation = "update_block_logic", string? format = "source")
        => new() { OperationId = id, Operation = operation, BlockPath = blockPath, Format = format };

    private static WorkerCallResult Committed(WorkerCallResult? result = null)
        => (result ?? WorkerCallResult.Ok("{}")) with
        {
            BlockImportOutcome = new() { TargetMutationCommitted = true }
        };

    private static WorkerCallResult Report(string state = "Success", int errors = 0, int warnings = 0)
        => WorkerCallResult.Ok(WorkerJson.SerializePayload(new CompileCheckReport
        {
            OverallState = state is "Error" or "Warning" ? state : "Success",
            TotalErrorCount = errors,
            TotalWarningCount = warnings,
            Plcs = [new() { PlcName = "PLC_A", State = state, ErrorCount = errors, WarningCount = warnings }]
        }));

    private static async Task<PlcSharedCompilation> CompileOne(WorkerCallResult result)
    {
        var tracker = new PlcSharedCompilationTracker();
        tracker.Record(Import("first"), Committed());
        return Assert.Single(await tracker.CompileAsync(_ => Task.FromResult(result), Batch()));
    }

    private static StructuredOperationBatch Batch(params StructuredOperationItem[] items)
        => StructuredOperationBatch.FromItems(items);

    private static StructuredOperationItem Item(string id, string? category = null,
        string status = OperationBatchStatus.Succeeded)
        => new(id, "update_block_logic", category is null ? status : OperationBatchStatus.Failed, null,
            category is null ? null : new(category, "Import failed."), null, null, []);
}
