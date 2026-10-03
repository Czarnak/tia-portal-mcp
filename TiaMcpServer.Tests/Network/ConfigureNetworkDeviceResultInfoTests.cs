using System.Text.Json;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public class ConfigureNetworkDeviceResultInfoTests
{
    private static readonly JsonSerializerOptions JsonOptions = WorkerJson.PayloadOptionsFor(typeof(ConfigureNetworkDeviceResultInfo));

    [Fact]
    public void SerializesEmptyResult()
    {
        var result = new ConfigureNetworkDeviceResultInfo();

        var roundTripped = RoundTrip(result);

        Assert.Equal(string.Empty, roundTripped.DeviceName);
        Assert.NotNull(roundTripped.AppliedSettings);
        Assert.NotNull(roundTripped.SkippedSettings);
        Assert.NotNull(roundTripped.Messages);
        Assert.Empty(roundTripped.AppliedSettings);
        Assert.Empty(roundTripped.SkippedSettings);
        Assert.Empty(roundTripped.Messages);
    }

    [Fact]
    public void RoundTripsResultWithSettingsAndMessages()
    {
        var result = new ConfigureNetworkDeviceResultInfo
        {
            DeviceName = "ET200SP_1",
            AppliedSettings =
            {
                ["Address"] = "192.168.0.20",
                ["SubnetMask"] = "255.255.255.0"
            },
            SkippedSettings =
            {
                ["IoSystem"] = "IO system not found."
            },
            Messages = { "Configured network node.", "Skipped unavailable IO system." }
        };

        var roundTripped = RoundTrip(result);

        Assert.Equal("ET200SP_1", roundTripped.DeviceName);
        Assert.Equal("192.168.0.20", roundTripped.AppliedSettings["Address"]);
        Assert.Equal("255.255.255.0", roundTripped.AppliedSettings["SubnetMask"]);
        Assert.Equal("IO system not found.", roundTripped.SkippedSettings["IoSystem"]);
        Assert.Equal(new[] { "Configured network node.", "Skipped unavailable IO system." }, roundTripped.Messages);
    }

    private static ConfigureNetworkDeviceResultInfo RoundTrip(ConfigureNetworkDeviceResultInfo result)
    {
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return JsonSerializer.Deserialize<ConfigureNetworkDeviceResultInfo>(json, JsonOptions)!;
    }
}
