using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Library;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Library;

public sealed class LibraryWriteContractDecodeTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("case")]
    [InlineData("nullEntry")]
    public void Decode_TypeOutcome_RawEnvelopeRejectsMalformedNestedEvidence(string defect)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(WorkerJson.SerializePayload(new LibraryWriteOutcomeInfo { Type = new() }))!;
        var type = node["type"]!.AsObject();
        if (defect == "missing") type.Remove("versionGuid");
        if (defect == "unknown") type["unexpected"] = true;
        if (defect == "case") { type["VersionGuid"] = type["versionGuid"]?.DeepClone(); type.Remove("versionGuid"); }
        if (defect == "nullEntry") type["instancePaths"] = new System.Text.Json.Nodes.JsonArray((System.Text.Json.Nodes.JsonNode?)null);
        using var envelope = System.Text.Json.JsonDocument.Parse("{\"libraryWriteOutcome\":" + node.ToJsonString() + "}");
        Assert.Throws<System.Text.Json.JsonException>(() => LibraryWritePayloadContract.ValidateEnvelopeOutcome(envelope.RootElement));
    }
    [Theory]
    [InlineData("nullEntry")]
    [InlineData("nullName")]
    [InlineData("nullGuid")]
    public void Decode_TypeOutcome_Malformed_ProtocolError(string defect)
    {
        var type = new LibraryTypeResultInfo();
        if (defect == "nullEntry") type.InstancePaths = [null!];
        if (defect == "nullName") type.TypeName = null!;
        if (defect == "nullGuid") type.TypeGuid = null!;
        var result = LibraryWritePayloadContract.Project(Item, WorkerCallResult.Fail("worker_operation_failed", "failed") with
        { LibraryWriteOutcome = new() { Type = type } });
        Assert.Equal("protocol_error", result.Failure!.Category);
        Assert.Null(result.Result);
    }
    private static readonly LibraryWriteItem Item = new() { OperationId = "a", Operation = "close_global" };

    [Fact]
    public void SuccessDecodesTypedResultWithExplicitNulls()
    {
        var result = LibraryWritePayloadContract.Project(Item, WorkerCallResult.Ok(WorkerJson.SerializePayload(new LibraryWriteResultInfo
        {
            Library = new() { Scope = "global", Kind = "library", Name = "Fixture", LibraryPath = @"C:\Libraries\Fixture.al21" }, Saved = true,
        })));
        Assert.Equal("succeeded", result.Status);
        Assert.True(result.Result!.Value.GetProperty("saved").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, result.Result.Value.GetProperty("target").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Object, result.Result.Value.GetProperty("library").ValueKind);
    }

    [Fact]
    public void FailureWithoutOutcomeHasNoInventedReceipt()
    {
        var result = LibraryWritePayloadContract.Project(Item, WorkerCallResult.Fail("target_not_found", "missing", ["warning"]));
        Assert.Equal("failed", result.Status);
        Assert.Null(result.Result);
        Assert.Equal("target_not_found", result.Failure!.Category);
        Assert.Equal(["warning"], result.Warnings);
    }

    [Theory]
    [InlineData("library")]
    [InlineData("target")]
    public void NullFolderSegmentInAnyIdentityRejectsResultEvidence(string position)
    {
        var identity = new LibraryIdentityInfo { Name = "leak-sentinel", FolderPath = [null!] };
        var payload = new LibraryWriteResultInfo();
        if (position == "library") payload.Library = identity;
        else payload.Target = identity;
        var result = LibraryWritePayloadContract.Project(Item, WorkerCallResult.Ok(WorkerJson.SerializePayload(payload)));
        Assert.Equal("failed", result.Status);
        Assert.Equal("protocol_error", result.Failure!.Category);
        Assert.Null(result.Result);
        Assert.DoesNotContain("leak-sentinel", TiaMcpServer.Json.CanonicalJson.Serialize(result));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyFolderPathsAndOptionalNullTargetRemainValid(bool targetPresent)
    {
        var payload = new LibraryWriteResultInfo { Target = targetPresent ? new() : null };
        var result = LibraryWritePayloadContract.Project(Item, WorkerCallResult.Ok(WorkerJson.SerializePayload(payload)));
        Assert.Equal("succeeded", result.Status);
        var document = result.Result!.Value;
        Assert.Equal(0, document.GetProperty("library").GetProperty("folderPath").GetArrayLength());
        if (targetPresent) Assert.Equal(0, document.GetProperty("target").GetProperty("folderPath").GetArrayLength());
        else Assert.Equal(System.Text.Json.JsonValueKind.Null, document.GetProperty("target").ValueKind);
    }

    [Fact]
    public void FailurePreservesPartialReceipt()
    {
        var result = LibraryWritePayloadContract.Project(Item, WorkerCallResult.Fail("engineering_rejected", "close failed") with
            { LibraryWriteOutcome = new() { Saved = true, ActualName = null } });
        Assert.Equal("failed", result.Status);
        Assert.True(result.Result!.Value.GetProperty("saved").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, result.Result.Value.GetProperty("actualName").ValueKind);
        Assert.Equal("engineering_rejected", result.Failure!.Category);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("not json")]
    [InlineData("{\"leak-sentinel\":1}")]
    public void InvalidSuccessFailsClosedWithoutEcho(string payload)
    {
        var result = LibraryWritePayloadContract.Project(Item, WorkerCallResult.Ok(payload));
        Assert.Equal("protocol_error", result.Failure!.Category);
        Assert.Null(result.Result);
        Assert.DoesNotContain("leak-sentinel", result.Failure.Message);
    }
}
