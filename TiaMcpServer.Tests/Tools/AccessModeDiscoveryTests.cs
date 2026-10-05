using System.Reflection;
using ModelContextProtocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Tools;

[Collection("Mcp protocol serial")]
public class AccessModeDiscoveryTests
{
    private static readonly string[] Reads = { "bind_project", "browse_project_tree", "execute_read_batch", "get_project_status", "network_read", "plc_read" };
    private static readonly string[] Edits = { "apply_write_batch", "compile_check", "network_write", "preview_write_batch" };
    private static readonly string[] Lifecycle = { "archive_project", "close_project", "create_project", "open_project", "save_project", "save_project_as" };

    [Theory]
    [InlineData(McpAccessMode.ReadOnly, 6)]
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
