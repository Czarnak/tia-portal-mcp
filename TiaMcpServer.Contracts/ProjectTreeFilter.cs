using System;
using System.Collections.Generic;
using System.Linq;

namespace TiaMcpServer.Contracts;

/// <summary>
/// Pure post-processing for browse_project_tree: typed subtree selection and depth limiting.
/// Lives in Contracts so the net48 worker applies it after walking a full
/// Openness tree while the net8 test suite covers the logic without Siemens DLLs. Never
/// mutates the input tree.
/// </summary>
public static class ProjectTreeFilter
{
    public static ProjectTreeSelectionResult Apply(
        List<ProjectTreeNode> roots,
        IReadOnlyList<ProjectTreeSelectorSegment>? startSelector,
        int? depth)
    {
        ProjectTreeNodeTypes.Validate(startSelector);

        IReadOnlyList<ProjectTreeSelectorSegment>? canonicalSelector = null;
        IEnumerable<ProjectTreeNode> selected = roots;
        if (startSelector is not null)
        {
            var canonical = new List<ProjectTreeSelectorSegment>(startSelector.Count);
            var current = ResolveOne(roots, startSelector[0], canonical);
            for (var index = 1; index < startSelector.Count; index++)
            {
                current = ResolveOne(
                    current.Children ?? Enumerable.Empty<ProjectTreeNode>(),
                    startSelector[index],
                    canonical);
            }

            canonicalSelector = canonical;
            selected = new[] { current };
        }

        var resultRoots = depth is null
            ? selected.Select(Clone).ToList()
            : ApplyDepth(selected, depth.Value);
        return new ProjectTreeSelectionResult(resultRoots, canonicalSelector);
    }

    private static ProjectTreeNode ResolveOne(
        IEnumerable<ProjectTreeNode> candidates,
        ProjectTreeSelectorSegment requested,
        List<ProjectTreeSelectorSegment> canonical)
    {
        var matches = candidates.Where(node =>
            string.Equals(node.NodeType, requested.NodeType, StringComparison.Ordinal)
            && string.Equals(node.Name, requested.Name, StringComparison.OrdinalIgnoreCase)).ToList();

        if (matches.Count == 0)
        {
            throw new ProjectTreeSelectionException(
                WorkerFailureCategories.TargetNotFound,
                "The typed project-tree selector did not resolve to a target.");
        }

        if (matches.Count != 1)
        {
            throw new ProjectTreeSelectionException(
                WorkerFailureCategories.TargetAmbiguous,
                "The typed project-tree selector resolved to multiple targets.");
        }

        canonical.Add(new ProjectTreeSelectorSegment
        {
            NodeType = matches[0].NodeType,
            Name = matches[0].Name
        });
        return matches[0];
    }

    private static List<ProjectTreeNode> ApplyDepth(IEnumerable<ProjectTreeNode> selected, int depth)
    {
        if (depth < 1)
        {
            throw new InvalidOperationException("depth must be 1 or greater; 1 returns only the selected root nodes.");
        }

        return selected.Select(node => Prune(node, depth)).ToList();
    }

    private static ProjectTreeNode Prune(ProjectTreeNode node, int remainingDepth)
    {
        var children = node.Children;
        if (children == null || children.Count == 0)
        {
            return Clone(node);
        }

        if (remainingDepth <= 1)
        {
            var details = CopyDetails(node.Details);
            details["ChildrenOmitted"] = children.Count.ToString();

            return new ProjectTreeNode
            {
                Name = node.Name,
                NodeType = node.NodeType,
                Details = details,
                Children = new List<ProjectTreeNode>()
            };
        }

        return new ProjectTreeNode
        {
            Name = node.Name,
            NodeType = node.NodeType,
            Details = CopyDetailsOrNull(node.Details),
            Children = children.Select(child => Prune(child, remainingDepth - 1)).ToList()
        };
    }

    private static ProjectTreeNode Clone(ProjectTreeNode node)
    {
        return new ProjectTreeNode
        {
            Name = node.Name,
            NodeType = node.NodeType,
            Details = CopyDetailsOrNull(node.Details),
            Children = node.Children == null
                ? null
                : node.Children.Select(Clone).ToList()
        };
    }

    private static Dictionary<string, string> CopyDetails(Dictionary<string, string>? details)
    {
        return details == null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(details);
    }

    private static Dictionary<string, string>? CopyDetailsOrNull(Dictionary<string, string>? details)
    {
        return details == null ? null : new Dictionary<string, string>(details);
    }
}
