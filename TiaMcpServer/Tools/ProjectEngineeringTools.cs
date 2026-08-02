using System.ComponentModel;
using ModelContextProtocol.Server;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tools;

/// <summary>Project engineering actions exposed only in read-write mode.</summary>
[McpServerToolType]
public class ProjectEngineeringTools
{
    [McpServerTool(Name = "compile_check", ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Compile a PLC or selected block scope and return compiler messages. Available only in read-write mode.")]
    public static async Task<string> CompileCheck(
        OpennessWorkerClient workerClient,
        [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null,
        [Description("Optional PLC software name to compile.")] string? plcName = null,
        [Description("Optional PLC block path to compile only that block.")] string? blockPath = null)
    {
        var result = await workerClient
            .CompileCheckAsync(blockPath, plcName, projectPath)
            .ConfigureAwait(false);
        return StandaloneToolResultFormatter.Format(
            result,
            "Narrow the compile with plcName or blockPath.");
    }
}