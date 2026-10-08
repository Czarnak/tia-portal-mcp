using Siemens.Engineering;
using Siemens.Engineering.HW;

namespace TiaMcpServer.OpennessWorker.Openness;

internal static class ProjectDeviceNameMatcher
{
    internal static IReadOnlyList<(Device Device, string Name)> FindMatches(
        Project project,
        string? requestedName,
        Action<EngineeringException>? onUnreadableName = null)
    {
        var matches = new List<(Device Device, string Name)>();
        foreach (Device candidate in ProjectDeviceEnumerator.Enumerate(project))
        {
            try
            {
                var candidateName = candidate.Name;
                if (!string.IsNullOrWhiteSpace(candidateName)
                    && !string.IsNullOrWhiteSpace(requestedName)
                    && string.Equals(candidateName, requestedName, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add((candidate, candidateName));
                }
            }
            catch (EngineeringException exception)
            {
                // An unreadable name cannot satisfy the selector.
                onUnreadableName?.Invoke(exception);
            }
        }

        return matches;
    }
}
