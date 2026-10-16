using Siemens.Engineering;
using Siemens.Engineering.VersionControl;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Vci;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Vci;

using Project = Siemens.Engineering.Project;

public static class VciReader
{
    public static VciWorkspaceListInfo ListWorkspaces(VersionControlInterface service)
        => new VciWorkspaceListInfo
        {
            Workspaces = service.WorkspaceGroup.Workspaces.Select(workspace => new VciWorkspaceInfo
            {
                Name = workspace.Name,
                RootPath = workspace.RootPath?.FullName,
                MappingCount = workspace.MappedObjects.Count
            }).ToList()
        };

    public static VciMappingListInfo ListMappings(VersionControlInterface service, Project project, VciQueryInfo query)
    {
        var offset = query.Offset ?? 0;
        var limit = query.Limit ?? 100;
        if (offset < 0 || limit < 1 || limit > 200)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                "offset must be nonnegative and limit must be between 1 and 200.");
        var workspace = FindWorkspace(service, query.WorkspaceName);
        // Resolve the scope even when empty, but retain excluded unit/system mappings by matching paths, not expanded leaves.
        if (query.Scope is not null) VciScopeResolver.Expand(project, query.Scope);
        var mappings = workspace.MappedObjects.Select(mapping => new { Mapping = mapping, Path = MappingPath(project, mapping) })
            .Where(entry => query.Scope is null || StartsWith(entry.Path, query.Scope)).ToList();

        return new VciMappingListInfo
        {
            Total = mappings.Count,
            Offset = offset,
            Limit = limit,
            Mappings = mappings.Skip(offset).Take(limit).Select(entry => new VciMappingInfo
            {
                FileKey = VciFileKey.FromMapping(workspace.RootPath.FullName, entry.Mapping.DirectoryPath.FullName,
                    entry.Mapping.FileNameWithoutExtension),
                Format = entry.Mapping.FileFormat,
                Path = entry.Path,
                Status = query.WithStatus ? ReadStatus(entry.Mapping) : null
            }).ToList()
        };
    }

    public static VciSupportedFormatsInfo SupportedFormats(VersionControlInterface service, Project project, VciQueryInfo query)
    {
        var workspace = FindWorkspace(service, query.WorkspaceName, useFirst: true);
        var leaves = VciScopeResolver.Expand(project, query.Scope!);
        if (query.Scope is null || !VciScope.IsLeaf(query.Scope[query.Scope.Count - 1].NodeType) || leaves.Count != 1)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                "supported_formats requires a scope naming exactly one leaf.");
        return new VciSupportedFormatsInfo { Formats = workspace.GetSupportedFileFormats(leaves[0].Object).ToList() };
    }

    internal static Workspace FindWorkspace(VersionControlInterface service, string? name, bool useFirst = false)
    {
        if (useFirst && name is null)
            return service.WorkspaceGroup.Workspaces.FirstOrDefault()
                ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                    "No workspace exists. Create a workspace before requesting supported formats.");
        if (string.IsNullOrWhiteSpace(name))
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "workspaceName is required.");
        var matches = service.WorkspaceGroup.Workspaces
            .Where(workspace => string.Equals(workspace.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count != 1)
            throw new WorkerOperationException(matches.Count == 0
                    ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.TargetAmbiguous,
                matches.Count == 0 ? "The selected workspace was not found." : "The selected workspace is ambiguous.");
        return matches[0];
    }

    private static List<ProjectTreeSelectorSegment>? MappingPath(Project project, MappedObject mapping)
    {
        try
        {
            var engineeringObject = mapping.EngineeringObject;
            return engineeringObject is null ? null : VciScopeResolver.PathOf(project, engineeringObject);
        }
        catch (NonRecoverableException) { throw; }
        catch (EngineeringObjectDisposedException) { return null; }
    }

    private static bool StartsWith(IReadOnlyList<ProjectTreeSelectorSegment>? path,
        IReadOnlyList<ProjectTreeSelectorSegment> scope)
        => path is not null && path.Count >= scope.Count && scope.Select((segment, index) =>
            string.Equals(segment.NodeType, path[index].NodeType, StringComparison.OrdinalIgnoreCase)
            && string.Equals(segment.Name, path[index].Name, StringComparison.OrdinalIgnoreCase)).All(matches => matches);

    private static VciStatusInfo ReadStatus(MappedObject mapping)
    {
        var status = mapping.GetStatus();
        return new VciStatusInfo
        {
            CompareState = status.CompareState.ToString(),
            Details = status.IndividualObjectCompareDetails.ToString()
        };
    }
}
