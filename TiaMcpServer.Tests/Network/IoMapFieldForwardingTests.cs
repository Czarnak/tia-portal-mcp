using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Network;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Network;

/// <summary>
/// Explicit field-forwarding evidence for the I/O-map read options. The reflective
/// <see cref="NetworkFieldForwardingTests.FlatFieldOperations"/> sweep only covers string/int
/// fields, so these tests assert the four new fields — <c>deviceName</c>, <c>plcName</c>,
/// <c>includeIoDetails</c>, <c>includeTagMatches</c> — forward exactly once through the echo
/// scenario, and that the internal snapshot default forwards none of them.
/// </summary>
public class IoMapFieldForwardingTests
{
    private static OpennessWorkerClient CreateClient()
        => new(new ProjectSessionBinding(null), logger: null, workerExecutablePath: FakeWorkerLocator.Locate());

    [Fact]
    public async Task ReadHardwareConfig_ForwardsEveryIoMapFieldExactlyOnce()
    {
        var operation = new NetworkOperationRequest
        {
            OperationId = "read-io-map",
            Operation = "read_hardware_config",
            ProjectPath = "echo",
            DeviceName = "ET 200SP station_1",
            PlcName = "PLC_1",
            IncludeIoDetails = true,
            IncludeTagMatches = true,
        };

        using var client = CreateClient();
        var result = await NetworkWorkerInvoker.InvokeReadAsync(client, operation);

        Assert.True(result.Success, result.Error);
        using var document = JsonDocument.Parse(result.Payload);
        var root = document.RootElement;

        Assert.Single(root.EnumerateObject().Where(property => property.NameEquals("deviceName")));
        Assert.Single(root.EnumerateObject().Where(property => property.NameEquals("plcName")));
        Assert.Single(root.EnumerateObject().Where(property => property.NameEquals("includeIoDetails")));
        Assert.Single(root.EnumerateObject().Where(property => property.NameEquals("includeTagMatches")));

        Assert.Equal("ET 200SP station_1", root.GetProperty("deviceName").GetString());
        Assert.Equal("PLC_1", root.GetProperty("plcName").GetString());
        Assert.True(root.GetProperty("includeIoDetails").GetBoolean());
        Assert.True(root.GetProperty("includeTagMatches").GetBoolean());
    }

    [Fact]
    public async Task ReadHardwareConfig_ForwardsFalseIncludeFlagsWhenNotRequested()
    {
        var operation = new NetworkOperationRequest
        {
            OperationId = "read-plain",
            Operation = "read_hardware_config",
            ProjectPath = "echo",
        };

        using var client = CreateClient();
        var result = await NetworkWorkerInvoker.InvokeReadAsync(client, operation);

        Assert.True(result.Success, result.Error);
        using var document = JsonDocument.Parse(result.Payload);
        var root = document.RootElement;

        Assert.False(root.GetProperty("includeIoDetails").GetBoolean());
        Assert.False(root.GetProperty("includeTagMatches").GetBoolean());
    }

    /// <summary>
    /// The internal network-write snapshot (NetworkSafetySnapshot.ReadCurrentStateAsync) calls
    /// <c>ReadHardwareConfigAsync(projectPath)</c> with the defaults. That call must forward the
    /// I/O-map fields at their DEFAULT values (no narrowing, no details), so snapshots stay
    /// lightweight and their canonical state hash stays byte-identical to the legacy shape. The
    /// flat WorkerRequest always serializes every field, so the guarantee is "defaults", not
    /// "absent".
    /// </summary>
    [Fact]
    public async Task SnapshotDefaultRead_ForwardsOnlyDefaultNarrowingAndIoMapFields()
    {
        using var client = CreateClient();
        var result = await client.ReadHardwareConfigAsync("echo");

        Assert.True(result.Success, result.Error);
        using var document = JsonDocument.Parse(result.Payload);
        var root = document.RootElement;

        Assert.Equal(JsonValueKind.Null, root.GetProperty("deviceName").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("plcName").ValueKind);
        Assert.False(root.GetProperty("includeIoDetails").GetBoolean());
        Assert.False(root.GetProperty("includeTagMatches").GetBoolean());
    }

    [Theory]
    [InlineData("IncludeIoDetails", typeof(bool))]
    [InlineData("IncludeTagMatches", typeof(bool))]
    public void WorkerRequest_ExposesTheIoMapFlagsAsPlainBools(string propertyName, Type expectedType)
    {
        var property = typeof(WorkerRequest).GetProperty(propertyName);
        Assert.NotNull(property);
        Assert.Equal(expectedType, property!.PropertyType);
    }
}
