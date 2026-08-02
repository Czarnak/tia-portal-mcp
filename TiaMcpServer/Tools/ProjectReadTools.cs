using System.ComponentModel;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tools;

/// <summary>Read-only project tools exposed in both access modes.</summary>
[McpServerToolType]
public class ProjectReadTools
{
    [McpServerTool(Name = "get_project_status", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Get status and metadata for the active TIA Portal project.")]
    public static async Task<string> GetProjectStatus(
        OpennessWorkerClient workerClient,
        [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null)
        => (await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false)).ToEnvelopeText();

    [McpServerTool(Name = "browse_project_tree", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Browse the active TIA Portal project hierarchy. Use depth and startPath to bound large projects.")]
    public static async Task<string> BrowseProjectTree(
        OpennessWorkerClient workerClient,
        [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null,
        [Description("Optional maximum tree depth. Must be 1 or greater; 1 returns only top-level nodes.")] int? depth = null,
        [Description("Optional subtree root matching a node Path exactly, case-insensitively, e.g. PLC_1/Blocks.")] string? startPath = null)
    {
        if (depth is < 1)
        {
            return StandaloneToolResultFormatter.Format(
                WorkerCallResult.Fail(
                    WorkerFailureCategories.ValidationError,
                    "'depth' must be 1 or greater."),
                "Use a valid depth or omit it.");
        }

        var result = await workerClient
            .BrowseProjectTreeAsync(projectPath, depth, startPath)
            .ConfigureAwait(false);
        return StandaloneToolResultFormatter.Format(
            result,
            "Narrow the read with a smaller depth or a more specific startPath.");
    }
}