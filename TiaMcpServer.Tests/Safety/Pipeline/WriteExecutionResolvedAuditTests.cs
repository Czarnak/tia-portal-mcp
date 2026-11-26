using System.Text.Json;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Safety.Pipeline;

public sealed class WriteExecutionResolvedAuditTests
{
    private readonly FakeWriteBindingGate _gate = new() { AccessMode = McpAccessMode.Full };
    private readonly FakeWriteDomain _domain;
    private readonly RecordingAuditSink _audit;
    private readonly WriteExecution _execution;

    public WriteExecutionResolvedAuditTests()
    {
        _domain = new FakeWriteDomain(_gate) { ResolvedOperationOnMutation = item => ResolvedInput(item) };
        _audit = new RecordingAuditSink(_gate);
        _execution = new WriteExecution(_gate, _audit, FakeWriteDomain.Catalog, TimeProvider.System);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(WorkerFailureCategories.EngineeringRejected, false)]
    [InlineData(WorkerFailureCategories.WorkerOperationFailed, false)]
    [InlineData(WorkerFailureCategories.BindingConflict, true)]
    public async Task AttemptedInput_RemainsPresentRegardlessOfDispatchOrOutcome(string? failure, bool notSent)
    {
        _domain.MutationResultOverride = _ => failure is null ? WorkerCallResult.Ok("{\"done\":true}")
            : WorkerCallResult.Fail(failure, "Attempt failed.") with
            { DispatchState = notSent ? WorkerDispatchState.NotSent : WorkerDispatchState.Sent };
        var requested = new FakeWriteItem("patch", Operation: "patch_block_logic");

        await _execution.RunAsync(_domain, new WriteCall<FakeWriteItem>([requested], false));

        var record = Assert.Single(_audit.Records);
        Assert.Equal("patch_block_logic", record.RequestedOperations!.Value[0].GetProperty("operation").GetString());
        Assert.Equal(ResolvedInput(requested).GetRawText(), Assert.Single(record.Items).ResolvedOperation!.Value.GetRawText());
        Assert.Equal(2, record.RecordVersion);
    }

    [Theory]
    [InlineData("mutate:b", true)]
    [InlineData("project:b", true)]
    [InlineData("replan:b", false)]
    [InlineData("verify", true)]
    [InlineData("compose", true)]
    public async Task UnexpectedFailure_PreservesEarlierAndCurrentAttemptedInput(string throwOnCall, bool secondAttempted)
    {
        _domain.ThrowOnCall = throwOnCall;
        var first = new FakeWriteItem("a");
        var second = new FakeWriteItem("b", DependsOn: "a");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _execution.RunAsync(
            _domain, new WriteCall<FakeWriteItem>([first, second], false)));

        var record = Assert.Single(_audit.Records);
        Assert.Equal(ResolvedInput(first).GetRawText(), record.Items[0].ResolvedOperation!.Value.GetRawText());
        Assert.Equal(secondAttempted, record.Items[1].ResolvedOperation.HasValue);
        if (secondAttempted)
            Assert.Equal(ResolvedInput(second).GetRawText(), record.Items[1].ResolvedOperation!.Value.GetRawText());
    }

    [Theory]
    [InlineData("preview")]
    [InlineData("plan-error")]
    [InlineData("blocked")]
    [InlineData("skipped")]
    [InlineData("replan-error")]
    public async Task UnattemptedInput_IsAbsent(string scenario)
    {
        if (scenario == "plan-error")
            _domain.PlanFailure = new WriteToolError(WorkerFailureCategories.ValidationError, "Missing patch anchor.");
        if (scenario == "replan-error")
            _domain.ReplanFailure = new WriteToolError(WorkerFailureCategories.StateChanged, "Changed patch base.");
        var patch = new FakeWriteItem("patch", Operation: "patch_block_logic",
            DependsOn: scenario == "replan-error" ? "first" : null,
            Guards: scenario == "blocked" ? [FakeWriteDomain.BlockGuard] : null);
        var items = scenario is "skipped" or "replan-error"
            ? new[] { new FakeWriteItem("first", FailWith: scenario == "skipped" ? WorkerFailureCategories.WorkerOperationFailed : null), patch }
            : [patch];

        await _execution.RunAsync(_domain, new WriteCall<FakeWriteItem>(items, scenario == "preview"));

        var record = Assert.Single(_audit.Records);
        var item = Assert.Single(record.Items, item => item.OperationId == "patch");
        Assert.Null(item.ResolvedOperation);
        var serialized = CanonicalJson.ToElement(item);
        Assert.False(serialized.TryGetProperty("resolvedOperation", out _));
    }

    [Fact]
    public async Task DomainWithoutResolvedInput_OmitsTheConditionalAuditField()
    {
        _domain.ResolvedOperationOnMutation = null;

        await _execution.RunAsync(_domain, new WriteCall<FakeWriteItem>([new FakeWriteItem("normal")], false));

        var item = Assert.Single(Assert.Single(_audit.Records).Items);
        Assert.Null(item.ResolvedOperation);
        Assert.False(CanonicalJson.ToElement(item).TryGetProperty("resolvedOperation", out _));
    }

    private static JsonElement ResolvedInput(FakeWriteItem item)
        => CanonicalJson.ToElement(new
        {
            operationId = item.OperationId, operation = "update_block_logic", blockPath = "PLC/Blocks/Block",
            format = "xml", expectedContentHash = "xml:sha256:original", content = "<Block>\r\nchanged\r\n</Block>\n"
        });
}
