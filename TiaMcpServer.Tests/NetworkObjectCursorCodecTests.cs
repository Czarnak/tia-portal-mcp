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

    private static string Hash(char value) => new(value, 64);
}
