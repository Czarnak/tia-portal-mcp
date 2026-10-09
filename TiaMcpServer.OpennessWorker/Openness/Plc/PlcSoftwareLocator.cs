using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Project;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Plc;

using Project = Siemens.Engineering.Project;

public static class PlcSoftwareLocator
{
    /// <summary>
    /// The write-path resolver: exactly one PLC (root, grouped or ungrouped device) whose software or
    /// device name equals <paramref name="plcName"/> case-insensitively (any PLC when null). Zero matches
    /// throw <c>target_not_found</c>, several <c>target_ambiguous</c>; a device item that cannot be read fails
    /// the call as <c>worker_operation_failed</c>, so it can never hide a second match.
    /// </summary>
    public static DiscoveredPlcSoftware FindUnique(ProjectBase project, string? plcName)
    {
        DiscoveredPlcSoftware? match = null;
        foreach (var device in ProjectDeviceEnumerator.Enumerate(project))
        {
            foreach (var software in EnumerateStrict(device.DeviceItems))
            {
                if (plcName is not null &&
                    !string.Equals(software.Name, plcName, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(device.Name, plcName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (match is not null)
                {
                    throw new WorkerOperationException(
                        WorkerFailureCategories.TargetAmbiguous,
                        $"Several PLC software instances match{Describe(plcName)}. Specify an unambiguous plcName.");
                }

                match = new DiscoveredPlcSoftware(device.Name, software);
            }
        }

        return match ?? throw new WorkerOperationException(
            WorkerFailureCategories.TargetNotFound,
            $"No PLC software{Describe(plcName)} was found in the project.");
    }

    private static string Describe(string? plcName) => plcName is null ? string.Empty : $" named '{plcName}'";

    /// <summary>Fails closed with a categorized error when a device item cannot be read.</summary>
    private static IEnumerable<PlcSoftware> EnumerateStrict(DeviceItemComposition items)
    {
        using var enumerator = FindInDeviceItemsStrict(items).GetEnumerator();
        while (true)
        {
            try
            {
                if (!enumerator.MoveNext()) yield break;
            }
            catch (EngineeringException ex)
            {
                throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                    $"A device item could not be read while locating PLC software: {ex.Message}");
            }

            yield return enumerator.Current;
        }
    }

    private static IEnumerable<PlcSoftware> FindInDeviceItemsStrict(DeviceItemComposition items)
    {
        foreach (DeviceItem item in items)
        {
            if (item.GetService<SoftwareContainer>()?.Software is PlcSoftware software)
            {
                yield return software;
            }

            foreach (var child in FindInDeviceItemsStrict(item.DeviceItems))
            {
                yield return child;
            }
        }
    }

    /// <summary>Enumerates every PLC software in the project (optionally filtered by device name), paired with its owning device name.</summary>
    public static IEnumerable<DiscoveredPlcSoftware> FindAll(ProjectBase project, string? plcName)
    {
        return Discover(project.Devices.Cast<Device>(), plcName);
    }

    /// <summary>
    /// Like <see cref="FindAll"/>, but enumerates devices as <c>browse_project_tree</c> does: root
    /// devices, devices in (nested) device groups and ungrouped devices.
    /// </summary>
    /// <param name="discoveryFailures">Receives one message per device item that could not be read, when not null.</param>
    public static IEnumerable<DiscoveredPlcSoftware> FindEveryPlc(
        ProjectBase project,
        string? plcName,
        ICollection<string>? discoveryFailures = null)
    {
        return Discover(ProjectDeviceEnumerator.Enumerate(project), plcName, discoveryFailures);
    }

    private static IEnumerable<DiscoveredPlcSoftware> Discover(
        IEnumerable<Device> devices,
        string? plcName,
        ICollection<string>? discoveryFailures = null)
    {
        foreach (Device device in devices)
        {
            foreach (var plcSoftware in FindInDeviceItems(device.DeviceItems, discoveryFailures))
            {
                if (plcName is not null &&
                    !string.Equals(plcSoftware.Name, plcName, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(device.Name, plcName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return new DiscoveredPlcSoftware(device.Name, plcSoftware);
            }
        }
    }

    /// <summary>Enumerates every PLC software hosted by a single device.</summary>
    public static IEnumerable<PlcSoftware> FindInDevice(Device device)
    {
        return FindInDeviceItems(device.DeviceItems, null);
    }

    private static IEnumerable<PlcSoftware> FindInDeviceItems(
        DeviceItemComposition items,
        ICollection<string>? discoveryFailures)
    {
        foreach (DeviceItem item in items)
        {
            PlcSoftware? plcSoftware = null;

            try
            {
                var container = item.GetService<SoftwareContainer>();
                plcSoftware = container?.Software as PlcSoftware;
            }
            catch (EngineeringException ex)
            {
                Console.Error.WriteLine($"Skipping a device item while locating PLC software: {ex.Message}");
                discoveryFailures?.Add($"A device item could not be read while locating PLC software: {ex.Message}");
            }

            if (plcSoftware is not null)
            {
                yield return plcSoftware;
            }

            foreach (var child in FindInDeviceItems(item.DeviceItems, discoveryFailures))
            {
                yield return child;
            }
        }
    }

    public sealed class DiscoveredPlcSoftware
    {
        public DiscoveredPlcSoftware(string deviceName, PlcSoftware software)
        {
            DeviceName = deviceName;
            Software = software;
        }

        public string DeviceName { get; }

        public PlcSoftware Software { get; }
    }
}
