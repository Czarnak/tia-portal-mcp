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

    [Fact]
    public void RejectionIncludesRecoveryGuidanceWithPreviewToolName()
    {
        var safety = new WriteSafetyService(() => DateTimeOffset.UtcNow);

        var result = safety.ValidateAndConsume(
            "unknown-token",
            toolName: "apply_write_batch",
            projectPath: null,
            target: new { },
            requestedInput: new { },
            currentState: "state",
            previewToolName: "preview_write_batch");

        Assert.False(result.IsValid);
        Assert.Contains("single-use", result.Error);
        Assert.Contains("10 minutes", result.Error);
        Assert.Contains("preview_write_batch", result.Error);
    }

    [Fact]
    public void RecoveryGuidanceUsesConfiguredLifetime()
    {
        var safety = new WriteSafetyService(() => DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));

        var result = safety.ValidateAndConsume(
            "unknown-token", "apply_write_batch", null, new { }, new { }, "state");

        Assert.False(result.IsValid);
        Assert.Contains("2 minutes", result.Error);
        Assert.Contains("the matching preview tool", result.Error);
    }

    [Fact]
    public void AppendAudit_WritesJsonlRecordToConfiguredDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tia-mcp-audit-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var now = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);
            var safety = new WriteSafetyService(() => now, TimeSpan.FromMinutes(10), dir);

            safety.AppendAudit("apply_write_batch", null, new { }, new { }, "state", "result");

            var auditPath = Path.Combine(dir, "2026-07-15.jsonl");
            Assert.True(File.Exists(auditPath));
            Assert.Contains("apply_write_batch", File.ReadAllText(auditPath));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void AppendAudit_LogsFailureToStdErrInsteadOfThrowing()
    {
        var blockingFile = Path.GetTempFileName();
        var originalError = Console.Error;
        var capture = new StringWriter();
        Console.SetError(capture);
        try
        {
            var safety = new WriteSafetyService(() => DateTimeOffset.UtcNow, TimeSpan.FromMinutes(10), blockingFile);

            safety.AppendAudit("apply_write_batch", null, new { }, new { }, "state", "result");
        }
        finally
        {
            Console.SetError(originalError);
            File.Delete(blockingFile);
        }

        Assert.Contains("failed to write audit record", capture.ToString());
    }

    [Fact]
    public void CreatePreview_EvictsExpiredTokens()
    {
        var now = new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
        var service = new WriteSafetyService(() => now, TimeSpan.FromMinutes(10));

        service.CreatePreview("apply_write_batch", null, new { a = 1 }, "s", new { b = 1 }, "state-1");
        service.CreatePreview("apply_write_batch", null, new { a = 2 }, "s", new { b = 2 }, "state-2");
        Assert.Equal(2, service.ActiveTokenCount);

        now = now.AddMinutes(11);
        service.CreatePreview("apply_write_batch", null, new { a = 3 }, "s", new { b = 3 }, "state-3");

        // The two expired tokens were swept; only the fresh one remains.
        Assert.Equal(1, service.ActiveTokenCount);
    }

    [Fact]
    public void CreatePreview_KeepsUnexpiredTokens()
    {
        var now = new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
        var service = new WriteSafetyService(() => now, TimeSpan.FromMinutes(10));

        service.CreatePreview("apply_write_batch", null, new { a = 1 }, "s", new { b = 1 }, "state-1");
        now = now.AddMinutes(5);
        service.CreatePreview("apply_write_batch", null, new { a = 2 }, "s", new { b = 2 }, "state-2");

        Assert.Equal(2, service.ActiveTokenCount);
    }

    private static string ReadToken(string previewJson)
    {
        using var preview = JsonDocument.Parse(previewJson);
        return preview.RootElement.GetProperty("safetyToken").GetString()!;
    }
}
