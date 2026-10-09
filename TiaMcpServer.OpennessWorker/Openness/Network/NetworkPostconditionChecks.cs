using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Network;

/// <summary>Siemens-free comparison; unreadable state can never establish a postcondition.</summary>
internal static class NetworkPostconditionChecks
{
    /// <summary>A partial discovery cannot prove either uniqueness or absence.</summary>
    public static string? ClassifySelection(int matchCount, bool complete)
        => !complete ? WorkerFailureCategories.WorkerOperationFailed
            : matchCount == 1 ? null : WorkerFailureCategories.PostconditionFailed;

    public static string? ClassifyDependencySelection(int matchCount, bool complete)
        => complete && matchCount == 1 ? null : WorkerFailureCategories.WorkerOperationFailed;

    public static string? IoSystemSkipReason(bool subnetRequested, bool subnetConnected, bool connectorAvailable)
        => subnetRequested && !subnetConnected
            ? "Requested subnet was not connected, so IO system attachment was skipped."
            : !connectorAvailable ? "The network interface does not expose an IO connector." : null;

    /// <summary>
    /// The created item is the unique exact-name match among the device's top-level items.
    /// Children are never searched: an ET200SP head module's sub-item repeats the head's name.
    /// </summary>
    public static T? SelectCreatedItem<T>(IEnumerable<T> topLevelItems, Func<T, string?> name, string itemName) where T : class
    {
        var matches = topLevelItems.Where(item => string.Equals(name(item), itemName, StringComparison.Ordinal)).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    public static NetworkVerificationCheckInfo Compare(string name, string? expected, string? observed, bool readable)
    {
        var status = !readable ? "unverified"
            : string.Equals(expected, observed, StringComparison.Ordinal) ? "passed" : "failed";
        return new NetworkVerificationCheckInfo
        {
            Name = name, Expected = expected, Observed = readable ? observed : null, Status = status,
            Message = status == "passed" ? null : "Immediate post-read " + (readable ? "did not match." : "was unavailable.")
                + " Inspect the project before retrying.",
        };
    }

    public static NetworkMutationVerificationInfo Complete(NetworkMutationVerificationInfo evidence)
    {
        evidence.Status = evidence.Checks.Any(check => check.Status == "failed") ? "failed"
            : evidence.Checks.Any(check => check.Status == "unverified") ? "unverified"
            : evidence.Checks.Count == 0 ? "not_required" : "passed";
        evidence.Message = evidence.Status == "failed" || evidence.Status == "unverified"
            ? "The mutation may have taken effect. Inspect current state before retrying." : null;
        return evidence;
    }
}
