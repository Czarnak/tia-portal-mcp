using System.ComponentModel;
using ModelContextProtocol.Server;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tools
{
    [McpServerToolType]
    public static class ListTagTablesTool
    {
        [McpServerTool(Name = "list_tag_tables")]
        [Description("DEPRECATED (prefer execute_read_batch): Retrieve all PLC tag tables with their tags and user constants. Returns a JSON array of tag tables.")]
        public static async Task<string> ListTagTables(
            OpennessWorkerClient workerClient,
            [Description("Optional PLC device name to filter. If omitted, uses the first PLC found.")] string? plcName = null,
            [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null)
        {
            return await workerClient.ListTagTablesAsync(plcName, projectPath).ConfigureAwait(false);
        }
    }
}
