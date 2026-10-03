using TiaMcpServer.Contracts;
using TiaMcpServer.Network;
using TiaMcpServer.OpennessWorker.Openness;
using TiaMcpServer.Worker;
using System.Text.Json;
using Xunit;

namespace TiaMcpServer.Tests.Network;

[Collection(RealWorkerProcessCollection.Name)]
public sealed class NetworkConnectionEvidenceTests
{
    private static NetworkNodeIdentityInfo Identity(string deviceName, string nodeId) => new()
    { DeviceName = deviceName, NodeId = nodeId };

    [Fact]
    public void ConnectedIdentity_UsesDeviceAndNodeId()
    {
        var evidence = NetworkConnectionEvidenceCapture.CaptureSubnet(() => new[] { "node-2" },
            node => Identity("PLC_Grouped", node));
        Assert.True(evidence.Complete);
        Assert.Equal("PLC_Grouped", evidence.Nodes[0].DeviceName);
        Assert.Equal("node-2", evidence.Nodes[0].NodeId);
    }

    [Fact]
    public void EmptyInventory_RequiresCompleteEvidence()
    {
        var evidence = NetworkConnectionEvidenceCapture.CaptureSubnet(() => Array.Empty<string>(), IdentityFor);
        Assert.True(evidence.Complete);
        Assert.Empty(evidence.Nodes);
        var failed = NetworkConnectionEvidenceCapture.CaptureSubnet<string>(
            () => throw new InvalidOperationException("enumerator unavailable"), IdentityFor);
        Assert.False(failed.Complete);
        Assert.NotEmpty(failed.Messages);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DegradedOwnerOrEnumerator_RemainsIncomplete(bool enumerationFails)
    {
        var degraded = NetworkConnectionEvidenceCapture.CaptureSubnet(() => Partial(enumerationFails), node =>
            node == "bad" ? throw new InvalidOperationException("owner unreadable") : IdentityFor(node));
        Assert.False(degraded.Complete);
        Assert.NotEmpty(degraded.Messages);
        Assert.Single(degraded.Nodes);
    }

    [Fact]
    public void GroupedAndUngroupedOwners_AreRetained()
    {
        var evidence = NetworkConnectionEvidenceCapture.CaptureSubnet(
            () => new[] { ("PLC_Ungrouped", "node-1"), ("PLC_Grouped", "node-2"), ("PLC_Grouped", "node-1") },
            node => Identity(node.Item1, node.Item2));
        Assert.Equal(new[] { "PLC_Grouped", "PLC_Grouped", "PLC_Ungrouped" }, evidence.Nodes.Select(node => node.DeviceName));
        Assert.Equal(new[] { "node-1", "node-2", "node-1" }, evidence.Nodes.Select(node => node.NodeId));
        Assert.True(evidence.Complete);
        var read = NetworkPayloadContract.DecodeHardwareConfig(WorkerJson.SerializePayload(new HardwareConfigInfo
        {
            RootDeviceCount = 2,
            Devices = new() { new() { Name = "Direct1" }, new() { Name = "Direct2" },
                new() { Name = "PLC_Grouped" }, new() { Name = "PLC_Ungrouped" } }
        }));
        Assert.Equal(2, read.RootDeviceCount);
        Assert.Equal(4, read.Devices.Count);
    }

    [Fact]
    public void KnownNullRelationship_IsDistinctFromUnreadable()
    {
        var disconnected = NetworkConnectionEvidenceCapture.CaptureNode(() => null, () => (null, null));
        Assert.True(disconnected.Complete);
        Assert.Null(disconnected.SubnetId);
        Assert.Null(disconnected.IoSystemSubnetId);
        Assert.Null(disconnected.IoSystemNumber);
        var unreadable = NetworkConnectionEvidenceCapture.CaptureNode(
            () => throw new InvalidOperationException("unreadable"), () => (null, null));
        Assert.False(unreadable.Complete);
        Assert.NotEmpty(unreadable.Messages);
        var json = WorkerJson.SerializePayload(disconnected);
        Assert.Contains("\"subnetId\":null", json);
        Assert.Contains("\"ioSystemSubnetId\":null", json);
        Assert.Contains("\"ioSystemNumber\":null", json);
    }

    [Theory]
    [InlineData(null, "node-1")]
    [InlineData("PLC_1", "")]
    public void MissingOwnerOrNodeIdentity_RemainsIncomplete(string? owner, string nodeId)
    {
        var evidence = NetworkConnectionEvidenceCapture.CaptureSubnet(() => new[] { 1 },
            _ => Identity(owner!, nodeId));
        Assert.False(evidence.Complete);
        Assert.Empty(evidence.Nodes);
        Assert.NotEmpty(evidence.Messages);
    }

    [Fact]
    public void PartialIoSystemIdentity_RemainsIncomplete()
    {
        var evidence = NetworkConnectionEvidenceCapture.CaptureNode(() => "subnet-1", () => (null, 100));
        Assert.False(evidence.Complete);
        Assert.Equal("subnet-1", evidence.SubnetId);
        Assert.NotEmpty(evidence.Messages);
    }

    [Fact]
    public void WorkerCapture_WiresProductionDelegatesAndPreservesAllScopeOwners()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName,
            "TiaMcpServer.OpennessWorker", "Openness", "HardwareConfigReader.cs"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(directory.FullName,
            "TiaMcpServer.OpennessWorker", "Openness", "HardwareConfigReader.cs"));
        Assert.Contains("result.RootDeviceCount = project.Devices.Count", source);
        Assert.Contains("NetworkConnectionEvidenceCapture.CaptureNode(", source);
        Assert.Contains("NetworkConnectionEvidenceCapture.CaptureSubnet(", source);
        Assert.Contains("ProjectDeviceNameMatcher.FindMatches(project, name.Value", source);
        Assert.Contains("return ReadConnectedNodeIdentity(node);", source);
        Assert.Contains("RequireSubnetIdentity(system.Subnet)", source);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FakeWorker_OrdinaryReadPreservesCompleteAndDegradedEvidence(bool degraded)
    {
        var scenario = degraded ? "network-connection-evidence-degraded" : "network-connection-evidence";
        using var uiOpen = FakeWorkerUiOpenProject.ForWorkerRelativePath(scenario);
        using var client = new OpennessWorkerClient(new ProjectSessionBinding(null), logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate());
        var result = await NetworkReadTools.NetworkRead(client, new[] { new NetworkOperationRequest
        { OperationId = "hardware", Operation = "read_hardware_config", ProjectPath = scenario } });
        Assert.False(result.IsError);
        var item = Assert.IsType<JsonElement>(result.StructuredContent).GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal("succeeded", item.GetProperty("status").GetString());
        var read = item.GetProperty("result");
        Assert.Equal(2, read.GetProperty("rootDeviceCount").GetInt32());
        var evidence = read.GetProperty("subnets")[0].GetProperty("connectionEvidence");
        Assert.Equal(!degraded, evidence.GetProperty("complete").GetBoolean());
        Assert.Equal("PLC_Grouped", evidence.GetProperty("nodes")[0].GetProperty("deviceName").GetString());
        Assert.Equal("node-2", evidence.GetProperty("nodes")[0].GetProperty("nodeId").GetString());
        Assert.Equal("PLC_Ungrouped", evidence.GetProperty("nodes")[1].GetProperty("deviceName").GetString());
        var nodes = read.GetProperty("devices")[0].GetProperty("items")[0]
            .GetProperty("networkInterfaces")[0].GetProperty("nodes");
        Assert.Equal("subnet-1", nodes[0].GetProperty("connectionEvidence").GetProperty("subnetId").GetString());
        var disconnected = nodes[1].GetProperty("connectionEvidence");
        Assert.True(disconnected.GetProperty("complete").GetBoolean());
        Assert.Equal(JsonValueKind.Null, disconnected.GetProperty("subnetId").ValueKind);
    }

