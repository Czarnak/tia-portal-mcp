using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Plc;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

public class PlcPayloadContractTests
{
    private static PlcOperationRequest Block(bool? withDependencies = null, string? format = null) => new()
    {
        OperationId = "a",
        Operation = "get_block_content",
        BlockPath = "PLC_1/Main",
        WithDependencies = withDependencies,
        Format = format,
    };

    private static PlcOperationRequest Tags() => new() { OperationId = "t", Operation = "list_tag_tables" };

    [Fact]
    public void ContentResultCarriesFormatAndHash()
    {
        var item = PlcPayloadContract.Project(Block(), WorkerCallResult.Ok("<Doc/>", new[] { "w" }));

        Assert.Equal(OperationBatchStatus.Succeeded, item.Status);
        var result = item.Result!.Value;
        Assert.Equal("xml", result.GetProperty("format").GetString());
        Assert.Equal("<Doc/>", result.GetProperty("content").GetString());
        Assert.Equal(PlcContentHashes.Compute("xml", "<Doc/>"), result.GetProperty("contentHash").GetString());
        Assert.StartsWith("xml:sha256:", result.GetProperty("contentHash").GetString());
        Assert.Equal("w", result.GetProperty("warnings")[0].GetString());
        Assert.Equal("w", Assert.Single(item.Warnings));
    }

    [Fact]
    public void TypeContentDefaultsToSourceFormat()
    {
        var op = new PlcOperationRequest { OperationId = "a", Operation = "get_type_content", TypePath = "PLC_1/Types/T" };

        var item = PlcPayloadContract.Project(op, WorkerCallResult.Ok("TYPE"));

        Assert.StartsWith("source:sha256:", item.Result!.Value.GetProperty("contentHash").GetString());
    }

    [Fact]
    public void WithDependenciesHashIsNull()
    {
        var item = PlcPayloadContract.Project(Block(withDependencies: true, format: "source"), WorkerCallResult.Ok("many"));

        var hash = item.Result!.Value.GetProperty("contentHash");
        Assert.Equal(JsonValueKind.Null, hash.ValueKind);
    }

    [Fact]
    public void TagInventoryDecodesTyped()
    {
        var inventory = new PlcTagInventoryInfo
        {
            IsComplete = false,
            Messages = { "skipped table X" },
            Plcs = { new PlcTagInventoryPlcInfo { PlcName = "PLC_1", DeviceName = null } },
        };

        var item = PlcPayloadContract.Project(Tags(), WorkerCallResult.Ok(WorkerJson.SerializePayload(inventory)));

        Assert.Equal(OperationBatchStatus.Succeeded, item.Status);
        var result = item.Result!.Value;
        Assert.False(result.GetProperty("isComplete").GetBoolean());
        Assert.Equal("PLC_1", result.GetProperty("plcs")[0].GetProperty("plcName").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("plcs")[0].GetProperty("deviceName").ValueKind);
    }

    [Theory]
    [InlineData("""{"tables":[],"secret":"leak-me"}""")]
    [InlineData("not json leak-me")]
    [InlineData("""{"isComplete":true}""")]
    public void MalformedInventoryIsProtocolErrorWithoutEcho(string payload)
    {
        var item = PlcPayloadContract.Project(Tags(), WorkerCallResult.Ok(payload));

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.Null(item.Result);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
        Assert.DoesNotContain("leak-me", item.Failure.Message);
        Assert.DoesNotContain("secret", item.Failure.Message);
    }

    private const string ValidTag =
        """{"name":"A","dataType":"Bool","logicalAddress":"%I0.0","externalAccessible":null,"externalVisible":null,"externalWritable":null}""";

    private static string Inventory(string messages = "[]", string tables = "[]")
        => $$"""{"isComplete":true,"messages":{{messages}},"plcs":[{"plcName":"P","deviceName":null,"tables":{{tables}}}]}""";

    private static string Table(string tags = "[" + ValidTag + "]", string constants = "[]")
        => $$"""[{"name":"T","folderPath":"/","isDefault":false,"tags":{{tags}},"userConstants":{{constants}}}]""";

    public static IEnumerable<object[]> NullElementInventories() => new[]
    {
        new object[] { """{"isComplete":true,"messages":[],"plcs":[null]}""" },
        new object[] { """{"isComplete":true,"messages":[],"plcs":null}""" },
        new object[] { Inventory(messages: "null") },
        new object[] { Inventory(tables: "null") },
        new object[] { Inventory(messages: "[null]") },
        new object[] { Inventory(tables: "[null]") },
        new object[] { Inventory(tables: Table(tags: "[null]")) },
        new object[] { Inventory(tables: Table(tags: "null")) },
        new object[] { Inventory(tables: Table(constants: "[null]")) },
        new object[] { Inventory(tables: Table(constants: "null")) },
    };

    [Fact]
    public void InventoryFixtureIsValid()
        => Assert.Equal(OperationBatchStatus.Succeeded, PlcPayloadContract.Project(Tags(), WorkerCallResult.Ok(Inventory(tables: Table()))).Status);

    [Theory]
    [MemberData(nameof(NullElementInventories))]
    public void NullInventoryElementsAreProtocolErrorWithoutEcho(string payload)
    {
        var item = PlcPayloadContract.Project(Tags(), WorkerCallResult.Ok(payload));

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.Null(item.Result);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
        Assert.DoesNotContain("null", item.Failure.Message);
    }

    [Fact]
    public void ProtocolErrorWritesServerDiagnosticWithoutPayload()
    {
        var lines = new List<string>();

        _ = PlcPayloadContract.Project(Tags(), WorkerCallResult.Ok("leak-me"), lines.Add);

        var line = Assert.Single(lines);
        Assert.Contains("operation=list_tag_tables", line);
        Assert.DoesNotContain("leak-me", line);
    }

    [Fact]
    public void FormatErrorTextCarriesNoParameterSuffix()
    {
        var error = Assert.Throws<ArgumentException>(() => PlcFormatNames.Normalize("get_block_content", "yaml"));
        Assert.DoesNotContain("(Parameter", error.Message);
        Assert.StartsWith("Invalid format 'yaml'", error.Message.Replace("'", "'"));
    }

    [Fact]
    public void WorkerFailureKeepsCategory()
    {
        var failure = WorkerCallResult.Fail(WorkerFailureCategories.TargetNotFound, "nope", new[] { "w" });

        var item = PlcPayloadContract.Project(Block(), failure);

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.Equal(WorkerFailureCategories.TargetNotFound, item.Failure!.Category);
        Assert.Equal("nope", item.Failure.Message);
        Assert.Equal("w", Assert.Single(item.Warnings));
    }
}
