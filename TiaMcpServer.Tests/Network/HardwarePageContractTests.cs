using System.Text.Json;
using System.Text.Json.Serialization;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public class HardwarePageContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void HardwarePaginationInfo_SerializesThePublicPageMetadata()
    {
        var json = JsonSerializer.Serialize(
            new HardwarePaginationInfo(10, 3, 2, 1, "next-page"),
            JsonOptions);

        Assert.Equal(
            """{"totalDevices":10,"totalSubnets":3,"returnedDevices":2,"returnedSubnets":1,"nextCursor":"next-page"}""",
            json);
    }

    [Fact]
    public void HardwarePageCandidateResultInfo_DoesNotExposeSessionIdentity()
    {
        var result = new HardwarePageCandidateResultInfo(
            1,
            "query-hash",
            "snapshot-hash",
            0,
            1,
            1,
            new[] { "warning" },
            new[] { new HardwareDevicePageCandidateInfo(0, new DeviceInfo { Name = "PLC_1" }, new[] { "device warning" }) },
            new[] { new HardwareSubnetPageCandidateInfo(1, new SubnetInfo { Name = "PN/IE_1" }, new[] { "subnet warning" }) });

        var json = JsonSerializer.Serialize(result, JsonOptions);

        Assert.DoesNotContain("sessionIdentity", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deviceCandidates", json);
        Assert.Contains("subnetCandidates", json);
    }

    [Fact]
    public void HardwarePageEvidence_NormalizesQueryWithoutMakingPageSizePartOfItsHash()
    {
        var first = HardwarePageEvidence.CreateQueryHash("PLC_1", "PLC_1", null, true);
        var second = HardwarePageEvidence.CreateQueryHash("plc_1", "PLC_1", false, true);

        Assert.Equal(first, second);
    }

    [Fact]
    public void HardwarePageEvidence_UsesLengthFramedUtf8QueryFields()
    {
        var hash = HardwarePageEvidence.CreateQueryHash("A", "C", false, false);

        Assert.Equal("a2bbc33c574303fd581b7c85b60d2b3e36452cbf852a6e3779bad4f6c9d7f0ca", hash);
    }

    [Fact]
    public void HardwarePageEvidence_DistinguishesDelimiterInjectionTuples()
    {
        var first = HardwarePageEvidence.CreateQueryHash("a\nplcName=b", "c", false, false);
        var second = HardwarePageEvidence.CreateQueryHash("a", "b\nplcName=c", false, false);

        Assert.NotEqual(first, second);
    }
}
