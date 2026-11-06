using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Cursors;
using TiaMcpServer.Library;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Tests.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Library;

[Collection("Mcp protocol serial")]
public sealed class LibraryReadToolsTests
{
    private const string Scenario = "library-read-roundtrip";

    private static LibraryPageCursorCodec NewCodec() => new(AuthenticatedCursorProtector.CreateProcessScoped());

    private static JsonElement Items(CallToolResult result)
        => CanonicalToolDocument.Read(result).GetProperty("batch").GetProperty("operations");

    private static LibraryReadItem GlobalList(string id = "g", int? limit = null, string? cursor = null)
        => new() { OperationId = id, Operation = "list_global_libraries", Limit = limit, Cursor = cursor };

    private static LibraryReadItem GlobalInspect(string id, string name)
        => new() { OperationId = id, Operation = "inspect_library", Scope = "global", LibraryName = name };

    private static ValueTask<CallToolResult> CallAsync(McpProtocolTestHarness harness, params object[] operations)
        => harness.Client.CallToolAsync("library_read", new Dictionary<string, object?> { ["operations"] = operations });

    [Fact]
    public async Task Unbound_AnyBatch_WholeCallBindingConflict()
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<LibraryReadTools>(accessMode: McpAccessMode.ReadOnly);

        var result = await CallAsync(harness,
            new { operationId = "g", operation = "list_global_libraries" });

