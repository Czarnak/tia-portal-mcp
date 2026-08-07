using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.OperationBatches;

/// <summary>
/// Semantics of the structured batch records and of the domain-free read execution engine: reads
/// are independent, order is data, and every inapplicable field is published as an explicit null.
/// </summary>
public class StructuredOperationBatchTests
{
    private sealed record TestOperation(string OperationId, string Operation, string? ProjectPath = null)
        : IOperationBatchItem;

    private static StructuredOperationItem Succeeded(
        TestOperation operation,
        JsonElement? result,
        IReadOnlyList<string> warnings)
        => new(
            operation.OperationId,
            operation.Operation,
            OperationBatchStatus.Succeeded,
            result,
            Failure: null,
            Omission: null,
            SkipReason: null,
            warnings);

    private static StructuredOperationItem Failed(
        TestOperation operation,
        string category,
        string message,
        IReadOnlyList<string> warnings)
        => new(
            operation.OperationId,
            operation.Operation,
            OperationBatchStatus.Failed,
            Result: null,
            new StructuredOperationFailure(category, message),
            Omission: null,
            SkipReason: null,
            warnings);

    /// <summary>
    /// Projects the way a real domain contract does: a worker failure fails the item, a successful
    /// payload that does not parse fails it as <c>protocol_error</c>, anything else succeeds.
    /// </summary>
    private static StructuredOperationItem ProjectLikeAContract(
        TestOperation operation,
        WorkerCallResult workerResult)
    {
        if (!workerResult.Success)
        {
            return Failed(
                operation,
                workerResult.FailureCategory!,
                workerResult.Error!,
                workerResult.Warnings);
        }

        try
        {
            return Succeeded(
                operation,
                CanonicalJson.Normalize<Dictionary<string, string>>(workerResult.Payload).Element,
                workerResult.Warnings);
        }
        catch (JsonException)
        {
            return Failed(
                operation,
                WorkerFailureCategories.ProtocolError,
                "payload rejected",
                workerResult.Warnings);
        }
    }

    [Fact]
    public async Task ExecuteReadsAsync_ContinuesAfterWorkerAndProtocolFailuresAndPreservesOrder()
    {
        var operations = new[]
        {
            new TestOperation("worker-failure", "read_a"),
            new TestOperation("protocol-failure", "read_b"),
            new TestOperation("good", "read_c"),
        };

        var invoked = new List<string>();

        var batch = await StructuredOperationBatchExecutionEngine.ExecuteReadsAsync(
            operations,
            operation =>
            {
                invoked.Add(operation.OperationId);
                return Task.FromResult(operation.OperationId switch
                {
                    "worker-failure" => WorkerCallResult.Fail(
                        WorkerFailureCategories.WorkerOperationFailed,
                        "boom"),
                    "protocol-failure" => WorkerCallResult.Ok("""{"value":[1]}"""),
                    _ => WorkerCallResult.Ok("""{"value":"ok"}"""),
                });
            },
            ProjectLikeAContract);

        // Every read is attempted: one bad operation must not hide the ones behind it.
        Assert.Equal(new[] { "worker-failure", "protocol-failure", "good" }, invoked);
        Assert.Equal(
            new[] { "worker-failure", "protocol-failure", "good" },
            batch.Operations.Select(item => item.OperationId).ToArray());
        Assert.Equal(
            new[] { "read_a", "read_b", "read_c" },
            batch.Operations.Select(item => item.Operation).ToArray());
        Assert.Equal(
            WorkerFailureCategories.WorkerOperationFailed,
            batch.Operations[0].Failure!.Category);
        Assert.Equal(WorkerFailureCategories.ProtocolError, batch.Operations[1].Failure!.Category);
        Assert.Equal(OperationBatchStatus.Succeeded, batch.Operations[2].Status);

        Assert.Equal(3, batch.OperationCount);
        Assert.Equal(new StructuredOperationCounts(1, 2, 0, 0), batch.Counts);
        Assert.Null(batch.Truncation);
        Assert.False(batch.IsFullySuccessful);
    }

