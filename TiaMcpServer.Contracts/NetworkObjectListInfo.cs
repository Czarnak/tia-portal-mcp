using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

/// <summary>
/// Paged result returned by a <c>list_network_objects</c> operation.
/// </summary>
public class NetworkObjectListInfo
{
    /// <summary>Network objects in this page.</summary>
    public List<NetworkObjectSummaryInfo> Items { get; set; } = new List<NetworkObjectSummaryInfo>();

    /// <summary>
    /// Total number of matching objects across all pages. May be absent when the underlying
    /// enumeration does not support a total count without full traversal.
    /// </summary>
    public int? TotalCount { get; set; }

    /// <summary>Number of objects returned in this page. Must equal <see cref="Items"/> count.</summary>
    public int ReturnedCount { get; set; }

    /// <summary>
    /// Opaque cursor value to supply in the next <c>list_network_objects</c> call to retrieve
    /// the following page. Null when this is the last (or only) page.
    /// </summary>
    public string? NextCursor { get; set; }

    /// <summary>Non-fatal notes captured while listing (e.g. objects that could not be read).</summary>
    public List<string> Messages { get; set; } = new List<string>();
}
