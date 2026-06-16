using System.ComponentModel;
using ModelContextProtocol.Server;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tools
{
    [McpServerToolType]
    public static class CompileCheckTool
    {
        [McpServerTool(Name = "compile_check")]
        [Description("DEPRECATED (prefer execute_read_batch): Invoke TIA Portal compile/check on PLC software and return errors and warnings. Without blockPath, compiles full PLC software. With blockPath, compiles only the specified block.")]
        public static async Task<string> CompileCheck(
            OpennessWorkerClient workerClient,
            [Description("Optional block path. If supplied, only that block is compiled. If omitted, full PLC software is compiled.")] string? blockPath = null,
            [Description("Optional PLC device name. If omitted, all PLCs in the project are compiled.")] string? plcName = null,
            [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null)
        {
            return await workerClient.CompileCheckAsync(blockPath, plcName, projectPath).ConfigureAwait(false);
        }
    }
}
