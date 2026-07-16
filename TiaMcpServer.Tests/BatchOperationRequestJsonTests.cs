using System.Text.Json;
using TiaMcpServer.Batch;
using Xunit;

namespace TiaMcpServer.Tests;

public class BatchOperationRequestJsonTests
{
    // Mirrors the camelCase + case-insensitive binding the MCP SDK uses for tool arguments.
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void MisspelledOptionalProperty_IsRejectedNotSilentlyDropped()
    {
        // "ip_adress" is the exact trap from the audit: a typo that previously succeeded
        // silently and left the device unconfigured while reporting success.
        var json = """{"operationId":"op1","operation":"configure_network_device","deviceName":"IO_Device_1","ip_adress":"192.168.0.10"}""";

        var ex = Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<BatchOperationRequest>(json, WebOptions));

        Assert.Contains("ip_adress", ex.Message);
    }

    [Fact]
    public void KnownCamelCaseProperties_StillDeserialize()
    {
        var json = """{"operationId":"op1","operation":"configure_network_device","deviceName":"IO_Device_1","ipAddress":"192.168.0.10"}""";

        var request = JsonSerializer.Deserialize<BatchOperationRequest>(json, WebOptions);

        Assert.NotNull(request);
        Assert.Equal("192.168.0.10", request!.IpAddress);
    }

    [Fact]
    public void Deserializes_BoundingFields()
    {
        var json = """{"operationId":"a","operation":"browse_project_tree","depth":3,"startPath":"PLC_1/Blocks","maxResults":25}""";

        var request = JsonSerializer.Deserialize<BatchOperationRequest>(json, WebOptions)!;

        Assert.Equal(3, request.Depth);
        Assert.Equal("PLC_1/Blocks", request.StartPath);
        Assert.Equal(25, request.MaxResults);
    }
}
