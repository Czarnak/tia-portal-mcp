using System.ComponentModel;
using ModelContextProtocol.Server;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tools
{
    [McpServerToolType]
    public static class GetBlockContentTool
    {
        [McpServerTool(Name = "get_block_content")]
        [Description("DEPRECATED (prefer execute_read_batch): Export a PLC block to its SIMATIC SD (YAML) representation. Supports all block languages: SCL, LAD, FBD, GRAPH, STL, and Data Blocks.")]
        public static async Task<string> GetBlockContent(
            OpennessWorkerClient workerClient,
            [Description("Block path: 'BlockName' for first PLC, or 'PLC_1/BlockName' to target a specific PLC. Optional suffix like ' [OB1]' is stripped automatically.")] string blockPath,
            [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null)
        {
            return await workerClient.GetBlockContentAsync(blockPath, projectPath).ConfigureAwait(false);
        }
    }
}
