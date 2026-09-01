using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Tools;
using TiaMcpServer.Tests.Worker;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public sealed class ProjectLifecycleDelegationTests
{
    private static WriteSafetyService CreateSafety(TempAuditDirectory audit, ProjectSessionBinding binding)
        => new(binding, () => DateTimeOffset.UtcNow, WriteSafetyService.DefaultTokenLifetime, audit.Path);

    private static OpennessWorkerClient CreateClient(ProjectSessionBinding binding)
        => new(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate(),
            accessPolicy: new OperationAccessPolicy(McpAccessMode.ReadWrite));

    [Fact]
    public async Task SaveProject_WrapperMatchesRegisteredBindingGate()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        var safety = CreateSafety(audit, binding);
        using var client = CreateClient(binding);

        var registered = await ProjectWriteTools.SaveProject(client, safety, projectPath: null);
        var wrapper = await ProjectLifecycleTools.SaveProject(client, safety, projectPath: null);

        using var registeredDoc = JsonDocument.Parse(registered);
        using var wrapperDoc = JsonDocument.Parse(wrapper);

        Assert.Equal(
            registeredDoc.RootElement.GetProperty("failureCategory").GetString(),
            wrapperDoc.RootElement.GetProperty("failureCategory").GetString());
        Assert.Equal(
            registeredDoc.RootElement.GetProperty("error").GetString(),
            wrapperDoc.RootElement.GetProperty("error").GetString());
        Assert.False(wrapperDoc.RootElement.TryGetProperty("safetyToken", out _));
    }
}
