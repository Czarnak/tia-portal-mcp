using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Siemens.Engineering;
using Siemens.Engineering.HW;

namespace TiaMcpServer.OpennessWorker.Openness.Project;

using Project = Siemens.Engineering.Project;

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

        // Decentral stations (ET 200SP, GSD devices, switches) usually live in the system
        // "Ungrouped devices" group, which is neither project.Devices nor a user group.
        var ungroupedIndex = 0;
        foreach (Device device in EnumerateUngroupedDevices(project))
        {
            devices.Add(new LocatedProjectDevice(device, $"ungroupedDevices/{ungroupedIndex}", sourceOrder));
            ungroupedIndex++;
            sourceOrder++;
        }

        return devices;
    }

    private static IEnumerable<Device> EnumerateUngroupedDevices(Project project)
    {
        // Read via reflection: Project.UngroupedDevicesGroup (DeviceSystemGroup) exists in the
        // real V21 API but not in the CI reference stubs under ref/.
        var ungrouped = ReadRequiredProperty(project, "UngroupedDevicesGroup");
        if (ReadRequiredProperty(ungrouped, "Devices") is not IEnumerable ungroupedDevices)
        {
            throw new InvalidOperationException("Cannot complete device discovery: ungrouped Devices is not enumerable.");
        }

        foreach (object item in ungroupedDevices)
        {
            if (item is not Device device)
            {
                throw new InvalidOperationException("Cannot complete device discovery: an ungrouped entry is not a device.");
            }

            yield return device;
        }
    }

    private static object ReadRequiredProperty(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName)
            ?? throw new InvalidOperationException($"Cannot complete device discovery: {propertyName} is unavailable.");
        try
        {
            return property.GetValue(instance)
                ?? throw new InvalidOperationException($"Cannot complete device discovery: {propertyName} is null.");
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            // Preserve the underlying Openness failure for strict readers and worker error handling.
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
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
