using TiaMcpServer.OpennessWorker;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public sealed class NetworkObjectDiscoveryEvidenceTests
{
    [Theory]
    [InlineData("laterTraversal")]
    [InlineData("unreadableDevice")]
    [InlineData("duplicateDevice")]
    [InlineData("filteredEvidence")]
    public void FinalInventoryEvidence_CannotCertifyAnEarlierNodeAfterLaterLoss(string failure)
    {
        var inventory = NetworkDiscoveryRepairFixture.Metadata(new() { Scope = "project", Complete = true });
        NetworkNodeReadSelectorBuilder.Apply(inventory.Devices[0], true);
        var earlierNode = inventory.Devices[0].Items[0].Items[0].NetworkInterfaces[0].Nodes[0];
        Assert.True(earlierNode.Selectable);
        if (failure == "laterTraversal") inventory.DiscoveryEvidence!.Complete = false;
        if (failure == "filteredEvidence") inventory.DiscoveryEvidence!.Scope = "device";
        if (failure == "unreadableDevice") inventory.Devices.Add(new() { Name = null });
        if (failure == "duplicateDevice") inventory.Devices.Add(new() { Name = "s7-1500/et200mp STATION_1" });
        NetworkNodeReadSelectorBuilder.ApplyInventory(inventory);
        Assert.False(earlierNode.Selectable);
        Assert.Null(earlierNode.Selector);
    }
    [Fact]
    public void TwoInterfacesWithE1_EmitDistinctRoundTripSelectors()
    {
        var device = NetworkDiscoveryRepairFixture.Metadata(new TiaMcpServer.Contracts.HardwareDiscoveryEvidenceInfo
            { Scope = "project", Complete = true }).Devices[0];
        NetworkNodeReadSelectorBuilder.Apply(device, true);
        var x1Selector = device.Items[0].Items[0].NetworkInterfaces[0].Nodes[0].Selector!;
        var x2Selector = device.Items[0].Items[1].NetworkInterfaces[0].Nodes[0].Selector!;
        Assert.NotNull(x1Selector);
        Assert.NotNull(x2Selector);
        Assert.Equal("E1", x1Selector.NodeId);
        Assert.Equal("E1", x2Selector.NodeId);
        Assert.Equal(32768, x1Selector.InterfacePath![1].PositionNumber);
        Assert.Equal(33024, x2Selector.InterfacePath![1].PositionNumber);
        Assert.Null(x1Selector.InterfacePath[1].TypeIdentifier);
        Assert.Equal("PROFINET interface_1", x1Selector.InterfaceName);
        Assert.Equal("PROFINET interface_2", x2Selector.InterfaceName);
        Assert.Null(x1Selector.ItemPath);
    }

    [Fact]
    public void NullParentType_DoesNotDisableQualifiedNode()
    {
        var device = NetworkDiscoveryRepairFixture.Metadata(new TiaMcpServer.Contracts.HardwareDiscoveryEvidenceInfo
            { Scope = "project", Complete = true }).Devices[0];
        NetworkNodeReadSelectorBuilder.Apply(device, true);
        Assert.True(device.Items[0].Items[0].NetworkInterfaces[0].Nodes[0].Selectable);
        Assert.Null(device.Items[0].Items[0].Selector);
    }

    [Theory]
    [InlineData("sibling")]
    [InlineData("duplicate")]
    [InlineData("traversal")]
    public void ReadSelectability_RequiresReadableUniqueOwnerAndNode(string failure)
    {
        var device = NetworkDiscoveryRepairFixture.Metadata(new TiaMcpServer.Contracts.HardwareDiscoveryEvidenceInfo
            { Scope = "project", Complete = true }).Devices[0];
        var networkInterface = device.Items[0].Items[0].NetworkInterfaces[0];
        networkInterface.Nodes[0].Selectable = true;
        if (failure == "sibling") device.Items[0].Items[1].Name = null;
        if (failure == "duplicate") networkInterface.Nodes.Add(new() { NodeId = "E1" });
        NetworkNodeReadSelectorBuilder.Apply(device, failure != "traversal");
        Assert.False(networkInterface.Nodes[0].Selectable);
        Assert.Null(networkInterface.Nodes[0].Selector);
        Assert.NotEmpty(networkInterface.Nodes[0].SelectorDiagnostics);
    }
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
