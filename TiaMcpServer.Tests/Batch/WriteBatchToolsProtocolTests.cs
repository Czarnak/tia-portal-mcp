using ModelContextProtocol.Protocol;
using TiaMcpServer.Batch;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

[Collection("Mcp protocol serial")]
public sealed class WriteBatchToolsProtocolTests
{
    [Fact]
    public async Task WriteBatchTools_AdvertisePreviewAndApplyOverToolsList()
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<WriteBatchTools>();

        var names = (await harness.Client.ListToolsAsync())
            .Select(tool => tool.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(new[] { "apply_write_batch", "preview_write_batch" }, names);
    }

    [Fact]
    public async Task PreviewWriteBatch_ProtocolCall_RejectsReadOperationsThroughTheRegisteredSurface()
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<WriteBatchTools>();

        var result = await harness.Client.CallToolAsync(
            "preview_write_batch",
            new Dictionary<string, object?>
            {
                ["operations"] = new object[]
                {
                    new
                    {
                        operationId = "bad-read",
                        operation = "get_block_content",
                        blockPath = "PLC_1/Blocks/Main"
                    }
                }
            });

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Contains("\"success\":false", text, StringComparison.Ordinal);
        Assert.Contains("get_block_content", text, StringComparison.Ordinal);
    }
}
