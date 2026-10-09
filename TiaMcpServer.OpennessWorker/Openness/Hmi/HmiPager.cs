using TiaMcpServer.Contracts.Hmi;

namespace TiaMcpServer.OpennessWorker.Openness.Hmi;

public static class HmiPager
{
    /// <summary>Case-insensitive ordinal order with a case-sensitive ordinal tie-break, so the order is total and stable.</summary>
    public static IOrderedEnumerable<T> InNameOrder<T>(IEnumerable<T> source, Func<T, string> name)
        => source.OrderBy(name, StringComparer.OrdinalIgnoreCase).ThenBy(name, StringComparer.Ordinal);

    /// <summary>One page of <paramref name="source"/> in name order; an offset past the total is an empty page.</summary>
    public static (IReadOnlyList<T> Items, HmiPage Page) Page<T>(
        IEnumerable<T> source, Func<T, string> name, int offset, int limit)
    {
        var ordered = InNameOrder(source, name).ToList();
        var items = ordered.Skip(offset).Take(limit).ToList();
        var end = (long)offset + limit; // long: offset is not bounded above, so int addition could wrap negative
        var next = end < ordered.Count ? (int)end : (int?)null;
        return (items, new HmiPage(offset, limit, ordered.Count, next));
    }
}
