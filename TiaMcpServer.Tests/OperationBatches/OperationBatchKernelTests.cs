using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.OperationBatches;

public class OperationBatchKernelTests
{
    private sealed record Item(
        string OperationId,
        string Operation,
        string? ProjectPath = null) : IOperationBatchItem;

    [Fact]
    public async Task ApplyWritesAsync_StopsAndMarksLaterItemsSkipped()
    {
        var items = new[]
        {
            new Item("a", "first"),
            new Item("b", "second"),
            new Item("c", "third")
        };

        var results = await OperationBatchExecutionEngine.ApplyWritesAsync(
            items,
            item => Task.FromResult(item.OperationId == "b"
                ? WorkerCallResult.Fail("worker_operation_failed", "boom")
                : WorkerCallResult.Ok("{}")));

        Assert.Equal(
            new[]
            {
                OperationBatchStatus.Succeeded,
                OperationBatchStatus.Failed,
                OperationBatchStatus.Skipped
            },
            results.Select(result => result.Status));
        Assert.Null(results[0].FailureCategory);
        Assert.Equal("worker_operation_failed", results[1].FailureCategory);
        Assert.Null(results[2].FailureCategory);
    }

    [Fact]
    public void StateComposer_IsOrderedAndResolvesOneNormalizedPath()
    {
        var states = new[]
        {
            new OperationBatchCurrentState("a", "first", "one"),
            new OperationBatchCurrentState("b", "second", "two")
        };
        var items = new[]
        {
            new Item("a", "first", @"C:\Projects\Line.ap21"),
            new Item("b", "second")
        };

        Assert.Equal(
            "a::first\none\n--- batch item ---\nb::second\ntwo",
            OperationBatchStateComposer.CombineCurrentState(states));
        Assert.Equal(
            @"C:\Projects\Line.ap21",
            OperationBatchStateComposer.ResolveProjectPath(items));
    }

    [Fact]
    public async Task ApplyWritesAsync_AllSucceeded_MarksEveryItemSucceeded()
    {
        var results = await OperationBatchExecutionEngine.ApplyWritesAsync(
            new[] { new Item("a", "first"), new Item("b", "second") },
            _ => Task.FromResult(WorkerCallResult.Ok("done")));

        Assert.All(results, result => Assert.Equal(OperationBatchStatus.Succeeded, result.Status));
    }

    [Fact]
    public async Task ApplyWritesAsync_DoesNotInvokeItemsAfterFailure()
    {
        var invoked = new List<string>();
        var results = await OperationBatchExecutionEngine.ApplyWritesAsync(
            new[] { new Item("a", "first"), new Item("b", "second"), new Item("c", "third") },
            item =>
            {
                invoked.Add(item.OperationId);
                return Task.FromResult(item.OperationId == "b"
                    ? WorkerCallResult.Fail("worker_operation_failed", "boom")
                    : WorkerCallResult.Ok("done"));
            });

        Assert.Equal(new[] { "a", "b" }, invoked);
        Assert.Equal(OperationBatchStatus.Skipped, results[2].Status);
        Assert.Contains("boom", results[1].Result);
    }

    [Fact]
    public async Task ApplyWritesAsync_ErrorPrefixedPayloadIsStillSucceeded()
    {
        var results = await OperationBatchExecutionEngine.ApplyWritesAsync(
            new[] { new Item("a", "first") },
            _ => Task.FromResult(WorkerCallResult.Ok("Error: literal SCL comment text")));

        Assert.Equal(OperationBatchStatus.Succeeded, results[0].Status);
        Assert.Null(results[0].FailureCategory);
    }

    [Fact]
    public async Task ApplyWritesAsync_CopiesWorkerWarnings()
    {
        var results = await OperationBatchExecutionEngine.ApplyWritesAsync(
            new[] { new Item("a", "first") },
            _ => Task.FromResult(WorkerCallResult.Ok("[]", new[] { "Skipping device 'X'." })));

        Assert.Single(results[0].Warnings!);
    }

