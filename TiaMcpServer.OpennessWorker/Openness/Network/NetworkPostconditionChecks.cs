using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Network;

/// <summary>Siemens-free selection classification and created-item matching.</summary>
internal static class NetworkPostconditionChecks
{
    /// <summary>A partial discovery cannot prove either uniqueness or absence.</summary>
    public static string? ClassifySelection(int matchCount, bool complete)
        => !complete ? WorkerFailureCategories.WorkerOperationFailed
            : matchCount == 1 ? null : WorkerFailureCategories.PostconditionFailed;

    public static string? ClassifyDependencySelection(int matchCount, bool complete)
        => complete && matchCount == 1 ? null : WorkerFailureCategories.WorkerOperationFailed;

    /// <summary>
    /// The created item is the unique exact-name match among the device's top-level items.
    /// Children are never searched: an ET200SP head module's sub-item repeats the head's name.
    /// </summary>
    public static T? SelectCreatedItem<T>(IEnumerable<T> topLevelItems, Func<T, string?> name, string itemName) where T : class
    {
        var matches = topLevelItems.Where(item => string.Equals(name(item), itemName, StringComparison.Ordinal)).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

}
