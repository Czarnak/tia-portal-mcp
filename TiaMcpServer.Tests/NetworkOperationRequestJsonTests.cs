using System.Text.Json;
using TiaMcpServer.Network;
using Xunit;

namespace TiaMcpServer.Tests;

public class NetworkOperationRequestJsonTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void IpAddress_BindsFromCamelCaseJson()
    {
        const string json = """{"operationId":"op1","operation":"configure_network_device","deviceName":"IO_Device_1","ipAddress":"192.168.0.10"}""";

        var operation = JsonSerializer.Deserialize<NetworkOperationRequest>(json, WebOptions);

        Assert.NotNull(operation);
        Assert.Equal("192.168.0.10", operation!.IpAddress);
    }

    [Fact]
    public void MisspelledIpAddress_IsRejected()
    {
        const string json = """{"operationId":"op1","operation":"configure_network_device","deviceName":"IO_Device_1","ip_adress":"192.168.0.10"}""";

        var exception = Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<NetworkOperationRequest>(json, WebOptions));

        Assert.Contains("ip_adress", exception.Message);
    }
}
