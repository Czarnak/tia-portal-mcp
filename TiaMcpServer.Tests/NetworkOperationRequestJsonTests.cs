using System.Text.Json;
using TiaMcpServer.Network;
using Xunit;

namespace TiaMcpServer.Tests;

public class NetworkOperationRequestJsonTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void ConfigureTargetAndChanges_BindFromCamelCaseJson()
    {
        const string json = """
            {"operationId":"op1","operation":"configure_network_device",
             "target":{"deviceName":"IO_Device_1","nodeId":"7"},
             "changes":{"ipAddress":"192.168.0.10","subnetMask":"255.255.255.0","pnDeviceName":"io-device-1",
                        "subnet":{"subnetId":"S1"},"ioSystem":{"subnetId":"S1","number":100}}}
            """;

        var operation = JsonSerializer.Deserialize<NetworkOperationRequest>(json, WebOptions);

        Assert.NotNull(operation);
        Assert.Equal("IO_Device_1", operation!.Target!.DeviceName);
        Assert.Equal("7", operation.Target!.NodeId);
        Assert.Equal("192.168.0.10", operation.Changes!.IpAddress);
        Assert.Equal("255.255.255.0", operation.Changes!.SubnetMask);
        Assert.Equal("io-device-1", operation.Changes!.PnDeviceName);
        Assert.Equal("S1", operation.Changes!.Subnet!.SubnetId);
        Assert.Equal("S1", operation.Changes!.IoSystem!.SubnetId);
        Assert.Equal(100, operation.Changes!.IoSystem!.Number);
    }

    [Fact]
    public void OmittedChangeMembers_StayNullSoTheyMeanNoChange()
    {
        const string json = """
            {"operationId":"op1","operation":"configure_network_device",
             "target":{"deviceName":"IO_Device_1","nodeId":"7"},
             "changes":{"ipAddress":"192.168.0.10"}}
            """;

        var operation = JsonSerializer.Deserialize<NetworkOperationRequest>(json, WebOptions);

        Assert.NotNull(operation);
        Assert.Null(operation!.Changes!.SubnetMask);
        Assert.Null(operation.Changes!.PnDeviceName);
        Assert.Null(operation.Changes!.Subnet);
        Assert.Null(operation.Changes!.IoSystem);
    }

    [Fact]
    public void MisspelledNestedChangeField_IsRejected()
    {
        const string json = """
            {"operationId":"op1","operation":"configure_network_device",
             "target":{"deviceName":"IO_Device_1","nodeId":"7"},
             "changes":{"ip_adress":"192.168.0.10"}}
            """;

        var exception = Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<NetworkOperationRequest>(json, WebOptions));

        Assert.Contains("ip_adress", exception.Message);
    }

    [Fact]
    public void UnknownNestedTargetField_IsRejected()
    {
        // "xUnknownField" is not a property on NetworkObjectTarget and must be rejected.
        const string json = """
            {"operationId":"op1","operation":"configure_network_device",
             "target":{"deviceName":"IO_Device_1","nodeId":"7","xUnknownField":"PROFINET"}}
            """;

        var exception = Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<NetworkOperationRequest>(json, WebOptions));

        Assert.Contains("xUnknownField", exception.Message);
    }

    [Theory]
    [InlineData("subnet")]
    [InlineData("ioSystem")]
    public void UnknownNestedSelectorField_IsRejected(string selector)
    {
        var json = "{\"operationId\":\"op1\",\"operation\":\"configure_network_device\","
            + "\"target\":{\"deviceName\":\"IO_Device_1\",\"nodeId\":\"7\"},"
            + "\"changes\":{\"" + selector + "\":{\"subnetName\":\"PN/IE_1\"}}}";

        var exception = Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<NetworkOperationRequest>(json, WebOptions));

        Assert.Contains("subnetName", exception.Message);
    }

    [Theory]
    [InlineData("ipAddress")]
    [InlineData("subnetMask")]
    [InlineData("pnDeviceName")]
    [InlineData("subnetName")]
    [InlineData("ioSystemName")]
    public void LegacyFlatConfigureField_IsRejected(string field)
    {
        var json = $$"""{"operationId":"op1","operation":"configure_network_device","{{field}}":"x"}""";

        var exception = Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<NetworkOperationRequest>(json, WebOptions));

        Assert.Contains(field, exception.Message);
    }

    [Theory]
    [InlineData("IpAddress")]
    [InlineData("SubnetMask")]
    [InlineData("PnDeviceName")]
    [InlineData("SubnetName")]
    [InlineData("IoSystemName")]
    public void RequestType_NoLongerExposesLegacyFlatConfigureProperty(string propertyName)
    {
        Assert.Null(typeof(NetworkOperationRequest).GetProperty(propertyName));
    }

    [Fact]
    public void AddNetworkDevice_RetainsItsFlatCreationFields()
    {
        const string json = """
            {"operationId":"op1","operation":"add_network_device","typeIdentifier":"OrderNumber:6ES7",
             "deviceName":"PLC_1","deviceItemName":"PLC_1"}
            """;

        var operation = JsonSerializer.Deserialize<NetworkOperationRequest>(json, WebOptions);

        Assert.NotNull(operation);
        Assert.Equal("OrderNumber:6ES7", operation!.TypeIdentifier);
        Assert.Equal("PLC_1", operation.DeviceName);
        Assert.Equal("PLC_1", operation.DeviceItemName);
    }
}
