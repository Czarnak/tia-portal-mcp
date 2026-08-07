using System.Text;
using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using Xunit;

namespace TiaMcpServer.Tests;

public class NetworkObjectCursorCodecTests
{
    [Fact]
    public void QueryHash_SortsKindsButKeepsDeviceNameCaseSensitive()
    {
        var reordered = NetworkObjectCursorCodec.CreateQueryHash(
            new[] { NetworkObjectKinds.Subnet, NetworkObjectKinds.Node }, "PLC_1");
        var ordered = NetworkObjectCursorCodec.CreateQueryHash(
            new[] { NetworkObjectKinds.Node, NetworkObjectKinds.Subnet }, "PLC_1");
        var differentCase = NetworkObjectCursorCodec.CreateQueryHash(
            new[] { NetworkObjectKinds.Node, NetworkObjectKinds.Subnet }, "plc_1");

        Assert.Equal(ordered, reordered);
        Assert.NotEqual(ordered, differentCase);
        Assert.Matches("^[0-9a-f]{64}$", ordered);
    }

    [Fact]
    public void EncodeDecode_RoundTripsOpaqueUnpaddedBase64UrlCursor()
    {
        var queryHash = Hash('a');
        var snapshotHash = Hash('b');

        var cursor = NetworkObjectCursorCodec.Encode(2, queryHash, snapshotHash);
        var decoded = NetworkObjectCursorCodec.Decode(cursor, queryHash, snapshotHash, totalCount: 3);

        Assert.DoesNotContain("=", cursor);
        Assert.DoesNotContain("+", cursor);
        Assert.DoesNotContain("/", cursor);
        Assert.Equal(2, decoded.Offset);
    }

    [Fact]
    public void Decode_RejectsMalformedCursorAndUnsupportedPayloads()
    {
        var queryHash = Hash('a');
        var snapshotHash = Hash('b');

        AssertCategory(WorkerFailureCategories.InvalidCursor, () =>
            NetworkObjectCursorCodec.Decode("%%%", queryHash, snapshotHash, totalCount: 3));
        AssertCategory(WorkerFailureCategories.InvalidCursor, () =>
            NetworkObjectCursorCodec.Decode(Cursor(version: 2, offset: 0), queryHash, snapshotHash, totalCount: 3));
        AssertCategory(WorkerFailureCategories.InvalidCursor, () =>
            NetworkObjectCursorCodec.Decode(Cursor(offset: -1), queryHash, snapshotHash, totalCount: 3));
    }

    [Theory]
    [InlineData("{\"offset\":0,\"queryHash\":\"{Q}\",\"snapshotHash\":\"{S}\"}")]
    [InlineData("{\"version\":1,\"queryHash\":\"{Q}\",\"snapshotHash\":\"{S}\"}")]
    [InlineData("{\"version\":1,\"offset\":0,\"snapshotHash\":\"{S}\"}")]
    [InlineData("{\"version\":1,\"offset\":0,\"queryHash\":\"{Q}\"}")]
    [InlineData("{\"version\":1,\"offset\":0,\"queryHash\":\"{Q}\",\"snapshotHash\":\"{S}\",\"extra\":true}")]
    [InlineData("{\"version\":1,\"version\":1,\"offset\":0,\"queryHash\":\"{Q}\",\"snapshotHash\":\"{S}\"}")]
    public void Decode_RejectsMissingUnknownAndDuplicateMembers(string json)
    {
        var queryHash = Hash('a');
        var snapshotHash = Hash('b');
        var cursor = RawCursor(json.Replace("{Q}", queryHash).Replace("{S}", snapshotHash));

        AssertCategory(WorkerFailureCategories.InvalidCursor, () =>
            NetworkObjectCursorCodec.Decode(cursor, queryHash, snapshotHash, totalCount: 3));
    }

    [Fact]
    public void Decode_RejectsPaddingAndStandardBase64Alphabet()
    {
        var queryHash = Hash('a');
        var snapshotHash = Hash('b');
        var encoded = NetworkObjectCursorCodec.Encode(0, queryHash, snapshotHash);
        var standard = StandardBase64Cursor(queryHash, snapshotHash);

        AssertCategory(WorkerFailureCategories.InvalidCursor, () =>
            NetworkObjectCursorCodec.Decode(encoded + "=", queryHash, snapshotHash, totalCount: 3));
        Assert.True(standard.Contains('+') || standard.Contains('/'));
        AssertCategory(WorkerFailureCategories.InvalidCursor, () =>
            NetworkObjectCursorCodec.Decode(standard, queryHash, snapshotHash, totalCount: 3));
    }

    [Fact]
    public void SnapshotHash_BindsUnselectableEvidenceAndOrder()
    {
        var original = new[]
        {
            Unselectable("first"),
            Unselectable("second"),
        };
        var reordered = new[]
        {
            Unselectable("second"),
            Unselectable("first"),
        };
        var changedIdentity = new[]
        {
            Unselectable("replacement"),
            Unselectable("second"),
        };

        var hash = NetworkObjectCursorCodec.CreateSnapshotHash(original);

        Assert.NotEqual(hash, NetworkObjectCursorCodec.CreateSnapshotHash(reordered));
        Assert.NotEqual(hash, NetworkObjectCursorCodec.CreateSnapshotHash(changedIdentity));
    }

