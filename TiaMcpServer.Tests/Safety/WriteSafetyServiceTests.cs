using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.Safety;
using Xunit;

namespace TiaMcpServer.Tests.Safety;

public class WriteSafetyServiceTests
{
    [Fact]
    public void PreviewBindsTokenToToolInputAndCurrentState()
    {
        var now = new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.Zero);
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety(() => now);

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
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety(() => now);
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
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety(() => now);
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
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety(() => now, TimeSpan.FromMinutes(10));
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
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();

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
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety(tokenLifetime: TimeSpan.FromMinutes(2));

        var result = safety.ValidateAndConsume(
            "unknown-token", "apply_write_batch", null, new { }, new { }, "state");

        Assert.False(result.IsValid);
        Assert.Contains("2 minutes", result.Error);
        Assert.Contains("the matching preview tool", result.Error);
    }

    [Fact]
    public void AppendAudit_WritesJsonlRecordToConfiguredDirectory()
    {
        using var audit = new TempAuditDirectory();
        var now = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);
        var safety = audit.CreateSafety(() => now, TimeSpan.FromMinutes(10));

        safety.AppendAudit("apply_write_batch", null, new { }, new { }, "state", "result");

        var auditPath = Path.Combine(audit.Path, "2026-07-15.jsonl");
        Assert.True(File.Exists(auditPath));
        Assert.Contains("apply_write_batch", File.ReadAllText(auditPath));
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
        using var audit = new TempAuditDirectory();
        var service = audit.CreateSafety(() => now, TimeSpan.FromMinutes(10));

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
        using var audit = new TempAuditDirectory();
        var service = audit.CreateSafety(() => now, TimeSpan.FromMinutes(10));

        service.CreatePreview("apply_write_batch", null, new { a = 1 }, "s", new { b = 1 }, "state-1");
        now = now.AddMinutes(5);
        service.CreatePreview("apply_write_batch", null, new { a = 2 }, "s", new { b = 2 }, "state-2");

        Assert.Equal(2, service.ActiveTokenCount);
    }

    [Fact]
    public void ValidateEnvelope_AcceptsMatchingTokenWithoutConsumingIt()
    {
        var now = new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
        using var audit = new TempAuditDirectory();
        var service = audit.CreateSafety(() => now, TimeSpan.FromMinutes(10));
        var previewJson = service.CreatePreview("apply_write_batch", "C:\\p.ap21", new { t = 1 }, "s", new { i = 1 }, "state");
        var token = ReadToken(previewJson);

        var first = service.ValidateEnvelope(token, "apply_write_batch", "C:\\p.ap21", new { t = 1 }, new { i = 1 });
        var second = service.ValidateEnvelope(token, "apply_write_batch", "C:\\p.ap21", new { t = 1 }, new { i = 1 });

        Assert.True(first.IsValid);
        Assert.True(second.IsValid);
        Assert.Equal(1, service.ActiveTokenCount);

        // The full consume still works afterwards.
        var consume = service.ValidateAndConsume(token, "apply_write_batch", "C:\\p.ap21", new { t = 1 }, new { i = 1 }, "state");
        Assert.True(consume.IsValid);
        Assert.Equal(0, service.ActiveTokenCount);
    }

    [Fact]
    public void ValidateEnvelope_RejectsUnknownExpiredAndMismatchedTokens()
    {
        var now = new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
        using var audit = new TempAuditDirectory();
        var service = audit.CreateSafety(() => now, TimeSpan.FromMinutes(10));
        var previewJson = service.CreatePreview("apply_write_batch", "C:\\p.ap21", new { t = 1 }, "s", new { i = 1 }, "state");
        var token = ReadToken(previewJson);

        Assert.False(service.ValidateEnvelope("bogus", "apply_write_batch", "C:\\p.ap21", new { t = 1 }, new { i = 1 }).IsValid);
        Assert.False(service.ValidateEnvelope(token, "other_tool", "C:\\p.ap21", new { t = 1 }, new { i = 1 }).IsValid);
        Assert.False(service.ValidateEnvelope(token, "apply_write_batch", "C:\\other.ap21", new { t = 1 }, new { i = 1 }).IsValid);
        Assert.False(service.ValidateEnvelope(token, "apply_write_batch", "C:\\p.ap21", new { t = 2 }, new { i = 1 }).IsValid);
        Assert.False(service.ValidateEnvelope(token, "apply_write_batch", "C:\\p.ap21", new { t = 1 }, new { i = 2 }).IsValid);

        now = now.AddMinutes(11);
        Assert.False(service.ValidateEnvelope(token, "apply_write_batch", "C:\\p.ap21", new { t = 1 }, new { i = 1 }).IsValid);
    }

    [Fact]
    public void TokenIsRejectedAfterSamePathSessionGenerationChanges()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        Assert.True(binding.BindVerified(
            new WorkerSessionIdentity
            {
                WorkerSessionId = "worker-a",
                SessionGeneration = 1,
                PortalProcessId = 4242,
                ProjectPath = "C:\\p.ap21"
            },
            forceRebind: false,
            out _));
        var service = new WriteSafetyService(
            binding,
            () => DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(10),
            audit.Path);
        var token = ReadToken(service.CreatePreview(
            "apply_write_batch", "C:\\p.ap21", new { t = 1 }, "s", new { i = 1 }, "state"));

        Assert.True(binding.BindVerified(
            new WorkerSessionIdentity
            {
                WorkerSessionId = "worker-a",
                SessionGeneration = 2,
                PortalProcessId = 4242,
                ProjectPath = "C:\\p.ap21"
            },
            forceRebind: true,
            out _));

        var result = service.ValidateEnvelope(
            token, "apply_write_batch", "C:\\p.ap21", new { t = 1 }, new { i = 1 });

        Assert.False(result.IsValid);
        Assert.Equal(WorkerFailureCategories.BindingConflict, result.FailureCategory);
        Assert.Contains("worker/Portal/project session binding", result.Error);
    }

    [Fact]
    public void TokenIsRejectedAfterSamePathForcedRebindBecomesUnverified()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        Assert.True(binding.BindVerified(
            new WorkerSessionIdentity
            {
                WorkerSessionId = "worker-a",
                SessionGeneration = 1,
                PortalProcessId = 4242,
                ProjectPath = @"C:\p.ap21"
            },
            forceRebind: false,
            out _));
        var service = new WriteSafetyService(
            binding,
            () => DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(10),
            audit.Path);
        var token = ReadToken(service.CreatePreview(
            "apply_write_batch",
            @"C:\p.ap21",
            new { t = 1 },
            "s",
            new { i = 1 },
            "state"));

        Assert.True(binding.Bind(
            @"C:\p.ap21",
            forceRebind: true,
            out _));

        var result = service.ValidateEnvelope(
            token,
            "apply_write_batch",
            @"C:\p.ap21",
            new { t = 1 },
            new { i = 1 });

        Assert.False(result.IsValid);
        Assert.Equal(WorkerFailureCategories.BindingConflict, result.FailureCategory);
    }

    // ---------------------------------------------------------------------------------------
    // Canonical (opt-in) entry points. The invariants below are the same ones the text path
    // guarantees; what changes is that binding is computed from canonical JSON, so a difference
    // that is pure object-property ORDER is no longer treated as a different value.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void CanonicalToken_AcceptsPropertyOrderOnlyDifferencesInTargetInputAndState()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        var preview = safety.CreateCanonicalPreview(
            "network_write",
            "C:\\Projects\\Line.ap21",
            Json("""{"deviceName":"PLC_1","operationId":"first"}"""),
            "Apply 1 network write operation(s).",
            Json("""{"ipAddress":"192.168.0.10","operation":"configure_network_device"}"""),
            Json("""{"devices":[],"subnets":[]}"""),
            "Preview only.");

        var result = safety.ValidateAndConsumeCanonical(
            preview.SafetyToken,
            "network_write",
            "C:\\Projects\\Line.ap21",

            // Every value below is the same logical document with its properties written in a
            // different order; only ordering differs, so the token must still validate.
            Json("""{"operationId":"first","deviceName":"PLC_1"}"""),
            Json("""{"operation":"configure_network_device","ipAddress":"192.168.0.10"}"""),
            Json("""{"subnets":[],"devices":[]}"""));

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void CanonicalToken_RejectsArrayReordering()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        var preview = safety.CreateCanonicalPreview(
            "network_write",
            null,
            Json("""[{"operationId":"first"},{"operationId":"second"}]"""),
            "Apply 2 network write operation(s).",
            Json("""["first","second"]"""),
            Json("""{"devices":[]}"""),
            "Preview only.");

        var result = safety.ValidateAndConsumeCanonical(
            preview.SafetyToken,
            "network_write",
            null,
            Json("""[{"operationId":"second"},{"operationId":"first"}]"""),
            Json("""["first","second"]"""),
            Json("""{"devices":[]}"""));

        Assert.False(result.IsValid);
        Assert.Contains("different target", result.Error);
    }

    [Theory]

    // A string "1" is not the number 1: a type change must never canonicalize away.
    [InlineData("""{"maxResults":"1"}""", """{"devices":[]}""", "input does not match")]
    [InlineData("""{"maxResults":1}""", """{"devices":[{"name":"PLC_1"}]}""", "current state")]
    public void CanonicalToken_RejectsTypeAndValueChanges(
        string requestedInput,
        string currentState,
        string expectedError)
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        var preview = safety.CreateCanonicalPreview(
            "network_write",
            null,
            Json("""{"operationId":"first"}"""),
            "Apply 1 network write operation(s).",
            Json("""{"maxResults":1}"""),
            Json("""{"devices":[]}"""),
            "Preview only.");

        var result = safety.ValidateAndConsumeCanonical(
            preview.SafetyToken,
            "network_write",
            null,
            Json("""{"operationId":"first"}"""),
            Json(requestedInput),
            Json(currentState));

        Assert.False(result.IsValid);
        Assert.Contains(expectedError, result.Error);
    }

    [Fact]
    public void CanonicalToken_ExpiresAfterTenMinutesAndReportsItsLifetime()
    {
        var now = new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.Zero);
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety(() => now);

        var preview = safety.CreateCanonicalPreview(
            "network_write",
            null,
            Json("""{"operationId":"first"}"""),
            "Apply 1 network write operation(s).",
            Json("""{"maxResults":1}"""),
            Json("""{"devices":[]}"""),
            "Preview only.");
        Assert.Equal(now.AddMinutes(10), preview.ExpiresAtUtc);

        var stillValid = safety.ValidateCanonicalEnvelope(
            preview.SafetyToken,
            "network_write",
            null,
            Json("""{"operationId":"first"}"""),
            Json("""{"maxResults":1}"""));

        now = now.AddMinutes(11);
        var expired = safety.ValidateAndConsumeCanonical(
            preview.SafetyToken,
            "network_write",
            null,
            Json("""{"operationId":"first"}"""),
            Json("""{"maxResults":1}"""),
            Json("""{"devices":[]}"""));

        Assert.True(stillValid.IsValid, stillValid.Error);
        Assert.False(expired.IsValid);
        Assert.Contains("expired", expired.Error);
        Assert.Contains("10 minutes", expired.Error);
    }

    [Fact]
    public void CanonicalToken_IsSingleUse()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        var preview = safety.CreateCanonicalPreview(
            "network_write",
            null,
            Json("""{"operationId":"first"}"""),
            "Apply 1 network write operation(s).",
            Json("""{"maxResults":1}"""),
            Json("""{"devices":[]}"""),
            "Preview only.");

        var first = Consume(safety, preview.SafetyToken);
        var second = Consume(safety, preview.SafetyToken);

        Assert.True(first.IsValid, first.Error);
        Assert.False(second.IsValid);
        Assert.Contains("expired, consumed, or unknown", second.Error);
        Assert.Equal(0, safety.ActiveTokenCount);
    }

    [Fact]
    public void AppendCanonicalAudit_RecordsCanonicalHashesAndTheExactResultDocument()
    {
        var now = new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.Zero);
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety(() => now);
        var target = Json("""{"operationId":"first"}""");
        var requestedInput = Json("""{"maxResults":1}""");
        var currentState = Json("""{"devices":[]}""");
        var result = Json("""{"phase":"apply","tool":"network_write"}""");

        safety.AppendCanonicalAudit("network_write", null, target, requestedInput, currentState, result);

        var record = JsonDocument.Parse(
            Assert.Single(File.ReadLines(Path.Combine(audit.Path, "2026-08-02.jsonl"))));
        Assert.Equal("network_write", record.RootElement.GetProperty("toolName").GetString());

        // The response document is stored as JSON, not as an escaped or shortened string.
        Assert.True(JsonElement.DeepEquals(result, record.RootElement.GetProperty("result")));
        Assert.Equal(
            WriteSafetyService.HashText(CanonicalJson.Serialize(currentState)),
            record.RootElement.GetProperty("currentStateHash").GetString());
        Assert.Equal(
            WriteSafetyService.HashText(CanonicalJson.Serialize(requestedInput)),
            record.RootElement.GetProperty("requestedInputHash").GetString());
    }

    private static WriteSafetyValidationResult Consume(WriteSafetyService safety, string token)
        => safety.ValidateAndConsumeCanonical(
            token,
            "network_write",
            null,
            Json("""{"operationId":"first"}"""),
            Json("""{"maxResults":1}"""),
            Json("""{"devices":[]}"""));

    /// <summary>
    /// A detached element parsed from literal JSON. Using elements rather than CLR types is what
    /// lets these tests vary property ORDER independently of value, which a record cannot express.
    /// </summary>
    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string ReadToken(string previewJson)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(previewJson);
        return doc.RootElement.GetProperty("safetyToken").GetString()!;
    }
}
