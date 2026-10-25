using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Tools;

public class BatchReadToolTests
{
    private sealed record Read(string OperationId, string Operation = "read") : IOperationBatchItem;

    private static OpennessWorkerClient Client(bool bound = true, McpAccessMode mode = McpAccessMode.ReadOnly)
    {
        var binding = new ProjectSessionBinding(null);
        if (bound)
        {
            Assert.True(binding.BindVerified(new WorkerSessionIdentity
            {
                WorkerSessionId = "batch-read", SessionGeneration = 1, PortalProcessId = 4242,
                ProjectPath = @"C:\Projects\Fixture.ap21",
            }, forceRebind: false, out var error), error);
        }
        return new(binding, workerExecutablePath: "never-started.exe", accessPolicy: new OperationAccessPolicy(mode));
    }

    private static JsonElement Root(CallToolResult result) => Assert.IsType<JsonElement>(result.StructuredContent);

    private static StructuredOperationItem Item(Read operation, string? failure = null)
        => new(operation.OperationId, operation.Operation,
            failure is null ? OperationBatchStatus.Succeeded : OperationBatchStatus.Failed,
            failure is null ? JsonSerializer.SerializeToElement(new { value = operation.OperationId }) : null,
            failure is null ? null : new StructuredOperationFailure(failure, "failed"),
            null, null, []);

    private static Task<CallToolResult> Run(
        OpennessWorkerClient client, Func<Read, Task<StructuredOperationItem>> execute,
        CancellationToken cancellationToken = default)
        => BatchReadTool.RunAsync(client, new[] { new Read("a"), new Read("b"), new Read("c") },
            "test_read", "1.0", _ => OperationValidation.Valid, (_, _) => [], execute, _ => "retry", cancellationToken);

    [Fact]
    public async Task ValidationDenialPrecedesAccessBindingAndCancellation()
    {
        using var client = Client(bound: false);
        var result = await BatchReadTool.RunAsync<Read>(client, null, "test_read", "1.0",
            operations =>
            {
                Assert.Null(operations);
                return OperationValidation.Invalid(WorkerFailureCategories.ValidationError, "invalid");
            },
            (_, _) => throw new InvalidOperationException("access must not run"),
            _ => throw new InvalidOperationException("execution must not run"),
            _ => "retry", new CancellationToken(true));

        Assert.True(result.IsError);
        Assert.Equal(WorkerFailureCategories.ValidationError, Root(result).GetProperty("error").GetProperty("category").GetString());
        Assert.Equal(JsonValueKind.Null, Root(result).GetProperty("batch").ValueKind);
    }

