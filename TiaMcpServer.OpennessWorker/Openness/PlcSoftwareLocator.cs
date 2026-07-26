using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Units;

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
            foreach (var plcSoftware in FindInDevice(device))
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

    /// <summary>
    /// Returns the PLC software that owns <paramref name="type"/>.
    ///
    /// <para>
    /// The external-source pipeline needs the owning PLC, not just any PLC: GenerateSource and
    /// CreateFromFile both hang off <c>PlcSoftware.ExternalSourceGroup</c>, and a non-deterministic
    /// type path (bare "MyType") carries no PLC qualifier to look one up with. Searching the type
    /// trees is what makes the answer correct in a multi-PLC project instead of "whichever device
    /// came first".
    /// </para>
    /// </summary>
    public static PlcSoftware ForType(Project project, PlcType type)
    {
        if (type is null) throw new ArgumentNullException(nameof(type));

        foreach (var discovered in FindAll(project, null))
        {
            if (OwnsType(discovered.Software, type))
            {
                return discovered.Software;
            }
        }

        throw new InvalidOperationException(
            $"No PLC software in the project owns the PLC data type '{type.Name}'.");
    }

    private static bool OwnsType(PlcSoftware plcSoftware, PlcType type)
    {
        if (GroupContainsType(plcSoftware.TypeGroup, type))
        {
            return true;
        }

        PlcUnitProvider? unitProvider = null;

        try
        {
            unitProvider = plcSoftware.GetService<PlcUnitProvider>();
        }
        catch (EngineeringException ex)
        {
            Console.Error.WriteLine($"Skipping software units for PLC software '{plcSoftware.Name}': {ex.Message}");
        }

        if (unitProvider is null)
        {
            return false;
        }

        foreach (PlcUnit unit in unitProvider.UnitGroup.Units)
        {
            if (GroupContainsType(unit.TypeGroup, type))
            {
                return true;
            }
        }

        return false;
    }

    private static bool GroupContainsType(PlcTypeGroup group, PlcType type)
    {
        // Equals, not reference equality: Openness hands out a fresh wrapper per traversal and
        // implements IEquatable<PlcType> precisely so two wrappers of one object compare equal.
        foreach (PlcType candidate in group.Types)
        {
            if (type.Equals(candidate))
            {
                return true;
            }
        }

        foreach (PlcTypeUserGroup childGroup in group.Groups)
        {
            if (GroupContainsType(childGroup, type))
            {
                return true;
            }
        }

        return false;
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
