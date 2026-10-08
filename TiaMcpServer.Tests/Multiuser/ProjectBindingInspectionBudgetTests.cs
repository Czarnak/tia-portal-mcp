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
    public async Task OversizedLocalContext_OmitsWholeBindingValueUnderCanonicalDocumentBudget()
    {
        const string path = "C:/Projects/local-context-oversized.amc21";
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, path));
        using var ui = new FakeWorkerUiOpenProject(path);
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectBindingTools>(
            accessMode: McpAccessMode.ReadOnly);

        var response = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?>
        {
            ["projectPath"] = path
        });
        var document = response.StructuredContent!.Value;
        var canonical = document.GetRawText();
        Assert.Equal(canonical, Assert.IsType<TextContentBlock>(Assert.Single(response.Content)).Text);
        Assert.True(canonical.Length <= 180_000);
        Assert.Empty(StructuredContractInspector.FindViolations(response));
        Assert.False(document.GetProperty("success").GetBoolean());
        var result = document.GetProperty("result");
        Assert.Equal("omitted", result.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("value").ValueKind);
        var omission = result.GetProperty("omission");
        Assert.Equal("resultExceededItemCharLimit", omission.GetProperty("reason").GetString());
        Assert.Equal(60_000, omission.GetProperty("limitChars").GetInt32());
        Assert.True(omission.GetProperty("originalChars").GetInt32() > 60_000);
        Assert.True(harness.WorkerClient.BindingSnapshot.IsVerified);
    }

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
