using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Safety.Pipeline;
using Xunit;

namespace TiaMcpServer.Tests.Safety.Pipeline;

public sealed class WriteExecutionFailureAuditTests
{
    private const string Ack = FakeWriteDomain.AcknowledgeGuard;
    private readonly FakeWriteBindingGate _gate = new() { AccessMode = McpAccessMode.Full };
    private readonly FakeWriteDomain _domain;
    private readonly RecordingAuditSink _audit;
    private readonly WriteExecution _execution;

    public WriteExecutionFailureAuditTests()
    {
        _domain = new FakeWriteDomain(_gate);
        _audit = new RecordingAuditSink(_gate);
        _execution = new WriteExecution(
            _gate, _audit, FakeWriteDomain.Catalog, new SteppingTimeProvider(TimeSpan.FromMilliseconds(7)));
    }

    [Theory]
    [InlineData("mutate:b", true)]
    [InlineData("project:b", true)]
    [InlineData("replan:b", false)]
    public async Task InterruptedItem_PreservesPartialWriteEvidence(string throwOnCall, bool mutationAttempted)
    {
        _domain.ThrowOnCall = throwOnCall;
        var record = await RunThrowsAsync(new[]
        {
            new FakeWriteItem("a", Guards: new[] { Ack }),
            new FakeWriteItem("b", DependsOn: "a", LateGuards: new[] { Ack }),
            new FakeWriteItem("c")
        });

        Assert.Equal(new[] { "succeeded", "failed", "skipped" }, record.Items.Select(item => item.Status));
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, record.Items[1].FailureCategory);
        Assert.Contains("Scripted domain failure.", record.Items[1].FailureMessage);
        var warning = mutationAttempted
            ? WriteExecution.PartialWriteMessage : WriteExecution.PartialWriteNotMutatedMessage;
        Assert.Equal(warning, Assert.Single(record.Items[1].Warnings));
        var partial = Assert.Single(record.Guards, guard => guard.Id == "partial_write_no_rollback");
        Assert.Equal("b", partial.OperationId);
        Assert.Equal(warning, partial.Message);
        Assert.Null(partial.SatisfiedBy);
        var acknowledged = record.Guards.Where(guard => guard.Id == Ack).ToArray();
        Assert.Equal(mutationAttempted ? 2 : 1, acknowledged.Length);
        Assert.All(acknowledged,
            guard => Assert.Equal("policy", guard.SatisfiedBy));
        Assert.True(record.Items[0].DurationMs > 0);
        Assert.True(record.Items[1].DurationMs > 0);
        Assert.Null(record.Items[2].DurationMs);
        Assert.DoesNotContain("verify", _domain.Calls);
        Assert.DoesNotContain("compose", _domain.Calls);
        var response = AssertBatchMatchesAudit(record);
        Assert.Equal(warning, Assert.Single(response.GetProperty("warnings").EnumerateArray()).GetString());
    }

    [Theory]
    [InlineData("verify", false)]
    [InlineData("compose", false)]
    [InlineData("verify", true)]
    [InlineData("compose", true)]
    public async Task CompletedBatch_ExceptionPreservesOutcomesAndAcknowledgements(
        string throwOnCall, bool itemFailed)
    {
        _domain.ThrowOnCall = throwOnCall;
        var record = await RunThrowsAsync(new[]
        {
            new FakeWriteItem("a", Guards: new[] { Ack }),
            new FakeWriteItem("b", FailWith: itemFailed ? WorkerFailureCategories.WorkerOperationFailed : null),
            new FakeWriteItem("c")
        });

        Assert.Equal(itemFailed ? new[] { "succeeded", "failed", "skipped" }
            : new[] { "succeeded", "succeeded", "succeeded" }, record.Items.Select(item => item.Status));
        Assert.Equal("policy", Assert.Single(record.Guards, guard => guard.Id == Ack).SatisfiedBy);
        if (itemFailed)
        {
            Assert.Single(record.Guards, guard => guard.Id == "partial_write_no_rollback");
            Assert.Equal(WriteExecution.PartialWriteMessage, Assert.Single(record.Items[1].Warnings));
        }
        else
        {
            Assert.DoesNotContain(record.Guards, guard => guard.Id == "partial_write_no_rollback");
            Assert.All(record.Items, item => Assert.Empty(item.Warnings));
        }

        AssertBatchMatchesAudit(record);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task FirstMutationException_PreservesTheSingleItemWarningRule(int itemCount)
    {
        _domain.ThrowOnCall = "mutate:a";
        var items = new[]
        {
            new FakeWriteItem("a", Guards: new[] { Ack }), new FakeWriteItem("b"), new FakeWriteItem("c")
        }.Take(itemCount).ToArray();
        var record = await RunThrowsAsync(items);

        Assert.Equal("failed", record.Items[0].Status);
        Assert.All(record.Items.Skip(1), item => Assert.Equal("skipped", item.Status));
        Assert.Equal("policy", Assert.Single(record.Guards, guard => guard.Id == Ack).SatisfiedBy);
        if (itemCount == 1)
        {
            Assert.DoesNotContain(record.Guards, guard => guard.Id == "partial_write_no_rollback");
            Assert.Empty(record.Items[0].Warnings);
        }
        else
        {
            Assert.Equal(WriteExecution.PartialWriteMessage, Assert.Single(record.Items[0].Warnings));
            Assert.Single(record.Guards, guard => guard.Id == "partial_write_no_rollback");
        }

        AssertBatchMatchesAudit(record);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ComposeException_BeforeLiveApply_DoesNotRecordGuardSatisfaction(bool dryRun)
    {
        _domain.ThrowOnCall = "compose";
        var guards = dryRun ? new[] { Ack } : new[] { Ack, FakeWriteDomain.BlockGuard };
        var record = await RunThrowsAsync(new[] { new FakeWriteItem("a", Guards: guards) }, dryRun);

        var guard = Assert.Single(record.Guards, guard => guard.Id == Ack);
        Assert.True(guard.Acknowledged);
        Assert.Null(guard.SatisfiedBy);
        Assert.Equal(0, _domain.MutationCount);
        Assert.Equal("skipped", Assert.Single(record.Items).Status);
        using var document = JsonDocument.Parse(record.ResponseText);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("batch").ValueKind);
    }

    private async Task<WriteAuditRecord> RunThrowsAsync(IReadOnlyList<FakeWriteItem> items, bool dryRun = false)
    {
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => _execution.RunAsync(
            _domain, new WriteCall<FakeWriteItem>(FakeWriteBindingGate.ProjectPath, items, dryRun)));

        Assert.Same(_domain.ScriptedException, thrown);
        var record = Assert.Single(_audit.Records);
        Assert.Equal("error", record.Phase);
        Assert.True(Assert.Single(_audit.LeaseActiveAtAppend));
        Assert.Equal("sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(record.ResponseText))),
            record.ResponseHash);
        return record;
    }

    private static JsonElement AssertBatchMatchesAudit(WriteAuditRecord record)
    {
        using var document = JsonDocument.Parse(record.ResponseText);
        var response = document.RootElement;
        var operations = response.GetProperty("batch").GetProperty("operations").EnumerateArray().ToArray();
        Assert.Equal(record.Items.Count, operations.Length);
        for (var index = 0; index < operations.Length; index++)
        {
            var item = record.Items[index];
            var operation = operations[index];
            Assert.Equal(item.OperationId, operation.GetProperty("operationId").GetString());
            Assert.Equal(item.Status, operation.GetProperty("status").GetString());
            Assert.Equal(item.Warnings, operation.GetProperty("warnings").EnumerateArray().Select(value => value.GetString()));
        }

        Assert.Equal(record.Guards.Select(guard => guard.Id),
            response.GetProperty("guards").EnumerateArray().Select(guard => guard.GetProperty("id").GetString()));
        return response.Clone();
    }
}
