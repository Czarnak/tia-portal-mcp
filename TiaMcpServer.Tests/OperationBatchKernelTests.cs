using System.Text.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests;

public class OperationBatchKernelTests
{
    private sealed record Item(
        string OperationId,
        string Operation,
        string? ProjectPath = null) : IOperationBatchItem;

    [Fact]
    public async Task ExecuteReadsAsync_FailureDoesNotStopLaterItems()
    {
        var items = new[] { new Item("a", "first"), new Item("b", "second") };
        var invoked = new List<string>();

        var results = await OperationBatchExecutionEngine.ExecuteReadsAsync(
            items,
            item =>
            {
                invoked.Add(item.OperationId);
                return Task.FromResult(item.OperationId == "a"
                    ? WorkerCallResult.Fail("validation_error", "bad read")
                    : WorkerCallResult.Ok("{\"ok\":true}"));
            });

        Assert.Equal(new[] { "a", "b" }, invoked);
        Assert.Equal(OperationBatchStatus.Failed, results[0].Status);
        Assert.Equal(OperationBatchStatus.Succeeded, results[1].Status);
    }

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
    public void ReadFormatter_UsesCallerSuppliedToolAndKeepsResultAsString()
    {
        var json = OperationBatchResultFormatter.Read(
            "execute_read_batch",
            new[]
            {
                new OperationBatchResult(
                    "a",
                    "read_hardware_config",
                    OperationBatchStatus.Succeeded,
                    "{\"devices\":[]}")
            });

        using var document = JsonDocument.Parse(json);
        Assert.Equal("execute_read_batch", document.RootElement.GetProperty("tool").GetString());
        Assert.Equal(
            JsonValueKind.String,
            document.RootElement.GetProperty("operations")[0].GetProperty("result").ValueKind);
    }

    [Fact]
    public async Task ExecuteReadsAsync_AllSucceeded_PreservesOrderAndPayloads()
    {
        var items = new[] { new Item("a", "first"), new Item("b", "second") };

        var results = await OperationBatchExecutionEngine.ExecuteReadsAsync(
            items,
            item => Task.FromResult(WorkerCallResult.Ok($"payload-{item.OperationId}")));

        Assert.Equal(new[] { "a", "b" }, results.Select(result => result.OperationId));
        Assert.All(results, result => Assert.Equal(OperationBatchStatus.Succeeded, result.Status));
        Assert.Equal("payload-a", results[0].Result);
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
    public async Task ExecuteReadsAsync_ErrorPrefixedPayloadIsStillSucceeded()
    {
        var results = await OperationBatchExecutionEngine.ExecuteReadsAsync(
            new[] { new Item("a", "first") },
            _ => Task.FromResult(WorkerCallResult.Ok("Error: literal SCL comment text")));

        Assert.Equal(OperationBatchStatus.Succeeded, results[0].Status);
    }

    [Fact]
    public async Task ExecuteReadsAsync_CopiesWorkerWarnings()
    {
        var results = await OperationBatchExecutionEngine.ExecuteReadsAsync(
            new[] { new Item("a", "first") },
            _ => Task.FromResult(WorkerCallResult.Ok("[]", new[] { "Skipping device 'X'." })));

        Assert.Single(results[0].Warnings!);
    }

    [Fact]
    public void ErrorFormatter_ProducesUnsuccessfulEnvelopeWithMessage()
    {
        using var document = JsonDocument.Parse(OperationBatchResultFormatter.Error("execute_read_batch", "boom"));

        Assert.Equal("execute_read_batch", document.RootElement.GetProperty("tool").GetString());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("boom", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void ReadFormatter_ProjectsCountsWarningsAndOmissions()
    {
        var results = new[]
        {
            new OperationBatchResult("a", "first", OperationBatchStatus.Succeeded, "x", new[] { "warning" }),
            new OperationBatchResult("b", "second", OperationBatchStatus.Omitted, "[OMITTED]")
        };

        using var document = JsonDocument.Parse(OperationBatchResultFormatter.Read("execute_read_batch", results));
        var root = document.RootElement;

        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(1, root.GetProperty("succeeded").GetInt32());
        Assert.Equal(1, root.GetProperty("omitted").GetInt32());
        Assert.Equal("warning", root.GetProperty("operations")[0].GetProperty("warnings")[0].GetString());
    }

    [Fact]
    public void ReadFormatter_FailureClearsSuccess()
    {
        using var document = JsonDocument.Parse(OperationBatchResultFormatter.Read(
            "execute_read_batch",
            new[]
            {
                new OperationBatchResult("a", "first", OperationBatchStatus.Succeeded, "x"),
                new OperationBatchResult("b", "second", OperationBatchStatus.Failed, "Error: nope")
            }));

        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(1, document.RootElement.GetProperty("failed").GetInt32());
    }

    [Fact]
    public void ApplyFormatter_CountsSucceededFailedAndSkipped()
    {
        using var document = JsonDocument.Parse(OperationBatchResultFormatter.Apply(
            "apply_write_batch",
            new[]
            {
                new OperationBatchResult("a", "first", OperationBatchStatus.Succeeded, "ok"),
                new OperationBatchResult("b", "second", OperationBatchStatus.Failed, "Error: boom"),
                new OperationBatchResult("c", "third", OperationBatchStatus.Skipped, null)
            }));

        var root = document.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("apply_write_batch", root.GetProperty("tool").GetString());
        Assert.Equal(1, root.GetProperty("succeeded").GetInt32());
        Assert.Equal(1, root.GetProperty("failed").GetInt32());
        Assert.Equal(1, root.GetProperty("skipped").GetInt32());
    }
}
