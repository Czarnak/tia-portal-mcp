using System.Reflection;
using System.Text.Json;
using ModelContextProtocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Tools;

[Collection("Mcp protocol serial")]
public sealed class StandaloneToolsProtocolTests
{
    [Theory]
    [InlineData("status-malformed", "failed")]
    [InlineData("status-oversized", "omitted")]
    public async Task StatusDecodePrecedesBudgetProjection_ThroughActualSdk(string scenario, string status)
    {
        using var uiOpen = FakeWorkerUiOpenProject.ForWorkerRelativePath(scenario);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full);
        var response = await harness.Client.CallToolAsync("get_project_status", new Dictionary<string, object?> { ["projectPath"] = scenario });
        Assert.Empty(StructuredContractInspector.FindViolations(response));
        Assert.False(response.IsError);
        var root = response.StructuredContent!.Value;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        var outcome = root.GetProperty("result");
        Assert.Equal(status, outcome.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, outcome.GetProperty("value").ValueKind);
        Assert.DoesNotContain("PRIVATE_STATUS_MARKER", root.GetRawText());
        if (status == "failed") Assert.Equal("protocol_error", outcome.GetProperty("failure").GetProperty("category").GetString());
    }

    [Theory]
    [InlineData("compile-errors", "failed", false)]
    [InlineData("compile-malformed", "failed", false)]
    [InlineData("compile-oversized", "omitted", false)]
    [InlineData("compile-attempt-failure", "failed", false)]
    [InlineData("compile-identity-drift", "failed", false)]
    public async Task CompileOutcomes_SurviveActualSdkSerialization(string scenario, string status, bool isError)
    {
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, startupProjectPath: scenario);
        var result = await harness.Client.CallToolAsync("compile_check", new Dictionary<string, object?>());
        Assert.Empty(StructuredContractInspector.FindViolations(result));
        Assert.Equal(isError, result.IsError == true);
        var root = result.StructuredContent!.Value;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        var outcome = root.GetProperty("result");
        Assert.Equal(status, outcome.GetProperty("status").GetString());
        Assert.True(outcome.TryGetProperty("failure", out _));
        Assert.True(outcome.TryGetProperty("omission", out _));
        if (scenario == "compile-errors")
        {
            Assert.Equal(1, outcome.GetProperty("value").GetProperty("totalErrorCount").GetInt32());
            Assert.Equal(JsonValueKind.Null, outcome.GetProperty("value").GetProperty("blockPath").ValueKind);
        }
        Assert.DoesNotContain("PRIVATE_COMPILER_MARKER", root.GetRawText());
    }

    [Theory]
    [InlineData(McpAccessMode.ReadOnly)]
    [InlineData(McpAccessMode.ReadWrite)]
    [InlineData(McpAccessMode.Full)]
    public async Task ModeDiscovery_AdvertisesConcreteSchemasAndStatusStaysReadOnly(McpAccessMode mode)
    {
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(mode);
        var tools = await harness.Client.ListToolsAsync();
        Assert.Equal(mode == McpAccessMode.ReadOnly ? 7 : 17, tools.Count);
        var statusTool = Assert.Single(tools, tool => tool.Name == "get_project_status");
        var statusSchema = statusTool.ProtocolTool.OutputSchema!.Value;
        Assert.Equal(new[] { "contractVersion", "error", "result", "success", "tool", "warnings" },
            statusSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(new[] { "projectPath" }, statusTool.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name));
        var status = await harness.Client.CallToolAsync("get_project_status", new Dictionary<string, object?> { ["projectPath"] = "status-no-project" });
        Assert.Empty(StructuredContractInspector.FindViolations(status));
        Assert.True(status.StructuredContent!.Value.GetProperty("success").GetBoolean());

        if (mode == McpAccessMode.ReadOnly)
        {
            Assert.DoesNotContain(tools, tool => tool.Name == "compile_check");
            var exception = await Assert.ThrowsAsync<McpProtocolException>(() => harness.Client.CallToolAsync("compile_check", new Dictionary<string, object?>()).AsTask());
            Assert.Equal(McpErrorCode.InvalidParams, exception.ErrorCode);
        }
        else
        {
            var compileTool = Assert.Single(tools, tool => tool.Name == "compile_check");
            Assert.NotNull(compileTool.ProtocolTool.OutputSchema);
            Assert.Equal(new[] { "blockPath", "plcName", "projectPath" },
                compileTool.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order());
        }
    }

    [Theory]
    [InlineData(McpAccessMode.ReadOnly)]
    [InlineData(McpAccessMode.ReadWrite)]
    [InlineData(McpAccessMode.Full)]
    public async Task DirectCompileAuthorization_IsEnforcedBeforeWorkerStartup(McpAccessMode mode)
    {
        using var client = new OpennessWorkerClient(new ProjectSessionBinding(null), null,
            workerExecutablePath: "worker-must-not-start.exe", accessPolicy: new TiaMcpServer.Safety.OperationAccessPolicy(mode));
        var result = await ProjectEngineeringTools.CompileCheck(client);
        Assert.True(result.IsError);
        Assert.Equal(mode == McpAccessMode.ReadOnly ? "access_denied" : "binding_conflict",
            result.StructuredContent!.Value.GetProperty("error").GetProperty("category").GetString());
        Assert.Null(typeof(OpennessWorkerClient).GetField("_transport", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client));
    }
}
