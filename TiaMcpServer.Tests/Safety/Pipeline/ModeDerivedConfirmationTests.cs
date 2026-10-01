using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety.Pipeline;
using Xunit;

namespace TiaMcpServer.Tests.Safety.Pipeline;

public sealed class ModeDerivedConfirmationTests
{
    [Fact]
    public async Task Full_NeverPrompts_AuditsPolicy()
    {
        var gate = new FakeWriteBindingGate { AccessMode = McpAccessMode.Full };
        var domain = new FakeWriteDomain(gate);
        var audit = new RecordingAuditSink(gate);
        var prompts = 0;
        var confirmation = new UserConfirmation(true, (_, _) =>
        {
            prompts++;
            return ValueTask.FromResult(new ElicitResult { Action = "decline" });
        }, TimeSpan.FromSeconds(1));
        var execution = new WriteExecution(gate, audit, FakeWriteDomain.Catalog, TimeProvider.System);
        var result = await execution.RunAsync(domain,
            new WriteCall<FakeWriteItem>(FakeWriteBindingGate.ProjectPath,
                [new("a", Guards: [FakeWriteDomain.AcknowledgeGuard])], false),
            new WriteConfirmationContext(confirmation));

        Assert.Equal(0, prompts);
        Assert.False(result.IsError == true);
        Assert.Equal(1, domain.MutationCount);
        Assert.Equal("policy", Assert.Single(Assert.Single(audit.Records).Guards).SatisfiedBy);
    }

