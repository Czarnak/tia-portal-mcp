using System.ComponentModel;
using ModelContextProtocol.Server;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tools;

/// <summary>
/// Read-only project tools. Exposed in both read-only and read-write modes.
/// </summary>
[McpServerToolType]
public class ProjectReadTools
{
    [McpServerTool(Name = "get_project_status", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Get status and metadata for the active TIA Portal project.")]
    public static async Task<string> GetProjectStatus(
        OpennessWorkerClient workerClient,
        [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null)
        => (await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false)).ToEnvelopeText();
}