    [Fact]
    public async Task ExecuteReadsAsync_KeepsEachWorkerWarningOnTheItemThatProducedIt()
    {
        var operations = new[]
        {
            new TestOperation("first", "read_a"),
            new TestOperation("second", "read_b"),
            new TestOperation("third", "read_c"),
        };

        var batch = await StructuredOperationBatchExecutionEngine.ExecuteReadsAsync(
            operations,
            operation => Task.FromResult(operation.OperationId switch
            {
                "second" => WorkerCallResult.Ok("""{"value":"ok"}""", new[] { "degraded-second" }),
                "third" => WorkerCallResult.Fail(
                    WorkerFailureCategories.WorkerOperationFailed,
                    "boom",
                    new[] { "degraded-third" }),
                _ => WorkerCallResult.Ok("""{"value":"ok"}"""),
            }),
            ProjectLikeAContract);

        Assert.Empty(batch.Operations[0].Warnings);
        Assert.Equal(new[] { "degraded-second" }, batch.Operations[1].Warnings);
        Assert.Equal(new[] { "degraded-third" }, batch.Operations[2].Warnings);
    }

    [Fact]
    public async Task ExecuteReadsAsync_TreatsASuccessfulJsonNullAsSucceededWithANullResult()
    {
        var operations = new[] { new TestOperation("nullable", "read_a") };

        var batch = await StructuredOperationBatchExecutionEngine.ExecuteReadsAsync(
            operations,
            _ => Task.FromResult(WorkerCallResult.Ok("null")),
            (operation, workerResult) => Succeeded(
                operation,
                CanonicalJson.ToElement<string?>(null),
                workerResult.Warnings));

        var item = Assert.Single(batch.Operations);
        Assert.Equal(OperationBatchStatus.Succeeded, item.Status);

        // A JSON null is a value the operation legitimately produced, not an absent result: it
        // must not be reclassified as failed or omitted.
        Assert.Equal(JsonValueKind.Null, item.Result!.Value.ValueKind);
        Assert.Equal(new StructuredOperationCounts(1, 0, 0, 0), batch.Counts);
        Assert.True(batch.IsFullySuccessful);
    }

    [Fact]
    public void StructuredOperationItem_PublishesEveryInapplicableFieldAsAnExplicitNull()
    {
        var succeeded = Succeeded(
            new TestOperation("good", "read_a"),
            CanonicalJson.ToElement(new { value = "ok" }),
            Array.Empty<string>());

        var text = CanonicalJson.Serialize(StructuredOperationBatch.FromItems(new[] { succeeded }));

        // Absent and null must never be indistinguishable to a schema consumer, so failure,
        // omission and skipReason are all present and null on a succeeded item.
        Assert.Equal(
            """{"counts":{"failed":0,"omitted":0,"skipped":0,"succeeded":1},"operationCount":1,"operations":[{"failure":null,"omission":null,"operation":"read_a","operationId":"good","result":{"value":"ok"},"skipReason":null,"status":"succeeded","warnings":[]}],"truncation":null}""",
            text);
    }

    [Fact]
    public void FromItems_DerivesCountsFromTheFinalItemStatuses()
    {
        var operation = new TestOperation("op", "read_a");
        var items = new[]
        {
            Succeeded(operation, CanonicalJson.ToElement(new { value = "ok" }), Array.Empty<string>()),
            Failed(operation, WorkerFailureCategories.WorkerOperationFailed, "boom", Array.Empty<string>()),
            new StructuredOperationItem(
                "omitted",
                "read_a",
                OperationBatchStatus.Omitted,
                Result: null,
                Failure: null,
                new StructuredOperationOmission("reason", 10, 20, "network_read", "guidance"),
                SkipReason: null,
                Array.Empty<string>()),
            new StructuredOperationItem(
                "skipped",
                "read_a",
                OperationBatchStatus.Skipped,
                Result: null,
                Failure: null,
                Omission: null,
                StructuredOperationSkipReasons.EarlierOperationFailed,
                Array.Empty<string>()),
        };

        var batch = StructuredOperationBatch.FromItems(items);

        Assert.Equal(new StructuredOperationCounts(1, 1, 1, 1), batch.Counts);
        Assert.Equal(4, batch.OperationCount);
        Assert.False(batch.IsFullySuccessful);
    }
}
