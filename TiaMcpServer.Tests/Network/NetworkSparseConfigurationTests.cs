using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Network;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public class NetworkSparseConfigurationTests
{
    private const string PartialPayload = """{"deviceName":"PLC_1","appliedSettings":{"Address":"192.168.0.10"},"skippedSettings":{"IoSystem":"No IO connector."},"messages":[]}""";

    private static NetworkOperationRequest Configure(string id, bool includeIoSystem = true) => new()
    {
        OperationId = id,
        Operation = "configure_network_device",
        Target = new NetworkObjectTarget { DeviceName = "PLC_1", NodeId = "node-1" },
        Changes = new NetworkDeviceChanges
        {
            IpAddress = "192.168.0.10",
            IoSystem = includeIoSystem
                ? new NetworkIoSystemTarget { SubnetId = "subnet-1", Number = 100 }
                : null,
        },
    };

    [Fact]
    public void Configuration_ReportsOnlyRequestedKeys()
    {
        var item = NetworkPayloadContract.Project(Configure("configure"), WorkerCallResult.Ok(PartialPayload));
        var result = JsonSerializer.Deserialize<ConfigureNetworkDeviceResultInfo>(
            item.Result!.Value, WorkerJson.PayloadOptionsFor(typeof(ConfigureNetworkDeviceResultInfo)))!;
        var applied = result.AppliedSettings;
        var skipped = result.SkippedSettings;

        Assert.Equal(new[] { "Address" }, applied.Keys);
        Assert.Equal(new[] { "IoSystem" }, skipped.Keys);
        Assert.Equal("192.168.0.10", applied["Address"]);
        Assert.Equal("No IO connector.", skipped["IoSystem"]);
    }

    [Fact]
    public async Task PartialConfiguration_RetainsResultAndStopsBatch()
    {
        var invocations = new List<string>();
        var batch = await StructuredOperationBatchExecutionEngine.ApplyWritesAsync(
            new[] { Configure("partial"), Configure("later") },
            operation =>
            {
                invocations.Add(operation.OperationId);
                return Task.FromResult(WorkerCallResult.Ok(PartialPayload));
            },
            NetworkPayloadContract.Project);
        var partial = batch.Operations[0];
        var later = batch.Operations[1];

        Assert.Equal("failed", partial.Status);
        Assert.Equal("worker_operation_failed", partial.Failure!.Category);
        Assert.NotNull(partial.Result);
        Assert.Equal("Address", Assert.Single(partial.Result.Value.GetProperty("appliedSettings").EnumerateObject()).Name);
        Assert.Equal("IoSystem", Assert.Single(partial.Result.Value.GetProperty("skippedSettings").EnumerateObject()).Name);
        Assert.Equal("earlierOperationFailed", later.SkipReason);
        Assert.Equal(new[] { "partial" }, invocations);
        Assert.Equal(1, batch.Counts.Failed);
        Assert.Equal(1, batch.Counts.Skipped);
    }

    [Theory]
    [InlineData("""{"deviceName":"PLC_1","appliedSettings":{},"skippedSettings":{"Address":"Read only.","IoSystem":"No IO connector."},"messages":[]}""", true)]
    [InlineData("""{"deviceName":"PLC_1","appliedSettings":{},"skippedSettings":{"Address":"Read only."},"messages":[]}""", false)]
    public void AllSkipped_RetainsTypedResult(string payload, bool includeIoSystem)
    {
        var item = NetworkPayloadContract.Project(Configure("all-skipped", includeIoSystem), WorkerCallResult.Ok(payload));

        Assert.Equal("failed", item.Status);
        Assert.Equal("worker_operation_failed", item.Failure!.Category);
        Assert.NotNull(item.Result);
        var result = JsonSerializer.Deserialize<ConfigureNetworkDeviceResultInfo>(
            item.Result.Value, WorkerJson.PayloadOptionsFor(typeof(ConfigureNetworkDeviceResultInfo)))!;
        Assert.Equal("PLC_1", result.DeviceName);
        Assert.Empty(result.AppliedSettings);
        Assert.Equal("Read only.", result.SkippedSettings["Address"]);
        Assert.Equal(
            includeIoSystem ? new[] { "Address", "IoSystem" } : new[] { "Address" },
            result.SkippedSettings.Keys);
        Assert.Empty(result.Messages);
    }
}
