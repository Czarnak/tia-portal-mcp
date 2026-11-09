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

    // Reused by read and create-result decoders: each mutation breaks one declared wire rule.
    public static IEnumerable<object[]> MalformedMetadata()
    {
        var valid = WorkerJson.SerializePayload(new LibraryMetadataInfo
        {
            Comments = [new() { Text = "leak-sentinel" }], SupportedLanguages = ["en-US"],
            ActiveLanguages = ["en-US"], UsedProducts = [new()], History = [new()],
        });
        var metadata = System.Text.Json.Nodes.JsonNode.Parse(valid)!.AsObject();
        foreach (var member in metadata.ToArray())
        {
            var missing = metadata.DeepClone().AsObject();
            missing.Remove(member.Key);
            yield return ["missing:" + member.Key, missing.ToJsonString()];
        }
        foreach (var collection in new[] { "comments", "supportedLanguages", "activeLanguages", "usedProducts", "history", "diagnostics" })
        {
            var broken = metadata.DeepClone().AsObject();
            broken[collection] = new System.Text.Json.Nodes.JsonArray((System.Text.Json.Nodes.JsonNode?)null);
            yield return ["nullElement:" + collection, broken.ToJsonString()];
        }
        foreach (var collection in new[] { "comments", "usedProducts", "history" })
        {
            foreach (var field in metadata[collection]![0]!.AsObject().ToArray())
            {
                var broken = metadata.DeepClone().AsObject();
                broken[collection]![0]!.AsObject().Remove(field.Key);
                yield return ["missing:" + collection + ":" + field.Key, broken.ToJsonString()];
            }
        }
        yield return ["wrongType", valid.Replace("\"author\":null", "\"author\":42")];
        yield return ["wrongCollectionType", valid.Replace("\"diagnostics\":[]", "\"diagnostics\":false")];
        yield return ["wrongNestedType", valid.Replace("\"text\":\"leak-sentinel\"", "\"text\":42")];
        yield return ["duplicate", valid.Replace("\"author\":null", "\"author\":null,\"author\":\"leak-sentinel\"")];
        yield return ["case", valid.Replace("\"author\":null", "\"Author\":\"leak-sentinel\"")];
        yield return ["unknown", valid.Replace("\"author\":null", "\"unknown\":\"leak-sentinel\",\"author\":null")];
    }

    [Theory]
    [MemberData(nameof(MalformedMetadata))]
    public void MetadataMalformed_IsProtocolErrorWithoutEcho(string defect, string metadata)
    {
        var payload = WorkerJson.SerializePayload(new LibraryPageInfo { Items = [new() { Metadata = new() }] });
        payload = payload.Replace(WorkerJson.SerializePayload(new LibraryMetadataInfo()), metadata);
        var item = LibraryPayloadContract.Project(Request, WorkerCallResult.Ok(payload), _ => { });
        Assert.True(item.Status == OperationBatchStatus.Failed, defect);
        Assert.Equal("protocol_error", item.Failure!.Category);
        Assert.Null(item.Result);
        Assert.DoesNotContain("leak-sentinel", TiaMcpServer.Json.CanonicalJson.Serialize(item));
    }

    [Theory]
    [InlineData("pageDiagnostics")]
    [InlineData("rowDiagnostics")]
    [InlineData("nullRow")]
    [InlineData("identitySegment")]
    public void PageNullElements_AreProtocolErrorWithoutEcho(string defect)
    {
        var page = new LibraryPageInfo { Items = [new() { Identity = new() { Name = "leak-sentinel" } }] };
        if (defect == "pageDiagnostics") page.Diagnostics = [null!];
        if (defect == "rowDiagnostics") page.Items[0].Diagnostics = [null!];
        if (defect == "nullRow") page.Items = [null!];
        if (defect == "identitySegment") page.Items[0].Identity.FolderPath = [null!];
        var result = LibraryPayloadContract.Project(Request, WorkerCallResult.Ok(WorkerJson.SerializePayload(page)), _ => { });
        Assert.Equal("protocol_error", result.Failure!.Category);
        Assert.Null(result.Result);
        Assert.DoesNotContain("leak-sentinel", TiaMcpServer.Json.CanonicalJson.Serialize(result));
    }

    [Fact]
    public void MetadataNullableValuesRemainValid()
    {
        var payload = WorkerJson.SerializePayload(new LibraryPageInfo
            { Items = [new() { Metadata = new() }, new() { Metadata = new()
                { Comments = [new()], UsedProducts = [new()], History = [new()] } }] });
        var result = LibraryPayloadContract.Project(Request, WorkerCallResult.Ok(payload), _ => { });
        Assert.Equal("succeeded", result.Status);
        var metadata = result.Result!.Value.GetProperty("items")[0].GetProperty("metadata");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, metadata.GetProperty("author").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, metadata.GetProperty("comments").ValueKind);
    }

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
