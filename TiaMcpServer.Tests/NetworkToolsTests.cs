using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts;
using TiaMcpServer.Network;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests;

public class NetworkToolsTests
{
    /// <summary>A FakeWorker scenario whose hardware read AND write payloads both satisfy their
    /// declared Phase 2 result contracts, and whose hardware state is stable across requests so a
    /// preview/apply round trip binds.</summary>
    private const string StableScenario = "network-roundtrip";

    private static Type RequiredToolType(string name)
    {
        var type = typeof(NetworkOperationRequest).Assembly.GetType($"TiaMcpServer.Network.{name}");
        Assert.NotNull(type);
        return type!;
    }

    private static MethodInfo RequiredToolMethod(string typeName, string methodName)
    {
        var method = RequiredToolType(typeName).GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        return method!;
    }

    private static async Task<CallToolResult> NetworkRead(
        OpennessWorkerClient? client,
        NetworkOperationRequest[] operations)
    {
        var task = RequiredToolMethod("NetworkReadTools", "NetworkRead")
            .Invoke(null, new object?[] { client, operations }) as Task<CallToolResult>;
        Assert.NotNull(task);
        return await task!;
    }

    private static string ReadText(CallToolResult result)
        => Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    private static JsonElement ReadStructured(CallToolResult result)
        => Assert.IsType<JsonElement>(result.StructuredContent);

    private static async Task<CallToolResult> NetworkWrite(
        OpennessWorkerClient? client,
        WriteSafetyService safety,
        NetworkOperationRequest[] operations,
        bool confirm = false,
        string? safetyToken = null)
    {
        var task = RequiredToolMethod("NetworkWriteTools", "NetworkWrite")
            .Invoke(null, new object?[] { client, safety, operations, confirm, safetyToken }) as Task<CallToolResult>;
        Assert.NotNull(task);
        return await task!;
    }

    private static OpennessWorkerClient CreateClient(McpAccessMode mode = McpAccessMode.ReadWrite)
        => new(
            new ProjectSessionBinding(null),
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate(),
            accessPolicy: new OperationAccessPolicy(mode));

    private static NetworkOperationRequest ReadHardware(string id, string projectPath) => new()
    {
        OperationId = id,
        Operation = "read_hardware_config",
        ProjectPath = projectPath,
    };

    private static NetworkOperationRequest AddDevice(
        string id,
        string projectPath = StableScenario,
        string deviceName = "PLC_1",
        string? deviceItemName = null) => new()
    {
        OperationId = id,
        Operation = "add_network_device",
        ProjectPath = projectPath,
        TypeIdentifier = "OrderNumber:6ES7 510-1DJ01-0AB0/V2.0",
        DeviceName = deviceName,
        DeviceItemName = deviceItemName,
    };

    private static NetworkOperationRequest ConfigureDevice(
        string id,
        string projectPath = StableScenario,
        string deviceName = "PLC_1") => new()
    {
        OperationId = id,
        Operation = "configure_network_device",
        ProjectPath = projectPath,
        Target = new NetworkDeviceTarget { DeviceName = deviceName, NodeId = "node-1" },
        Changes = new NetworkDeviceChanges { IpAddress = "192.168.0.10" },
    };

