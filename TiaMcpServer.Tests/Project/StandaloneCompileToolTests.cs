using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public sealed class StandaloneCompileToolTests
{
    [Theory]
    [InlineData("compile-passed", true, "Success")]
    [InlineData("compile-warning", true, "Warning")]
    [InlineData("compile-errors", false, "Error")]
    [InlineData("compile-unavailable", false, "Error")]
    [InlineData("compile-unknown", false, "Cancelled")]
    public async Task CompilationOutcome_RetainsTheTypedReport(string scenario, bool passed, string plcState)
    {
        var binding = new ProjectSessionBinding(scenario);
        using var client = new OpennessWorkerClient(binding, null, workerExecutablePath: FakeWorkerLocator.Locate());
        Assert.True((await client.GetProjectStatusAsync(scenario)).Success);
        var response = await ProjectEngineeringTools.CompileCheck(client, scenario);
        var root = StandaloneStatusToolTests.Document(response);
        Assert.False(((CallToolResult)(object)response).IsError);
        Assert.Equal(passed, root.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        var outcome = root.GetProperty("result");
        Assert.Equal(passed ? "succeeded" : "failed", outcome.GetProperty("status").GetString());
        Assert.Equal(plcState, outcome.GetProperty("value").GetProperty("plcs")[0].GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, outcome.GetProperty("failure").ValueKind);
        Assert.Equal(JsonValueKind.Null, outcome.GetProperty("value").GetProperty("blockPath").ValueKind);
    }

    [Theory]
    [InlineData("compile-attempt-failure", "failed", "worker_operation_failed")]
    [InlineData("compile-malformed", "failed", "protocol_error")]
    [InlineData("compile-inconsistent", "failed", "protocol_error")]
    [InlineData("compile-oversized", "omitted", null)]
    public async Task AttemptedFailuresAndOmission_AreNotToolRejections(string scenario, string status, string? category)
    {
        var binding = new ProjectSessionBinding(scenario);
        using var client = new OpennessWorkerClient(binding, null, workerExecutablePath: FakeWorkerLocator.Locate());
        Assert.True((await client.GetProjectStatusAsync(scenario)).Success);
        var response = await ProjectEngineeringTools.CompileCheck(client, scenario);
        var root = StandaloneStatusToolTests.Document(response);
        Assert.False(((CallToolResult)(object)response).IsError);
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        var outcome = root.GetProperty("result");
        Assert.Equal(status, outcome.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, outcome.GetProperty("value").ValueKind);
        if (category is not null) Assert.Equal(category, outcome.GetProperty("failure").GetProperty("category").GetString());
        else Assert.Equal("compile_check", outcome.GetProperty("omission").GetProperty("retryTool").GetString());
        Assert.DoesNotContain("PRIVATE_COMPILER_MARKER", root.GetRawText());
    }

    [Theory]
    [InlineData(McpAccessMode.ReadOnly, "access_denied")]
    [InlineData(McpAccessMode.ReadWrite, "binding_conflict")]
    public async Task PreDispatchRejection_IsStructuredAndDoesNotStartWorker(McpAccessMode mode, string category)
    {
        using var client = new OpennessWorkerClient(new ProjectSessionBinding(null), null,
            workerExecutablePath: "worker-must-not-start.exe", accessPolicy: new OperationAccessPolicy(mode));
        var response = await ProjectEngineeringTools.CompileCheck(client);
        var root = StandaloneStatusToolTests.Document(response);
        Assert.True(((CallToolResult)(object)response).IsError);
        Assert.Equal(category, root.GetProperty("error").GetProperty("category").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("result").ValueKind);
    }
}
