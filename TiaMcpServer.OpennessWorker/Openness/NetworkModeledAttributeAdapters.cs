using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>Typed V21 property readers keyed by the Siemens-free modeled catalog.</summary>
public static class NetworkModeledAttributeAdapters
{
    public static bool TryCreateReader(
        ResolvedNetworkObject resolved,
        string adapterKey,
        out Func<object?>? reader)
    {
        switch (adapterKey)
        {
            case "deviceItem.Classification" when resolved.Value is DeviceItem item:
                reader = () => item.Classification;
                return true;
            case "deviceItem.IsBuiltIn" when resolved.Value is DeviceItem item:
                reader = () => item.IsBuiltIn;
                return true;
            case "deviceItem.IsPlugged" when resolved.Value is DeviceItem item:
                reader = () => item.IsPlugged;
                return true;
            case "deviceItem.Name" when resolved.Value is DeviceItem item:
                reader = () => item.Name;
                return true;
            case "deviceItem.PositionNumber" when resolved.Value is DeviceItem item:
                reader = () => item.PositionNumber;
                return true;
            case "deviceItem.TypeIdentifier" when resolved.Value is DeviceItem item:
                reader = () => item.TypeIdentifier;
                return true;
            case "networkInterface.InterfaceOperatingMode" when resolved.Value is NetworkInterface networkInterface:
                reader = () => networkInterface.InterfaceOperatingMode;
                return true;
            case "networkInterface.InterfaceType" when resolved.Value is NetworkInterface networkInterface:
                reader = () => networkInterface.InterfaceType;
                return true;
            case "node.Name" when resolved.Value is Node node:
                reader = () => node.Name;
                return true;
            case "node.NodeId" when resolved.Value is Node node:
                reader = () => node.NodeId;
                return true;
            case "node.NodeType" when resolved.Value is Node node:
                reader = () => node.NodeType;
                return true;
            case "subnet.Name" when resolved.Value is Subnet subnet:
                reader = () => subnet.Name;
                return true;
            case "subnet.NetworkType" when resolved.Value is Subnet subnet:
                reader = () => subnet.NetType;
                return true;
            case "subnet.TypeIdentifier" when resolved.Value is Subnet subnet:
                reader = () => subnet.TypeIdentifier;
                return true;
            case "ioSystem.Name" when resolved.Value is IoSystem ioSystem:
                reader = () => ioSystem.Name;
                return true;
            case "ioSystem.Number" when resolved.Value is IoSystem ioSystem:
                reader = () => ioSystem.Number;
                return true;
            default:
                reader = null;
                return false;
        }
    }
}
