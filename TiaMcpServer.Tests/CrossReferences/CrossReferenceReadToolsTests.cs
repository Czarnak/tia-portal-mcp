using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.CrossReferences;
using TiaMcpServer.Safety;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Tests.Worker;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.CrossReferences;

[Collection(RealWorkerProcessCollection.Name)]
public class CrossReferenceReadToolsTests
{
    private const string Scenario = "xref-roundtrip";

    private static OpennessWorkerClient CreateClient()
        => new(
            new ProjectSessionBinding(null),
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate(),
            accessPolicy: new OperationAccessPolicy(McpAccessMode.ReadOnly));

    private static async Task<OpennessWorkerClient> CreateBoundClientAsync()
    {
        var binding = new ProjectSessionBinding(null);
        var client = new OpennessWorkerClient(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate(),
            accessPolicy: new OperationAccessPolicy(McpAccessMode.ReadOnly));
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, Scenario);
        return client;
    }

    private static CrossReferenceTargetSelector Plc(string softwareName = "PLC_1") => new()
    {
        Path =
        {
            new ProjectTreeSelectorSegment { NodeType = ProjectTreeNodeTypes.Device, Name = "PLC_1_Device" },
            new ProjectTreeSelectorSegment { NodeType = ProjectTreeNodeTypes.PlcSoftware, Name = softwareName },
        },
    };

    private static JsonElement Structured(CallToolResult result) => Assert.IsType<JsonElement>(result.StructuredContent);

    private static string Category(CallToolResult result)
        => Structured(result).GetProperty("error").GetProperty("category").GetString()!;

    [Fact]
    public async Task ReturnsTypedReportForPlcSweep()
    {
        using var client = await CreateBoundClientAsync();

        var result = await CrossReferenceReadTools.ReadCrossReferences(client, Plc());

        Assert.False(result.IsError);
        var root = Structured(result);
        Assert.Equal("read_cross_references", root.GetProperty("tool").GetString());
        Assert.Equal("1.0", root.GetProperty("contractVersion").GetString());
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("warnings").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        var value = root.GetProperty("result").GetProperty("value");
        Assert.True(value.GetProperty("isComplete").GetBoolean());
        Assert.Equal("FB_Main", value.GetProperty("sources")[0].GetProperty("name").GetString());
        Assert.Equal("Read", value.GetProperty("sources")[0].GetProperty("references")[0]
            .GetProperty("locations")[0].GetProperty("access").GetString());
    }

    [Fact]
    public async Task InvalidPathIsInvalidSelectorRejection()
    {
        using var client = CreateClient();
        var target = new CrossReferenceTargetSelector
        {
            Path = { new ProjectTreeSelectorSegment { NodeType = ProjectTreeNodeTypes.PlcSoftware, Name = "PLC_1" } },
        };

        var result = await CrossReferenceReadTools.ReadCrossReferences(client, target);

        Assert.True(result.IsError);
        Assert.Equal("invalid_selector", Category(result));
        var message = Structured(result).GetProperty("error").GetProperty("message").GetString()!;
        Assert.Contains("target.path", message);
        Assert.DoesNotContain("startSelector", message);
        Assert.Equal(JsonValueKind.Null, Structured(result).GetProperty("result").ValueKind);
    }

    [Fact]
    public async Task Selector_NeitherPathNorHmi_IsValidationError()
    {
        using var client = CreateClient();

        var result = await CrossReferenceReadTools.ReadCrossReferences(client, new CrossReferenceTargetSelector());

        Assert.True(result.IsError);
        Assert.Equal("validation_error", Category(result));
    }

    private static CrossReferenceHmiSelector HmiTag(string hmiName = "HMI_RT_1", string tagName = "Tag1")
        => new() { HmiName = hmiName, TagName = tagName };

    [Fact]
    public async Task HmiSelector_WithPath_IsValidationError()
    {
        using var client = CreateClient();
        var target = Plc();
        target.Hmi = HmiTag();

        var result = await CrossReferenceReadTools.ReadCrossReferences(client, target);

        Assert.True(result.IsError);
        Assert.Equal("validation_error", Category(result));
    }

    [Theory]
    [InlineData(" ", "Tag1")]
    [InlineData("", "Tag1")]
    public async Task HmiSelector_BlankHmiName_IsValidationError(string hmiName, string tagName)
    {
        using var client = CreateClient();

        var result = await CrossReferenceReadTools.ReadCrossReferences(
            client, new CrossReferenceTargetSelector { Hmi = HmiTag(hmiName, tagName) });

        Assert.True(result.IsError);
        Assert.Equal("validation_error", Category(result));
        Assert.Contains("target.hmi.hmiName", Structured(result).GetProperty("error").GetProperty("message").GetString());
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("")]
    public async Task HmiSelector_BlankTagName_IsValidationError(string tagName)
    {
        using var client = CreateClient();

        var result = await CrossReferenceReadTools.ReadCrossReferences(
            client, new CrossReferenceTargetSelector { Hmi = HmiTag(tagName: tagName) });

        Assert.True(result.IsError);
        Assert.Equal("validation_error", Category(result));
        Assert.Contains("target.hmi.tagName", Structured(result).GetProperty("error").GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("""{"hmi":{"hmiName":"HMI_RT_1","tagName":"Tag1","member":"x"}}""", "member")]
    [InlineData("""{"hmi":{"tagName":"Tag1"}}""", "hmiName")]
    public void HmiSelector_UnknownOrMissingKey_IsRejectedAtBinding(string json, string key)
    {
        // The SDK binds arguments with these rules, so an unknown or missing key never reaches the tool.
        var exception = Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<CrossReferenceTargetSelector>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        Assert.Contains(key, exception.Message);
    }

    [Fact]
    public void HmiSelector_ParsesWithoutPath()
    {
        var target = JsonSerializer.Deserialize<CrossReferenceTargetSelector>(
            """{"hmi":{"hmiName":"HMI_RT_1","tagName":"Tag1"}}""", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        var info = target.ToInfo();
        Assert.Equal("HMI_RT_1", info.Hmi!.HmiName);
        Assert.Equal("Tag1", info.Hmi.TagName);
        Assert.Empty(info.Path);
    }

    [Theory]
    [InlineData("Bogus", false)]
    [InlineData("tag", true)]
    [InlineData("Tag", false)]
    public async Task BadMemberIsInvalidSelectorRejection(string kind, bool tablePath)
    {
        using var client = CreateClient();
        var target = Plc();
        if (tablePath)
        {
            target.Path.Add(new ProjectTreeSelectorSegment { NodeType = ProjectTreeNodeTypes.TagTableFolder, Name = "PLC tags" });
            target.Path.Add(new ProjectTreeSelectorSegment { NodeType = ProjectTreeNodeTypes.TagTable, Name = "Default" });
        }
        target.Member = new CrossReferenceMemberSelector { Kind = kind, Name = "Tag1" };

        var result = await CrossReferenceReadTools.ReadCrossReferences(client, target);

        Assert.True(result.IsError);
        Assert.Equal("invalid_selector", Category(result));
    }

    [Fact]
    public async Task UnknownFilterIsValidationError()
    {
        using var client = CreateClient();

        var result = await CrossReferenceReadTools.ReadCrossReferences(client, Plc(), filter: "Everything");

        Assert.True(result.IsError);
        Assert.Equal("validation_error", Category(result));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task MaxResultsZeroIsValidationError(int maxResults)
    {
        using var client = CreateClient();

        var result = await CrossReferenceReadTools.ReadCrossReferences(client, Plc(), maxResults: maxResults);

        Assert.True(result.IsError);
        Assert.Equal("validation_error", Category(result));
    }

    [Fact]
    public async Task TargetKindUnsupportedIsTypedFailureNotEmptySuccess()
    {
        using var client = await CreateBoundClientAsync();

        var result = await CrossReferenceReadTools.ReadCrossReferences(client, Plc("Unsupported"));

        Assert.False(result.IsError);
        var root = Structured(result);
        Assert.False(root.GetProperty("success").GetBoolean());
        var outcome = root.GetProperty("result");
        Assert.Equal("failed", outcome.GetProperty("status").GetString());
        Assert.Equal("target_kind_unsupported", outcome.GetProperty("failure").GetProperty("category").GetString());
        Assert.Equal(JsonValueKind.Null, outcome.GetProperty("value").ValueKind);
    }

    [Fact]
    public async Task IncompleteUnusedObjectsCarriesWarning()
    {
        using var client = await CreateBoundClientAsync();

        var result = await CrossReferenceReadTools.ReadCrossReferences(
            client, Plc(), filter: "UnusedObjects");

        var root = Structured(result);
        Assert.False(root.GetProperty("result").GetProperty("value").GetProperty("isComplete").GetBoolean());
        Assert.Contains(
            "An incomplete UnusedObjects result is not proof that objects can be deleted.",
            root.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()));
    }

    [Fact]
    public async Task TextEqualsStructuredContent()
    {
        using var client = await CreateBoundClientAsync();

        var result = await CrossReferenceReadTools.ReadCrossReferences(client, Plc());

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal(text, Structured(result).GetRawText());
    }
}
