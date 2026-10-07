using TiaMcpServer.OpennessWorker.Openness.Hmi;
using Xunit;

namespace TiaMcpServer.Tests.Hmi;

public class HmiInitialAddressParserTests
{
    // Spike S8: the observed form of every InitialAddress (host/PLC addresses anonymised in the report).
    private const string S8Sample =
        "Version=16.0.0.0;CommunicationInterface=Industrial Ethernet;HostAccessPoint=S7ONLINE;HostAddress=192.168.x.y;PlcAddress=192.168.x.z;";

    [Fact]
    public void ParsesS8Sample()
    {
        var map = HmiInitialAddressParser.Parse(S8Sample);

        Assert.Equal(5, map.Count);
        Assert.Equal("16.0.0.0", map["Version"]);
        Assert.Equal("Industrial Ethernet", map["CommunicationInterface"]);
        Assert.Equal("S7ONLINE", map["HostAccessPoint"]);
        Assert.Equal("192.168.x.y", map["HostAddress"]);
        Assert.Equal("192.168.x.z", map["PlcAddress"]);
    }

    [Fact]
    public void MalformedPairKeepsRawAndReturnsEmptyMap()
    {
        // A segment without '=' makes the whole string unparsable; the caller keeps the raw string beside the empty map.
        Assert.Empty(HmiInitialAddressParser.Parse("Version=16.0.0.0;garbage;HostAddress=1.2.3.4;"));
        Assert.Empty(HmiInitialAddressParser.Parse("=novalue-key;"));
        Assert.Empty(HmiInitialAddressParser.Parse(null));
        Assert.Empty(HmiInitialAddressParser.Parse(""));
    }

    [Fact]
    public void DuplicateKeyLastWins()
    {
        var map = HmiInitialAddressParser.Parse("A=1;B=2;A=3;");

        Assert.Equal(2, map.Count);
        Assert.Equal("3", map["A"]);
        Assert.Equal("2", map["B"]);
    }

    [Fact]
    public void ValueMayContainEqualsAndEmptyValueIsKept()
    {
        var map = HmiInitialAddressParser.Parse("Key=a=b;Empty=;");

        Assert.Equal("a=b", map["Key"]);
        Assert.Equal(string.Empty, map["Empty"]);
    }
}
