using System.Security.Cryptography;
using System.Text;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;

namespace TiaMcpServer.ProjectTree;

internal sealed class ProjectTreeRequestException : Exception
{
    internal ProjectTreeRequestException(string category, string message)
        : base(message)
    {
        Category = category;
    }

    internal string Category { get; }
}

internal sealed record ProjectTreeBrowseRequest(
    string? ProjectPath = null,
    IReadOnlyList<ProjectTreeSelectorSegment>? StartSelector = null,
    int? Depth = null,
    int? PageSize = null,
    string? Cursor = null)
{
    internal void Validate()
    {
        _ = ResolvePageSize();
        if (Depth is < 1)
        {
            throw new ProjectTreeRequestException(
                WorkerFailureCategories.ValidationError,
                "depth must be greater than or equal to 1.");
        }

        if (Cursor is not null && string.IsNullOrWhiteSpace(Cursor))
        {
            throw new ProjectTreeRequestException(
                WorkerFailureCategories.InvalidCursor,
                "cursor must not be blank when supplied.");
        }

        try
        {
            ProjectTreeNodeTypes.Validate(StartSelector);
        }
        catch (ProjectTreeSelectionException exception)
        {
            throw new ProjectTreeRequestException(exception.Category, exception.Message);
        }
    }

    internal int ResolvePageSize()
    {
        var value = PageSize ?? ProjectTreeContract.DefaultPageSize;
        if (value is < ProjectTreeContract.MinimumPageSize or > ProjectTreeContract.MaximumPageSize)
        {
            throw new ProjectTreeRequestException(
                WorkerFailureCategories.ValidationError,
                "pageSize must be between 1 and 200.");
        }

        return value;
    }

    internal string? ValidateRepeatedQuery(ProjectTreeQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (ProjectPath is not null
            && !SameProject(ProjectPath, query.ProjectPath))
        {
            return WorkerFailureCategories.CursorFilterMismatch;
        }

        if (StartSelector is not null && !SameSelector(StartSelector, query.StartSelector))
        {
            return WorkerFailureCategories.CursorFilterMismatch;
        }

        return Depth is not null && Depth != query.Depth
            ? WorkerFailureCategories.CursorFilterMismatch
            : null;
    }

    internal static string CreateQueryHash(ProjectTreeQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var bytes = Encoding.UTF8.GetBytes(CanonicalJson.Serialize(query));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static bool SameProject(string? first, string? second)
    {
        var normalizedFirst = ProjectPathNormalization.Canonicalize(first);
        var normalizedSecond = ProjectPathNormalization.Canonicalize(second);
        return normalizedFirst is not null
            && normalizedSecond is not null
            && string.Equals(normalizedFirst, normalizedSecond, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameSelector(
        IReadOnlyList<ProjectTreeSelectorSegment> first,
        IReadOnlyList<ProjectTreeSelectorSegment>? second)
    {
        if (second is null || first.Count != second.Count)
        {
            return false;
        }

        for (var index = 0; index < first.Count; index++)
        {
            var left = first[index];
            var right = second[index];
            if (left is null
                || right is null
                || !string.Equals(left.NodeType, right.NodeType, StringComparison.Ordinal)
                || !string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
