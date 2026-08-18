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
        var bindingGate = await workerClient.RequireVerifiedWriteBindingAsync(projectPath).ConfigureAwait(false);
        if (!bindingGate.Success)
        {
            return StandaloneToolResultFormatter.Format(bindingGate, string.Empty);
        }

        var execution = await workerClient.ExecuteWithPinnedBindingAsync(
            workerClient.BindingSnapshot,
            () => workerClient.CompileCheckAsync(blockPath, plcName, projectPath)).ConfigureAwait(false);
        var result = execution.Success ? execution.Value! : execution.Failure!;
        return StandaloneToolResultFormatter.Format(
            result,
            "Narrow the compile with plcName or blockPath.");
    }
}
