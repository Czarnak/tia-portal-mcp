using System.Text.Json;
using TiaMcpServer.Network;
using Xunit;

namespace TiaMcpServer.Tests.Network;

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

    [Fact]
    public void MissingItemPathIndex_RemainsDetectableForStrictValidation()
    {
        const string json = """
            {"operationId":"inspect","operation":"inspect_network_object",
             "target":{"kind":"deviceItem","deviceName":"PLC_1",
                       "itemPath":[{"name":"CPU","positionNumber":1,"typeIdentifier":"OrderNumber:CPU"}]}}
            """;

        var operation = JsonSerializer.Deserialize<NetworkOperationRequest>(json, WebOptions);
        var result = NetworkOperationCatalog.ValidateRead(new[] { operation! });

        Assert.False(result.IsValid);
        Assert.Contains("index", result.Error);
    }

    // ------------------------------------------------------------------
    // Phase 4 subnet lifecycle JSON shape
    // ------------------------------------------------------------------

    [Fact]
    public void CreateSubnet_EthernetJson_BindsCorrectly()
    {
        const string json = """
            {"operationId":"op1","operation":"create_subnet",
             "subnet":{"name":"PN/IE_1","networkType":"Ethernet"}}
            """;

        var operation = JsonSerializer.Deserialize<NetworkOperationRequest>(json, WebOptions);

        Assert.NotNull(operation);
        Assert.Equal("PN/IE_1", operation!.Subnet!.Name);
        Assert.Equal("Ethernet", operation.Subnet!.NetworkType);
        Assert.Null(operation.Subnet!.HighestAddress);
        Assert.Null(operation.Subnet!.TransmissionSpeed);
    }

    [Fact]
    public void CreateSubnet_ProfibusJsonWithBothOptionalAttributes_BindsCorrectly()
    {
        const string json = """
            {"operationId":"op1","operation":"create_subnet",
             "subnet":{"name":"PB_1","networkType":"Profibus","highestAddress":31,"transmissionSpeed":"Baud1500000"}}
            """;

        var operation = JsonSerializer.Deserialize<NetworkOperationRequest>(json, WebOptions);

        Assert.NotNull(operation);
        Assert.Equal("PB_1", operation!.Subnet!.Name);
        Assert.Equal("Profibus", operation.Subnet!.NetworkType);
        Assert.Equal(31, operation.Subnet!.HighestAddress);
        Assert.Equal("Baud1500000", operation.Subnet!.TransmissionSpeed);
    }

    [Fact]
    public void UpdateSubnet_RenameOnlyJson_BindsCorrectly()
    {
        const string json = """
            {"operationId":"op1","operation":"update_subnet",
             "target":{"kind":"subnet","subnetId":"S1"},
             "subnetChanges":{"name":"PN/IE_1_Renamed"}}
            """;

        var operation = JsonSerializer.Deserialize<NetworkOperationRequest>(json, WebOptions);

        Assert.NotNull(operation);
        Assert.Equal("S1", operation!.Target!.SubnetId);
        Assert.Equal("PN/IE_1_Renamed", operation.SubnetChanges!.Name);
        Assert.Null(operation.SubnetChanges!.HighestAddress);
        Assert.Null(operation.SubnetChanges!.TransmissionSpeed);
    }

    [Fact]
    public void UpdateSubnet_ProfibusAttributeJson_BindsCorrectly()
    {
        const string json = """
            {"operationId":"op1","operation":"update_subnet",
             "target":{"kind":"subnet","subnetId":"S1"},
             "subnetChanges":{"highestAddress":16,"transmissionSpeed":"Baud500000"}}
            """;

        var operation = JsonSerializer.Deserialize<NetworkOperationRequest>(json, WebOptions);

        Assert.NotNull(operation);
        Assert.Null(operation!.SubnetChanges!.Name);
        Assert.Equal(16, operation.SubnetChanges!.HighestAddress);
        Assert.Equal("Baud500000", operation.SubnetChanges!.TransmissionSpeed);
    }

    [Fact]
    public void DeleteSubnet_Json_BindsCorrectly()
    {
        const string json = """
            {"operationId":"op1","operation":"delete_subnet",
             "target":{"kind":"subnet","subnetId":"S1"}}
            """;

        var operation = JsonSerializer.Deserialize<NetworkOperationRequest>(json, WebOptions);

        Assert.NotNull(operation);
        Assert.Equal("delete_subnet", operation!.Operation);
        Assert.Equal("S1", operation.Target!.SubnetId);
    }

    [Fact]
    public void SubnetDefinition_UnknownNestedMember_IsRejected()
    {
        const string json = """
            {"operationId":"op1","operation":"create_subnet",
             "subnet":{"name":"PN/IE_1","networkType":"Ethernet","xUnknownField":"x"}}
            """;

        var exception = Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<NetworkOperationRequest>(json, WebOptions));

        Assert.Contains("xUnknownField", exception.Message);
    }

    [Fact]
    public void SubnetChanges_UnknownNestedMember_IsRejected()
    {
        const string json = """
            {"operationId":"op1","operation":"update_subnet",
             "target":{"kind":"subnet","subnetId":"S1"},
             "subnetChanges":{"name":"Renamed","xUnknownField":"x"}}
            """;

        var exception = Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<NetworkOperationRequest>(json, WebOptions));

        Assert.Contains("xUnknownField", exception.Message);
    }

    [Theory]
    [InlineData("subnet", """{"name":"PN/IE_1","networkType":"Ethernet","subnetId":"S1"}""")]
    [InlineData("subnetChanges", """{"name":"Renamed","subnetId":"S1"}""")]
    public void WritableSubnetId_UnderSubnetOrSubnetChanges_IsRejectedAsUnmapped(string field, string nestedJson)
    {
        var json = $$"""{"operationId":"op1","operation":"create_subnet","{{field}}":{{nestedJson}}}""";

        var exception = Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<NetworkOperationRequest>(json, WebOptions));

        Assert.Contains("subnetId", exception.Message);
    }

    [Fact]
    public void WritableNetworkType_UnderSubnetChanges_IsRejectedAsUnmapped()
    {
        const string json = """
            {"operationId":"op1","operation":"update_subnet",
             "target":{"kind":"subnet","subnetId":"S1"},
             "subnetChanges":{"name":"Renamed","networkType":"Ethernet"}}
            """;

        var exception = Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<NetworkOperationRequest>(json, WebOptions));

        Assert.Contains("networkType", exception.Message);
    }
}
