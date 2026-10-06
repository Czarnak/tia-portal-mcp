using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.Tools;
using Xunit;

namespace TiaMcpServer.Tests.Multiuser;

[Collection("Mcp protocol serial")]
public sealed class ProjectBindingInspectionBudgetTests
{
    [Fact]
    public async Task OversizedInventory_OmitsWholeBindingValueWithActionValidGuidance()
    {
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, null));
        using var ui = new FakeWorkerUiOpenProject(null);
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectBindingTools>(accessMode: McpAccessMode.ReadOnly);
        var response = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?>
        {
            ["action"] = "list_server_projects", ["serverAlias"] = "oversized", ["portalProcessId"] = 42,
            ["group"] = JsonSerializer.Deserialize<JsonElement>("""{"isRoot":true,"name":null}""")
        });
        Assert.False(response.IsError == true);
        var document = response.StructuredContent!.Value;
        Assert.Equal(document.GetRawText(), Assert.IsType<TextContentBlock>(Assert.Single(response.Content)).Text);
        Assert.Empty(StructuredContractInspector.FindViolations(response));
        Assert.False(document.GetProperty("success").GetBoolean());
        var result = document.GetProperty("result");
        Assert.Equal("omitted", result.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("value").ValueKind);
        Assert.Equal(JsonValueKind.Null, result.GetProperty("failure").ValueKind);
        Assert.DoesNotContain("projectPath", result.GetProperty("omission").GetRawText());
        Assert.False(document.TryGetProperty("inspection", out _));
        Assert.True(document.GetRawText().Length < 180000);
    }
}
