using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts;
using TiaMcpServer.ProjectTree;
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
    {
        var result = await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false);
        return StandaloneToolResultFormatter.Format(
            result,
            "Extended metadata (history, comments, languages) was too large to return in full.");
    }

    [McpServerTool(
        Name = "browse_project_tree",
        ReadOnly = true,
        Destructive = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(BrowseProjectTreeResponse))]
    [Description("Browse a point-in-time TIA project tree through typed, bounded, resumable flat-node pages.")]
    public static async Task<CallToolResult> BrowseProjectTree(
        ProjectTreeBrowseCoordinator coordinator,
        [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal. On continuation, omit it or repeat the same project.")] string? projectPath = null,
        [Description("Optional ordered selector segments { nodeType, name }. Names match case-insensitively, node types exactly, and each segment must identify one direct child. On continuation, omit it or repeat the equivalent selector.")] ProjectTreeSelectorSegment[]? startSelector = null,
        [Description("Optional maximum depth from the selected root. Must be 1 or greater. On continuation, omit it or repeat the same depth.")] int? depth = null,
        [Description("Optional number of flat nodes requested for this page, from 1 through 200; defaults to 100 and may change between continuation pages.")] int? pageSize = null,
        [Description("Opaque cursor for the same point-in-time snapshot. Cursors can be replayed until idle expiry or eviction, but become unavailable after the server process restarts; restart without a cursor to observe again.")] string? cursor = null)
    {
        var rendered = await coordinator.BrowseAsync(
            new ProjectTreeBrowseRequest(projectPath, startSelector, depth, pageSize, cursor)).ConfigureAwait(false);
        return StructuredToolResult.CreateCanonical(rendered.CanonicalText, isError: !rendered.IsSuccess);
    }
}