    private static string SafetyToken(CallToolResult preview)
    {
        var token = ReadStructured(preview).GetProperty("preview").GetProperty("safetyToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        return token!;
    }

    [Fact]
    public void DedicatedNetworkTools_HaveExactMcpMetadataAndDescriptions()
    {
        var readType = RequiredToolType("NetworkReadTools");
        var writeType = RequiredToolType("NetworkWriteTools");
        Assert.NotNull(readType.GetCustomAttribute<McpServerToolTypeAttribute>());
        Assert.NotNull(writeType.GetCustomAttribute<McpServerToolTypeAttribute>());

        var read = RequiredToolMethod("NetworkReadTools", "NetworkRead");
        var readAttribute = read.GetCustomAttribute<McpServerToolAttribute>();
        Assert.NotNull(readAttribute);
        Assert.Equal("network_read", readAttribute!.Name);
        Assert.True(readAttribute.ReadOnly);
        Assert.False(readAttribute.Destructive);
        Assert.False(readAttribute.OpenWorld);
        Assert.False(string.IsNullOrWhiteSpace(read.GetCustomAttribute<DescriptionAttribute>()?.Description));
        Assert.False(string.IsNullOrWhiteSpace(
            read.GetParameters().Single(parameter => parameter.Name == "operations")
                .GetCustomAttribute<DescriptionAttribute>()?.Description));

        var write = RequiredToolMethod("NetworkWriteTools", "NetworkWrite");
        var writeAttribute = write.GetCustomAttribute<McpServerToolAttribute>();
        Assert.NotNull(writeAttribute);
        Assert.Equal("network_write", writeAttribute!.Name);
        Assert.False(writeAttribute.ReadOnly);
        Assert.True(writeAttribute.Destructive);
        Assert.False(writeAttribute.OpenWorld);
        Assert.True(writeAttribute.UseStructuredContent);
        Assert.Equal(typeof(NetworkWriteResponse), writeAttribute.OutputSchemaType);
        Assert.False(string.IsNullOrWhiteSpace(write.GetCustomAttribute<DescriptionAttribute>()?.Description));
        foreach (var parameterName in new[] { "operations", "confirm", "safetyToken" })
        {
            Assert.False(string.IsNullOrWhiteSpace(
                write.GetParameters().Single(parameter => parameter.Name == parameterName)
                    .GetCustomAttribute<DescriptionAttribute>()?.Description));
        }
    }

    [Fact]
    public async Task NetworkRead_RejectsEmptyBatchBeforeWorkerStartup()
    {
        var result = await NetworkRead(null, Array.Empty<NetworkOperationRequest>());

        Assert.True(result.IsError);
        Assert.Contains("at least one", ReadText(result));
    }

    [Fact]
    public async Task NetworkRead_RejectsWriteOperationBeforeWorkerStartup()
    {
        var result = await NetworkRead(null, new[] { AddDevice("w1") });

        Assert.True(result.IsError);
        Assert.Contains("write operation", ReadText(result));
    }

    [Fact]
    public async Task NetworkRead_ReturnsDeclaredJsonResultAndCopiesWarnings()
    {
        using var client = CreateClient();

        var result = await NetworkRead(client, new[] { ReadHardware("r1", "network-read-warnings") });

        var operation = ReadStructured(result).GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal("succeeded", operation.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Object, operation.GetProperty("result").ValueKind);
        Assert.Equal(0, operation.GetProperty("result").GetProperty("devices").GetArrayLength());
        Assert.Equal(2, operation.GetProperty("warnings").GetArrayLength());
    }

    [Fact]
    public async Task NetworkRead_ContinuesAfterWorkerFailureAndAfterPayloadContractFailure()
    {
        using var client = CreateClient();
        var operations = new[]
        {
            ReadHardware("worker-failure", "worker-error"),

            // The worker reports success, but its payload is not a HardwareConfigInfo: a
            // contract violation must fail the item instead of publishing unusable data.
            ReadHardware("contract-failure", "ok"),
            ReadHardware("good", "network-roundtrip"),
        };

        var result = await NetworkRead(client, operations);

        Assert.False(result.IsError);
        var root = ReadStructured(result);
        Assert.False(root.GetProperty("success").GetBoolean());

        var items = root.GetProperty("batch").GetProperty("operations");
        Assert.Equal("failed", items[0].GetProperty("status").GetString());
        Assert.Equal(
            WorkerFailureCategories.WorkerOperationFailed,
            items[0].GetProperty("failure").GetProperty("category").GetString());

        Assert.Equal("failed", items[1].GetProperty("status").GetString());
        Assert.Equal(
            WorkerFailureCategories.ProtocolError,
            items[1].GetProperty("failure").GetProperty("category").GetString());
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("result").ValueKind);

        // The rejected payload must never be echoed back; "seq" is the only field it contained.
        Assert.DoesNotContain("seq", items[1].GetProperty("failure").GetProperty("message").GetString());

        Assert.Equal("succeeded", items[2].GetProperty("status").GetString());
        Assert.Equal(2, root.GetProperty("batch").GetProperty("counts").GetProperty("failed").GetInt32());
        Assert.Equal(1, root.GetProperty("batch").GetProperty("counts").GetProperty("succeeded").GetInt32());
    }

