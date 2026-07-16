using Siemens.Engineering;
using Siemens.Engineering.HW;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

public static class NetworkDeviceCreator
{
    public static AddDeviceResultInfo Create(
        Project project,
        string typeIdentifier,
        string deviceName,
        string deviceItemName)
    {
        var result = new AddDeviceResultInfo
        {
            DeviceName = deviceName,
            RootItemName = deviceItemName,
            TypeIdentifier = typeIdentifier
        };

        Device device;
        try
        {
            // EngineeringException propagates on purpose: a failed CreateWithItem must surface as
            // WorkerResponse.Success=false (via Program.Execute), never as a success with a warning.
            device = project.Devices.CreateWithItem(typeIdentifier, deviceName, deviceItemName);
        }
        catch (Exception ex) when (ex is not EngineeringException)
        {
            throw new InvalidOperationException(
                $"Failed to create network device '{deviceName}' from type identifier '{typeIdentifier}': {ex.Message}",
                ex);
        }

        result.DeviceName = ReadString(() => device.Name, result.DeviceName, result.Warnings, "created device name");
        result.TypeIdentifier = ReadString(() => device.TypeIdentifier, result.TypeIdentifier, result.Warnings, "created device type identifier");

        var firstItem = GetFirstDeviceItem(device);
        if (firstItem is not null)
        {
            result.RootItemName = ReadString(() => firstItem.Name, result.RootItemName, result.Warnings, "created root device item name");
        }
        else
        {
            result.Warnings.Add($"Created device '{result.DeviceName}' did not expose a root device item.");
        }

        return result;
    }

    private static DeviceItem? GetFirstDeviceItem(Device device)
    {
        try
        {
            foreach (DeviceItem item in device.DeviceItems)
            {
                return item;
            }
        }
        catch (EngineeringException ex)
        {
            Console.Error.WriteLine($"Could not read device items for created device '{device.Name}': {ex.Message}");
        }

        return null;
    }

    private static string ReadString(Func<string> read, string fallback, List<string> warnings, string description)
    {
        try
        {
            return read();
        }
        catch (EngineeringException ex)
        {
            warnings.Add($"Could not read {description}: {ex.Message}");
            return fallback;
        }
    }
}
