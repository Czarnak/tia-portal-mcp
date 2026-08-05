using TiaMcpServer.OpennessWorker;
using Xunit;

namespace TiaMcpServer.Tests;

public sealed class NetworkObjectDiscoveryEvidenceTests
{
    [Fact]
    public void ReadString_AcceptsOnlyNonblankExactStrings()
    {
        var valid = NetworkObjectDiscoveryEvidence.ReadString("node-1", "Node identity");
        var nullValue = NetworkObjectDiscoveryEvidence.ReadString(null, "Node identity");
        var blank = NetworkObjectDiscoveryEvidence.ReadString(" ", "Node identity");
        var wrongType = NetworkObjectDiscoveryEvidence.ReadString(new ThrowsOnToString(), "Node identity");

        Assert.True(valid.IsUsable);
        Assert.Equal("node-1", valid.Value);
        Assert.Equal("value:node-1", valid.SnapshotToken);

        Assert.False(nullValue.IsUsable);
        Assert.Equal("null", nullValue.SnapshotToken);
        Assert.Contains("was null", nullValue.Diagnostic, StringComparison.Ordinal);

        Assert.False(blank.IsUsable);
        Assert.Equal("blank", blank.SnapshotToken);
        Assert.Contains("was blank", blank.Diagnostic, StringComparison.Ordinal);

        Assert.False(wrongType.IsUsable);
        Assert.Equal("wrongType", wrongType.SnapshotToken);
        Assert.Contains("unexpected CLR type", wrongType.Diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain(ThrowsOnToString.LeakToken, wrongType.Diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadInt_AcceptsOnlyExactInt32Values()
    {
        var valid = NetworkObjectDiscoveryEvidence.ReadInt(7, "IO system number");
        var wrongType = NetworkObjectDiscoveryEvidence.ReadInt(7L, "IO system number");

        Assert.True(valid.IsUsable);
        Assert.Equal(7, valid.Value);
        Assert.Equal("value:7", valid.SnapshotToken);
        Assert.False(wrongType.IsUsable);
        Assert.Equal("wrongType", wrongType.SnapshotToken);
    }

    private sealed class ThrowsOnToString
    {
        public const string LeakToken = "discovery-tostring-leak-canary";

        public override string ToString() => throw new InvalidOperationException(LeakToken);
    }
}
