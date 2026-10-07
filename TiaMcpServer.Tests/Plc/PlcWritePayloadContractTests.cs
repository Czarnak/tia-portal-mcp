using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Plc;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

public class PlcWritePayloadContractTests
{
    private static PlcOperationRequest Op(string operation, string? format = null) => new()
    {
        OperationId = "w",
        Operation = operation,
        Format = format,
    };

    private static BlockImportOutcomeInfo Outcome() => new()
    {
        ImportStage = "completed",
        ImportResultState = "succeeded",
        TargetMutationCommitted = true,
        CompileStage = "completed",
        FinalReadStage = "completed",
        TargetPresent = true,
        ContentRelation = "unverified",
        TemporarySourceState = "not_applicable",
    };

    [Fact]
    public void TagMutationDecodesTyped()
    {
        var payload = WorkerJson.SerializePayload(new TagMutationResultInfo
        {
            Operation = "create_tag",
            ProjectPath = null,
            PlcName = "PLC_1",
            TableName = "Inputs",
            TagName = "Start",
        });

        var item = PlcPayloadContract.ProjectWrite(Op("create_tag"), WorkerCallResult.Ok(payload, new[] { "w" }));

        Assert.Equal(OperationBatchStatus.Succeeded, item.Status);
        var result = item.Result!.Value;
        Assert.Equal("PLC_1", result.GetProperty("plcName").GetString());
        Assert.Equal("Start", result.GetProperty("tagName").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("userConstantName").ValueKind);
        Assert.Equal("w", Assert.Single(item.Warnings));
    }

    [Theory]
    [InlineData("create_block")]
    [InlineData("delete_block_group")]
    public void BlockMutationDecodesTyped(string operation)
    {
        var payload = WorkerJson.SerializePayload(new BlockMutationResultInfo
        {
            Operation = operation,
            PlcName = "PLC_1",
            BlockPath = "PLC_1/Blocks/B",
        });

        var item = PlcPayloadContract.ProjectWrite(Op(operation), WorkerCallResult.Ok(payload));

        Assert.Equal(OperationBatchStatus.Succeeded, item.Status);
        Assert.Equal("PLC_1/Blocks/B", item.Result!.Value.GetProperty("blockPath").GetString());
    }

    [Fact]
    public void TypeImportDecodesTyped()
    {
        var payload = WorkerJson.SerializePayload(new PlcTypeImportResultInfo
        {
            Operation = "update_type_content",
            TypePath = "PLC_1/Types/T",
            TypeName = "T",
            Format = "source",
            ProjectNodeRemoved = true,
            GeneratedObjectCount = 1,
        });

        var item = PlcPayloadContract.ProjectWrite(Op("update_type_content"), WorkerCallResult.Ok(payload));

        Assert.Equal(OperationBatchStatus.Succeeded, item.Status);
        Assert.Equal(1, item.Result!.Value.GetProperty("generatedObjectCount").GetInt32());
    }

    [Theory]
    [InlineData("create_tag", """{"success":true,"operation":"create_tag","secret":"leak-me"}""")]
    [InlineData("create_tag", """{"success":true,"operation":"create_tag"}""")]
    [InlineData("create_tag", "not json leak-me")]
    [InlineData("create_block", """{"success":true,"operation":"create_block"}""")]
    [InlineData("update_type_content", """{"success":true}""")]
    public void MissingMemberIsProtocolErrorWithoutEcho(string operation, string payload)
    {
        var diagnostics = new List<string>();

        var item = PlcPayloadContract.ProjectWrite(Op(operation), WorkerCallResult.Ok(payload), diagnostics.Add);

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.Null(item.Result);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
        Assert.DoesNotContain("leak-me", item.Failure.Message);
        Assert.DoesNotContain(diagnostics, line => line.Contains("leak-me"));
        Assert.Single(diagnostics);
    }

    [Fact]
    public void BlockImportSuccessCarriesOutcome()
    {
        var result = WorkerCallResult.Ok("Import succeeded.") with { BlockImportOutcome = Outcome() };

        var item = PlcPayloadContract.ProjectWrite(Op("update_block_logic", "source"), result);

        Assert.Equal(OperationBatchStatus.Succeeded, item.Status);
        var json = item.Result!.Value;
        Assert.Equal("source", json.GetProperty("format").GetString());
        Assert.Equal("completed", json.GetProperty("importOutcome").GetProperty("importStage").GetString());
        Assert.DoesNotContain("Import succeeded", json.GetRawText());
    }

    [Fact]
    public void BlockImportSuccessWithoutOutcomeIsProtocolError()
    {
        var item = PlcPayloadContract.ProjectWrite(
            Op("update_block_logic"), WorkerCallResult.Ok("Import succeeded."), _ => { });

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
    }

    [Fact]
    public void FailureKeepsCategoryAndOutcome()
    {
        var failed = WorkerCallResult.Fail(WorkerFailureCategories.StateChanged, "hash changed")
            with { BlockImportOutcome = Outcome() };

        var item = PlcPayloadContract.ProjectWrite(Op("update_block_logic"), failed);

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.Equal(WorkerFailureCategories.StateChanged, item.Failure!.Category);
        Assert.Equal("hash changed", item.Failure.Message);
        Assert.Equal(Outcome(), item.Failure.BlockImportOutcome);
    }

    [Fact]
    public void FailureWithoutOutcomeOmitsTheMember()
    {
        var item = PlcPayloadContract.ProjectWrite(
            Op("create_tag"), WorkerCallResult.Fail(WorkerFailureCategories.TargetNotFound, "no table"));

        Assert.Null(item.Failure!.BlockImportOutcome);
        Assert.DoesNotContain("blockImportOutcome", JsonSerializer.Serialize(item.Failure, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public void ReadOperationIsRejectedByTheWriteProjection()
    {
        var item = PlcPayloadContract.ProjectWrite(Op("get_block_content"), WorkerCallResult.Ok("x"), _ => { });

        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
    }
}
