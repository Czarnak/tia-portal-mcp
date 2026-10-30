using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Json;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Hmi;

[Collection("Mcp protocol serial")]
public class HmiGuardedWriteMcpTests
{
    private const string Scenario = "hmi-write-roundtrip";
    private static object[] Sequence() =>
    [
        new { operationId = "table", operation = "create_tag_table", tableName = "NewTable" },
        new { operationId = "tag", operation = "create_tag", tableName = "NewTable", tagName = "NewTag", dataType = "Bool" },
        new { operationId = "alarm", operation = "create_alarm", alarmName = "NewAlarm", alarmKind = "discrete", triggerTag = "NewTag", eventText = new[] { new { culture = "en-US", text = "A & B" } } },
    ];

    [Theory]
    [InlineData(McpAccessMode.ReadWrite, "none")]
    [InlineData(McpAccessMode.Full, "policy")]
    public async Task PreviewThenApplyDependenciesNormalizeTextsWithoutElicitation(McpAccessMode mode, string confirmation)
    {
        using var audit = new TempAuditDirectory();
        var prompts = 0;
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(mode, audit.Path, Scenario, new McpClientOptions
        {
            Capabilities = new() { Elicitation = new() { Form = new() } },
            Handlers = new() { ElicitationHandler = (_, _) => { prompts++; return ValueTask.FromResult(new ElicitResult { Action = "decline" }); } },
        });
        var preview = NetworkGuardedWriteMcpTests.Document(await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = Sequence(), ["dryRun"] = true }));
        var applied = NetworkGuardedWriteMcpTests.Document(await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = Sequence() }));
        Assert.True(preview.GetProperty("success").GetBoolean(), preview.GetRawText());
        Assert.True(applied.GetProperty("success").GetBoolean(), applied.GetRawText());
        Assert.Equal(preview.GetProperty("effects").GetRawText(), applied.GetProperty("effects").GetRawText());
        Assert.Equal("table", applied.GetProperty("effects")[1].GetProperty("effect").GetProperty("dependsOn").GetString());
        Assert.Contains("&amp;", applied.GetRawText().Replace("\\u0026", "&"));
        Assert.Equal(0, prompts);
        var lines = NetworkGuardedWriteMcpTests.AuditLines(audit.Path);
        Assert.Equal(2, lines.Length);
        Assert.Equal(confirmation, JsonDocument.Parse(lines[1]).RootElement.GetProperty("confirmation").GetProperty("by").GetString());
    }

    [Fact]
    public async Task FailedCreateCarriesCleanupStopsLaterItemsAndKeepsEarlierWrite()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);
        var reply = await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = new object[]
        {
            Sequence()[0],
            new { operationId = "bad", operation = "create_tag", tableName = "NewTable", tagName = "BadTag", connection = "Connection", plcTag = "Missing" },
            new { operationId = "last", operation = "create_tag_table", tableName = "NeverCreated" },
        } });
        var root = NetworkGuardedWriteMcpTests.Document(reply);
        Assert.False(reply.IsError == true);
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        var items = root.GetProperty("batch").GetProperty("operations");
        Assert.Equal(new[] { "succeeded", "failed", "skipped" }, items.EnumerateArray().Select(i => i.GetProperty("status").GetString()));
        var result = items[1].GetProperty("result");
        Assert.Equal("deleted", result.GetProperty("cleanup").GetString());
        Assert.Equal("PlcTag", result.GetProperty("failedProperty").GetString());
        Assert.Equal(new[] { "Name", "Connection" }, result.GetProperty("appliedProperties").EnumerateArray().Select(p => p.GetString()));
        Assert.True(root.GetProperty("verification").GetProperty("success").GetBoolean(), root.GetRawText());
        var repeat = NetworkGuardedWriteMcpTests.Document(await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = new[] { Sequence()[0] } }));
        Assert.Equal("applied", repeat.GetProperty("phase").GetString());
        var rejected = repeat.GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal("engineering_rejected", rejected.GetProperty("failure").GetProperty("category").GetString());
        Assert.Equal(NativeDuplicateTableMessage, rejected.GetProperty("failure").GetProperty("engineeringError").GetProperty("message").GetString());
        Assert.Equal("notNeeded", rejected.GetProperty("result").GetProperty("cleanup").GetString());
        Assert.Equal(2, NetworkGuardedWriteMcpTests.AuditLines(audit.Path).Length);
    }

    // Verbatim installed-tool captures, 2026-10-30 (P1 table, P4 tag, P8 discrete, P9 analog alarm).
    private const string NativeDuplicateTableMessage = "Error when calling method 'Create' of type 'Siemens.Engineering.HmiUnified.HmiTags.HmiTagTableComposition'.\r\n\r\n-The value must be unique.\r\n";
    private const string NativeDuplicateTagMessage = "Error when calling method 'Create' of type 'Siemens.Engineering.HmiUnified.HmiTags.HmiTagComposition'.\r\n\r\n-The value must be unique.\r\n";
    private const string NativeDuplicateDiscreteAlarmMessage = "Error when calling method 'Create' of type 'Siemens.Engineering.HmiUnified.HmiAlarm.HmiDiscreteAlarmComposition'.\r\n\r\n-The value must be unique.\r\n";
    private const string NativeDuplicateAnalogAlarmMessage = "Error when calling method 'Create' of type 'Siemens.Engineering.HmiUnified.HmiAlarm.HmiAnalogAlarmComposition'.\r\n\r\n-The value must be unique.\r\n";

    [Theory]
    [InlineData("tag")]
    [InlineData("discrete")]
    [InlineData("analog")]
    public async Task DuplicateCreate_CarriesThatKindsNativeMessage(string kind)
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);
        object create = kind switch
        {
            "tag" => new { operationId = "x", operation = "create_tag", tableName = "Existing", tagName = "Twice", dataType = "Bool" },
            "discrete" => new { operationId = "x", operation = "create_alarm", alarmName = "Twice", alarmKind = kind, triggerTag = "T" },
            _ => new { operationId = "x", operation = "create_alarm", alarmName = "Twice", alarmKind = kind, triggerTag = "T", condition = "UpperLimit", conditionValue = 1 },
        };
        var first = NetworkGuardedWriteMcpTests.Document(await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = new[] { create } }));
        Assert.True(first.GetProperty("success").GetBoolean(), first.GetRawText());
        var again = NetworkGuardedWriteMcpTests.Document(await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = new[] { create } }));
        var expected = kind switch { "tag" => NativeDuplicateTagMessage, "discrete" => NativeDuplicateDiscreteAlarmMessage, _ => NativeDuplicateAnalogAlarmMessage };
        Assert.Equal(expected, again.GetProperty("batch").GetProperty("operations")[0].GetProperty("failure").GetProperty("engineeringError").GetProperty("message").GetString());
    }
    private const string NativeBoundDataTypeMessage = "Error when calling method 'set_DataType' of type 'Siemens.Engineering.HmiUnified.HmiTags.HmiTag'.\r\n\r\n-Set is not allowed for disabled fields\r\n";

    [Fact]
    public async Task LiveP4_DuplicateTagCreate_IsEngineeringRejected()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path, Scenario);
        var setup = NetworkGuardedWriteMcpTests.Document(await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?>
        { ["operations"] = new[] { new { operationId = "fixture", operation = "create_tag", tableName = "Existing", tagName = "CP1029_TagA", dataType = "Int" } } }));
        Assert.True(setup.GetProperty("success").GetBoolean());
        var auditBefore = NetworkGuardedWriteMcpTests.AuditLines(audit.Path).Length;
        var methodsBefore = requests.Methods().Length;

        var reply = await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = new[]
        {
            new { operationId = "p4", operation = "create_tag", tableName = "Existing", tagName = "CP1029_TagA", dataType = "Bool",
                comment = new[] { new { culture = "zu-ZA", text = "Checkpoint missing culture" } } }
        } });
        var root = NetworkGuardedWriteMcpTests.Document(reply);
        Assert.False(reply.IsError == true);
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("applied", root.GetProperty("phase").GetString());
        Assert.Equal("2.0", root.GetProperty("contractVersion").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        AssertLiveTagCreateRejection(Assert.Single(root.GetProperty("batch").GetProperty("operations").EnumerateArray()));
        Assert.True(root.GetProperty("verification").GetProperty("success").GetBoolean());
        Assert.Empty(root.GetProperty("verification").GetProperty("operations").EnumerateArray());
        Assert.DoesNotContain(requests.Methods().Skip(methodsBefore), m => m.StartsWith("hmi_delete_", StringComparison.Ordinal));
        AssertAudit(root, Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path).Skip(auditBefore)));

        var read = NetworkGuardedWriteMcpTests.Document(await harness.Client.CallToolAsync("hmi_read", new Dictionary<string, object?>
        { ["operations"] = new[] { new { operationId = "original", operation = "get_tag", tagName = "CP1029_TagA" } } }));
        Assert.Equal("Int", read.GetProperty("batch").GetProperty("operations")[0].GetProperty("result").GetProperty("tag").GetProperty("dataType").GetString());
    }

    [Fact]
    public async Task LiveL2_MiddleRejection_EarlierAppliedRejectedTypedRestSkipped()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path, Scenario);
        var setup = NetworkGuardedWriteMcpTests.Document(await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?>
        { ["operations"] = new[] { new { operationId = "fixture", operation = "create_tag", tableName = "Existing", tagName = "CP1029_TagA", dataType = "Int" } } }));
        Assert.True(setup.GetProperty("success").GetBoolean());
        var auditBefore = NetworkGuardedWriteMcpTests.AuditLines(audit.Path).Length;
        var methodsBefore = requests.Methods().Length;

        var reply = await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = new object[]
        {
            new { operationId = "l1-table", operation = "create_tag_table", tableName = "L1030_Table" },
            new { operationId = "l1-table-rename", operation = "update_tag_table", tableName = "L1030_Table", newName = "L1030_TableR" },
            new { operationId = "l1-tag-a", operation = "create_tag", tableName = "L1030_TableR", tagName = "L1030_TagA", dataType = "Int" },
            new { operationId = "l1-tag-b", operation = "create_tag", tableName = "L1030_TableR", tagName = "L1030_TagB", dataType = "Int" },
            new { operationId = "l1-tag-rename", operation = "update_tag", tagName = "L1030_TagA", newName = "L1030_TagA2" },
            new { operationId = "l2-dup-tag", operation = "create_tag", tableName = "Existing", tagName = "CP1029_TagA", dataType = "Bool" },
            new { operationId = "l1-discrete", operation = "create_alarm", alarmKind = "discrete", alarmName = "L1030_Discrete", triggerTag = "CP1029_TagA", bitNumber = 3, alarmClass = "Alarm", eventText = new[] { new { culture = "en-US", text = "L1 discrete" } } },
            new { operationId = "l1-discrete-update", operation = "update_alarm", alarmKind = "discrete", alarmName = "L1030_Discrete", priority = 2 },
            new { operationId = "l1-analog", operation = "create_alarm", alarmKind = "analog", alarmName = "L1030_Analog", triggerTag = "CP1029_Reuse", condition = "UpperLimit", conditionValue = 40, alarmClass = "Warning", eventText = new[] { new { culture = "en-US", text = "Checkpoint analog" } } },
            new { operationId = "l1-analog-delete", operation = "delete_alarm", alarmKind = "analog", alarmName = "L1030_Analog" },
            new { operationId = "l1-tag-delete", operation = "delete_tag", tagName = "L1030_TagB" },
            new { operationId = "l1-table-delete", operation = "delete_tag_table", tableName = "L1030_TableR" },
        } });
        var root = NetworkGuardedWriteMcpTests.Document(reply);
        Assert.False(reply.IsError == true);
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("applied", root.GetProperty("phase").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        var items = root.GetProperty("batch").GetProperty("operations").EnumerateArray().ToArray();
        Assert.Equal(12, items.Length);
        Assert.All(items.Take(5), i => Assert.Equal("succeeded", i.GetProperty("status").GetString()));
        AssertLiveTagCreateRejection(items[5]);
        Assert.All(items.Skip(6), i =>
        {
            Assert.Equal("skipped", i.GetProperty("status").GetString());
            Assert.Equal("earlierOperationFailed", i.GetProperty("skipReason").GetString());
        });
        var verification = root.GetProperty("verification");
        Assert.True(verification.GetProperty("success").GetBoolean());
        var checks = verification.GetProperty("operations").EnumerateArray().ToArray();
        Assert.Equal(items.Take(5).Select(i => i.GetProperty("operationId").GetString()), checks.Select(i => i.GetProperty("operationId").GetString()));
        Assert.Equal(new[] { "superseded", "trusted", "superseded", "trusted", "trusted" }, checks.Select(i => i.GetProperty("check").GetString()));
        Assert.DoesNotContain(requests.Methods().Skip(methodsBefore), m => m is "hmi_create_alarm" or "hmi_update_alarm" or "hmi_delete_alarm" or "hmi_delete_tag" or "hmi_delete_tag_table");
        AssertAudit(root, Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path).Skip(auditBefore)));
    }

    private static void AssertLiveTagCreateRejection(JsonElement item)
    {
        Assert.Equal("failed", item.GetProperty("status").GetString());
        var failure = item.GetProperty("failure");
        Assert.Equal("engineering_rejected", failure.GetProperty("category").GetString());
        Assert.Equal("TIA Portal rejected the operation: " + NativeDuplicateTagMessage, failure.GetProperty("message").GetString());
        var engineering = failure.GetProperty("engineeringError");
        Assert.Equal(new[] { "inner", "message", "type" }, engineering.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("EngineeringTargetInvocationException", engineering.GetProperty("type").GetString());
        Assert.Equal(NativeDuplicateTagMessage, engineering.GetProperty("message").GetString());
        Assert.Empty(engineering.GetProperty("inner").EnumerateArray());
        var outcome = item.GetProperty("result");
        Assert.Equal("CP1029_TagA", outcome.GetProperty("objectName").GetString());
        Assert.Empty(outcome.GetProperty("appliedProperties").EnumerateArray());
        Assert.Equal("Create", outcome.GetProperty("failedProperty").GetString());
        Assert.Equal("notNeeded", outcome.GetProperty("cleanup").GetString());
        Assert.Equal(JsonValueKind.Null, outcome.GetProperty("cleanupMessage").ValueKind);
    }

    [Fact]
    public async Task DuplicateCreate_ReachesPortalAndIsTypedWithoutCleanup()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);
        var reply = await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = new object[]
        {
            Sequence()[0],
            new { operationId = "duplicate", operation = "create_tag_table", tableName = "existing" },
            new { operationId = "last", operation = "create_tag_table", tableName = "NeverCreated" },
        } });
        var root = NetworkGuardedWriteMcpTests.Document(reply);
        Assert.False(reply.IsError == true);
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        var items = root.GetProperty("batch").GetProperty("operations");
        Assert.Equal(new[] { "succeeded", "failed", "skipped" }, items.EnumerateArray().Select(i => i.GetProperty("status").GetString()));
        var failure = items[1].GetProperty("failure");
        Assert.Equal("engineering_rejected", failure.GetProperty("category").GetString());
        Assert.Equal("TIA Portal rejected the operation: " + NativeDuplicateTableMessage, failure.GetProperty("message").GetString());
        var engineering = failure.GetProperty("engineeringError");
        Assert.Equal("EngineeringTargetInvocationException", engineering.GetProperty("type").GetString());
        Assert.Equal(NativeDuplicateTableMessage, engineering.GetProperty("message").GetString());
        Assert.Equal(0, engineering.GetProperty("inner").GetArrayLength());
        Assert.Equal("notNeeded", items[1].GetProperty("result").GetProperty("cleanup").GetString());
        var verification = root.GetProperty("verification");
        Assert.True(verification.GetProperty("success").GetBoolean());
        var check = Assert.Single(verification.GetProperty("operations").EnumerateArray());
        Assert.Equal("table", check.GetProperty("operationId").GetString());
        Assert.DoesNotContain(requests.Methods(), m => m.StartsWith("hmi_delete_", StringComparison.Ordinal));
        AssertAudit(root, Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path)));
    }

    [Fact]
    public async Task BoundDataTypeRejection_KeepsAppliedProperties()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path, Scenario);
        var bound = NetworkGuardedWriteMcpTests.Document(await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = new[]
        { new { operationId = "bound", operation = "create_tag", tableName = "Existing", tagName = "Bound", connection = "Connection", plcTag = "Motor" } } }));
        Assert.True(bound.GetProperty("success").GetBoolean(), bound.GetRawText());
        var auditBefore = NetworkGuardedWriteMcpTests.AuditLines(audit.Path).Length;
        var reply = await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = new[]
        { new { operationId = "retype", operation = "update_tag", tagName = "Bound", newName = "Renamed", dataType = "Int" } } });
        var root = NetworkGuardedWriteMcpTests.Document(reply);
        Assert.False(reply.IsError == true);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        var item = root.GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal("engineering_rejected", item.GetProperty("failure").GetProperty("category").GetString());
        Assert.Equal("EngineeringTargetInvocationException", item.GetProperty("failure").GetProperty("engineeringError").GetProperty("type").GetString());
        Assert.Equal(NativeBoundDataTypeMessage, item.GetProperty("failure").GetProperty("engineeringError").GetProperty("message").GetString());
        Assert.Equal(new[] { "Name" }, item.GetProperty("result").GetProperty("appliedProperties").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal("DataType", item.GetProperty("result").GetProperty("failedProperty").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("result").GetProperty("cleanup").ValueKind);
        Assert.Empty(root.GetProperty("verification").GetProperty("operations").EnumerateArray());
        Assert.Equal(auditBefore + 1, NetworkGuardedWriteMcpTests.AuditLines(audit.Path).Length);
    }

    [Fact]
    public async Task BoundDataTypeRejectionOnCreate_DeletesTheCreatedTag()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path, Scenario);
        var root = NetworkGuardedWriteMcpTests.Document(await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = new[]
        { new { operationId = "bound", operation = "create_tag", tableName = "Existing", tagName = "Bound", connection = "Connection", plcTag = "Motor", dataType = "Int" } } }));
        var item = root.GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal("engineering_rejected", item.GetProperty("failure").GetProperty("category").GetString());
        Assert.Equal("DataType", item.GetProperty("result").GetProperty("failedProperty").GetString());
        Assert.Equal("deleted", item.GetProperty("result").GetProperty("cleanup").GetString());
        var retry = NetworkGuardedWriteMcpTests.Document(await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = new[]
        { new { operationId = "plain", operation = "create_tag", tableName = "Existing", tagName = "Bound", dataType = "Int" } } }));
        Assert.True(retry.GetProperty("success").GetBoolean(), retry.GetRawText());
    }

    [Fact]
    public async Task AppliedCall_IssuesNoPostWriteRead()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);
        var root = NetworkGuardedWriteMcpTests.Document(await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = Sequence() }));
        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        var methods = requests.Methods().ToArray();
        var first = Array.FindIndex(methods, m => m.StartsWith("hmi_create_", StringComparison.Ordinal));
        Assert.True(first >= 0);
        Assert.DoesNotContain(methods.Skip(first), m => m is "hmi_validate" || m.StartsWith("hmi_get_", StringComparison.Ordinal) || m.StartsWith("hmi_list_", StringComparison.Ordinal));
        Assert.All(root.GetProperty("verification").GetProperty("operations").EnumerateArray(), c => Assert.Equal("trusted", c.GetProperty("check").GetString()));
    }

    [Fact]
    public async Task DanglingReferenceCreate_IsTrusted()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);
        var root = NetworkGuardedWriteMcpTests.Document(await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = new[]
        { new { operationId = "alarm", operation = "create_alarm", alarmName = "Orphan", alarmKind = "discrete", triggerTag = "MissingTag" } } }));
        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        var check = Assert.Single(root.GetProperty("verification").GetProperty("operations").EnumerateArray());
        Assert.Equal("trusted", check.GetProperty("check").GetString());
        Assert.DoesNotContain("hmi_validate", requests.Methods());
    }

    [Fact]
    public async Task NameCollision_ReachesWorker()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);
        var twice = new object[] { Sequence()[0], new { operationId = "again", operation = "create_tag_table", tableName = "NewTable" } };
        var root = NetworkGuardedWriteMcpTests.Document(await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = twice }));
        Assert.Equal(2, requests.Methods().Count(m => m == "hmi_create_tag_table"));
        Assert.DoesNotContain(root.GetProperty("guards").EnumerateArray(), g => g.GetProperty("id").GetString() == "hmi_name_collision");
    }

    [Fact]
    public async Task DryRun_ReadsOnlyPlanningEvidence()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);
        var operations = Sequence().Append(new { operationId = "delete", operation = "delete_tag_table", tableName = "Existing" }).ToArray();
        var root = NetworkGuardedWriteMcpTests.Document(await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = operations, ["dryRun"] = true }));
        Assert.Equal("preview", root.GetProperty("phase").GetString());
        var hmi = requests.Methods().Where(m => m.StartsWith("hmi_", StringComparison.Ordinal) || m == "read_cross_references").ToArray();
        Assert.Equal(["hmi_list_hmi_devices", "hmi_list_tag_tables", "hmi_list_alarms", "hmi_list_tags"], hmi);
    }

    [Fact]
    public async Task DryRun_DeleteOfExistingTag_ReadsItsCrossReferences()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);
        var seed = NetworkGuardedWriteMcpTests.Document(await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = new[]
        { new { operationId = "seed", operation = "create_tag", tableName = "Existing", tagName = "Seed", dataType = "Bool" } } }));
        Assert.True(seed.GetProperty("success").GetBoolean(), seed.GetRawText());
        static bool Hmi(string m) => m.StartsWith("hmi_", StringComparison.Ordinal) || m == "read_cross_references";
        var before = requests.Methods().Count(Hmi);
        var root = NetworkGuardedWriteMcpTests.Document(await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = new[]
        { new { operationId = "delete", operation = "delete_tag", tagName = "Seed" } }, ["dryRun"] = true }));
        Assert.Equal("preview", root.GetProperty("phase").GetString());
        Assert.Equal(["hmi_list_hmi_devices", "hmi_list_tag_tables", "hmi_get_tag", "hmi_list_alarms", "read_cross_references"], requests.Methods().Where(Hmi).Skip(before));
    }

    [Theory]
    [InlineData("confirm", "true")]
    [InlineData("acknowledge", "[]")]
    [InlineData("unknown", "true")]
    [InlineData("dryRun", "null")]
    [InlineData("dryRun", "\"true\"")]
    public async Task StrictWrapperRejectsWithoutWorkerOrAudit(string key, string json)
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path);
        var before = requests.Methods();
        var result = await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = Sequence(), [key] = JsonSerializer.Deserialize<JsonElement>(json) });
        ArgumentRejectionAssert.IsCanonicalValidationError(result, "hmi_write");
        Assert.Equal("error", result.StructuredContent!.Value.GetProperty("phase").GetString());
        Assert.Equal("hmi_write", result.StructuredContent.Value.GetProperty("tool").GetString());
        Assert.Equal(before, requests.Methods());
        Assert.Empty(NetworkGuardedWriteMcpTests.AuditLines(audit.Path));
    }

    [Fact]
    public async Task EscapedMaximumIdentitiesDeniedBeforeBindingWithOneAudit()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path);
        var before = requests.Methods();
        var result = await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = Enumerable.Range(0, 50).Select(i => new { operationId = new string('\u0001', 253) + i.ToString("D3"), operation = "create_tag_table", tableName = "T" + i }).ToArray() });
        var root = NetworkGuardedWriteMcpTests.Document(result);
        Assert.True(result.IsError);
        Assert.Equal("validation_error", root.GetProperty("error").GetProperty("category").GetString());
        Assert.Equal(before, requests.Methods());
        Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path));
    }

    [Theory]
    [InlineData("CleanupFails", "failed")]
    [InlineData("Missing", "deleted")]
    public async Task FailedCreateReadbackDistinguishesCleanupOutcome(string plcTag, string cleanup)
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path, Scenario);
        var reply = await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = new[]
        { new { operationId = "a", operation = "create_tag", tableName = "Existing", tagName = "Bad", connection = "Connection", plcTag } } });
        var root = NetworkGuardedWriteMcpTests.Document(reply);
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(cleanup, root.GetProperty("batch").GetProperty("operations")[0].GetProperty("result").GetProperty("cleanup").GetString());
        Assert.True(root.GetProperty("verification").GetProperty("success").GetBoolean());
        AssertAudit(root, Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path)));
    }

    [Fact]
    public async Task OversizedEffectOmitsDeliveryButPreservesExecutionAndExactAudit()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path, Scenario);
        var reply = await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = new[]
        { new { operationId = "a", operation = "create_tag", tableName = "Existing", tagName = "Big", dataType = "Bool", comment = new[] { new { culture = "en-US", text = new string('x', 61000) } } } } });
        var root = NetworkGuardedWriteMcpTests.Document(reply);
        Assert.False(reply.IsError == true);
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("succeeded", root.GetProperty("batch").GetProperty("operations")[0].GetProperty("status").GetString());
        Assert.True(root.GetProperty("verification").GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("effects")[0].GetProperty("effect").ValueKind);
        Assert.True(CanonicalJson.Serialize(root).Length <= 180000);
        AssertAudit(root, Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path)));
    }

    [Fact]
    public async Task MalformedFailureAfterCreateKeepsCanonicalResponseAndAuditUnderDiagnosticCompaction()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path, Scenario);
        var operations = new[] { new { operationId = "a", operation = "create_tag", tableName = "Existing", tagName = "Malformed", connection = "Connection", plcTag = "MalformedOutcome", comment = new[] { new { culture = "en-US", text = new string('x', 61000) } } } };
        var reply = await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = operations });
        var root = NetworkGuardedWriteMcpTests.Document(reply);
        Assert.False(reply.IsError == true);
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("applied", root.GetProperty("phase").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        var item = root.GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal("failed", item.GetProperty("status").GetString());
        Assert.Equal("protocol_error", item.GetProperty("failure").GetProperty("category").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("result").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, root.GetProperty("omission").ValueKind);
        Assert.DoesNotContain("malformed evidence", root.GetRawText());
        Assert.True(CanonicalJson.Serialize(root).Length <= 180000);
        AssertAudit(root, Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path)));
        var after = NetworkGuardedWriteMcpTests.Document(await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = operations, ["dryRun"] = true }));
        Assert.Equal("preview", after.GetProperty("phase").GetString());
    }

    [Fact]
    public async Task PortalLostWithOutcome_KeepsOutcomeAndInvalidatesBinding()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path, Scenario);
        var operations = new object[]
        {
            new { operationId = "a", operation = "create_tag", tableName = "Existing", tagName = "A", dataType = "Bool" },
            new { operationId = "b", operation = "create_tag", tableName = "Existing", tagName = "B", connection = "Connection", plcTag = "PortalLost" },
            new { operationId = "c", operation = "create_tag", tableName = "Existing", tagName = "C", dataType = "Bool" },
        };
        var root = NetworkGuardedWriteMcpTests.Document(await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = operations }));
        var items = root.GetProperty("batch").GetProperty("operations");
        Assert.Equal("succeeded", items[0].GetProperty("status").GetString());
        Assert.Equal("failed", items[1].GetProperty("status").GetString());
        Assert.Equal("portal_lost", items[1].GetProperty("failure").GetProperty("category").GetString());
        Assert.Contains("Create", items[1].GetProperty("result").GetRawText());
        Assert.Equal("skipped", items[2].GetProperty("status").GetString());
        Assert.False(harness.WorkerClient.BindingSnapshot.IsVerified);
    }

    private static void AssertAudit(JsonElement root, string line)
    {
        var audit = JsonDocument.Parse(line).RootElement;
        var text = CanonicalJson.Serialize(root);
        Assert.Equal(text, audit.GetProperty("responseText").GetString());
        Assert.Equal("sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant(), audit.GetProperty("responseHash").GetString());
    }

    [Fact]
    public async Task AllNineOperationsRoundtripWithRenamesAndDeletes()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path, Scenario);
        var result = await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = Sequence().Concat(new object[]
        {
            new { operationId = "renameTable", operation = "update_tag_table", tableName = "NewTable", newName = "RenamedTable" },
            new { operationId = "renameTag", operation = "update_tag", tagName = "NewTag", newName = "RenamedTag", acquisitionCycle = "T1s" },
            new { operationId = "renameAlarm", operation = "update_alarm", alarmName = "NewAlarm", alarmKind = "discrete", newName = "RenamedAlarm", priority = 1 },
            new { operationId = "deleteAlarm", operation = "delete_alarm", alarmName = "RenamedAlarm", alarmKind = "discrete" },
            new { operationId = "deleteTag", operation = "delete_tag", tagName = "RenamedTag" },
            new { operationId = "deleteTable", operation = "delete_tag_table", tableName = "RenamedTable" },
        }).ToArray() });
        var root = NetworkGuardedWriteMcpTests.Document(result);
        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        Assert.All(root.GetProperty("batch").GetProperty("operations").EnumerateArray(), i => Assert.Equal("succeeded", i.GetProperty("status").GetString()));
        AssertAudit(root, Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path)));
    }

    [Fact]
    public async Task ToolSchemaContainsOnlyOperationsAndBooleanDryRun()
    {
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite);
        var tool = Assert.Single(await harness.Client.ListToolsAsync(), t => t.Name == "hmi_write").ProtocolTool;
        var properties = tool.InputSchema.GetProperty("properties");
        Assert.Equal(new[] { "dryRun", "operations" }, properties.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("boolean", properties.GetProperty("dryRun").GetProperty("type").GetString());
        Assert.False(properties.GetProperty("dryRun").GetProperty("default").GetBoolean());
        Assert.False(tool.InputSchema.GetProperty("additionalProperties").GetBoolean());
        Assert.NotNull(tool.OutputSchema);
    }

    [Theory]
    [InlineData("50.0", "50.5")]
    [InlineData("50.5", "50.0")]
    public async Task AnalogCreateAndUpdateRoundtrip_IsTrusted(string created, string updated)
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path, Scenario);
        var tag = NetworkGuardedWriteMcpTests.Document(await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?>
        { ["operations"] = new[] { new { operationId = "tag", operation = "create_tag", tableName = "Existing", tagName = "Signal", dataType = "Int" } } }));
        Assert.True(tag.GetProperty("success").GetBoolean(), tag.GetRawText());
        foreach (var (operation, input) in new[] { ("create_alarm", created), ("update_alarm", updated) })
        {
            var reply = await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?>
            { ["operations"] = new[] { new { operationId = "alarm", operation, alarmKind = "analog", alarmName = "Analog", triggerTag = "Signal", condition = "UpperLimit", conditionValue = JsonSerializer.Deserialize<JsonElement>(input) } } });
            var root = NetworkGuardedWriteMcpTests.Document(reply);
            Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
            var check = Assert.Single(root.GetProperty("verification").GetProperty("operations").EnumerateArray());
            Assert.Equal("trusted", check.GetProperty("check").GetString());
            Assert.True(check.GetProperty("success").GetBoolean());
            AssertAudit(root, NetworkGuardedWriteMcpTests.AuditLines(audit.Path)[^1]);
        }
    }

    [Fact]
    public async Task ReadOnlyToolIsAbsentAndCannotDispatch()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadOnly, audit.Path);
        Assert.DoesNotContain(await harness.Client.ListToolsAsync(), t => t.Name == "hmi_write");
        await Assert.ThrowsAsync<McpProtocolException>(() => harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = Sequence() }).AsTask());
        Assert.Empty(requests.Methods());
        Assert.Empty(NetworkGuardedWriteMcpTests.AuditLines(audit.Path));
    }

    [Fact]
    public async Task FiftyMaximumAsciiIdentitiesAreAdmittedAndPlanned()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path, Scenario);
        var result = await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["dryRun"] = true, ["operations"] = Enumerable.Range(0, 50).Select(i => new { operationId = new string('x', 253) + i.ToString("D3"), operation = "create_tag_table", tableName = "Table" + i }).ToArray() });
        var root = NetworkGuardedWriteMcpTests.Document(result);
        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        Assert.Equal(50, root.GetProperty("effects").GetArrayLength());
        AssertAudit(root, Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path)));
    }
}
