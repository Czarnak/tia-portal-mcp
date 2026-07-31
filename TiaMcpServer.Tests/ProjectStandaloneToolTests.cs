using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests;

public class ProjectStandaloneToolTests
{
    private static OpennessWorkerClient CreateClient(string workerPath)
        => new(
            new ProjectSessionBinding(null),
            logger: null,
            workerExecutablePath: workerPath);

    private static JsonElement WorkerRequestFromEnvelope(string response)
    {
        using var envelope = JsonDocument.Parse(response);
        var payload = envelope.RootElement.GetProperty("payload").GetString();
        Assert.False(string.IsNullOrWhiteSpace(payload));
        using var request = JsonDocument.Parse(payload!);
        return request.RootElement.Clone();
    }

    [Fact]
    public void BrowseProjectTree_HasReadOnlyMcpMetadata()
    {
        var method = typeof(ProjectReadTools).GetMethod(
            nameof(ProjectReadTools.BrowseProjectTree),
            BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(method);
        var attribute = method!.GetCustomAttribute<McpServerToolAttribute>();
        Assert.NotNull(attribute);
        Assert.Equal("browse_project_tree", attribute!.Name);
        Assert.True(attribute.ReadOnly);
        Assert.False(attribute.Destructive);
        Assert.False(attribute.OpenWorld);
    }

    [Fact]
    public async Task BrowseProjectTree_ForwardsEveryArgument()
    {
        using var client = CreateClient(FakeWorkerLocator.Locate());

        var response = await ProjectReadTools.BrowseProjectTree(
            client,
            projectPath: "echo",
            depth: 2,
            startPath: "PLC_1/Blocks");
        var request = WorkerRequestFromEnvelope(response);

        Assert.Equal("browse_project_tree", request.GetProperty("method").GetString());
        Assert.Equal("echo", request.GetProperty("projectPath").GetString());
        Assert.Equal(2, request.GetProperty("depth").GetInt32());
        Assert.Equal("PLC_1/Blocks", request.GetProperty("startPath").GetString());
    }

    [Fact]
    public async Task BrowseProjectTree_InvalidDepthFailsBeforeWorkerAccess()
    {
        using var client = CreateClient("missing-worker.exe");

        var response = await ProjectReadTools.BrowseProjectTree(client, depth: 0);

        using var document = JsonDocument.Parse(response);
        var root = document.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(
            WorkerFailureCategories.ValidationError,
            root.GetProperty("failureCategory").GetString());
        Assert.Contains("depth", root.GetProperty("error").GetString());
    }
}