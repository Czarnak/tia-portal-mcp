using System.Reflection;
using ModelContextProtocol;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Tools;

[Collection("Mcp protocol serial")]
public class AccessModeDiscoveryTests
{
    private static readonly string[] Reads = { "bind_project", "browse_project_tree", "get_project_status", "hmi_read", "network_read", "plc_read", "read_cross_references" };
    private static readonly string[] Edits = { "compile_check", "network_write", "plc_write" };
    private static readonly string[] Lifecycle = { "archive_project", "close_project", "create_project", "open_project", "save_project", "save_project_as" };

    [Theory]
    [InlineData(McpAccessMode.ReadOnly, 7)]
    [InlineData(McpAccessMode.ReadWrite, 16)]
    [InlineData(McpAccessMode.Full, 16)]
    public async Task ToolsList_AdvertisesOnlyTheModeSurface(McpAccessMode mode, int count)
    {
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(mode);
        var actual = (await harness.Client.ListToolsAsync()).Select(tool => tool.Name).Order().ToArray();
        var expected = mode switch
        {
            McpAccessMode.ReadOnly => Reads,
            McpAccessMode.ReadWrite => Reads.Concat(Edits).Concat(Lifecycle),
            McpAccessMode.Full => Reads.Concat(Edits).Concat(Lifecycle),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        Assert.Equal(expected.Order(), actual);
        Assert.Equal(count, actual.Length);
    }

    [Theory]
    [InlineData(McpAccessMode.ReadOnly)]
    [InlineData(McpAccessMode.ReadWrite)]
    [InlineData(McpAccessMode.Full)]
    public async Task BindingInspection_IsObservationInEveryModeWithoutElicitation(McpAccessMode mode)
    {
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, null));
        using var ui = new FakeWorkerUiOpenProject(null);
        using var directory = new TempAuditDirectory();
        Directory.CreateDirectory(directory.Path);
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(mode, directory.Path);
        var result = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?>
            { ["action"] = "list_server_connections", ["portalProcessId"] = 42 });
        Assert.True(result.StructuredContent!.Value.GetProperty("success").GetBoolean());
        Assert.Equal(OperationCapability.Observe, OperationPolicyCatalog.GetCapability("list_server_connections"));
        Assert.Equal(OperationCapability.SessionSelection, OperationPolicyCatalog.GetCapability("select_portal_project"));
        Assert.Empty(Directory.GetFiles(directory.Path, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(McpAccessMode.ReadOnly)]
    public async Task ToolsCall_CannotReachHiddenLifecycleTools(McpAccessMode mode)
    {
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(mode);
        foreach (var name in Lifecycle)
        {
            var arguments = name switch
            {
                "open_project" => new Dictionary<string, object?> { ["projectPath"] = @"C:\Fixture\Line.ap21" },
                "create_project" => new Dictionary<string, object?> { ["projectDirectory"] = @"C:\Fixture", ["projectName"] = "Line" },
                "save_project_as" => new Dictionary<string, object?> { ["targetDirectory"] = @"C:\Fixture", ["targetName"] = "Copy" },
                "archive_project" => new Dictionary<string, object?> { ["archiveDirectory"] = @"C:\Fixture", ["archiveName"] = "Archive" },
                _ => new Dictionary<string, object?>()
            };
            var exception = await Assert.ThrowsAsync<McpProtocolException>(() => harness.Client.CallToolAsync(name, arguments).AsTask());
            Assert.Equal(McpErrorCode.InvalidParams, exception.ErrorCode);
            Assert.Contains(name, exception.Message);
            Assert.Null(typeof(OpennessWorkerClient).GetField("_transport", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(harness.WorkerClient));
        }
    }
}
