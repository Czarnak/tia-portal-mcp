using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Contracts.Network;

/// <summary>A unique owner candidate or the existing selection failure category.</summary>
public sealed class NetworkInterfacePathMatch<TItem> where TItem : class
{
    private NetworkInterfacePathMatch(TItem? item, string? category, string? error)
    { Item = item; FailureCategory = category; Error = error; }
    public bool Success => Item is not null;
    public TItem? Item { get; }
    public string? FailureCategory { get; }
    public string? Error { get; }
    public static NetworkInterfacePathMatch<TItem> Ok(TItem item) => new(item, null, null);
    public static NetworkInterfacePathMatch<TItem> Fail(string category, string error) => new(null, category, error);
}

/// <summary>Resolves each owner level by ordinal name/position, then checks optional type evidence.</summary>
public static class NetworkInterfacePathMatcher
{
    public static NetworkInterfacePathMatch<TItem> Match<TItem>(IEnumerable<TItem> roots,
        IReadOnlyList<NetworkInterfacePathSegmentInfo> path, Func<TItem, IEnumerable<TItem>> children,
        Func<TItem, string?> name, Func<TItem, int?> position, Func<TItem, string?> type) where TItem : class
    {
        if (path is null || path.Count == 0)
            return NetworkInterfacePathMatch<TItem>.Fail(WorkerFailureCategories.TargetNotFound, "The interface owner path is empty.");
        var siblings = roots;
        TItem? selected = null;
        for (var depth = 0; depth < path.Count; depth++)
        {
            var segment = path[depth];
            if (segment is null || string.IsNullOrWhiteSpace(segment.Name) || segment.PositionNumber < 0
                || (segment.TypeIdentifier is not null && string.IsNullOrWhiteSpace(segment.TypeIdentifier)))
                return NetworkInterfacePathMatch<TItem>.Fail(WorkerFailureCategories.TargetEvidenceMismatch, "Interface owner path evidence is invalid.");
            try
            {
                var matches = new List<TItem>();
                foreach (var sibling in siblings)
                {
                    var siblingName = name(sibling);
                    var siblingPosition = position(sibling);
                    if (string.IsNullOrWhiteSpace(siblingName) || siblingPosition is null || siblingPosition < 0)
                        return NetworkInterfacePathMatch<TItem>.Fail(WorkerFailureCategories.TargetEvidenceMismatch,
                            $"Interface owner path segment {depth} has unreadable sibling name or position evidence.");
                    if (string.Equals(siblingName, segment.Name, StringComparison.Ordinal)
                        && siblingPosition == segment.PositionNumber) matches.Add(sibling);
                }
                if (matches.Count == 0)
                    return NetworkInterfacePathMatch<TItem>.Fail(WorkerFailureCategories.TargetNotFound, $"Interface owner path segment {depth} was not found.");
                if (matches.Count != 1)
                    return NetworkInterfacePathMatch<TItem>.Fail(WorkerFailureCategories.TargetAmbiguous, $"Interface owner path segment {depth} matches multiple siblings.");
                selected = matches[0];
                if (segment.TypeIdentifier is not null && !string.Equals(type(selected), segment.TypeIdentifier, StringComparison.Ordinal))
                    return NetworkInterfacePathMatch<TItem>.Fail(WorkerFailureCategories.TargetEvidenceMismatch, $"Interface owner path segment {depth} type evidence does not match.");
                if (depth + 1 < path.Count) siblings = children(selected);
            }
            catch (Exception)
            {
                return NetworkInterfacePathMatch<TItem>.Fail(WorkerFailureCategories.TargetEvidenceMismatch,
                    $"Interface owner path segment {depth} could not be read completely.");
            }
        }
        return NetworkInterfacePathMatch<TItem>.Ok(selected!);
    }
}