    [Theory]
    [InlineData(McpAccessMode.ReadOnly)]
    [InlineData(McpAccessMode.ReadWrite)]
    [InlineData(McpAccessMode.Full)]
    public async Task AccessDenialUsesEffectiveModeAndPrecedesBinding(McpAccessMode mode)
    {
        using var client = Client(bound: false, mode);
        var result = await BatchReadTool.RunAsync(client, new[] { new Read("a") }, "test_read", "1.0",
            _ => OperationValidation.Valid,
            (_, observed) =>
            {
                Assert.Equal(mode, observed);
                return ["first", "second"];
            },
            _ => throw new InvalidOperationException("execution must not run"), _ => "retry");

        Assert.True(result.IsError);
        Assert.Equal(WorkerFailureCategories.AccessDenied, Root(result).GetProperty("error").GetProperty("category").GetString());
        Assert.Equal("first\nsecond", Root(result).GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task UnboundReadRejectsBeforeExecutionAndCancellation()
    {
        using var client = Client(bound: false);
        var result = await Run(client, _ => throw new InvalidOperationException("execution must not run"), new CancellationToken(true));
        Assert.True(result.IsError);
        Assert.Equal(WorkerFailureCategories.BindingConflict, Root(result).GetProperty("error").GetProperty("category").GetString());
    }

    [Fact]
    public async Task FailedItemsRemainOrderedAndDoNotMakeAnMcpError()
    {
        using var client = Client();
        var invoked = new List<string>();
        var result = await Run(client, operation =>
        {
            invoked.Add(operation.OperationId);
            return Task.FromResult(Item(operation, operation.OperationId switch
            {
                "a" => WorkerFailureCategories.WorkerOperationFailed,
                "b" => WorkerFailureCategories.ProtocolError,
                _ => null,
            }));
        });

        Assert.Equal(new[] { "a", "b", "c" }, invoked);
        Assert.False(result.IsError);
        var root = Root(result);
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        Assert.Equal(2, root.GetProperty("batch").GetProperty("counts").GetProperty("failed").GetInt32());
        Assert.Equal(1, root.GetProperty("batch").GetProperty("counts").GetProperty("succeeded").GetInt32());
        Assert.Equal(invoked, root.GetProperty("batch").GetProperty("operations").EnumerateArray().Select(item => item.GetProperty("operationId").GetString()));
        Assert.Equal(Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text, CanonicalJson.Serialize(root));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationStopsBeforeTheNextItem(bool beforeFirst)
    {
        using var client = Client();
        using var cancellation = new CancellationTokenSource();
        if (beforeFirst) cancellation.Cancel();
        var invoked = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(client, operation =>
        {
            invoked++;
            cancellation.Cancel();
            return Task.FromResult(Item(operation));
        }, cancellation.Token));
        Assert.Equal(beforeFirst ? 0 : 1, invoked);
    }

    [Fact]
    public async Task UnexpectedExecutionExceptionPropagates()
    {
        using var client = Client();
        var error = new InvalidOperationException("unexpected");
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => Run(client, _ => throw error)));
    }

    [Fact]
    public void BudgetUsesExplicitItemLimitAndKeepsGuidance()
    {
        var batch = StructuredOperationBatch.FromItems([Item(new Read("quoted\"Ż")) with
        {
            Result = JsonSerializer.SerializeToElement(new string('x', 1_000)),
        }]);
        var bounded = BatchReadTool.ApplyBudget(batch, "test_read", "1.0", _ => "narrow", 500, 4_000);
        var omitted = Assert.Single(bounded.Operations);
        Assert.Equal(OperationBatchStatus.Omitted, omitted.Status);
        Assert.Null(omitted.Result);
        Assert.Equal(500, omitted.Omission!.LimitChars);
        Assert.Equal("test_read", omitted.Omission.RetryTool);
        Assert.Equal("narrow", omitted.Omission.Guidance);
        Assert.Equal(OperationBatchStatus.Succeeded, batch.Operations[0].Status);
    }

    [Fact]
    public void BudgetMeasuresExactEnvelopePreservesFailureAndUsesStableTies()
    {
        var batch = StructuredOperationBatch.FromItems([
            Item(new Read("first\"Ż")) with { Result = JsonSerializer.SerializeToElement(new string('x', 1_000)) },
            Item(new Read("second")) with { Result = JsonSerializer.SerializeToElement(new string('x', 1_000)) },
            Item(new Read("failure"), WorkerFailureCategories.ProtocolError) with
            {
                Failure = new(WorkerFailureCategories.ProtocolError, new string('f', 2_000)),
                Warnings = [new string('w', 2_000)],
            },
        ]);
        var original = CanonicalJson.Serialize(batch);
        var bounded = BatchReadTool.ApplyBudget(batch, "test_read", "1.0", _ => "retry", 6_000, 2_600);
        var document = CanonicalJson.Serialize(new OperationBatchReadResponse("test_read", "1.0",
            bounded.IsFullySuccessful, null, [], bounded));
        Assert.True(document.Length <= 2_600);
        Assert.Equal(document.Length, bounded.Truncation!.PresentedChars);
        Assert.Equal(new[] { "first\"Ż", "second" }, bounded.Truncation.AffectedOperationIds.Take(2));
        Assert.Equal(WorkerFailureCategories.ProtocolError, bounded.Operations[2].Failure!.Category);
        Assert.Empty(bounded.Operations[2].Warnings);
        Assert.Equal(original, CanonicalJson.Serialize(batch));
    }
}
