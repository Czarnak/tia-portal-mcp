using TiaMcpServer.OpennessWorker;
using Xunit;

namespace TiaMcpServer.Tests.Network;

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

    [Fact]
    public void ReadInt_NegativeRootAndNestedDeviceItemPositionsAreUnusable_AndLaterPositionRemainsUsable()
    {
        var root = NetworkObjectDiscoveryEvidence.ReadInt(-1, "Device item position number");
        var nested = NetworkObjectDiscoveryEvidence.ReadInt(-2, "Device item position number");
        var later = NetworkObjectDiscoveryEvidence.ReadInt(7, "Device item position number");

        Assert.False(root.IsUsable);
        Assert.Equal("negative", root.SnapshotToken);
        Assert.Equal(
            "Device item position number was negative; selector not available.",
            root.Diagnostic);

        Assert.False(nested.IsUsable);
        Assert.Equal("negative", nested.SnapshotToken);
        Assert.Equal(root.Diagnostic, nested.Diagnostic);

        Assert.True(later.IsUsable);
        Assert.Equal(7, later.Value);
        Assert.Equal("value:7", later.SnapshotToken);
        Assert.Empty(later.Diagnostic);
    }

    [Fact]
    public void ReadInt_NegativeIoSystemNumberIsUnusable_AndLaterNumberRemainsUsable()
    {
        var invalid = NetworkObjectDiscoveryEvidence.ReadInt(-1, "IO system number");
        var later = NetworkObjectDiscoveryEvidence.ReadInt(3, "IO system number");

        Assert.False(invalid.IsUsable);
        Assert.Equal("negative", invalid.SnapshotToken);
        Assert.Equal(
            "IO system number was negative; selector not available.",
            invalid.Diagnostic);

        Assert.True(later.IsUsable);
        Assert.Equal(3, later.Value);
        Assert.Equal("value:3", later.SnapshotToken);
        Assert.Empty(later.Diagnostic);
    }

    private sealed class ThrowsOnToString
    {
        public const string LeakToken = "discovery-tostring-leak-canary";

        public override string ToString() => throw new InvalidOperationException(LeakToken);
    }
}
