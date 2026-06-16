using System.ComponentModel;
using ModelContextProtocol.Server;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tools
{
    [McpServerToolType]
    public static class BrowseProjectTreeTool
    {
        [McpServerTool(Name = "browse_project_tree")]
        [Description("DEPRECATED (prefer execute_read_batch): Recursively enumerate the TIA Portal project tree: devices, PLC software, block folders, blocks, tag tables, and types. Returns a structured JSON tree.")]
        public static async Task<string> BrowseProjectTree(
            OpennessWorkerClient workerClient,
            [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null)
        {
            return await workerClient.BrowseProjectTreeAsync(projectPath).ConfigureAwait(false);
        }
    }
}