    [Fact]
    public void ErrorFormatter_ProducesUnsuccessfulEnvelopeWithMessage()
    {
        using var document = JsonDocument.Parse(OperationBatchResultFormatter.Error("preview_write_batch", "boom"));

        Assert.Equal("preview_write_batch", document.RootElement.GetProperty("tool").GetString());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("boom", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void ApplyFormatter_CountsSucceededFailedAndSkipped()
    {
        using var document = JsonDocument.Parse(OperationBatchResultFormatter.Apply(
            "apply_write_batch",
            new[]
            {
                new OperationBatchResult("a", "first", OperationBatchStatus.Succeeded, "ok"),
                new OperationBatchResult(
                    "b", "second", OperationBatchStatus.Failed, "Error: boom",
                    FailureCategory: WorkerFailureCategories.WorkerOperationFailed),
                new OperationBatchResult("c", "third", OperationBatchStatus.Skipped, null)
            }));

        var root = document.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("apply_write_batch", root.GetProperty("tool").GetString());
        Assert.Equal(1, root.GetProperty("succeeded").GetInt32());
        Assert.Equal(1, root.GetProperty("failed").GetInt32());
        Assert.Equal(1, root.GetProperty("skipped").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("operations")[0].GetProperty("failureCategory").ValueKind);
        Assert.Equal("worker_operation_failed", root.GetProperty("operations")[1].GetProperty("failureCategory").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("operations")[2].GetProperty("failureCategory").ValueKind);
    }

    [Fact]
    public void Formatter_UnrelatedOperation_RemainsByteIdentical()
    {
        var json = OperationBatchResultFormatter.Apply("apply_write_batch",
            new[] { new OperationBatchResult("a", "set_tag", OperationBatchStatus.Succeeded, "ok") });
        Assert.Equal(
            """{"tool":"apply_write_batch","success":true,"operationCount":1,"succeeded":1,"failed":0,"skipped":0,"operations":[{"operationId":"a","operation":"set_tag","status":"succeeded","result":"ok","warnings":null,"failureCategory":null}]}""",
            json);
    }

    [Fact]
    public async Task ApplyWritesAsync_PropagatesTypedFailureAndSkipsLaterItem()
    {
        var outcome = new BlockImportOutcomeInfo
        {
            ImportStage = "completed", ImportResultState = "success",
            TargetMutationCommitted = true, CompileStage = "failed",
            CompileReport = new CompileCheckReport { OverallState = "Error", TotalErrorCount = 1 },
            FinalReadStage = "unavailable", TemporarySourceState = "not_applicable",
            ContentRelation = "unknown"
        };
        var invoked = new List<string>();
        var results = await OperationBatchExecutionEngine.ApplyWritesAsync(
            new[] { new Item("a", "update_block_logic"), new Item("b", "set_tag") },
            item =>
            {
                invoked.Add(item.OperationId);
                return Task.FromResult(WorkerCallResult.Fail(
                    WorkerFailureCategories.PostconditionFailed, "compile failed",
                    new[] { "bounded warning" }) with { BlockImportOutcome = outcome });
            });
        var json = OperationBatchResultFormatter.Apply("apply_write_batch", results);
        using var document = JsonDocument.Parse(json);
        var operations = document.RootElement.GetProperty("operations");
        Assert.Equal(new[] { "a" }, invoked);
        Assert.Equal("failed", operations[0].GetProperty("status").GetString());
        Assert.Equal("Error: compile failed", operations[0].GetProperty("result").GetString());
        Assert.Equal("postcondition_failed", operations[0].GetProperty("failureCategory").GetString());
        Assert.Equal("bounded warning", operations[0].GetProperty("warnings")[0].GetString());
        Assert.Equal("completed", operations[0].GetProperty("blockImportOutcome")
            .GetProperty("importStage").GetString());
        Assert.Equal("skipped", operations[1].GetProperty("status").GetString());
        Assert.False(operations[1].TryGetProperty("blockImportOutcome", out _));
    }
}