    [Fact]
    public void SnapshotHash_BindsPublicPathEvidenceForOtherwiseIndistinguishableUnselectableSummaries()
    {
        var first = new[]
        {
            UnselectableWithPath("device/0/node/0"),
            UnselectableWithPath("device/0/node/1"),
        };
        var reorderedEvidence = new[]
        {
            UnselectableWithPath("device/0/node/1"),
            UnselectableWithPath("device/0/node/0"),
        };
        var replacementEvidence = new[]
        {
            UnselectableWithPath("device/0/node/replacement"),
            UnselectableWithPath("device/0/node/1"),
        };

        var hash = NetworkObjectCursorCodec.CreateSnapshotHash(first);

        Assert.NotEqual(hash, NetworkObjectCursorCodec.CreateSnapshotHash(reorderedEvidence));
        Assert.NotEqual(hash, NetworkObjectCursorCodec.CreateSnapshotHash(replacementEvidence));
    }

    [Fact]
    public void SnapshotHash_BindsNodeAndIoSystemDisambiguationFields()
    {
        var node = Selectable(new NetworkObjectSelectorInfo
        {
            Kind = NetworkObjectKinds.Node,
            DeviceName = "PLC_1",
            NodeId = "node-1",
            NodeIndex = 0,
        });
        var ioSystem = Selectable(new NetworkObjectSelectorInfo
        {
            Kind = NetworkObjectKinds.IoSystem,
            SubnetId = "subnet-1",
            Number = 100,
            IoSystemIndex = 0,
            IoSystemName = "PNIO_1",
        });

        Assert.NotEqual(
            NetworkObjectCursorCodec.CreateSnapshotHash(new[] { node }),
            NetworkObjectCursorCodec.CreateSnapshotHash(new[]
            {
                Selectable(new NetworkObjectSelectorInfo
                {
                    Kind = NetworkObjectKinds.Node,
                    DeviceName = "PLC_1",
                    NodeId = "node-1",
                    NodeIndex = 1,
                }),
            }));
        Assert.NotEqual(
            NetworkObjectCursorCodec.CreateSnapshotHash(new[] { ioSystem }),
            NetworkObjectCursorCodec.CreateSnapshotHash(new[]
            {
                Selectable(new NetworkObjectSelectorInfo
                {
                    Kind = NetworkObjectKinds.IoSystem,
                    SubnetId = "subnet-1",
                    Number = 100,
                    IoSystemIndex = 1,
                    IoSystemName = "PNIO_1",
                }),
            }));
        Assert.NotEqual(
            NetworkObjectCursorCodec.CreateSnapshotHash(new[] { ioSystem }),
            NetworkObjectCursorCodec.CreateSnapshotHash(new[]
            {
                Selectable(new NetworkObjectSelectorInfo
                {
                    Kind = NetworkObjectKinds.IoSystem,
                    SubnetId = "subnet-1",
                    Number = 100,
                    IoSystemIndex = 0,
                    IoSystemName = "PNIO_2",
                }),
            }));
    }

    [Fact]
    public void Decode_RejectsFilterSnapshotAndOutOfRangeCursors()
    {
        var queryHash = Hash('a');
        var snapshotHash = Hash('b');

        AssertCategory(WorkerFailureCategories.CursorFilterMismatch, () =>
            NetworkObjectCursorCodec.Decode(
                NetworkObjectCursorCodec.Encode(0, Hash('c'), snapshotHash), queryHash, snapshotHash, totalCount: 3));
        AssertCategory(WorkerFailureCategories.CursorSnapshotMismatch, () =>
            NetworkObjectCursorCodec.Decode(
                NetworkObjectCursorCodec.Encode(0, queryHash, Hash('c')), queryHash, snapshotHash, totalCount: 3));
        AssertCategory(WorkerFailureCategories.CursorOutOfRange, () =>
            NetworkObjectCursorCodec.Decode(
                NetworkObjectCursorCodec.Encode(4, queryHash, snapshotHash), queryHash, snapshotHash, totalCount: 3));
    }

    private static void AssertCategory(string expected, Action action)
    {
        var exception = Assert.Throws<NetworkCursorException>(action);
        Assert.Equal(expected, exception.Category);
    }

    private static string Cursor(int version = 1, int offset = 0)
    {
        var payload = new NetworkObjectCursorPayload
        {
            Version = version,
            Offset = offset,
            QueryHash = Hash('a'),
            SnapshotHash = Hash('b'),
        };

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string RawCursor(string json)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string StandardBase64Cursor(string queryHash, string snapshotHash)
    {
        var json = $"{{\"version\":1,\"offset\":0,\"queryHash\":\"{queryHash}\",\"snapshotHash\":\"{snapshotHash}\",\"noise\":\"\U0001003e\"}}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=');
    }

    private static NetworkObjectSummaryInfo Unselectable(string name)
        => new()
        {
            Kind = NetworkObjectKinds.Node,
            Evidence = new NetworkObjectEvidenceInfo { Name = name },
            Diagnostics = new List<string> { "Node identity unavailable." },
        };

    private static NetworkObjectSummaryInfo UnselectableWithPath(string pathEvidence)
        => new()
        {
            Kind = NetworkObjectKinds.Node,
            Evidence = new NetworkObjectEvidenceInfo
            {
                DeviceItemPath = new List<string> { pathEvidence },
            },
            Diagnostics = new List<string> { "Node identity unavailable." },
        };

    private static NetworkObjectSummaryInfo Selectable(NetworkObjectSelectorInfo selector)
        => new()
        {
            Kind = selector.Kind!,
            Selectable = true,
            Selector = selector,
            Evidence = new NetworkObjectEvidenceInfo(),
        };

    private static string Hash(char value) => new(value, 64);
}
