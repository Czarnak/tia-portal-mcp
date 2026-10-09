using Siemens.Engineering;
using Siemens.Engineering.HW;
using TiaMcpServer.Contracts.Network;

namespace TiaMcpServer.OpennessWorker.Openness.Network;

public static class NetworkDeviceCreator
{
    public static AddDeviceResultInfo Create(
        ProjectBase project,
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
            device = project.Devices.CreateWithItem(typeIdentifier, deviceItemName, deviceName);
        }
        catch (Exception ex) when (ex is not EngineeringException)
        {
            throw new InvalidOperationException(
                $"Failed to create network device '{deviceName}' from type identifier '{typeIdentifier}': {ex.Message}",
                ex);
        }

        result.DeviceName = ReadString(() => device.Name, result.DeviceName, result.Warnings, "created device name");
        var createdItem = GetCreatedDeviceItem(device, deviceItemName);
        if (createdItem is not null)
        {
            result.RootItemName = ReadString(() => createdItem.Name, result.RootItemName, result.Warnings, "created device item name");
            result.TypeIdentifier = ReadString(() => createdItem.TypeIdentifier, result.TypeIdentifier, result.Warnings, "created device item type identifier");
        }
        else
        {
            result.Warnings.Add($"Created device '{result.DeviceName}' did not expose a root device item.");
        }

        return result;
    }

    private static DeviceItem? GetCreatedDeviceItem(Device device, string itemName)
    {
        try
        {
            return NetworkPostconditionChecks.SelectCreatedItem(device.DeviceItems.Cast<DeviceItem>(), item => item.Name, itemName);
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
            // Openness has no nullability annotations; a null here would be written as an explicit
            // null and rejected by the host reader after the device was already created.
            var value = read();
            if (value is null)
            {
                warnings.Add($"Could not read {description}: Openness returned no value.");
                return fallback;
            }

            return value;
        }
        catch (EngineeringException ex)
        {
            warnings.Add($"Could not read {description}: {ex.Message}");
            return fallback;
        }
    }
}
