using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Contracts.Vci;

public static class VciScope
{
    public const string TechnologyObjectFolder = "TechnologyObjectFolder";
    public const string TechnologyObject = "TechnologyObject";
    public const string HmiSoftware = "HmiSoftware";
    public const string ScriptModule = "ScriptModule";

    public static IReadOnlyCollection<string> NodeTypes { get; } = Array.AsReadOnly(
        ProjectTreeNodeTypes.All
            .Where(type => type != ProjectTreeNodeTypes.SoftwareUnit && type != ProjectTreeNodeTypes.SystemBlockFolder)
            .Concat(new[] { TechnologyObjectFolder, TechnologyObject, HmiSoftware, ScriptModule }).ToArray());

    public static void Validate(IReadOnlyList<ProjectTreeSelectorSegment>? scope)
    {
        if (scope is null)
            return;
        if (scope.Count == 0)
            throw ProjectTreeSelectionException.Invalid("scope must be omitted or contain at least one segment.");

        for (var index = 0; index < scope.Count; index++)
        {
            var segment = scope[index];
            if (segment is null || string.IsNullOrWhiteSpace(segment.Name))
                throw ProjectTreeSelectionException.Invalid("scope contains an invalid segment.");
            if (segment.NodeType == ProjectTreeNodeTypes.SoftwareUnit || segment.NodeType == ProjectTreeNodeTypes.SystemBlockFolder)
                throw new ProjectTreeSelectionException(WorkerFailureCategories.TargetKindUnsupported,
                    "scope cannot include software units or system block folders.");
            if (!NodeTypes.Contains(segment.NodeType))
                throw ProjectTreeSelectionException.Invalid("scope contains an invalid segment.");

            var parentType = index == 0 ? null : scope[index - 1].NodeType;
            if (!CanFollow(parentType, segment.NodeType))
                throw ProjectTreeSelectionException.Invalid("scope contains an impossible parent-child transition.");
        }
    }

    public static bool IsLeaf(string nodeType)
        => ProjectTreeNodeTypes.BlockLeaves.Contains(nodeType)
            || nodeType == ProjectTreeNodeTypes.TagTable || nodeType == ProjectTreeNodeTypes.Type
            || nodeType == TechnologyObject || nodeType == ScriptModule;

    public static string CategoryFor(string folderNodeType) => folderNodeType switch
    {
        ProjectTreeNodeTypes.BlockFolder => "Program blocks",
        ProjectTreeNodeTypes.TagTableFolder => "PLC tags",
        ProjectTreeNodeTypes.TypeFolder => "PLC data types",
        TechnologyObjectFolder => "Technology objects",
        HmiSoftware => "Scripts",
        _ => throw ProjectTreeSelectionException.Invalid("scope contains an unsupported category folder.")
    };

    private static bool CanFollow(string? parentType, string childType) => parentType switch
    {
        null => childType == ProjectTreeNodeTypes.Device,
        ProjectTreeNodeTypes.Device => childType == ProjectTreeNodeTypes.PlcSoftware || childType == HmiSoftware,
        ProjectTreeNodeTypes.PlcSoftware => childType == ProjectTreeNodeTypes.BlockFolder
            || childType == ProjectTreeNodeTypes.TagTableFolder || childType == ProjectTreeNodeTypes.TypeFolder
            || childType == TechnologyObjectFolder,
        ProjectTreeNodeTypes.BlockFolder => childType == ProjectTreeNodeTypes.BlockFolder
            || ProjectTreeNodeTypes.BlockLeaves.Contains(childType),
        ProjectTreeNodeTypes.TagTableFolder => childType == ProjectTreeNodeTypes.TagTableFolder || childType == ProjectTreeNodeTypes.TagTable,
        ProjectTreeNodeTypes.TypeFolder => childType == ProjectTreeNodeTypes.TypeFolder || childType == ProjectTreeNodeTypes.Type,
        TechnologyObjectFolder => childType == TechnologyObjectFolder || childType == TechnologyObject,
        HmiSoftware => childType == ScriptModule,
        _ => false
    };
}
