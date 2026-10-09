using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Block;

namespace TiaMcpServer.Tools;

/// <summary>Project engineering actions exposed in read-write and full modes.</summary>
[McpServerToolType]
public class ProjectEngineeringTools
{
    [McpServerTool(Name = "compile_check", ReadOnly = false, Destructive = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(CompileCheckResponse))]
    [Description("Compile a PLC or selected block scope and return compiler messages. Available in read-write and full modes.")]
    public static async Task<CallToolResult> CompileCheck(
        OpennessWorkerClient workerClient,
        [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null,
        [Description("Optional PLC software name to compile.")] string? plcName = null,
        [Description("Optional PLC block path to compile only that block.")] string? blockPath = null)
    {
        var accessDenial = workerClient.AccessPolicy.Authorize("compile_check");
        if (accessDenial is not null)
            return StructuredStandaloneResult.Create<CompileCheckReport>("compile_check", null,
                accessDenial.Warnings, StandalonePayloadContract.Failure(accessDenial));

        var bindingGate = await workerClient.RequireVerifiedWriteBindingAsync(projectPath).ConfigureAwait(false);
        if (!bindingGate.Success)
        {
            return StructuredStandaloneResult.Create<CompileCheckReport>("compile_check", null,
                bindingGate.Warnings, StandalonePayloadContract.Failure(bindingGate));
        }

        var execution = await workerClient.ExecuteWithPinnedBindingAsync(
            workerClient.BindingSnapshot,
            () => workerClient.CompileCheckAsync(blockPath, plcName, projectPath)).ConfigureAwait(false);
        var result = execution.Success ? execution.Value! : execution.Failure!;
        var rejection = StandalonePayloadContract.Rejection(result);
        return StructuredStandaloneResult.Create("compile_check",
            rejection is null ? StandalonePayloadContract.DecodeCompile(result) : null,
            result.Warnings, rejection);
    }
}