    [Fact]
    public void Policy_FullIsPolicy_EveryOtherValueAsksUser()
    {
        Assert.Equal(ConfirmationMode.Policy, WriteConfirmationPolicy.For(McpAccessMode.Full));
        foreach (var mode in new[] { McpAccessMode.ReadOnly, McpAccessMode.ReadWrite, (McpAccessMode)(-1), (McpAccessMode)99 })
            Assert.Equal(ConfirmationMode.AskUser, WriteConfirmationPolicy.For(mode));
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public async Task ReadWrite_EveryCallDomain_PromptsOnceBeforeMutation(bool everyCall, int expectedPrompts)
    {
        var gate = new FakeWriteBindingGate { AccessMode = McpAccessMode.ReadWrite };
        var domain = new FakeWriteDomain(gate) { ConfirmsEveryCall = everyCall };
        var audit = new RecordingAuditSink(gate);
        var prompts = 0;
        var confirmation = new UserConfirmation(true, (request, _) =>
        {
            Assert.Equal(0, domain.MutationCount);
            Assert.True(gate.LeaseActive);
            Assert.Contains("delete target-a", request.Message);
            prompts++;
            return ValueTask.FromResult(Confirmed());
        }, TimeSpan.FromSeconds(1));
        var result = await new WriteExecution(gate, audit, FakeWriteDomain.Catalog, TimeProvider.System)
            .RunAsync(domain, new WriteCall<FakeWriteItem>(FakeWriteBindingGate.ProjectPath, [new("a")], false), new(confirmation));
        Assert.False(result.IsError == true);
        Assert.Equal(expectedPrompts, prompts);
        Assert.Equal(1, domain.MutationCount);
    }

    [Theory]
    [InlineData(UserConfirmationOutcomes.Declined)]
    [InlineData(UserConfirmationOutcomes.Cancelled)]
    [InlineData(UserConfirmationOutcomes.TimedOut)]
    [InlineData(UserConfirmationOutcomes.Unsupported)]
    [InlineData(UserConfirmationOutcomes.Failed)]
    public async Task ReadWrite_NonConfirmedOutcome_DeniesWithoutMutation(string outcome)
    {
        var gate = new FakeWriteBindingGate { AccessMode = McpAccessMode.ReadWrite };
        var domain = new FakeWriteDomain(gate) { ConfirmsEveryCall = true };
        var audit = new RecordingAuditSink(gate);
        var confirmation = new UserConfirmation(outcome != UserConfirmationOutcomes.Unsupported, async (_, ct) =>
        {
            if (outcome == UserConfirmationOutcomes.Failed) throw new IOException("private detail");
            if (outcome == UserConfirmationOutcomes.TimedOut) await Task.Delay(Timeout.Infinite, ct);
            return new ElicitResult { Action = outcome == UserConfirmationOutcomes.Declined ? "decline" : "cancel" };
        }, TimeSpan.FromMilliseconds(10));
        var result = await new WriteExecution(gate, audit, FakeWriteDomain.Catalog, TimeProvider.System)
            .RunAsync(domain, new WriteCall<FakeWriteItem>(FakeWriteBindingGate.ProjectPath, [new("a")], false), new(confirmation));
        Assert.True(result.IsError);
        Assert.Equal(0, domain.MutationCount);
        var record = Assert.Single(audit.Records);
        using var doc = JsonDocument.Parse(record.ResponseText);
        Assert.Equal(WorkerFailureCategories.AccessDenied, doc.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Equal(new WriteAuditConfirmation("user", outcome), record.Confirmation);
        Assert.DoesNotContain("private detail", record.ResponseText);
    }

    [Theory]
    [InlineData(McpAccessMode.ReadWrite, false, false, "user", "confirmed")]
    [InlineData(McpAccessMode.Full, false, false, "policy", "not_requested")]
    [InlineData(McpAccessMode.Full, true, false, "none", "not_requested")]
    [InlineData(McpAccessMode.ReadWrite, true, false, "none", "not_requested")]
    [InlineData(McpAccessMode.ReadWrite, false, true, "user", "declined")]
    public async Task AuditV2_RecordsConfirmation(McpAccessMode mode, bool dryRun, bool declined, string by, string outcome)
    {
        var gate = new FakeWriteBindingGate { AccessMode = mode };
        var domain = new FakeWriteDomain(gate) { ConfirmsEveryCall = true };
        var audit = new RecordingAuditSink(gate);
        var prompts = 0;
        var confirmation = new UserConfirmation(true, (_, _) =>
        {
            prompts++;
            return ValueTask.FromResult(declined ? new ElicitResult { Action = "decline" } : Confirmed());
        }, TimeSpan.FromSeconds(1));
        await new WriteExecution(gate, audit, FakeWriteDomain.Catalog, TimeProvider.System)
            .RunAsync(domain, new WriteCall<FakeWriteItem>(FakeWriteBindingGate.ProjectPath, [new("a", Guards: [FakeWriteDomain.AcknowledgeGuard])], dryRun), new(confirmation));
        var record = Assert.Single(audit.Records);
        Assert.Equal(2, record.RecordVersion);
        Assert.Equal(new WriteAuditConfirmation(by, outcome), record.Confirmation);
        Assert.Equal(by == "user" ? 1 : 0, prompts);
        Assert.Equal(dryRun || declined ? null : by, Assert.Single(record.Guards).SatisfiedBy);
    }

    [Fact]
    public async Task ConsequenceChangedWhilePending_RefusesStale()
    {
        var gate = new FakeWriteBindingGate { AccessMode = McpAccessMode.ReadWrite };
        var domain = new FakeWriteDomain(gate) { ConfirmsEveryCall = true };
        var audit = new RecordingAuditSink(gate);
        var confirmation = new UserConfirmation(true, (_, _) =>
        {
            domain.EffectChange = "new consequence";
            return ValueTask.FromResult(Confirmed());
        }, TimeSpan.FromSeconds(1));
        var result = await new WriteExecution(gate, audit, FakeWriteDomain.Catalog, TimeProvider.System)
            .RunAsync(domain, new WriteCall<FakeWriteItem>(FakeWriteBindingGate.ProjectPath, [new("a")], false), new(confirmation));
        Assert.True(result.IsError);
        Assert.Equal(0, domain.MutationCount);
        using var doc = JsonDocument.Parse(Assert.Single(audit.Records).ResponseText);
        Assert.Equal(WorkerFailureCategories.BindingConflict, doc.RootElement.GetProperty("error").GetProperty("category").GetString());
    }

    private static ElicitResult Confirmed() => new()
    {
        Action = "accept",
        Content = new Dictionary<string, JsonElement> { ["confirm"] = JsonSerializer.SerializeToElement(true) }
    };
}
