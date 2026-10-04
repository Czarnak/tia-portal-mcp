namespace TiaMcpServer.Contracts;

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

public static class NetworkInterfacePathMatcher
{
    public static NetworkInterfacePathMatch<TItem> Match<TItem>(IEnumerable<TItem> roots,
        IReadOnlyList<NetworkInterfacePathSegmentInfo> path, Func<TItem, IEnumerable<TItem>> children,
        Func<TItem, string?> name, Func<TItem, int?> position, Func<TItem, string?> type) where TItem : class
        => NetworkInterfacePathMatch<TItem>.Fail(WorkerFailureCategories.TargetNotFound, "Not implemented.");

}
