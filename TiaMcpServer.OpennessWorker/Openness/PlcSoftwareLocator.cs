using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;

namespace TiaMcpServer.OpennessWorker.Openness;

public static class PlcSoftwareLocator
{
    /// <summary>Returns the first PLC software in the project (optionally filtered by device name), or throws if none.</summary>
    public static PlcSoftware Find(Project project, string? plcName)
    {
        foreach (var discovered in FindAll(project, plcName))
        {
            return discovered.Software;
        }

        var detail = plcName is not null
            ? $" named '{plcName}'"
            : string.Empty;

        throw new InvalidOperationException($"No PLC software{detail} was found in the project.");
    }

    /// <summary>Enumerates every PLC software in the project (optionally filtered by device name), paired with its owning device name.</summary>
    public static IEnumerable<DiscoveredPlcSoftware> FindAll(Project project, string? plcName)
    {
        foreach (Device device in project.Devices)
        {
            if (plcName is not null && !string.Equals(device.Name, plcName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var plcSoftware in FindInDevice(device))
            {
                yield return new DiscoveredPlcSoftware(device.Name, plcSoftware);
            }
        }
    }

    /// <summary>Enumerates every PLC software hosted by a single device.</summary>
    public static IEnumerable<PlcSoftware> FindInDevice(Device device)
    {
        return FindInDeviceItems(device.DeviceItems);
    }

    private static IEnumerable<PlcSoftware> FindInDeviceItems(DeviceItemComposition items)
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
            }

            if (plcSoftware is not null)
            {
                yield return plcSoftware;
            }

            foreach (var child in FindInDeviceItems(item.DeviceItems))
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