    // confirm=false with no token is the ordinary preview, not an invalid combination; the preview
    // path is covered by NetworkWrite_PreviewBindsExactOrderedTargetsAndPerformsOnlyOneStateRead.
    [Theory]
    [InlineData(false, "supplied", "confirm=false")]
    [InlineData(true, null, "preview")]
    public async Task NetworkWrite_RejectsInvalidConfirmationCombinations(
        bool confirm,
        string? token,
        string expectedText)
    {
        using var audit = new TempAuditDirectory();
        using var client = CreateClient();

        var result = await NetworkWrite(
            client,
            audit.CreateSafety(),
            new[] { AddDevice("w1") },
            confirm,
            token);

        Assert.True(result.IsError);
        Assert.Contains(expectedText, ReadText(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NetworkWrite_PreviewBindsExactOrderedTargetsAndPerformsOnlyOneStateRead()
    {
        using var audit = new TempAuditDirectory();
        using var client = CreateClient();
        var operations = new[]
        {
            AddDevice("first", "network-state-seq", "PLC_1"),
            ConfigureDevice("second", "network-state-seq", "PLC_2"),
        };

        var preview = await NetworkWrite(client, audit.CreateSafety(), operations);

        var root = ReadStructured(preview);
        Assert.False(preview.IsError);
        Assert.Equal("network_write", root.GetProperty("tool").GetString());
        Assert.Equal("preview", root.GetProperty("phase").GetString());
        var target = root.GetProperty("preview").GetProperty("target");
        Assert.False(string.IsNullOrWhiteSpace(
            root.GetProperty("preview").GetProperty("safetyToken").GetString()));
        Assert.Equal("first", target[0].GetProperty("operationId").GetString());
        Assert.Equal("add_network_device", target[0].GetProperty("operation").GetString());
        Assert.Equal("PLC_1", target[0].GetProperty("deviceName").GetString());
        Assert.Equal("second", target[1].GetProperty("operationId").GetString());
        Assert.Equal("configure_network_device", target[1].GetProperty("operation").GetString());
        Assert.Equal("PLC_2", target[1].GetProperty("deviceName").GetString());

        // The scenario stamps its request sequence into the hardware payload, so the third request
        // proves the preview issued exactly one state read.
        var nextRead = await client.ReadHardwareConfigAsync("network-state-seq");
        Assert.Contains("seq:2", nextRead.Payload);
    }

    [Fact]
    public async Task NetworkWrite_ApplyRejectsReorderedInput()
    {
        using var audit = new TempAuditDirectory();
        using var client = CreateClient();
        var safety = audit.CreateSafety();
        var operations = new[] { AddDevice("first"), ConfigureDevice("second") };
        var token = SafetyToken(await NetworkWrite(client, safety, operations));

        var result = await NetworkWrite(client, safety, operations.Reverse().ToArray(), true, token);

        Assert.True(result.IsError);
        Assert.Contains("different target", ReadText(result));
    }

    [Fact]
    public async Task NetworkWrite_ApplyRejectsChangedField()
    {
        using var audit = new TempAuditDirectory();
        using var client = CreateClient();
        var safety = audit.CreateSafety();
        var operations = new[] { AddDevice("first", deviceItemName: "rack-original") };
        var token = SafetyToken(await NetworkWrite(client, safety, operations));
        var changed = new[] { AddDevice("first", deviceItemName: "rack-changed") };

        var result = await NetworkWrite(client, safety, changed, true, token);

        // deviceItemName is a requested-input field, not target evidence: the rejection must name
        // the changed INPUT rather than a changed target.
        Assert.True(result.IsError);
        Assert.Contains("input does not match", ReadText(result));
    }

    [Fact]
    public async Task NetworkWrite_RejectsMixedProjectPathsBeforeWorkerStartup()
    {
        using var audit = new TempAuditDirectory();
        var operations = new[]
        {
            AddDevice("first", @"C:\a.ap21"),
            ConfigureDevice("second", @"C:\b.ap21"),
        };

        var result = await NetworkWrite(null, audit.CreateSafety(), operations);

        Assert.True(result.IsError);
        Assert.Contains("same project path", ReadText(result));
    }

    [Fact]
    public async Task NetworkWrite_ReadOnlyDefenseRejectsBeforeSnapshot()
    {
        using var audit = new TempAuditDirectory();
        using var client = CreateClient(McpAccessMode.ReadOnly);

        var result = await NetworkWrite(client, audit.CreateSafety(), new[] { AddDevice("w1") });

        Assert.True(result.IsError);
        Assert.Contains("read-only mode", ReadText(result));
        Assert.Equal(
            WorkerFailureCategories.AccessDenied,
            ReadStructured(result).GetProperty("error").GetProperty("category").GetString());
    }

    [Fact]
    public async Task NetworkWrite_BadTokenIsRejectedBeforeHardwareStateRead()
    {
        using var audit = new TempAuditDirectory();
        using var client = CreateClient();
        var operation = AddDevice("w1", "worker-error");

        var result = await NetworkWrite(
            client,
            audit.CreateSafety(),
            new[] { operation },
            confirm: true,
            safetyToken: "bogus-token");

        Assert.True(result.IsError);
        Assert.Contains("Safety token", ReadText(result));
        Assert.DoesNotContain("Could not read current", ReadText(result));
    }

    [Fact]
    public async Task NetworkWrite_SnapshotFailureReturnsErrorWithoutWriting()
    {
        using var audit = new TempAuditDirectory();
        using var client = CreateClient();

        var result = await NetworkWrite(
            client,
            audit.CreateSafety(),
            new[] { AddDevice("w1", "worker-error") });

        var root = ReadStructured(result);
        Assert.True(result.IsError);
        Assert.Contains("boom", ReadText(result));
        Assert.Equal("error", root.GetProperty("phase").GetString());
        Assert.False(root.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task NetworkWrite_StateDecodeFailureIsAWholeToolErrorAndIssuesNoToken()
    {
        using var audit = new TempAuditDirectory();
        using var client = CreateClient();
        var safety = audit.CreateSafety();

        // Scenario "ok" answers read_hardware_config with {"seq":N}, which is not a
        // HardwareConfigInfo: no token may be bound to a state that failed its contract.
        var result = await NetworkWrite(client, safety, new[] { AddDevice("w1", "ok") });

        var root = ReadStructured(result);
        Assert.True(result.IsError);
        Assert.Equal("error", root.GetProperty("phase").GetString());
        Assert.Equal(
            WorkerFailureCategories.ProtocolError,
            root.GetProperty("error").GetProperty("category").GetString());
        Assert.DoesNotContain("seq", ReadText(result));
        Assert.Equal(0, safety.ActiveTokenCount);
    }

    [Fact]
    public async Task NetworkWrite_SuccessfulApplyAppendsOneAuditRecord()
    {
        using var audit = new TempAuditDirectory();
        using var client = CreateClient();
        var safety = audit.CreateSafety();
        var operations = new[] { AddDevice("w1") };
        var token = SafetyToken(await NetworkWrite(client, safety, operations));

        var result = await NetworkWrite(client, safety, operations, confirm: true, safetyToken: token);

        var root = ReadStructured(result);
        Assert.False(result.IsError);
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("network_write", root.GetProperty("tool").GetString());
        Assert.Equal("apply", root.GetProperty("phase").GetString());

        var auditFile = Assert.Single(Directory.GetFiles(audit.Path, "*.jsonl"));
        var record = JsonDocument.Parse(Assert.Single(File.ReadLines(auditFile)));

        // The audit entry carries the exact canonical response document that was returned.
        Assert.True(JsonElement.DeepEquals(root, record.RootElement.GetProperty("result")));
    }

    [Fact]
    public async Task NetworkWrite_ApplyFailureSkipsLaterOperationsAndWarnsThatNoRollbackWasAttempted()
    {
        using var audit = new TempAuditDirectory();
        using var client = CreateClient();
        var safety = audit.CreateSafety();
        var operations = new[]
        {
            AddDevice("first", "network-write-item-failure"),
            ConfigureDevice("second", "network-write-item-failure"),
        };
        var token = SafetyToken(await NetworkWrite(client, safety, operations));

        var result = await NetworkWrite(client, safety, operations, confirm: true, safetyToken: token);

        var root = ReadStructured(result);
        Assert.False(result.IsError);
        Assert.False(root.GetProperty("success").GetBoolean());

        var items = root.GetProperty("batch").GetProperty("operations");
        Assert.Equal("failed", items[0].GetProperty("status").GetString());
        Assert.Equal("skipped", items[1].GetProperty("status").GetString());
        Assert.Equal(
            StructuredOperationSkipReasons.EarlierOperationFailed,
            items[1].GetProperty("skipReason").GetString());

        var warning = items[0].GetProperty("warnings")[0].GetString();
        Assert.Contains("may already have changed", warning);
        Assert.Contains("no rollback", warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NetworkWriteStructuredApplyEngine_StopsOnProtocolErrorAndSkipsLaterOperations()
    {
        var operations = new[] { AddDevice("first"), ConfigureDevice("second") };
        var invocations = 0;

        var batch = await StructuredOperationBatchExecutionEngine.ApplyWritesAsync(
            operations,
            _ =>
            {
                invocations++;

                // The worker reports SUCCESS; only projecting its payload reveals the contract
                // violation, so the stop decision must be made after projection, not before it.
                return Task.FromResult(WorkerCallResult.Ok("""{"unexpected":true}"""));
            },
            NetworkPayloadContract.Project);

        Assert.Equal(1, invocations);
        Assert.Equal(OperationBatchStatus.Failed, batch.Operations[0].Status);
        Assert.Equal(
            WorkerFailureCategories.ProtocolError,
            batch.Operations[0].Failure!.Category);
        Assert.Equal(OperationBatchStatus.Skipped, batch.Operations[1].Status);
        Assert.Equal(
            StructuredOperationSkipReasons.EarlierOperationFailed,
            batch.Operations[1].SkipReason);
        Assert.Equal(1, batch.Counts.Failed);
        Assert.Equal(1, batch.Counts.Skipped);
    }

    [Fact]
    public async Task NetworkWriteApplyEngine_FirstFailureStopsAndMarksLaterItemsSkipped()
    {
        var operations = new[] { AddDevice("first"), ConfigureDevice("second") };
        var invocations = 0;

        var results = await OperationBatchExecutionEngine.ApplyWritesAsync(
            operations,
            _ =>
            {
                invocations++;
                return Task.FromResult(WorkerCallResult.Fail(
                    WorkerFailureCategories.WorkerOperationFailed,
                    "first write failed"));
            });

        Assert.Equal(1, invocations);
        Assert.Equal(OperationBatchStatus.Failed, results[0].Status);
        Assert.Equal(OperationBatchStatus.Skipped, results[1].Status);
    }
}
