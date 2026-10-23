using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Cursors;
using TiaMcpServer.Network;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public class NetworkObjectPageCursorCodecTests
{
    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

    [Fact]
    public void RoundTrip_PreservesWorkerTokenAndCompleteIdentity()
    {
        using var protector = new AuthenticatedCursorProtector(Key, "process-a");
        var codec = new NetworkObjectPageCursorCodec(protector);
        var state = State();
        var decoded = codec.Decode(codec.Encode(state));
        Assert.Equal(state.WorkerCursor, decoded.WorkerCursor);
        Assert.Equal(state.HostBinding, decoded.HostBinding);
        Assert.Equal(state.SessionIdentity.WorkerSessionId, decoded.SessionIdentity.WorkerSessionId);
        Assert.Equal(state.SessionIdentity.SessionGeneration, decoded.SessionIdentity.SessionGeneration);
        Assert.Equal(state.SessionIdentity.PortalProcessId, decoded.SessionIdentity.PortalProcessId);
        Assert.Equal(state.SessionIdentity.ProjectPath, decoded.SessionIdentity.ProjectPath);
    }

    [Theory]
    [InlineData("legacy-unsigned-token")]
    [InlineData("bad.token")]
    [InlineData("")]
    public void MalformedOrUnsigned_IsInvalidCursor(string token)
    {
        using var protector = new AuthenticatedCursorProtector(Key, "process-a");
        AssertInvalid(() => new NetworkObjectPageCursorCodec(protector).Decode(token));
    }

    [Fact]
    public void TamperedWrongPurposeAndForeignProcess_AreInvalidCursor()
    {
        using var protector = new AuthenticatedCursorProtector(Key, "process-a");
        using var other = new AuthenticatedCursorProtector(Key, "process-b");
        var codec = new NetworkObjectPageCursorCodec(protector);
        var token = codec.Encode(State());
        AssertInvalid(() => codec.Decode((token[0] == 'a' ? "b" : "a") + token[1..]));
        AssertInvalid(() => codec.Decode(protector.Protect("hardware-page", State())));
        AssertInvalid(() => new NetworkObjectPageCursorCodec(other).Decode(token));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("workerCursor")]
    [InlineData("binding")]
    [InlineData("unbound")]
    [InlineData("bindingId")]
    [InlineData("revision")]
    [InlineData("session")]
    [InlineData("sessionId")]
    [InlineData("generation")]
    [InlineData("pid")]
    [InlineData("path")]
    [InlineData("noncanonicalPath")]
    public void InvalidState_IsRejectedOnEncodeAndDecode(string field)
    {
        var state = State();
        state = field switch
        {
            "version" => state with { Version = 2 },
            "workerCursor" => state with { WorkerCursor = " " },
            "binding" => state with { HostBinding = null! },
            "unbound" => state with { HostBinding = state.HostBinding with { IsBound = false } },
            "bindingId" => state with { HostBinding = state.HostBinding with { BindingId = " " } },
            "revision" => state with { HostBinding = state.HostBinding with { Revision = -1 } },
            "session" => state with { SessionIdentity = null! },
            _ => state,
        };
        if (field == "sessionId") state.SessionIdentity.WorkerSessionId = " ";
        if (field == "generation") state.SessionIdentity.SessionGeneration = -1;
        if (field == "pid") state.SessionIdentity.PortalProcessId = 0;
        if (field == "path") state.SessionIdentity.ProjectPath = @"C:\Projects\Other.ap21";
        if (field == "noncanonicalPath") state.SessionIdentity.ProjectPath = @"C:\Projects\sub\..\Demo.ap21";
        using var protector = new AuthenticatedCursorProtector(Key, "process-a");
        var codec = new NetworkObjectPageCursorCodec(protector);
        AssertInvalid(() => codec.Encode(state));
        AssertInvalid(() => codec.Decode(protector.Protect("network-object-page", state)));
    }

    private static void AssertInvalid(Action action)
    {
        var error = Assert.Throws<CursorException>(action);
        Assert.Equal(WorkerFailureCategories.InvalidCursor, error.Category);
        Assert.Equal("The supplied network object cursor is not valid for this request.", error.Message);
    }

    private static NetworkObjectPageCursorState State() => new(1, "worker-internal-token",
        new ProjectBindingCursorState(true, "binding-a", 1, @"C:\Projects\Demo.ap21"),
        new WorkerSessionIdentity
        {
            WorkerSessionId = "worker-a", SessionGeneration = 1, PortalProcessId = 4242,
            ProjectPath = @"C:\Projects\Demo.ap21",
        });
}
