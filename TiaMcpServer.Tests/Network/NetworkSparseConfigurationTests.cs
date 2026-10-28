using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Network;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public class NetworkSparseConfigurationTests
{
    private const string PartialPayload = """{"deviceName":"PLC_1","appliedSettings":{"Address":"192.168.0.10"},"messages":[]}""";

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

        Assert.Equal(new[] { "Address" }, applied.Keys);
        Assert.Equal("192.168.0.10", applied["Address"]);
    }

    [Fact]
    public void PartialConfiguration_SucceedsAndRetainsResult()
    {
        var partial = NetworkPayloadContract.Project(Configure("partial"), WorkerCallResult.Ok(PartialPayload));

        Assert.Equal("succeeded", partial.Status);
        Assert.Null(partial.Failure);
        Assert.NotNull(partial.Result);
        Assert.Equal("Address", Assert.Single(partial.Result.Value.GetProperty("appliedSettings").EnumerateObject()).Name);
    }

    [Theory]
    [InlineData("""{"deviceName":"PLC_1","appliedSettings":{},"messages":[]}""", true)]
    [InlineData("""{"deviceName":"PLC_1","appliedSettings":{},"messages":[]}""", false)]
    public void EmptyAppliedSubset_RetainsTypedResult(string payload, bool includeIoSystem)
    {
        var item = NetworkPayloadContract.Project(Configure("empty-applied", includeIoSystem), WorkerCallResult.Ok(payload));

        Assert.Equal("succeeded", item.Status);
        Assert.Null(item.Failure);
        Assert.NotNull(item.Result);
        var result = JsonSerializer.Deserialize<ConfigureNetworkDeviceResultInfo>(
            item.Result.Value, WorkerJson.PayloadOptionsFor(typeof(ConfigureNetworkDeviceResultInfo)))!;
        Assert.Equal("PLC_1", result.DeviceName);
        Assert.Empty(result.AppliedSettings);
        Assert.Empty(result.Messages);
    }
}
