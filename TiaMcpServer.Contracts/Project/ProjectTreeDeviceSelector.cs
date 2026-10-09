using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Contracts.Project;

public static class ProjectTreeDeviceSelector
{
    public static TDevice Select<TDevice>(
        IReadOnlyList<TDevice> devices,
        Func<TDevice, string> getName,
        ProjectTreeSelectorSegment firstSegment)
    {
        if (!string.Equals(firstSegment.NodeType, ProjectTreeNodeTypes.Device, StringComparison.Ordinal))
        {
            throw ProjectTreeSelectionException.Invalid("The first startSelector segment must be Device.");
        }

        var matches = devices.Where(device =>
            string.Equals(getName(device), firstSegment.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new ProjectTreeSelectionException(
                WorkerFailureCategories.TargetNotFound,
                "The selected device was not found."),
            _ => throw new ProjectTreeSelectionException(
                WorkerFailureCategories.TargetAmbiguous,
                "The selected device name is ambiguous.")
        };
    }
}
