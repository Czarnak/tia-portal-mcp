using System.Text.Json;
using TiaMcpServer.Safety;
using Xunit;

namespace TiaMcpServer.Tests;

public class WriteSafetyServiceTests
{
    [Fact]
    public void PreviewBindsTokenToToolInputAndCurrentState()
    {
        var now = new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.Zero);
        var safety = new WriteSafetyService(() => now);

        var previewJson = safety.CreatePreview(
            toolName: "update_block_logic",
            projectPath: "C:\\Projects\\Line.ap21",
            target: new { blockPath = "PLC_1/Main" },
            summary: "Update PLC block PLC_1/Main.",
            requestedInput: new { blockPath = "PLC_1/Main", yamlContent = "new" },
            currentState: "old");

        using var preview = JsonDocument.Parse(previewJson);
        var token = preview.RootElement.GetProperty("safetyToken").GetString();

        var result = safety.ValidateAndConsume(
            token,
            toolName: "update_block_logic",
            projectPath: "C:\\Projects\\Line.ap21",
            target: new { blockPath = "PLC_1/Main" },
            requestedInput: new { blockPath = "PLC_1/Main", yamlContent = "new" },
            currentState: "old");

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void TokenCannotBeReused()
    {
        var now = new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.Zero);
        var safety = new WriteSafetyService(() => now);
        var token = ReadToken(safety.CreatePreview(
            "create_tag_table",
            null,
            new { tableName = "Inputs" },
            "Create tag table Inputs.",
            new { tableName = "Inputs" },
            "[]"));

        var first = safety.ValidateAndConsume(
            token,
            "create_tag_table",
            null,
            new { tableName = "Inputs" },
            new { tableName = "Inputs" },
            "[]");
        var second = safety.ValidateAndConsume(
            token,
            "create_tag_table",
            null,
            new { tableName = "Inputs" },
            new { tableName = "Inputs" },
            "[]");

        Assert.True(first.IsValid, first.Error);
        Assert.False(second.IsValid);
        Assert.Contains("expired, consumed, or unknown", second.Error);
    }

    [Fact]
    public void TokenRejectsChangedInputAndChangedCurrentState()
    {
        var now = new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.Zero);
        var safety = new WriteSafetyService(() => now);
        var changedInputToken = ReadToken(safety.CreatePreview(
            "update_tag",
            null,
            new { tableName = "Inputs", name = "Start" },
            "Update tag Start.",
            new { tableName = "Inputs", name = "Start", dataType = "Bool" },
            "[{\"name\":\"Start\"}]"));
        var changedStateToken = ReadToken(safety.CreatePreview(
            "update_tag",
            null,
            new { tableName = "Inputs", name = "Start" },
            "Update tag Start.",
            new { tableName = "Inputs", name = "Start", dataType = "Bool" },
            "[{\"name\":\"Start\"}]"));

        var changedInput = safety.ValidateAndConsume(
            changedInputToken,
            "update_tag",
            null,
            new { tableName = "Inputs", name = "Start" },
            new { tableName = "Inputs", name = "Start", dataType = "Int" },
            "[{\"name\":\"Start\"}]");
        var changedState = safety.ValidateAndConsume(
            changedStateToken,
            "update_tag",
            null,
            new { tableName = "Inputs", name = "Start" },
            new { tableName = "Inputs", name = "Start", dataType = "Bool" },
            "[{\"name\":\"Start\",\"dataType\":\"Int\"}]");

        Assert.False(changedInput.IsValid);
        Assert.Contains("input", changedInput.Error);
        Assert.False(changedState.IsValid);
        Assert.Contains("current state", changedState.Error);
    }

    [Fact]
    public void TokenExpiresAfterConfiguredLifetime()
    {
        var now = new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.Zero);
        var safety = new WriteSafetyService(() => now, TimeSpan.FromMinutes(10));
        var token = ReadToken(safety.CreatePreview(
            "close_project",
            null,
            new { projectPath = "" },
            "Close active project.",
            new { saveBeforeClose = true },
            "{}"));

        now = now.AddMinutes(11);
        var result = safety.ValidateAndConsume(
            token,
            "close_project",
            null,
            new { projectPath = "" },
            new { saveBeforeClose = true },
            "{}");

        Assert.False(result.IsValid);
        Assert.Contains("expired", result.Error);
    }

    private static string ReadToken(string previewJson)
    {
        using var preview = JsonDocument.Parse(previewJson);
        return preview.RootElement.GetProperty("safetyToken").GetString()!;
    }
}
