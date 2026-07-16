using System;
using System.Collections.Generic;
using System.Linq;

namespace TiaMcpServer.Contracts;

/// <summary>
/// Pure post-processing for browse_project_tree: subtree selection via startPath and
/// depth limiting. Lives in Contracts so the net48 worker applies it after walking the
/// full Openness tree while the net8 test suite covers the logic without Siemens DLLs.
/// Never mutates the input tree.
/// </summary>
public static class ProjectTreeFilter
{
    public static List<ProjectTreeNode> Apply(List<ProjectTreeNode> roots, string? startPath, int? depth)
    {
        var hasStartPath = !string.IsNullOrWhiteSpace(startPath);
        if (!hasStartPath && depth is null)
        {
            return roots;
        }

        var selected = hasStartPath
            ? new List<ProjectTreeNode> { FindByPath(roots, startPath!.Trim()) }
            : roots;

        if (depth is null)
        {
            return selected.Select(Clone).ToList();
        }

        if (depth.Value < 1)
        {
            throw new InvalidOperationException("depth must be 1 or greater; 1 returns only the selected root nodes.");
        }

        return selected.Select(node => Prune(node, depth.Value)).ToList();
    }

    private static ProjectTreeNode FindByPath(List<ProjectTreeNode> roots, string startPath)
    {
        var stack = new Stack<ProjectTreeNode>(roots);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node.Details != null &&
                node.Details.TryGetValue("Path", out var path) &&
                string.Equals(path, startPath, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    stack.Push(child);
                }
            }
        }

        throw new InvalidOperationException(
            $"startPath '{startPath}' does not match any node's Path in the project tree. "
            + "Call browse_project_tree without startPath (optionally with a small depth) to discover valid paths.");
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
