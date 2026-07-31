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
    public void DeserializesRetainedMaxResultsField()
    {
        var json = """{"operationId":"a","operation":"search_equipment_catalog","query":"CPU","maxResults":25}""";

        var request = JsonSerializer.Deserialize<BatchOperationRequest>(json, WebOptions)!;

        Assert.Equal(25, request.MaxResults);
    }

    [Theory]
    [InlineData("""{"operationId":"a","operation":"read_hardware_config","depth":3}""", "depth")]
    [InlineData("""{"operationId":"a","operation":"read_hardware_config","startPath":"PLC_1/Blocks"}""", "startPath")]
    public void RemovedProjectTreeFields_AreRejected(string json, string field)
    {
        var exception = Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<BatchOperationRequest>(json, WebOptions));

        Assert.Contains(field, exception.Message);
    }

    [Fact]
    public void BatchRequestType_DoesNotExposeProjectTreeFields()
    {
        Assert.Null(typeof(BatchOperationRequest).GetProperty("Depth"));
        Assert.Null(typeof(BatchOperationRequest).GetProperty("StartPath"));
    }
}
