using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;
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
            workerExecutablePath: FakeWorkerLocator.Locate());

        var result = await client.BrowseProjectTreeV3SnapshotAsync(
            projectPath: "project-tree-v3-snapshot",
            startSelector: new[]
            {
                new ProjectTreeSelectorSegment { NodeType = "Device", Name = "PLC_1" }
            },
            depth: 1);

        Assert.True(result.Success, result.Error);
        using var response = JsonDocument.Parse(result.Payload);
        Assert.Equal("Device", response.RootElement.GetProperty("startSelector")[0].GetProperty("nodeType").GetString());
        Assert.Equal("PLC_1", response.RootElement.GetProperty("roots")[0].GetProperty("name").GetString());
    }
}
