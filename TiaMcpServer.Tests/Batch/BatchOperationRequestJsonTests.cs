using System.Text.Json;
using TiaMcpServer.Batch;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public class BatchOperationRequestJsonTests
{
    // Mirrors the camelCase + case-insensitive binding the MCP SDK uses for tool arguments.
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void DeserializesRetainedMaxResultsField()
    {
        var json = """{"operationId":"a","operation":"read_cross_references","maxResults":25}""";

        var request = JsonSerializer.Deserialize<BatchOperationRequest>(json, WebOptions)!;

        Assert.Equal(25, request.MaxResults);
    }

    [Theory]
    [InlineData("""{"operationId":"a","operation":"list_tag_tables","depth":3}""", "depth")]
    [InlineData("""{"operationId":"a","operation":"list_tag_tables","startPath":"PLC_1/Blocks"}""", "startPath")]
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

    [Fact]
    public void BatchRequestType_DoesNotExposeNetworkFields()
    {
        foreach (var propertyName in new[]
        {
            "Query",
            "TypeIdentifier",
            "DeviceName",
            "DeviceItemName",
            "IpAddress",
            "SubnetMask",
            "PnDeviceName",
            "SubnetName",
            "IoSystemName"
        })
        {
            Assert.Null(typeof(BatchOperationRequest).GetProperty(propertyName));
        }
    }

    [Fact]
    public void RemovedNetworkField_IsRejectedDuringDeserialization()
    {
        var json = """{"operationId":"a","operation":"create_tag_table","tableName":"Inputs","deviceName":"PLC_1"}""";

        var exception = Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<BatchOperationRequest>(json, WebOptions));

        Assert.Contains("deviceName", exception.Message);
    }
}