    [Fact]
    public void HistoricalPayload_OmitsEvidenceWithoutInventingEmptyInventory()
    {
        var read = NetworkPayloadContract.DecodeHardwareConfig(WorkerJson.SerializePayload(new HardwareConfigInfo()));
        Assert.Null(read.RootDeviceCount);
        Assert.Null(new NodeInfo().ConnectionEvidence);
        Assert.Null(new SubnetInfo().ConnectionEvidence);
    }

    [Theory]
    [InlineData("null-node")]
    [InlineData("blank-owner")]
    [InlineData("missing-message")]
    [InlineData("negative-root")]
    public void MalformedEvidence_IsRejected(string variant)
    {
        var evidence = new NetworkSubnetConnectionsInfo { Complete = true };
        var config = new HardwareConfigInfo { RootDeviceCount = 2, Subnets = new()
        {
            new() { SubnetId = "subnet-1", Selectable = false,
                SelectorDiagnostics = new() { "No selector for this fixture." }, ConnectionEvidence = evidence }
        } };
        switch (variant)
        {
            case "null-node": evidence.Nodes.Add(null!); break;
            case "blank-owner": evidence.Nodes.Add(Identity("", "node-1")); break;
            case "missing-message": evidence.Complete = false; break;
            case "negative-root": config.RootDeviceCount = -1; break;
        }
        Assert.Throws<JsonException>(() => NetworkPayloadContract.DecodeHardwareConfig(WorkerJson.SerializePayload(config)));
    }

