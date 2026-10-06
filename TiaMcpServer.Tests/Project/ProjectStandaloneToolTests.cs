using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Project;

[Collection(RealWorkerProcessCollection.Name)]
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
        using var uiOpen = new FakeWorkerUiOpenProject(projectPath);
        var result = await client.GetProjectStatusAsync(projectPath);
        Assert.True(result.Success, result.Error);
        Assert.True(binding.IsVerified);
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
        const string projectPath = "compile-arguments";
        var binding = new ProjectSessionBinding(projectPath);
        using var client = CreateClient(FakeWorkerLocator.Locate(), binding);
        await VerifyBindingAsync(client, binding, projectPath);

        var response = await ProjectEngineeringTools.CompileCheck(
            client,
            projectPath,
            plcName: "PLC_1",
            blockPath: "PLC_1/Blocks/Main");
        var root = StandaloneStatusToolTests.Document(response);
        Assert.True(root.GetProperty("success").GetBoolean());
        var report = root.GetProperty("result").GetProperty("value");
        Assert.Equal("block", report.GetProperty("scope").GetString());
        Assert.Equal("PLC_1", report.GetProperty("plcs")[0].GetProperty("plcName").GetString());
        Assert.Equal("PLC_1/Blocks/Main", report.GetProperty("blockPath").GetString());
    }

    [Fact]
    public async Task CompileCheck_OversizedBlockPath_IsWholeValueOmission()
    {
        const string projectPath = "compile-arguments";
        var binding = new ProjectSessionBinding(projectPath);
        using var client = CreateClient(FakeWorkerLocator.Locate(), binding);
        await VerifyBindingAsync(client, binding, projectPath);

        var response = await ProjectEngineeringTools.CompileCheck(
            client,
            projectPath,
            blockPath: new string('x', StructuredOperationBatchPayloadBudget.MaxItemChars + 100));
        var root = StandaloneStatusToolTests.Document(response);
        var outcome = root.GetProperty("result");
        Assert.Equal("omitted", outcome.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, outcome.GetProperty("value").ValueKind);
        Assert.Contains("plcName or blockPath", outcome.GetProperty("omission").GetProperty("guidance").GetString());
    }

}
