using System.Collections.Generic;
using Siemens.Engineering;
using Siemens.Engineering.HW;

namespace TiaMcpServer.OpennessWorker.Openness;

internal static class ProjectDeviceEnumerator
{
    public static IEnumerable<Device> Enumerate(Project project)
    {
        foreach (Device device in project.Devices)
        {
            yield return device;
        }

        foreach (DeviceUserGroup group in project.DeviceGroups)
        {
            foreach (var device in Enumerate(group))
            {
                yield return device;
            }
        }
    }

    private static IEnumerable<Device> Enumerate(DeviceUserGroup group)
    {
        foreach (Device device in group.Devices)
        {
            yield return device;
        }

        foreach (DeviceUserGroup childGroup in group.Groups)
        {
            foreach (var device in Enumerate(childGroup))
            {
                yield return device;
            }
        }
    }
}