        Assert.True(result.IsError);
        Assert.Empty(StructuredContractInspector.FindViolations(result));
        var root = result.StructuredContent!.Value;
        Assert.Equal("binding_conflict", root.GetProperty("error").GetProperty("category").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("batch").ValueKind);
    }

    [Fact]
    public async Task ItemFailures_DoNotHideLaterItems()
    {
        using var client = await FakeWorkerClients.CreateBoundAsync(Scenario, McpAccessMode.ReadOnly);

        var result = await LibraryReadTools.LibraryRead(client, NewCodec(),
            new[] { GlobalInspect("missing", "Missing"), GlobalList("after") });

        Assert.False(result.IsError);
        var items = Items(result);
        Assert.Equal("failed", items[0].GetProperty("status").GetString());
        Assert.Equal("target_not_found", items[0].GetProperty("failure").GetProperty("category").GetString());
        Assert.Equal("succeeded", items[1].GetProperty("status").GetString());
        Assert.False(CanonicalToolDocument.Read(result).GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task MalformedPayload_ProtocolError_NotEchoed()
    {
        using var client = await FakeWorkerClients.CreateBoundAsync(Scenario, McpAccessMode.ReadOnly);

        var result = await LibraryReadTools.LibraryRead(client, NewCodec(), new[] { GlobalInspect("bad", "Malformed") });

        Assert.False(result.IsError);
        var item = Items(result)[0];
        Assert.Equal("protocol_error", item.GetProperty("failure").GetProperty("category").GetString());
        Assert.DoesNotContain("secret-sentinel", CanonicalToolDocument.Read(result).GetRawText());
    }

    // Item members bind case-insensitively through the SDK serializer in every batch tool, so a miscased
    // member is only detectable at the root, where keys are matched ordinally.
    [Theory]
    [InlineData("itemUnknown")]
    [InlineData("rootMiscased")]
    public async Task UnknownOrMiscasedField_ValidationErrorBeforeDispatch(string kind)
    {
        using var directory = new TempAuditDirectory();
        Directory.CreateDirectory(directory.Path);
        using var log = new FakeWorkerRequestLog(directory.Path);
        await using var harness = await McpProtocolTestHarness.StartAsync<LibraryReadTools>(
            startupProjectPath: Scenario, accessMode: McpAccessMode.ReadOnly);
        var before = log.Methods().Length;
        var item = new Dictionary<string, object?> { ["operationId"] = "g", ["operation"] = "list_global_libraries" };
        var arguments = new Dictionary<string, object?> { ["operations"] = new[] { item } };
        if (kind == "itemUnknown") item["unknown"] = "x";
        else arguments["Operations"] = new[] { item };

        var result = await harness.Client.CallToolAsync("library_read", arguments);

        ArgumentRejectionAssert.IsCanonicalValidationError(result, "library_read");
        Assert.DoesNotContain(log.Methods().Skip(before), method => method.StartsWith("library_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CursorWithOffset_ValidationError()
    {
        using var client = await FakeWorkerClients.CreateBoundAsync(Scenario, McpAccessMode.ReadOnly);
        var item = GlobalList(cursor: "x");
        item.Offset = 1;

        var result = await LibraryReadTools.LibraryRead(client, NewCodec(), new[] { item });

        Assert.True(result.IsError);
        var error = CanonicalToolDocument.Read(result).GetProperty("error");
        Assert.Equal("validation_error", error.GetProperty("category").GetString());
        Assert.Contains("cursor is not valid with offset", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Cursor_RoundTripAcrossPages_NoSnapshotIdInResponse()
    {
        using var client = await FakeWorkerClients.CreateBoundAsync(Scenario, McpAccessMode.ReadOnly);
        var codec = NewCodec();

        var first = await LibraryReadTools.LibraryRead(client, codec, new[] { GlobalList(limit: 1) });
        var firstPage = Items(first)[0].GetProperty("result");
        var cursor = firstPage.GetProperty("nextCursor").GetString();
        Assert.False(string.IsNullOrEmpty(cursor));
        Assert.Equal("Fixture-1", firstPage.GetProperty("items")[0].GetProperty("identity").GetProperty("name").GetString());
        Assert.Equal(1, firstPage.GetProperty("nextOffset").GetInt32());
        Assert.False(firstPage.GetProperty("complete").GetBoolean());

        var second = await LibraryReadTools.LibraryRead(client, codec, new[] { GlobalList(limit: 1, cursor: cursor) });
        var secondPage = Items(second)[0].GetProperty("result");

        Assert.Equal("Fixture-2", secondPage.GetProperty("items")[0].GetProperty("identity").GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, secondPage.GetProperty("nextCursor").ValueKind);
        Assert.True(secondPage.GetProperty("complete").GetBoolean());
        foreach (var text in new[] { CanonicalToolDocument.Read(first).GetRawText(), CanonicalToolDocument.Read(second).GetRawText() })
        {
            Assert.DoesNotContain("snapshotId", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("library-s1", text);
        }
    }

    [Fact]
    public async Task CursorFromOtherSelectors_FailsItemWithoutWorkerCall()
    {
        using var client = await FakeWorkerClients.CreateBoundAsync(Scenario, McpAccessMode.ReadOnly);
        var codec = NewCodec();
        var first = await LibraryReadTools.LibraryRead(client, codec, new[] { GlobalList(limit: 1) });
        var cursor = Items(first)[0].GetProperty("result").GetProperty("nextCursor").GetString();
        var other = new LibraryReadItem
        {
            OperationId = "c", Operation = "list_children", Scope = "global", LibraryName = "Fixture",
            FolderKind = "types", Cursor = cursor,
        };

        var result = await LibraryReadTools.LibraryRead(client, codec, new[] { other });

        Assert.Equal("cursor_filter_mismatch", Items(result)[0].GetProperty("failure").GetProperty("category").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task Count0And51_Rejected(int count)
    {
        using var client = await FakeWorkerClients.CreateBoundAsync(Scenario, McpAccessMode.ReadOnly);
        var operations = Enumerable.Range(0, count).Select(i => GlobalList($"g{i}")).ToArray();

        var result = await LibraryReadTools.LibraryRead(client, NewCodec(), operations);

        Assert.True(result.IsError);
        Assert.Equal("validation_error", CanonicalToolDocument.Read(result).GetProperty("error").GetProperty("category").GetString());
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("overlong")]
    public async Task DuplicateOrOverlongId_Rejected(string kind)
    {
        using var client = await FakeWorkerClients.CreateBoundAsync(Scenario, McpAccessMode.ReadOnly);
        var operations = kind == "duplicate"
            ? new[] { GlobalList("same"), GlobalList("same") }
            : new[] { GlobalList(new string('x', 257)) };

        var result = await LibraryReadTools.LibraryRead(client, NewCodec(), operations);

        Assert.True(result.IsError);
        Assert.Equal("validation_error", CanonicalToolDocument.Read(result).GetProperty("error").GetProperty("category").GetString());
    }

    [Theory]
    [InlineData(McpAccessMode.ReadOnly)]
    [InlineData(McpAccessMode.ReadWrite)]
    [InlineData(McpAccessMode.Full)]
    public async Task ReadOnlyMode_Available_NoMutationMethodSent(McpAccessMode mode)
    {
        using var directory = new TempAuditDirectory();
        Directory.CreateDirectory(directory.Path);
        using var log = new FakeWorkerRequestLog(directory.Path);
        using var client = await FakeWorkerClients.CreateBoundAsync(Scenario, mode);
        var operations = new[]
        {
            GlobalList("list"),
            GlobalInspect("inspect", "Fixture"),
            new LibraryReadItem { OperationId = "children", Operation = "list_children", Scope = "global", LibraryName = "Fixture", FolderKind = "types" },
            new LibraryReadItem { OperationId = "project", Operation = "inspect_library", Scope = "project" },
        };

        var result = await LibraryReadTools.LibraryRead(client, NewCodec(), operations);

        Assert.False(result.IsError);
        Assert.All(Items(result).EnumerateArray(), item => Assert.Equal("succeeded", item.GetProperty("status").GetString()));
        var sent = log.Methods().Where(method => method.StartsWith("library_", StringComparison.Ordinal)).ToArray();
        Assert.Equal(operations.Length, sent.Length);
        Assert.All(sent, method => Assert.Contains(method, LibraryReadMethods.All));
        Assert.DoesNotContain(log.Methods(), method => method.Contains("write", StringComparison.Ordinal)
            || method is "save_project" or "close_project" or "open_project");
    }

    [Fact]
    public async Task TextEqualsStructuredContent()
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<LibraryReadTools>(
            startupProjectPath: Scenario, accessMode: McpAccessMode.ReadOnly);

        var result = await CallAsync(harness,
            new { operationId = "g", operation = "list_global_libraries", limit = 1 },
            new { operationId = "i", operation = "inspect_library", scope = "project" });

        Assert.False(result.IsError);
        Assert.Empty(StructuredContractInspector.FindViolations(result));
        var root = result.StructuredContent!.Value;
        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        Assert.Equal("library_read", root.GetProperty("tool").GetString());
        Assert.Equal("1.0", root.GetProperty("contractVersion").GetString());
        Assert.Equal(root.GetRawText(), Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    [Fact]
    public void ItemFieldDescriptions_NameTheirOperations()
    {
        var shared = new[] { nameof(LibraryReadItem.OperationId), nameof(LibraryReadItem.Operation) };
        foreach (var property in typeof(LibraryReadItem).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(property => !shared.Contains(property.Name)))
        {
            var description = property.GetCustomAttribute<DescriptionAttribute>()?.Description;
            Assert.False(string.IsNullOrWhiteSpace(description), property.Name);
            Assert.True(LibraryReadMethods.Operations.Any(operation => description!.Contains(operation, StringComparison.Ordinal)),
                $"{property.Name} description names no operation: {description}");
        }
    }
}
