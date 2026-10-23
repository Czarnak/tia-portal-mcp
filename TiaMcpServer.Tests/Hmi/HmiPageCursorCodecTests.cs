using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Cursors;
using TiaMcpServer.Hmi;
using TiaMcpServer.Network;
using Xunit;

namespace TiaMcpServer.Tests.Hmi;

public class HmiPageCursorCodecTests
{
    private static readonly byte[] TestKey = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
    private static readonly byte[] OtherKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();

    internal static HmiPageCursorState State(int offset = 2) => new(
        "s1",
        "list_tags",
        HmiPageCursorCodec.QueryHash(new HmiOperationRequest { Operation = "list_tags" }),
        offset,
        ProjectBindingCursorState.FromSnapshot(new ProjectBindingSnapshot(
            ProjectBindingSnapshot.VerifiedState, "binding-a", 3, @"C:\Projects\Hmi.ap21",
            "worker-a", 1, 123, null)));

    [Fact]
    public void EncodeDecode_RoundTrips()
    {
        using var protector = new AuthenticatedCursorProtector(TestKey, "process-a");
        var codec = new HmiPageCursorCodec(protector);
        var state = State();

        var decoded = codec.Decode(codec.Encode(state));

        Assert.Equal(state, decoded);
    }

    [Fact]
    public void Decode_Tampered_IsInvalidCursor()
    {
        using var protector = new AuthenticatedCursorProtector(TestKey, "process-a");
        var codec = new HmiPageCursorCodec(protector);
        var cursor = codec.Encode(State());
        var tampered = cursor[..^2] + (cursor[^2] == 'A' ? "B" : "A") + cursor[^1];

        var error = Assert.Throws<CursorException>(() => codec.Decode(tampered));

        Assert.Equal(WorkerFailureCategories.InvalidCursor, error.Category);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cursor_FromOtherProcessKey_IsInvalidCursor(bool sameKey)
    {
        using var issuer = new AuthenticatedCursorProtector(TestKey, "process-a");
        using var reader = new AuthenticatedCursorProtector(sameKey ? TestKey : OtherKey, "process-b");
        var cursor = new HmiPageCursorCodec(issuer).Encode(State());

        var error = Assert.Throws<CursorException>(() => new HmiPageCursorCodec(reader).Decode(cursor));

        Assert.Equal(WorkerFailureCategories.InvalidCursor, error.Category);
    }

    [Fact]
    public void InvalidCursor_KeepsDomainMessage()
    {
        const string message = "The supplied hmi_read cursor is invalid.";
        using var issuer = new AuthenticatedCursorProtector(TestKey, "process-a");
        using var reader = new AuthenticatedCursorProtector(OtherKey, "process-b");
        var cursor = new HmiPageCursorCodec(issuer).Encode(State());
        var tampered = cursor[..^2] + (cursor[^2] == 'A' ? "B" : "A") + cursor[^1];

        Assert.Equal(message, Assert.ThrowsAny<Exception>(() =>
            new HmiPageCursorCodec(issuer).Decode(tampered)).Message);
        Assert.Equal(message, Assert.ThrowsAny<Exception>(() =>
            new HmiPageCursorCodec(reader).Decode(cursor)).Message);
    }

    [Fact]
    public void QueryHash_IgnoresLimit()
    {
        var a = new HmiOperationRequest { Operation = "list_tags", TableName = "T", Limit = 5 };
        var b = new HmiOperationRequest { Operation = "list_tags", TableName = "T", Limit = 500 };

        Assert.Equal(HmiPageCursorCodec.QueryHash(a), HmiPageCursorCodec.QueryHash(b));
        Assert.Matches("^[0-9a-f]{64}$", HmiPageCursorCodec.QueryHash(a));
    }

    [Theory]
    [InlineData("operation")]
    [InlineData("hmiName")]
    [InlineData("tableName")]
    [InlineData("language")]
    [InlineData("alarmKind")]
    [InlineData("dataLogName")]
    [InlineData("tagName")]
    [InlineData("screenName")]
    [InlineData("category")]
    [InlineData("name")]
    public void QueryHash_DiffersByEachFilter(string field)
    {
        var baseline = new HmiOperationRequest { Operation = "list_tags" };
        var changed = new HmiOperationRequest { Operation = "list_tags" };
        switch (field)
        {
            case "operation": changed.Operation = "list_alarms"; break;
            case "hmiName": changed.HmiName = "x"; break;
            case "tableName": changed.TableName = "x"; break;
            case "language": changed.Language = "x"; break;
            case "alarmKind": changed.AlarmKind = "x"; break;
            case "dataLogName": changed.DataLogName = "x"; break;
            case "tagName": changed.TagName = "x"; break;
            case "screenName": changed.ScreenName = "x"; break;
            case "category": changed.Category = "x"; break;
            case "name": changed.Name = "x"; break;
        }

        Assert.NotEqual(HmiPageCursorCodec.QueryHash(baseline), HmiPageCursorCodec.QueryHash(changed));
    }
}
