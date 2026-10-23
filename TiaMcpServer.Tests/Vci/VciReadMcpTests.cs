using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Vci;
using Xunit;

namespace TiaMcpServer.Tests.Vci;

[Collection("Mcp protocol serial")]
public class VciReadMcpTests
{
    private const string Scenario = "vci-read-roundtrip";
    private static object[] Leaf => [new { nodeType = "Device", name = "Fixture" }, new { nodeType = "HmiSoftware", name = "Fixture" }, new { nodeType = "ScriptModule", name = "Fixture" }];

    [Fact]
    public async Task ThreeReadsRoundtripAsDeclaredObjects()
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<VciReadTools>(startupProjectPath: Scenario, accessMode: McpAccessMode.ReadOnly);
        var tool = Assert.Single(await harness.Client.ListToolsAsync());
        Assert.True(tool.ProtocolTool.Annotations!.ReadOnlyHint);
        Assert.False(tool.ProtocolTool.Annotations.DestructiveHint);
        Assert.False(tool.ProtocolTool.Annotations.OpenWorldHint);
        Assert.NotNull(tool.ProtocolTool.OutputSchema);
        Assert.Equal(new[] { "operations" }, tool.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name));
        Assert.False(tool.ProtocolTool.InputSchema.GetProperty("additionalProperties").GetBoolean());
        var result = await harness.Client.CallToolAsync("vci_read", new Dictionary<string, object?>
        {
            ["operations"] = new object[]
            {
                new { operationId = "w", operation = "list_workspaces"},
                new { operationId = "m", operation = "list_mappings", workspaceName = "Fixture", withStatus = true, offset = 2, limit = 7, scope = Leaf },
                new { operationId = "f", operation = "supported_formats", scope = Leaf },
            }
        });
        Assert.False(result.IsError);
        Assert.Empty(StructuredContractInspector.FindViolations(result));
        var root = result.StructuredContent!.Value;
        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        Assert.Equal("1.0", root.GetProperty("contractVersion").GetString());
        Assert.Equal(root.GetRawText(), Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        var items = root.GetProperty("batch").GetProperty("operations");
        Assert.Equal("Fixture", items[0].GetProperty("result").GetProperty("workspaces")[0].GetProperty("name").GetString());
        var mappings = items[1].GetProperty("result");
        Assert.Equal(2, mappings.GetProperty("offset").GetInt32());
        Assert.Equal(7, mappings.GetProperty("limit").GetInt32());
        Assert.Equal("Equal", mappings.GetProperty("mappings")[0].GetProperty("status").GetProperty("compareState").GetString());
        Assert.Equal("Fixture", mappings.GetProperty("mappings")[0].GetProperty("path")[2].GetProperty("name").GetString());
        Assert.Equal("SimaticML", items[2].GetProperty("result").GetProperty("formats")[0].GetString());
    }

    [Theory]
    [InlineData("Missing", "target_not_found")]
    [InlineData("Malformed", "protocol_error")]
    [InlineData("MissingMember", "protocol_error")]
    public async Task ItemFailureContinuesBatchWithoutMcpError(string workspaceName, string category)
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<VciReadTools>(startupProjectPath: Scenario, accessMode: McpAccessMode.ReadOnly);
        var result = await harness.Client.CallToolAsync("vci_read", new Dictionary<string, object?> { ["operations"] = new object[]
        {
            new { operationId = "bad", operation = "list_mappings", workspaceName},
            new { operationId = "ok", operation = "list_workspaces"},
        } });
        Assert.False(result.IsError);
        var root = result.StructuredContent!.Value;
        Assert.False(root.GetProperty("success").GetBoolean());
        var items = root.GetProperty("batch").GetProperty("operations");
        Assert.Equal(category, items[0].GetProperty("failure").GetProperty("category").GetString());
        Assert.Equal("succeeded", items[1].GetProperty("status").GetString());
        Assert.DoesNotContain("PRIVATE_MARKER", root.GetRawText());
    }

    [Theory]
    [InlineData("root")]
    [InlineData("item")]
    [InlineData("boolean")]
    public async Task MalformedInputsRejectBeforeWorker(string kind)
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<VciReadTools>(accessMode: McpAccessMode.ReadOnly);
        var item = new Dictionary<string, object?> { ["operationId"] = "r", ["operation"] = "list_workspaces" };
        var args = new Dictionary<string, object?> { ["operations"] = new[] { item } };
        if (kind == "root") args["dryRun"] = false;
        if (kind == "item") item["unknown"] = "x";
        if (kind == "boolean") { item["operation"] = "list_mappings"; item["workspaceName"] = "Fixture"; item["withStatus"] = "true"; }
        var result = await harness.Client.CallToolAsync("vci_read", args);
        ArgumentRejectionAssert.IsCanonicalValidationError(result, "vci_read");
    }

    [Theory]
    [InlineData("SoftwareUnit")]
    [InlineData("SystemBlockFolder")]
    public async Task UnsupportedScopePreservesKindCategoryBeforeWorker(string nodeType)
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<VciReadTools>(accessMode: McpAccessMode.ReadOnly);
        var result = await harness.Client.CallToolAsync("vci_read", new Dictionary<string, object?>
        {
            ["operations"] = new[]
            {
                new { operationId = "r", operation = "list_mappings", workspaceName = "Fixture", scope = new[]
                {
                    new { nodeType = "Device", name = "Fixture" },
                    new { nodeType = "PlcSoftware", name = "Fixture" },
                    new { nodeType, name = "Fixture" },
                } },
            },
        });
        Assert.True(result.IsError);
        Assert.Empty(StructuredContractInspector.FindViolations(result));
        var root = result.StructuredContent!.Value;
        Assert.Equal("target_kind_unsupported", root.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("scope", root.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("batch").ValueKind);
        Assert.Null(typeof(TiaMcpServer.Worker.OpennessWorkerClient)
            .GetField("_transport", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(harness.WorkerClient));
    }

    [Fact]
    public void OversizedResultIsOmittedWithVciRetryGuidance()
    {
        var item = VciPayloadContract.Project(new() { OperationId = "r", Operation = "supported_formats" },
            TiaMcpServer.Worker.WorkerCallResult.Ok(JsonSerializer.Serialize(new { formats = new[] { new string('x', 3000) } })));
        var batch = TiaMcpServer.OperationBatches.StructuredOperationBatch.FromItems(new[] { item });
        var result = VciReadTools.ApplyBudget(batch, maxItemChars: 1000, maxDocumentChars: 5000).Operations[0];
        Assert.Equal("omitted", result.Status);
        Assert.Null(result.Result);
        Assert.Equal("vci_read", result.Omission!.RetryTool);
        Assert.Contains("--max-item-chars", result.Omission.Guidance);
    }
}
