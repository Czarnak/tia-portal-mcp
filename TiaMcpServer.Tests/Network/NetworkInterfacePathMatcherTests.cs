using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public sealed class NetworkInterfacePathMatcherTests
{
    [Fact]
    public void OrdinaryNodeProducer_PreservesCaptureFailuresAndRemainingCandidates()
    {
        var diagnostics = new List<string>();
        var capture = new TiaMcpServer.OpennessWorker.Openness.HardwareDiscoveryEvidenceCapture("project", diagnostics.Add);
        var nodes = NetworkNodeReadSelectorBuilder.ReadNodes(() => ThrowingNodes(), node =>
            node.NodeId == "bad" ? throw new InvalidOperationException("candidate failure") : node, capture);
        Assert.Equal(new[] { "E2", "E1" }, nodes.Select(node => node.NodeId));
        Assert.Equal(new[] { "nodeMaterialization", "nodeEnumeration" }, capture.Evidence.Failures.Select(f => f.Stage));
        Assert.False(capture.Evidence.Complete);
        Assert.Equal(2, diagnostics.Count);
        static IEnumerable<NodeInfo> ThrowingNodes()
        {
            yield return new() { NodeId = "E2" };
            yield return new() { NodeId = "bad" };
            yield return new() { NodeId = "E1" };
            throw new InvalidOperationException("source failure");
        }
    }
    [Fact]
    public void OwnerPath_IsIndependentOfSiblingOrder()
    {
        var roots = Fixture();
        roots[0].Items.Reverse();
        var result = Match(roots, Path());
        Assert.True(result.Success, result.Error);
        Assert.Equal(32768, result.Item!.PositionNumber);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("position")]
    [InlineData("access")]
    [InlineData("enumeration")]
    public void UnreadableSiblingIdentity_RefusesUniqueness(string failure)
    {
        var roots = Fixture();
        if (failure == "name") roots[0].Items[1].Name = null;
        if (failure == "position") roots[0].Items[1].PositionNumber = null;
        var result = NetworkInterfacePathMatcher.Match(roots, Path(),
            item => failure == "enumeration" ? ThrowingItems(item.Items) : item.Items,
            item => failure == "access" && item.PositionNumber == 33024 ? throw new InvalidOperationException() : item.Name,
            item => item.PositionNumber, item => item.TypeIdentifier);
        Assert.False(result.Success);
        Assert.Equal(WorkerFailureCategories.TargetEvidenceMismatch, result.FailureCategory);
    }

    [Fact]
    public void DuplicateOwnerPairs_CannotBeDisambiguatedByOptionalType()
    {
        var roots = Fixture();
        roots[0].Items[0].TypeIdentifier = "OrderNumber:A";
        roots[0].Items.Add(new() { Name = "PROFINET interface_1", PositionNumber = 32768, TypeIdentifier = "OrderNumber:B" });
        var path = Path(); path[1].TypeIdentifier = "OrderNumber:A";
        Assert.Equal(WorkerFailureCategories.TargetAmbiguous, Match(roots, path).FailureCategory);
    }

    [Fact]
    public void QualifiedNamespace_RejectsDuplicateNodeIds()
    {
        var result = NetworkNodeReadSelectorBuilder.MatchNode(new[] { new NodeInfo { NodeId = "E1" }, new NodeInfo { NodeId = "E1" } },
            "E1", 0, node => node.NodeId);
        Assert.False(result.Success);
        Assert.Equal(WorkerFailureCategories.TargetAmbiguous, result.FailureCategory);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("position")]
    [InlineData("type")]
    public void Constraints_NeverRetarget(string constraint)
    {
        var path = Path();
        if (constraint == "name") path[1].Name = "profinet interface_1";
        if (constraint == "position") path[1].PositionNumber = 33024;
        if (constraint == "type") path[1].TypeIdentifier = "OrderNumber:wrong";
        Assert.False(Match(Fixture(), path).Success);
    }

    [Fact]
    public void OptionalType_IsReadOnlyWhenSupplied()
    {
        var roots = Fixture();
        Assert.True(NetworkInterfacePathMatcher.Match(roots, Path(), item => item.Items,
            item => item.Name, item => item.PositionNumber, _ => throw new InvalidOperationException()).Success);
        var path = Path(); path[1].TypeIdentifier = "OrderNumber:expected";
        Assert.Equal(WorkerFailureCategories.TargetEvidenceMismatch,
            NetworkInterfacePathMatcher.Match(roots, path, item => item.Items, item => item.Name,
                item => item.PositionNumber, _ => throw new InvalidOperationException()).FailureCategory);
    }

    [Fact]
    public void NodeIndex_OnlyChecksTheUniqueOrdinalIdentity()
    {
        var nodes = new[] { new NodeInfo { NodeId = "E2" }, new NodeInfo { NodeId = "E1" } };
        Assert.True(NetworkNodeReadSelectorBuilder.MatchNode(nodes, "E1", 1, node => node.NodeId).Success);
        Assert.Equal(WorkerFailureCategories.TargetEvidenceMismatch,
            NetworkNodeReadSelectorBuilder.MatchNode(nodes, "E1", 0, node => node.NodeId).FailureCategory);
        Assert.Equal(WorkerFailureCategories.TargetNotFound,
            NetworkNodeReadSelectorBuilder.MatchNode(nodes, "e1", null, node => node.NodeId).FailureCategory);
        nodes[0].NodeId = "";
        Assert.Equal(WorkerFailureCategories.TargetEvidenceMismatch,
            NetworkNodeReadSelectorBuilder.MatchNode(nodes, "E1", null, node => node.NodeId).FailureCategory);
    }

    private static NetworkInterfacePathMatch<DeviceItemInfo> Match(List<DeviceItemInfo> roots, List<NetworkInterfacePathSegmentInfo> path)
        => NetworkInterfacePathMatcher.Match(roots, path, item => item.Items, item => item.Name,
            item => item.PositionNumber, item => item.TypeIdentifier);
    private static IEnumerable<DeviceItemInfo> ThrowingItems(List<DeviceItemInfo> items)
    { foreach (var item in items) yield return item; throw new InvalidOperationException(); }
    internal static List<NetworkInterfacePathSegmentInfo> Path() => new()
    { new() { Name = "PLC_DP", PositionNumber = 1 }, new() { Name = "PROFINET interface_1", PositionNumber = 32768 } };
    private static List<DeviceItemInfo> Fixture() => NetworkDiscoveryRepairFixture.Metadata(
        new HardwareDiscoveryEvidenceInfo { Scope = "project", Complete = true }).Devices[0].Items;
}
