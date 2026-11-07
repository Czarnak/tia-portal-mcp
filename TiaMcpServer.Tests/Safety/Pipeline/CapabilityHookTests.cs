using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Safety.Pipeline;

public sealed class CapabilityHookTests
{
    [Fact]
    public async Task CapabilityHook_FirstUnsupportedOperation_RefusesBeforePlan()
    {
        var gate = LocalSessionGate();
        var domain = new FakeWriteDomain(gate)
        {
            CapabilityOperationsOverride = items => items.Select(item => item.Operation)
        };
        var audit = new RecordingAuditSink(gate);
        var result = await new WriteExecution(gate, audit, FakeWriteDomain.Catalog, TimeProvider.System)
            .RunAsync(domain, new WriteCall<FakeWriteItem>(
                [new("a", "plc_write"), new("b", "close_project"), new("c", "archive_project")], false));

        Assert.True(result.IsError);
        var error = CanonicalToolDocument.Read(result).GetProperty("error");
        Assert.Equal(WorkerFailureCategories.UnsupportedCapability, error.GetProperty("category").GetString());
        Assert.Contains("close_project", error.GetProperty("message").GetString()!);
        Assert.DoesNotContain("archive_project", error.GetProperty("message").GetString()!);
        Assert.Equal(new[] { "validate", "compose" }, domain.Calls);
        Assert.Equal(0, domain.MutationCount);
        Assert.Equal(0, gate.LeaseCalls);
        Assert.Single(audit.Records);
    }

    [Fact]
    public async Task CapabilityHook_Default_ChecksToolName()
    {
        var gate = LocalSessionGate();
        var domain = new FakeWriteDomain(gate);
        var audit = new RecordingAuditSink(gate);
        var result = await new WriteExecution(gate, audit, FakeWriteDomain.Catalog, TimeProvider.System)
            .RunAsync(domain, new WriteCall<FakeWriteItem>([new("a", "plc_write")], false));

        Assert.True(result.IsError);
        var error = CanonicalToolDocument.Read(result).GetProperty("error");
        Assert.Equal(WorkerFailureCategories.UnsupportedCapability, error.GetProperty("category").GetString());
        Assert.Contains("fake_write_tool", error.GetProperty("message").GetString()!);
        Assert.Equal(new[] { "validate", "compose" }, domain.Calls);
        Assert.Equal(0, domain.MutationCount);
        Assert.Equal(0, gate.LeaseCalls);
    }

    [Fact]
    public async Task CapabilityHook_SupportedOperations_AllowMutation()
    {
        var gate = LocalSessionGate();
        var domain = new FakeWriteDomain(gate)
        {
            CapabilityOperationsOverride = items => items.Select(item => item.Operation)
        };
        var audit = new RecordingAuditSink(gate);
        var result = await new WriteExecution(gate, audit, FakeWriteDomain.Catalog, TimeProvider.System)
            .RunAsync(domain, new WriteCall<FakeWriteItem>(
                [new("a", "plc_write"), new("b", "network_write")], false));

        Assert.False(result.IsError == true);
        Assert.Equal(2, domain.MutationCount);
        Assert.Equal(1, gate.LeaseCalls);
    }

    private static FakeWriteBindingGate LocalSessionGate() => new()
    {
        AccessMode = McpAccessMode.ReadWrite,
        CurrentBinding = new ProjectBindingSnapshot(
            ProjectBindingSnapshot.VerifiedState, "binding-1", 3, FakeWriteBindingGate.ProjectPath,
            "session-1", 1, 4242, null, new ProjectContextInfo { ContainerKind = ProjectContainerKinds.LocalSession })
    };
}
