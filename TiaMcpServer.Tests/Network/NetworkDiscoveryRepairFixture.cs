using TiaMcpServer.Contracts;

namespace TiaMcpServer.Tests.Network;

/// <summary>
/// Portable metadata from retained assembled-baseline.json, SHA256
/// E55BD004450BD1B7763B06FD63DD353238CC6FBF0FCC0D55A18EF14B4E7581CE.
/// Historical public pages establish metadata only. Completeness below is a synthetic
/// ordinary-read test input; sorted historical arrays establish no sibling indices.
/// </summary>
internal static class NetworkDiscoveryRepairFixture
{
    public static HardwareConfigInfo Metadata(HardwareDiscoveryEvidenceInfo evidence) => new()
    {
        DiscoveryEvidence = evidence,
        RootDeviceCount = 1,
        Messages = new() { "Could not read device item 'PROFINET interface_1' type identifier: Device item 'PROFINET interface_1' type identifier was null; selector not available.",
            "Could not read device item 'PROFINET interface_2' type identifier: Device item 'PROFINET interface_2' type identifier was null; selector not available.",
            "Could not read device item 'PROFINET interface_1' address: 'Address' is not supported by type 'Siemens.Engineering.HW.DeviceItemImpl'." },
        Devices = new() { new() { Name = "PLC_DP", Items = new() { new() { Name = "PLC_DP", PositionNumber = 1,
            Items = new() { Owner("PROFINET interface_1", 32768, "X1", "192.168.12.2"),
                Owner("PROFINET interface_2", 33024, "X2", "192.168.13.20") } } } } }
    };

    private static DeviceItemInfo Owner(string name, int position, string port, string address) => new()
    {
        Name = name, PositionNumber = position, TypeIdentifier = null,
        SelectorDiagnostics = new() { "Optional owner TypeIdentifier is unavailable." },
        NetworkInterfaces = new() { new() { Name = port, Nodes = new() { new() { NodeId = "E1", Name = port, IpAddress = address } } } }
    };
}
