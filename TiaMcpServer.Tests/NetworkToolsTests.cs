using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
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
    private const string StableScenario = "ok-with-warnings";

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

    private static async Task<string> NetworkRead(
        OpennessWorkerClient? client,
        NetworkOperationRequest[] operations)
    {
        var task = RequiredToolMethod("NetworkReadTools", "NetworkRead")
            .Invoke(null, new object?[] { client, operations }) as Task<string>;
        Assert.NotNull(task);
        return await task!;
    }

    private static async Task<string> NetworkWrite(
        OpennessWorkerClient? client,
        WriteSafetyService safety,
        NetworkOperationRequest[] operations,
        bool confirm = false,
        string? safetyToken = null)
    {
        var task = RequiredToolMethod("NetworkWriteTools", "NetworkWrite")
            .Invoke(null, new object?[] { client, safety, operations, confirm, safetyToken }) as Task<string>;
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
        DeviceName = deviceName,
        IpAddress = "192.168.0.10",
    };

    private static string SafetyToken(string preview)
    {
        using var document = JsonDocument.Parse(preview);
        var token = document.RootElement.GetProperty("safetyToken").GetString();
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

        Assert.Contains("at least one", result);
    }

    [Fact]
    public async Task NetworkRead_RejectsWriteOperationBeforeWorkerStartup()
    {
        var result = await NetworkRead(null, new[] { AddDevice("w1") });

        Assert.Contains("write operation", result);
    }

    [Fact]
    public async Task NetworkRead_PreservesJsonPayloadAsAStringAndCopiesWarnings()
    {
        using var client = CreateClient();

        var result = await NetworkRead(client, new[] { ReadHardware("r1", StableScenario) });

        using var document = JsonDocument.Parse(result);
        var operation = document.RootElement.GetProperty("operations")[0];
        Assert.Equal(JsonValueKind.String, operation.GetProperty("result").ValueKind);
        using var payload = JsonDocument.Parse(operation.GetProperty("result").GetString()!);
        Assert.True(payload.RootElement.GetProperty("hello").GetBoolean());
        Assert.Equal(2, operation.GetProperty("warnings").GetArrayLength());
    }

    [Fact]
    public async Task NetworkRead_OneFailureDoesNotStopALaterOperation()
    {
        using var client = CreateClient();
        var operations = new[]
        {
            ReadHardware("bad", "worker-error"),
            ReadHardware("good", "ok"),
        };

        var result = await NetworkRead(client, operations);

        using var document = JsonDocument.Parse(result);
        var items = document.RootElement.GetProperty("operations");
        Assert.Equal("failed", items[0].GetProperty("status").GetString());
        Assert.Equal("succeeded", items[1].GetProperty("status").GetString());
    }

    [Fact]
    public async Task NetworkRead_BudgetOmissionInstructsRetryThroughNetworkRead()
    {
        using var client = CreateClient();
        var operations = Enumerable.Range(0, 4)
            .Select(index => new NetworkOperationRequest
            {
                OperationId = $"search-{index}",
                Operation = "search_equipment_catalog",
                ProjectPath = "echo",
                Query = new string((char)('a' + index), 50_000),
            })
            .ToArray();

        var result = await NetworkRead(client, operations);

        Assert.Contains("[OMITTED", result);
        Assert.Contains("network_read", result);
    }

    [Theory]
    [InlineData(false, null, "safetyToken")]
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

        Assert.Contains(expectedText, result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NetworkWrite_PreviewBindsExactOrderedTargetsAndPerformsOnlyOneStateRead()
    {
        using var audit = new TempAuditDirectory();
        using var client = CreateClient();
        var operations = new[]
        {
            AddDevice("first", "ok", "PLC_1"),
            ConfigureDevice("second", "ok", "PLC_2"),
        };

        var preview = await NetworkWrite(client, audit.CreateSafety(), operations);

        using var document = JsonDocument.Parse(preview);
        Assert.Equal("network_write", document.RootElement.GetProperty("toolName").GetString());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("safetyToken").GetString()));
        var targets = document.RootElement.GetProperty("target");
        Assert.Equal("first", targets[0].GetProperty("operationId").GetString());
        Assert.Equal("add_network_device", targets[0].GetProperty("operation").GetString());
        Assert.Equal("second", targets[1].GetProperty("operationId").GetString());
        Assert.Equal("configure_network_device", targets[1].GetProperty("operation").GetString());

        var nextRead = await client.ReadHardwareConfigAsync("ok");
        Assert.Contains("\"seq\":2", nextRead.Payload);
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

        Assert.Contains("different target", result);
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

        Assert.Contains("input does not match", result);
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

        Assert.Contains("same project path", result);
    }

    [Fact]
    public async Task NetworkWrite_ReadOnlyDefenseRejectsBeforeSnapshot()
    {
        using var audit = new TempAuditDirectory();
        using var client = CreateClient(McpAccessMode.ReadOnly);

        var result = await NetworkWrite(client, audit.CreateSafety(), new[] { AddDevice("w1") });

        Assert.Contains("read-only mode", result);
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

        Assert.Contains("Safety token", result);
        Assert.DoesNotContain("Could not read current state", result);
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

        Assert.Contains("boom", result);
        Assert.False(JsonDocument.Parse(result).RootElement.GetProperty("success").GetBoolean());
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

        using var document = JsonDocument.Parse(result);
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("network_write", document.RootElement.GetProperty("tool").GetString());
        Assert.Single(Directory.GetFiles(audit.Path, "*.jsonl"));
        Assert.Single(File.ReadLines(Directory.GetFiles(audit.Path, "*.jsonl").Single()));
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
