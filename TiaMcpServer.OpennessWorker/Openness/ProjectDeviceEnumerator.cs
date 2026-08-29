using System.Collections.Generic;
using Siemens.Engineering;
using Siemens.Engineering.HW;

namespace TiaMcpServer.OpennessWorker.Openness;

internal static class ProjectDeviceEnumerator
{
    public static IEnumerable<Device> Enumerate(Project project)
    {
        foreach (LocatedProjectDevice locatedDevice in EnumerateWithLocations(project))
        {
            yield return locatedDevice.Device;
        }
    }

    internal static IReadOnlyList<LocatedProjectDevice> EnumerateWithLocations(Project project)
    {
        var devices = new List<LocatedProjectDevice>();
        var sourceOrder = 0;
        var deviceIndex = 0;
        foreach (Device device in project.Devices)
        {
            devices.Add(new LocatedProjectDevice(device, $"devices/{deviceIndex}", sourceOrder));
            deviceIndex++;
            sourceOrder++;
        }

        var groupIndex = 0;
        foreach (DeviceUserGroup group in project.DeviceGroups)
        {
            Enumerate(group, $"deviceGroups/{groupIndex}", devices, ref sourceOrder);
            groupIndex++;
        }

        return devices;
    }

    private static void Enumerate(
        DeviceUserGroup group,
        string groupLocator,
        ICollection<LocatedProjectDevice> devices,
        ref int sourceOrder)
    {
        var deviceIndex = 0;
        foreach (Device device in group.Devices)
        {
            devices.Add(new LocatedProjectDevice(
                device,
                $"{groupLocator}/devices/{deviceIndex}",
                sourceOrder));
            deviceIndex++;
            sourceOrder++;
        }

        var childGroupIndex = 0;
        foreach (DeviceUserGroup childGroup in group.Groups)
        {
            Enumerate(childGroup, $"{groupLocator}/groups/{childGroupIndex}", devices, ref sourceOrder);
            childGroupIndex++;
        }
    }
}

internal sealed class LocatedProjectDevice
{
    public LocatedProjectDevice(Device device, string structuralLocator, int sourceOrder)
    {
        Device = device;
        StructuralLocator = structuralLocator;
        SourceOrder = sourceOrder;
    }

    public Device Device { get; }

    public string StructuralLocator { get; }

    public int SourceOrder { get; }
}
