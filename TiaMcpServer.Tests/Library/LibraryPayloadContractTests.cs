using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Cursors;
using TiaMcpServer.Library;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Library;

public sealed class LibraryPayloadContractTests
{
    private static readonly LibraryPageCursorCodec Codec = new(AuthenticatedCursorProtector.CreateProcessScoped());

    private static readonly ProjectBindingSnapshot Binding = new(
        ProjectBindingSnapshot.VerifiedState, "binding-a", 3, @"C:\Projects\Lib.ap21", "worker-a", 1, 123, null);

    private static readonly LibraryReadItem Request = new()
    {
        OperationId = "a", Operation = "list_children", Scope = "project", FolderKind = "types",
    };

    private static string Page(int? nextOffset, string? snapshotId, bool complete = false)
        => WorkerJson.SerializePayload(new LibraryPageInfo
        {
            Items = { new LibraryEntryInfo { Identity = { Scope = "project", Kind = "type", Name = "T" } } },
            Total = 5, Offset = 0, NextOffset = nextOffset, SnapshotId = snapshotId, Complete = complete,
        });

    [Fact]
    public void Project_PageWithSnapshot_SealsCursorAndDropsSnapshotId()
    {
        var item = LibraryPayloadContract.Project(Request, WorkerCallResult.Ok(Page(2, "s1")), Codec, Binding);

        var page = item.Result!.Value;
        Assert.False(page.TryGetProperty("snapshotId", out _));
        var state = Codec.Decode(page.GetProperty("nextCursor").GetString()!);
        Assert.Equal("s1", state.SnapshotId);
        Assert.Equal("list_children", state.Operation);
        Assert.Equal(LibraryPageCursorCodec.QueryHash(Request), state.QueryHash);
        Assert.Equal(2, state.Offset);
        Assert.True(state.HostBinding.Matches(Binding));
    }

    [Fact]
    public void Project_LastPage_HasExplicitNullCursorAndNoSnapshotId()
    {
        var item = LibraryPayloadContract.Project(Request, WorkerCallResult.Ok(Page(null, "s1", complete: true)), Codec, Binding);

        var page = item.Result!.Value;
        Assert.Equal(System.Text.Json.JsonValueKind.Null, page.GetProperty("nextCursor").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, page.GetProperty("nextOffset").ValueKind);
        Assert.False(page.TryGetProperty("snapshotId", out _));
    }

    [Fact]
    public void Project_TypeRow_WritesExplicitNullLibraryTypeAndIsOpen()
    {
        var item = LibraryPayloadContract.Project(Request, WorkerCallResult.Ok(Page(null, null, complete: true)), Codec, Binding);

        var row = item.Result!.Value.GetProperty("items")[0];
        Assert.Equal(System.Text.Json.JsonValueKind.Null, row.GetProperty("libraryType").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, row.GetProperty("isOpen").ValueKind);
        Assert.Contains("\"libraryType\":null", Page(null, null), StringComparison.Ordinal);
        Assert.Contains("\"isOpen\":null", Page(null, null), StringComparison.Ordinal);
    }

    [Fact]
    public void Project_PayloadWithMissingMember_IsProtocolErrorWithoutEcho()
    {
        var diagnostics = new List<string>();

        var item = LibraryPayloadContract.Project(Request,
            WorkerCallResult.Ok("{\"items\":[],\"leak-sentinel\":1}"), diagnostics.Add);

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.Equal("protocol_error", item.Failure!.Category);
        Assert.DoesNotContain("leak-sentinel", item.Failure.Message);
    }

    [Fact]
    public void Project_WorkerFailure_KeepsCategoryAndWarnings()
    {
        var failed = WorkerCallResult.Fail("target_not_found", "Library is not open.") with { Warnings = new[] { "w" } };

        var item = LibraryPayloadContract.Project(Request, failed, Codec, Binding);

        Assert.Equal("target_not_found", item.Failure!.Category);
        Assert.Equal(new[] { "w" }, item.Warnings);
    }
}
