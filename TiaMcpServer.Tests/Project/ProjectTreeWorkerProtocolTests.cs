using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;
using TiaMcpServer.Safety;
using Xunit;

namespace TiaMcpServer.Tests.Project;

[Collection(RealWorkerProcessCollection.Name)]
public sealed class ProjectTreeWorkerProtocolTests
{
    [Fact]
    public async Task BrowseProjectTreeV3Snapshot_SendsTypedSelectorAndReturnsTypedTreeResult()
    {
        using var uiOpen = FakeWorkerUiOpenProject.ForWorkerRelativePath("project-tree-v3-snapshot");
        using var client = new OpennessWorkerClient(
            new ProjectSessionBinding(null),
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate(),
            accessPolicy: new OperationAccessPolicy(McpAccessMode.ReadOnly));

        var call = await client.BrowseProjectTreeV3SnapshotAsync(
            projectPath: "project-tree-v3-snapshot",
            startSelector: new[]
            {
                new ProjectTreeSelectorSegment { NodeType = "Device", Name = "PLC_1" }
            },
            depth: 1);

        var result = call.WorkerResult;
        Assert.True(result.Success, result.Error);
        Assert.Equal(ProjectBindingSnapshot.UnboundState, call.HostBinding.State);
        using var response = JsonDocument.Parse(result.Payload);
        Assert.False(response.RootElement.TryGetProperty("hostBinding", out _));
        Assert.False(response.RootElement.TryGetProperty("workerResult", out _));
        Assert.Equal("Device", response.RootElement.GetProperty("startSelector")[0].GetProperty("nodeType").GetString());
        Assert.Equal("PLC_1", response.RootElement.GetProperty("roots")[0].GetProperty("name").GetString());
    }
}
