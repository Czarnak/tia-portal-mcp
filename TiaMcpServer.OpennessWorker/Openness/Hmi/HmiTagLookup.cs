using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiTags;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Hmi;

/// <summary>Finds one top-level tag by name; shared by <c>hmi_read</c> and the cross-reference resolver.</summary>
internal static class HmiTagLookup
{
    /// <summary>Exact <c>Tags.Find</c> hit first; otherwise a case-insensitive scan that must match exactly one tag.</summary>
    internal static HmiTag FindUnique(HmiSoftware software, string tagName, bool describe = false)
    {
        var exact = HmiReadLog.Guard(() => software.Tags.Find(tagName), $"Tag '{tagName}'", describe);
        if (exact is not null)
        {
            return exact;
        }

        var matches = HmiReadLog.Guard(() => software.Tags.ToList(), "The tags", describe)
            .Where(t => string.Equals(HmiReadLog.Guard(() => t.Name, "A tag name", describe), tagName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 0)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.TargetNotFound, $"Tag '{tagName}' was not found. Use list_tags to see the tags.");
        }

        if (matches.Count > 1)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.TargetAmbiguous, $"Several tags are named '{tagName}'.");
        }

        return matches[0];
    }
}