    [Fact]
    public void KnownConnectedRelationship_RetainsExactIoSystemIdentity()
    {
        var evidence = NetworkConnectionEvidenceCapture.CaptureNode(() => "subnet-1", () => ("subnet-1", 100));
        Assert.True(evidence.Complete);
        Assert.Equal("subnet-1", evidence.SubnetId);
        Assert.Equal("subnet-1", evidence.IoSystemSubnetId);
        Assert.Equal(100, evidence.IoSystemNumber);
    }

    [Theory]
    [InlineData("blank-subnet")]
    [InlineData("partial-io")]
    [InlineData("blank-io")]
    [InlineData("negative-io")]
    [InlineData("no-diagnostic")]
    [InlineData("null-messages")]
    public void MalformedNodeEvidence_IsRejected(string variant)
    {
        var evidence = new NetworkNodeConnectionInfo { Complete = true };
        switch (variant)
        {
            case "blank-subnet": evidence.SubnetId = " "; break;
            case "partial-io": evidence.IoSystemNumber = 1; break;
            case "blank-io": evidence.IoSystemSubnetId = ""; evidence.IoSystemNumber = 1; break;
            case "negative-io": evidence.IoSystemSubnetId = "subnet-1"; evidence.IoSystemNumber = -1; break;
            case "no-diagnostic": evidence.Complete = false; break;
            case "null-messages": evidence.Messages = null!; break;
        }
        var config = new HardwareConfigInfo { Devices = new() { new() { Items = new() { new()
        {
            SelectorDiagnostics = new() { "Fixture item." }, NetworkInterfaces = new() { new()
            {
                SelectorDiagnostics = new() { "Fixture interface." }, Nodes = new() { new()
                { SelectorDiagnostics = new() { "Fixture node." }, ConnectionEvidence = evidence } }
            } }
        } } } } };
        Assert.Throws<JsonException>(() => NetworkPayloadContract.DecodeHardwareConfig(WorkerJson.SerializePayload(config)));
    }

    [Fact]
    public void OwnerFailure_DoesNotDiscardLaterReadableNodes()
    {
        var evidence = NetworkConnectionEvidenceCapture.CaptureSubnet(() => new[] { "bad", "node-2" },
            node => node == "bad" ? throw new InvalidOperationException("owner failure") : IdentityFor(node));
        Assert.False(evidence.Complete);
        Assert.Equal("node-2", Assert.Single(evidence.Nodes).NodeId);
    }

    [Fact]
    public void IoReadFailure_RetainsSubnetAndIncompleteDiagnostics()
    {
        var evidence = NetworkConnectionEvidenceCapture.CaptureNode(() => "subnet-1",
            () => throw new InvalidOperationException("IO enumeration failure"));
        Assert.False(evidence.Complete);
        Assert.Equal("subnet-1", evidence.SubnetId);
        Assert.Null(evidence.IoSystemNumber);
        Assert.NotEmpty(evidence.Messages);
    }

    private static NetworkNodeIdentityInfo IdentityFor(string node) => Identity("PLC_Grouped", node);
    private static IEnumerable<string> Partial(bool enumerationFails)
    {
        yield return "node-2";
        if (enumerationFails) throw new InvalidOperationException("enumeration failed after a node");
        yield return "bad";
    }
}
