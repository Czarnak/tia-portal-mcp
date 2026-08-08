using System.Text.Json;
using System.Text.Json.Serialization;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public class AddDeviceResultInfoTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    public void SerializesEmptyResult()
    {
        var result = new AddDeviceResultInfo();

        var roundTripped = RoundTrip(result);

        Assert.Equal(string.Empty, roundTripped.DeviceName);
        Assert.Equal(string.Empty, roundTripped.RootItemName);
        Assert.Equal(string.Empty, roundTripped.TypeIdentifier);
        Assert.NotNull(roundTripped.Warnings);
        Assert.Empty(roundTripped.Warnings);
    }

    [Fact]
    public void RoundTripsResultWithWarnings()
    {
        var result = new AddDeviceResultInfo
        {
            DeviceName = "ET200SP_1",
            RootItemName = "IM_155_6_PN_ST",
            TypeIdentifier = "OrderNumber:6ES7 155-6AU01-0BN0/V4.2",
            Warnings = { "Name was adjusted to be unique.", "Device requires hardware compile." }
        };

        var roundTripped = RoundTrip(result);

        Assert.Equal("ET200SP_1", roundTripped.DeviceName);
        Assert.Equal("IM_155_6_PN_ST", roundTripped.RootItemName);
        Assert.Equal("OrderNumber:6ES7 155-6AU01-0BN0/V4.2", roundTripped.TypeIdentifier);
        Assert.Equal(new[] { "Name was adjusted to be unique.", "Device requires hardware compile." }, roundTripped.Warnings);
    }

    private static AddDeviceResultInfo RoundTrip(AddDeviceResultInfo result)
    {
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return JsonSerializer.Deserialize<AddDeviceResultInfo>(json, JsonOptions)!;
    }
}
