namespace TiaMcpServer.OpennessWorker.Openness.Hmi;

/// <summary>
/// Parses <c>HmiConnection.InitialAddress</c> (spike S8): <c>key=value</c> pairs separated by <c>;</c> with a
/// trailing <c>;</c>, no quoting or escaping observed (only S7-1200/1500 connections were available).
/// Empty segments are dropped, each pair splits at its first <c>=</c>, a repeated key keeps its last value.
/// Null, empty or unparsable input (a segment without a key or without <c>=</c>) is an empty map; the caller
/// always keeps the raw string beside it.
/// </summary>
public static class HmiInitialAddressParser
{
    public static IReadOnlyDictionary<string, string> Parse(string? raw)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(raw))
        {
            return map;
        }

        foreach (var segment in raw!.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = segment.IndexOf('=');
            if (separator <= 0)
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            map[segment.Substring(0, separator)] = segment.Substring(separator + 1);
        }

        return map;
    }
}
