using System.Collections;
using System.Text.Json;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public sealed class NetworkConnectionEvidenceTests
{
    private static Type CaptureType()
    {
        var type = typeof(NetworkConnectionEvidenceTests).Assembly.GetType(
            "TiaMcpServer.OpennessWorker.Openness.NetworkConnectionEvidenceCapture");
        Assert.NotNull(type);
        return type;
    }

    private static JsonElement Inventory(Func<IEnumerable> enumerate, Func<object, object> identity)
    {
        var method = CaptureType().GetMethod("CaptureSubnet");
        Assert.NotNull(method);
        return JsonSerializer.SerializeToElement(method.Invoke(null, new object[] { enumerate, identity }),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    private static object Identity(string deviceName, string nodeId)
    {
        var type = typeof(NodeInfo).Assembly.GetType("TiaMcpServer.Contracts.NetworkNodeIdentityInfo");
        Assert.NotNull(type);
        var value = Activator.CreateInstance(type)!;
        type.GetProperty("DeviceName")!.SetValue(value, deviceName);
        type.GetProperty("NodeId")!.SetValue(value, nodeId);
        return value;
    }

    [Fact]
    public void ConnectedIdentity_UsesDeviceAndNodeId()
    {
        var evidence = Inventory(() => new[] { "node-2" }, node => Identity("PLC_Grouped", (string)node));
        Assert.True(evidence.GetProperty("complete").GetBoolean());
        Assert.Equal("PLC_Grouped", evidence.GetProperty("nodes")[0].GetProperty("deviceName").GetString());
        Assert.Equal("node-2", evidence.GetProperty("nodes")[0].GetProperty("nodeId").GetString());
    }

    [Fact]
    public void EmptyInventory_RequiresCompleteEvidence()
    {
        var evidence = Inventory(() => Array.Empty<string>(), _ => throw new Exception());
        Assert.True(evidence.GetProperty("complete").GetBoolean());
        Assert.Empty(evidence.GetProperty("nodes").EnumerateArray());
        var failed = Inventory(() => throw new InvalidOperationException("enumerator unavailable"), IdentityFor);
        Assert.False(failed.GetProperty("complete").GetBoolean());
        Assert.NotEmpty(failed.GetProperty("messages").EnumerateArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DegradedOwnerOrEnumerator_RemainsIncomplete(bool enumerationFails)
    {
        var degraded = Inventory(() => Partial(enumerationFails), node =>
            (string)node == "bad" ? throw new InvalidOperationException("owner unreadable") : IdentityFor(node));
        Assert.False(degraded.GetProperty("complete").GetBoolean());
        Assert.NotEmpty(degraded.GetProperty("messages").EnumerateArray());
        Assert.Single(degraded.GetProperty("nodes").EnumerateArray());
    }

    [Fact]
    public void GroupedAndUngroupedOwners_AreRetained()
    {
        var evidence = Inventory(() => new[] { "PLC_Ungrouped", "PLC_Grouped" }, node => Identity((string)node, "node-2"));
        Assert.Equal(new[] { "PLC_Grouped", "PLC_Ungrouped" }, evidence.GetProperty("nodes").EnumerateArray()
            .Select(node => node.GetProperty("deviceName").GetString()));
        Assert.True(evidence.GetProperty("complete").GetBoolean());
        Assert.NotNull(typeof(HardwareConfigInfo).GetProperty("RootDeviceCount"));
    }

    [Fact]
    public void KnownNullRelationship_IsDistinctFromUnreadable()
    {
        var method = CaptureType().GetMethod("CaptureNode");
        Assert.NotNull(method);
        var disconnected = JsonSerializer.SerializeToElement(method.Invoke(null, new object[]
        {
            (Func<string?>)(() => null), (Func<(string?, int?)>)(() => (null, null))
        }), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.True(disconnected.GetProperty("complete").GetBoolean());
        Assert.Equal(JsonValueKind.Null, disconnected.GetProperty("subnetId").ValueKind);
        var unreadable = JsonSerializer.SerializeToElement(method.Invoke(null, new object[]
        {
            (Func<string?>)(() => throw new InvalidOperationException("unreadable")),
            (Func<(string?, int?)>)(() => (null, null))
        }), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.False(unreadable.GetProperty("complete").GetBoolean());
        Assert.NotEmpty(unreadable.GetProperty("messages").EnumerateArray());
    }

    private static object IdentityFor(object node) => Identity("PLC_Grouped", (string)node);
    private static IEnumerable Partial(bool enumerationFails)
    {
        yield return "node-2";
        if (enumerationFails) throw new InvalidOperationException("enumeration failed after a node");
        yield return "bad";
    }
}
