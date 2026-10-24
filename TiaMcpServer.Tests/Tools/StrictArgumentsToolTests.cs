using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Tools;
using Xunit;

namespace TiaMcpServer.Tests.Tools;

[Collection("Mcp protocol serial")]
public sealed class StrictArgumentsToolTests
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record Item(int Id);

    public static class Fixture
    {
        [McpServerTool(Name = "echo"), Description("Strict argument fixture.")]
        public static string Echo(int[] values, bool flag = false, string? name = null, Item[]? items = null,
            string title = "default", int? count = null) => "entered";
    }

    [Fact]
    public async Task UnknownRootKey_IsRejectedBeforeEntry()
    {
        var text = await RejectedAsync("""{"values":[1],"extra":true}""");
        Assert.Contains("unknown argument 'extra'", text);
        Assert.Contains("Accepted arguments: count, flag, items, name, title, values.", text);
    }

    [Theory]
    [InlineData("plc_write", 256)]
    [InlineData("plc_write", 257)]
    [InlineData("plc_write", 1_000_000)]
    [InlineData("browse_project_tree", 256)]
    [InlineData("browse_project_tree", 257)]
    [InlineData("browse_project_tree", 1_000_000)]
    public async Task ProductionUnknownRootName_IsBoundedCanonicalAndUnaudited(string tool, int nameLength)
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.Full, audit.Path);
        var name = new string('n', nameLength);
        var before = requests.Methods();

        var reply = await harness.Client.CallToolAsync(tool, new Dictionary<string, object?> { [name] = true });

        var text = Text(reply);
        Assert.True(text.Length < 2_048, $"Rejection exceeded its fixed diagnostic bound: {text.Length} characters.");
        ArgumentRejectionAssert.IsCanonicalValidationError(reply, tool);
        var failure = reply.StructuredContent!.Value.GetProperty(tool == "browse_project_tree" ? "failure" : "error");
        var message = failure.GetProperty("message").GetString()!;
        Assert.Contains("unknown argument", message);
        Assert.Contains(tool == "browse_project_tree"
            ? "Accepted arguments: cursor, depth, pageSize, startSelector."
            : "Accepted arguments: dryRun, operations.", message);
        if (nameLength <= 256)
            Assert.Contains($"unknown argument '{name}'", message);
        else
            Assert.DoesNotContain(new string('n', 32), text);
        Assert.Equal(before, requests.Methods());
        Assert.Empty(Network.NetworkGuardedWriteMcpTests.AuditLines(audit.Path));
    }

    [Fact]
    public async Task MissingRequiredArgument_IsRejected()
        => Assert.Contains("missing required argument 'values'", await RejectedAsync("{}"));

    [Fact]
    public async Task WrongRootKind_IsRejected() => await RejectedAsync("""{"values":[1],"flag":"true"}""");

    [Fact]
    public async Task NullForNonNullableBoolean_IsRejected() => await RejectedAsync("""{"values":[1],"flag":null}""");

    [Fact]
    public async Task NullForNonNullableArray_IsRejected() => await RejectedAsync("""{"values":null}""");

    [Fact]
    public async Task NullForOptionalNonNullableString_IsRejected()
        => await RejectedAsync("""{"values":[1],"title":null}""");

    [Fact]
    public async Task NullForNullableString_Enters() => await EntersAsync("""{"values":[1],"name":null}""");

    [Fact]
    public async Task NullForNullableArray_Enters() => await EntersAsync("""{"values":[1],"items":null}""");

    [Fact]
    public async Task NullForNullableValueType_Enters() => await EntersAsync("""{"values":[1],"count":null}""");

    [Fact]
    public async Task NumberFromString_EntersLikeTheSdk() => await EntersAsync("""{"values":["1"]}""");

    [Fact]
    public async Task NestedUnmappedMember_IsRejectedWithPath()
        => Assert.Contains("$[0]", await RejectedAsync("""{"values":[1],"items":[{"bogus":1}]}"""));

    [Fact]
    public async Task MalformedValue_IsNotEchoed()
        => Assert.DoesNotContain("secret-token-xyz", await RejectedAsync("""{"values":["secret-token-xyz"]}"""));

    [Fact]
    public async Task OverlongPath_IsOmitted()
    {
        var name = new string('n', 300);
        var text = await RejectedAsync("{\"values\":[1],\"items\":[{\"" + name + "\":1}]}");
        Assert.DoesNotContain(" at ", text);
        Assert.DoesNotContain(name, text);
    }

    [Fact]
    public async Task RetiredItemMember_IsRejectedWithHint()
    {
        var retired = new Dictionary<string, string>(StringComparer.Ordinal) { ["old"] = "use 'new'" };
        var text = await RejectedAsync("""{"values":[1],"items":[{"old":1}]}""", retired);
        Assert.Contains("use 'new'", text);
    }

    [Fact]
    public async Task ValidCall_Enters() => await EntersAsync("""{"values":[1]}""");

    [Fact]
    public async Task InputSchemaRootIsClosed()
    {
        await using var harness = await StartAsync(null);
        var tool = Assert.Single(await harness.Client.ListToolsAsync()).ProtocolTool;
        Assert.False(tool.InputSchema.GetProperty("additionalProperties").GetBoolean());
    }

    private static async Task<string> RejectedAsync(string arguments, IReadOnlyDictionary<string, string>? retired = null)
    {
        var reply = await CallAsync(arguments, retired);
        var text = Text(reply);
        Assert.True(reply.IsError == true, text);
        Assert.StartsWith("REJECT:", text);
        return text;
    }

    private static async Task EntersAsync(string arguments)
    {
        var reply = await CallAsync(arguments, null);
        Assert.False(reply.IsError == true, Text(reply));
        Assert.Equal("entered", Text(reply));
    }

    private static async Task<CallToolResult> CallAsync(string arguments, IReadOnlyDictionary<string, string>? retired)
    {
        await using var harness = await StartAsync(retired);
        var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(arguments)!;
        return await harness.Client.CallToolAsync("echo", args.ToDictionary(pair => pair.Key, pair => (object?)pair.Value));
    }

    private static Task<McpProtocolTestHarness> StartAsync(IReadOnlyDictionary<string, string>? retired)
        => McpProtocolTestHarness.StartAsync(McpAccessMode.ReadOnly, builder => builder.Services.AddSingleton<McpServerTool>(services =>
            StrictArgumentsTool.CreateClosedTool(typeof(Fixture).GetMethod(nameof(Fixture.Echo))!, services, Reject,
                closeNestedSchemas: true, retiredItemMembers: retired)));

    private static CallToolResult Reject(string message) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = "REJECT:" + message }],
    };

    private static string Text(CallToolResult reply) => Assert.Single(reply.Content.OfType<TextContentBlock>()).Text;
}
