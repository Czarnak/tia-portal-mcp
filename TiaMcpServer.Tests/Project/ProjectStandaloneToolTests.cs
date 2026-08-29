using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Server;
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public class ProjectStandaloneToolTests
{
    private static OpennessWorkerClient CreateClient(
        string workerPath,
        ProjectSessionBinding? binding = null)
        => new(
            binding ?? new ProjectSessionBinding(null),
            logger: null,
            workerExecutablePath: workerPath);

    private static async Task VerifyBindingAsync(
        OpennessWorkerClient client,
        ProjectSessionBinding binding,
        string projectPath)
    {
        var result = await client.GetProjectStatusAsync(projectPath);
        Assert.True(result.Success, result.Error);
        Assert.True(binding.IsVerified);
    }

    private static JsonElement WorkerRequestFromEnvelope(string response)
    {
        using var envelope = JsonDocument.Parse(response);
        var payload = envelope.RootElement.GetProperty("payload").GetString();
        Assert.False(string.IsNullOrWhiteSpace(payload));
        using var request = JsonDocument.Parse(payload!);
        return request.RootElement.Clone();
    }

    private static string PayloadFromEnvelope(string response)
    {
        using var envelope = JsonDocument.Parse(response);
        return envelope.RootElement.GetProperty("payload").GetString()!;
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
    public async Task BrowseProjectTree_ProjectCompletenessFixtureKeepsDevicesFlatAndMarksSystemBlocks()
    {
        using var client = CreateClient(FakeWorkerLocator.Locate());

        var response = await ProjectReadTools.BrowseProjectTree(
            client,
            projectPath: "project-enumeration-completeness");
        using var document = JsonDocument.Parse(PayloadFromEnvelope(response));
        var nodes = document.RootElement
            .EnumerateArray()
            .SelectMany(Descendants)
            .ToArray();

        Assert.Contains(nodes, node =>
            node.GetProperty("nodeType").GetString() == "Device"
            && node.GetProperty("name").GetString() == "Grouped ET200");
        Assert.DoesNotContain(nodes, node => node.GetProperty("nodeType").GetString() == "DeviceFolder");

        var systemFolder = Assert.Single(nodes.Where(
            node => node.GetProperty("nodeType").GetString() == "SystemBlockFolder"));
        Assert.Equal("System blocks", systemFolder.GetProperty("name").GetString());

        var systemBlock = Assert.Single(nodes.Where(
            node => node.GetProperty("name").GetString() == "SafeFB"));
        Assert.Equal("FB", systemBlock.GetProperty("nodeType").GetString());
        Assert.Equal("true", systemBlock.GetProperty("details").GetProperty("IsSystemBlock").GetString());
    }

    private static IEnumerable<JsonElement> Descendants(JsonElement node)
    {
        yield return node;
        if (!node.TryGetProperty("children", out var children))
        {
            yield break;
        }

        foreach (var child in children.EnumerateArray())
        {
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
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

    [Fact]
    public async Task BrowseProjectTree_OversizedSuccess_IsCappedAtMaxItemChars()
    {
        using var client = CreateClient(FakeWorkerLocator.Locate());

        var response = await ProjectReadTools.BrowseProjectTree(
            client,
            projectPath: "echo",
            startPath: new string('x', OperationBatchPayloadBudget.MaxItemChars + 100));
        var payload = PayloadFromEnvelope(response);

        Assert.Equal(OperationBatchPayloadBudget.MaxItemChars, payload.Length);
        Assert.Contains("[TRUNCATED", payload);
        Assert.Contains("depth or a more specific startPath", payload);
    }

    [Fact]
    public void CompileCheck_HasEngineeringMcpMetadata()
    {
        var method = typeof(ProjectEngineeringTools).GetMethod(
            nameof(ProjectEngineeringTools.CompileCheck),
            BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(method);
        var attribute = method!.GetCustomAttribute<McpServerToolAttribute>();
        Assert.NotNull(attribute);
        Assert.Equal("compile_check", attribute!.Name);
        Assert.False(attribute.ReadOnly);
        Assert.False(attribute.Destructive);
        Assert.False(attribute.OpenWorld);
    }

    [Fact]
    public async Task CompileCheck_ForwardsEveryArgument()
    {
        const string projectPath = "echo";
        var binding = new ProjectSessionBinding(projectPath);
        using var client = CreateClient(FakeWorkerLocator.Locate(), binding);
        await VerifyBindingAsync(client, binding, projectPath);

        var response = await ProjectEngineeringTools.CompileCheck(
            client,
            projectPath,
            plcName: "PLC_1",
            blockPath: "PLC_1/Blocks/Main");
        var request = WorkerRequestFromEnvelope(response);

        Assert.Equal("compile_check", request.GetProperty("method").GetString());
        Assert.Equal(binding.BoundProjectPath, request.GetProperty("projectPath").GetString());
        Assert.Equal("PLC_1", request.GetProperty("plcName").GetString());
        Assert.Equal("PLC_1/Blocks/Main", request.GetProperty("blockPath").GetString());
    }

    [Fact]
    public async Task CompileCheck_OversizedSuccess_IsCappedAtMaxItemChars()
    {
        const string projectPath = "echo";
        var binding = new ProjectSessionBinding(projectPath);
        using var client = CreateClient(FakeWorkerLocator.Locate(), binding);
        await VerifyBindingAsync(client, binding, projectPath);

        var response = await ProjectEngineeringTools.CompileCheck(
            client,
            projectPath,
            blockPath: new string('x', OperationBatchPayloadBudget.MaxItemChars + 100));
        var payload = PayloadFromEnvelope(response);

        Assert.Equal(OperationBatchPayloadBudget.MaxItemChars, payload.Length);
        Assert.Contains("[TRUNCATED", payload);
        Assert.Contains("plcName or blockPath", payload);
    }

    [Fact]
    public async Task GetProjectStatus_OversizedSuccess_IsCappedAtMaxItemChars()
    {
        using var client = CreateClient(FakeWorkerLocator.Locate());

        var response = await ProjectReadTools.GetProjectStatus(client, projectPath: "status-oversized");
        var payload = PayloadFromEnvelope(response);

        Assert.Equal(OperationBatchPayloadBudget.MaxItemChars, payload.Length);
        Assert.Contains("[TRUNCATED", payload);
        Assert.Contains("Extended metadata", payload);
    }
}
