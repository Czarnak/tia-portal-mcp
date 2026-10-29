using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Json;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Network;

[Collection("Mcp protocol serial")]
public sealed class NetworkGuardedWriteMcpTests
{
    [Fact]
    public async Task RegisteredSchema_NetworkWriteIsContractTwo()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path, "network-guarded");
        var args = Arguments(); args["dryRun"] = true;
        var reply = await harness.Client.CallToolAsync("network_write", args);
        Assert.Equal("2.0", Document(reply).GetProperty("contractVersion").GetString());
        Assert.Equal(JsonValueKind.Null, Document(reply).GetProperty("verification").ValueKind);
        var tool = Assert.Single(await harness.Client.ListToolsAsync(), t => t.Name == "network_write").ProtocolTool;
        var properties = tool.OutputSchema!.Value.GetProperty("properties").GetProperty("verification").GetProperty("properties");
        Assert.Equal(new[] { "omission", "operations", "success" }, properties.EnumerateObject().Select(p => p.Name).Order());
    }

    [Fact]
    public async Task RegisteredSchema_CurrentSettingsAllowsSnapshotStringsAndExplicitNulls()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path, "network-guarded");
        var reply = await harness.Client.CallToolAsync("network_write", new Dictionary<string, object?>
        {
            ["dryRun"] = true,
            ["operations"] = new object[]
            {
                new { operationId = "configure", operation = "configure_network_device",
                    target = new { deviceName = "PLC_Grouped", nodeId = "node-2" },
                    changes = new { pnDeviceNameAutoGeneration = false } },
                new { operationId = "update", operation = "update_subnet",
                    target = new { kind = "subnet", subnetId = "subnet-1" },
                    subnetChanges = new { name = "Renamed", highestAddress = 31, transmissionSpeed = "Baud187500" } }
            }
        });
        var root = Document(reply);
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("preview", root.GetProperty("phase").GetString());
        var effects = root.GetProperty("effects");
        Assert.Equal(JsonValueKind.Null, effects[0].GetProperty("effect").GetProperty("currentSettings")
            .GetProperty("PnDeviceNameAutoGeneration").ValueKind);
        var subnet = effects[1].GetProperty("effect").GetProperty("currentSettings");
        Assert.Equal("Network", subnet.GetProperty("Name").GetString());
        Assert.Equal(JsonValueKind.Null, subnet.GetProperty("HighestAddress").ValueKind);
        Assert.Equal(JsonValueKind.Null, subnet.GetProperty("TransmissionSpeed").ValueKind);
        var tool = Assert.Single(await harness.Client.ListToolsAsync(), t => t.Name == "network_write").ProtocolTool;
        var types = tool.OutputSchema!.Value.GetProperty("properties").GetProperty("effects").GetProperty("items")
            .GetProperty("properties").GetProperty("effect").GetProperty("properties").GetProperty("currentSettings")
            .GetProperty("additionalProperties").GetProperty("type");
        Assert.Equal(JsonValueKind.Array, types.ValueKind);
        Assert.Equal(new[] { "string", "null" }, types.EnumerateArray().Select(type => type.GetString()));
    }

    [Theory]
    [InlineData(256)]
    [InlineData(257)]
    [InlineData(200000)]
    public async Task OperationNameDiagnosticLimit_DeniesBeforeWorkerAndRetainsCatalogAudit(int length)
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite,
            audit.Path, "network-guarded");
        var before = requests.Methods();
        var name = new string('n', length);

        var reply = await harness.Client.CallToolAsync("network_write", new Dictionary<string, object?>
        {
            ["operations"] = new[] { new { operationId = "id", operation = name } }
        });

        Assert.True(reply.IsError);
        var root = Document(reply);
        var text = Assert.Single(reply.Content.OfType<TextContentBlock>()).Text;
        Assert.Equal("validation_error", root.GetProperty("error").GetProperty("category").GetString());
        Assert.True(text.Length < 6000);
        if (length <= 256) Assert.Contains(name, text);
        else
        {
            Assert.Contains("Each operation name must be at most 256 characters.", text);
            Assert.DoesNotContain(name, text);
        }
        Assert.Equal(before, requests.Methods());
        Assert.Single(AuditLines(audit.Path));
    }

    [Fact]
    public async Task QualifiedSelector_RoundTripsRegisteredSchema()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path);
        var tools = await harness.Client.ListToolsAsync();
        foreach (var name in new[] { "network_read", "network_write" })
        {
            var schema = Assert.Single(tools, t => t.Name == name).ProtocolTool.InputSchema;
            Assert.Contains("\"interfacePath\"", schema.GetRawText());
            Assert.Contains("\"positionNumber\"", schema.GetRawText());
        }
        var selector = new NetworkObjectSelectorInfo
        {
            Kind = "node", DeviceName = "PLC_1", NodeId = "E1", InterfaceName = "X1",
            InterfacePath = new() { new() { Name = "X1", PositionNumber = 1 } }
        };
        var requestTarget = JsonSerializer.Deserialize<TiaMcpServer.Network.NetworkObjectTarget>(
            CanonicalJson.Serialize(selector), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var validation = TiaMcpServer.Network.NetworkOperationCatalog.ValidateWrite(new[]
        {
            new TiaMcpServer.Network.NetworkOperationRequest
            {
                OperationId = "configure", Operation = "configure_network_device", Target = requestTarget,
                Changes = new() { IpAddress = "192.168.0.10" }
            }
        });
        Assert.True(validation.IsValid, validation.Error);
        var reply = await harness.Client.CallToolAsync("network_write", new Dictionary<string, object?> { ["operations"] = Array.Empty<object>(), ["dryRun"] = true });
        var publicRoot = Document(reply);
        Assert.Equal("2.0", publicRoot.GetProperty("contractVersion").GetString());
    }

    [Theory]
    [InlineData("confirm", "false")]
    [InlineData("acknowledge", "[]")]
    [InlineData("unknown", "true")]
    [InlineData("dryRun", "\"true\"")]
    [InlineData("dryRun", "null")]
    public async Task LegacyOrMalformedArguments_DoNotReachWorker(string key, string json)
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path,
            "network-guarded");
        var before = requests.Methods();
        Assert.Contains("hello", before);
        Assert.Contains("get_project_status", before);
        var args = Arguments();
        args[key] = JsonSerializer.Deserialize<JsonElement>(json);
        var reply = await harness.Client.CallToolAsync("network_write", args);
        ArgumentRejectionAssert.IsCanonicalValidationError(reply, "network_write");
        Assert.Equal("error", reply.StructuredContent!.Value.GetProperty("phase").GetString());
        Assert.Equal("network_write", reply.StructuredContent.Value.GetProperty("tool").GetString());
        Assert.Equal(before, requests.Methods());
        Assert.Empty(AuditLines(audit.Path));
    }

    [Fact]
    public async Task RegisteredSchema_OnlyOperationsAndBooleanDryRun()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path);
        var tool = Assert.Single(await harness.Client.ListToolsAsync(), t => t.Name == "network_write").ProtocolTool;
        Assert.Equal(new[] { "dryRun", "operations" }, tool.InputSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order());
        Assert.False(tool.InputSchema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("boolean", tool.InputSchema.GetProperty("properties").GetProperty("dryRun").GetProperty("type").GetString());
        Assert.False(tool.InputSchema.GetProperty("properties").GetProperty("dryRun").GetProperty("default").GetBoolean());
        Assert.Contains("verification", tool.OutputSchema!.Value.GetProperty("properties").EnumerateObject().Select(p => p.Name));
        Assert.Contains("omission", tool.OutputSchema.Value.GetProperty("properties").EnumerateObject().Select(p => p.Name));
    }

    [Theory]
    [InlineData(McpAccessMode.ReadWrite, null, false)]
    [InlineData(McpAccessMode.ReadWrite, false, true)]
    [InlineData(McpAccessMode.ReadWrite, true, true)]
    [InlineData(McpAccessMode.Full, null, true)]
    [InlineData(McpAccessMode.Full, false, false)]
    [InlineData(McpAccessMode.Full, true, false)]
    public async Task NetworkNeverElicits(McpAccessMode mode, bool? dryRun, bool supported)
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        var prompts = 0;
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(mode, audit.Path,
            "network-guarded", supported ? Options(() => prompts++, "decline") : null);
        var args = Arguments();
        if (dryRun.HasValue) args["dryRun"] = dryRun.Value;
        var reply = await harness.Client.CallToolAsync("network_write", args);
        var root = Document(reply);
        Assert.False(reply.IsError == true);
        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("omission").ValueKind);
        Assert.Equal(dryRun == true ? "preview" : "applied", root.GetProperty("phase").GetString());
        Assert.Equal(0, prompts);
        Assert.Equal(dryRun != true, requests.Methods().Contains("delete_subnet"));
        var guard = Assert.Single(root.GetProperty("guards").EnumerateArray());
        Assert.Equal("network_delete_connected_subnet", guard.GetProperty("id").GetString());
        Assert.Equal("info", guard.GetProperty("severity").GetString());
        Assert.Equal(JsonValueKind.Null, guard.GetProperty("acknowledged").ValueKind);
        Assert.Single(AuditLines(audit.Path));
    }

    [Fact]
    public async Task LifecycleStillConfirms()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = new LifecycleProtocolFixture();
        using var uiOpen = new FakeWorkerUiOpenProject(fixture.SourcePath);
        var prompts = 0;
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite,
            audit.Path, fixture.SourcePath, Options(() => prompts++, "accept"));
        var reply = await harness.Client.CallToolAsync("save_project", new Dictionary<string, object?>());
        Assert.False(reply.IsError == true);
        Assert.Equal(1, prompts);
    }

    // Captured in 2026-10-29-network-write-native-rejections-live-rerun.md, L11c/L13mini (English UI).
    private const string LiveConfigureRejectionMessage = "Error when calling method 'ConnectToSubnet' of type 'Siemens.Engineering.HW.Node'."
        + "\r\n\r\nAssignment to subnet failed.";
    private const string LiveAddressRejectionMessage = "Error when calling method 'set_Address' of type 'Siemens.Engineering.HW.Node'."
        + "\r\n\r\nnot-an-ip is not a valid IP address";

    [Fact]
    public async Task LiveL11c_RejectedSubnetConnection_StopsItemWithAppliedSubset()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full,
            audit.Path, "network-guarded-rejected-setting");

        var reply = await harness.Client.CallToolAsync("network_write", new Dictionary<string, object?>
        {
            ["operations"] = new object[]
            {
                new { operationId = "l11c-partial", operation = "configure_network_device",
                    target = new { kind = "node", deviceName = "PLC_Grouped", nodeId = "node-2" },
                    changes = new { ipAddress = "192.168.203.42", subnetMask = "255.255.254.0", subnet = new { subnetId = "590-3" } } },
                new { operationId = "l11c-after", operation = "create_subnet", subnet = new { name = "ZZ_L11c_Skipped", networkType = "Ethernet" } }
            }
        });

        var root = Document(reply);
        Assert.False(reply.IsError == true);
        Assert.Equal("applied", root.GetProperty("phase").GetString());
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        var items = root.GetProperty("batch").GetProperty("operations").EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        AssertLiveConfigureRejection(items[0], LiveConfigureRejectionMessage);
        var result = items[0].GetProperty("result");
        Assert.Equal("PLC_Grouped", result.GetProperty("deviceName").GetString());
        Assert.Empty(result.GetProperty("messages").EnumerateArray());
        var applied = result.GetProperty("appliedSettings");
        Assert.Equal(new[] { "Address", "SubnetMask" }, applied.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("192.168.203.42", applied.GetProperty("Address").GetString());
        Assert.Equal("255.255.254.0", applied.GetProperty("SubnetMask").GetString());
        Assert.Equal("skipped", items[1].GetProperty("status").GetString());
        Assert.Equal("earlierOperationFailed", items[1].GetProperty("skipReason").GetString());
        Assert.True(root.GetProperty("verification").GetProperty("success").GetBoolean());
        Assert.Empty(root.GetProperty("verification").GetProperty("operations").EnumerateArray());
        Assert.Single(requests.Methods(), m => m == "configure_network_device");
        Assert.DoesNotContain("create_subnet", requests.Methods());
        Assert.Single(AuditLines(audit.Path));
    }

    [Fact]
    public async Task LiveL13_SecondItemRejected_FirstAppliedSecondTypedThirdSkipped()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full,
            audit.Path, "network-guarded-rejected-setting");

        var reply = await harness.Client.CallToolAsync("network_write", new Dictionary<string, object?>
        {
            ["operations"] = new object[]
            {
                new { operationId = "l13m-first", operation = "create_subnet", subnet = new { name = "ZZ_L13mini_First", networkType = "Ethernet" } },
                new { operationId = "l13m-rejected", operation = "configure_network_device",
                    target = new { kind = "node", deviceName = "PLC_Grouped", nodeId = "node-2" }, changes = new { ipAddress = "not-an-ip" } },
                new { operationId = "l13m-after", operation = "create_subnet", subnet = new { name = "ZZ_L13mini_Skipped", networkType = "Ethernet" } }
            }
        });

        var root = Document(reply);
        Assert.False(reply.IsError == true);
        Assert.Equal("applied", root.GetProperty("phase").GetString());
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        var batch = root.GetProperty("batch");
        var counts = batch.GetProperty("counts");
        Assert.Equal((1, 1, 1, 0), (counts.GetProperty("succeeded").GetInt32(), counts.GetProperty("failed").GetInt32(),
            counts.GetProperty("skipped").GetInt32(), counts.GetProperty("omitted").GetInt32()));
        var items = batch.GetProperty("operations").EnumerateArray().ToArray();
        Assert.Equal("succeeded", items[0].GetProperty("status").GetString());
        Assert.Equal("ZZ_L13mini_First", items[0].GetProperty("result").GetProperty("name").GetString());
        AssertLiveConfigureRejection(items[1], LiveAddressRejectionMessage);
        Assert.Empty(items[1].GetProperty("result").GetProperty("appliedSettings").EnumerateObject());
        Assert.Equal("skipped", items[2].GetProperty("status").GetString());
        Assert.Equal("earlierOperationFailed", items[2].GetProperty("skipReason").GetString());
        var guard = Assert.Single(root.GetProperty("guards").EnumerateArray());
        Assert.Equal(("partial_write_no_rollback", "info", "l13m-rejected"), (guard.GetProperty("id").GetString(),
            guard.GetProperty("severity").GetString(), guard.GetProperty("operationId").GetString()));
        var verification = root.GetProperty("verification");
        Assert.True(verification.GetProperty("success").GetBoolean());
        var check = Assert.Single(verification.GetProperty("operations").EnumerateArray());
        Assert.Equal(("l13m-first", "trusted"), (check.GetProperty("operationId").GetString(), check.GetProperty("check").GetString()));
        Assert.True(check.GetProperty("success").GetBoolean());
        Assert.Single(requests.Methods(), m => m == "create_subnet");
        Assert.Single(requests.Methods(), m => m == "configure_network_device");
        Assert.Single(AuditLines(audit.Path));
    }

    private static void AssertLiveConfigureRejection(JsonElement item, string message)
    {
        Assert.Equal("failed", item.GetProperty("status").GetString());
        var failure = item.GetProperty("failure");
        Assert.Equal("engineering_rejected", failure.GetProperty("category").GetString());
        Assert.Equal("TIA Portal rejected the operation: " + message, failure.GetProperty("message").GetString());
        var error = failure.GetProperty("engineeringError");
        Assert.Equal(new[] { "inner", "message", "type" }, error.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("EngineeringTargetInvocationException", error.GetProperty("type").GetString());
        Assert.Equal(message, error.GetProperty("message").GetString());
        Assert.Empty(error.GetProperty("inner").EnumerateArray());
    }

    internal static Dictionary<string, object?> Arguments() => new()
    {
        ["operations"] = new[] { NetworkGuardedWriteFixture.Delete() }
    };
    internal static string[] AuditLines(string directory) => Directory.Exists(directory)
        ? Directory.GetFiles(directory, "writes-*.jsonl").SelectMany(File.ReadLines).ToArray() : [];
    internal static JsonElement Document(CallToolResult reply)
    {
        var root = Assert.IsType<JsonElement>(reply.StructuredContent);
        Assert.Equal(CanonicalJson.Serialize(root), Assert.Single(reply.Content.OfType<TextContentBlock>()).Text);
        return root;
    }
    private static McpClientOptions Options(Action prompt, string action) => new()
    {
        Capabilities = new() { Elicitation = new() { Form = new() } },
        Handlers = new() { ElicitationHandler = (_, _) =>
        {
            prompt();
            return ValueTask.FromResult(new ElicitResult { Action = action,
                Content = new Dictionary<string, JsonElement> { ["confirm"] = JsonSerializer.SerializeToElement(true) } });
        } }
    };
}
