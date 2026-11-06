using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Library;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Library;

public sealed class LibraryWriteContractDecodeTests
{
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
