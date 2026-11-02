using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Network;
using TiaMcpServer.OpennessWorker.Worker;
using static TiaMcpServer.FakeWorker.ScenarioSupport;

namespace TiaMcpServer.FakeWorker.Network;

internal sealed partial class NetworkScenarios
{
    // A complete HardwareConfigInfo: every collection is present, and members that are genuinely
    // unset are explicit nulls rather than omitted, so the payload exercises the strict registry the
    // way a real worker read does.
    public static string HardwareConfigPayload() => WorkerJson.SerializePayload(RoundTripHardwareConfig());

    private static HardwareConfigInfo SingleNodeHardwareConfig(
        string deviceName,
        string itemName,
        string interfaceName,
        string nodeName,
        string nodeId,
        IEnumerable<string>? messages = null) => new()
    {
        DiscoveryEvidence = new() { Scope = "project", Complete = true },
        RootDeviceCount = 1,
        Devices = new List<DeviceInfo>
        {
            new()
            {
                Name = deviceName,
                TypeIdentifier = "OrderNumber:TEST",
                Items = new List<DeviceItemInfo>
                {
                    SelectableDeviceItem(
                        deviceName, 0, itemName, "OrderNumber:TEST", 1, interfaceName,
                        SelectableNode(deviceName, nodeName, nodeId, "Ethernet")),
                },
            },
        },
        Messages = messages?.ToList() ?? new List<string>(),
    };

    private static HardwareConfigInfo AmbiguousNodeHardwareConfig() => new()
    {
        DiscoveryEvidence = new() { Scope = "project", Complete = true },
        Devices = new List<DeviceInfo>
        {
            new()
            {
                Name = "PC_1",
                TypeIdentifier = "OrderNumber:PC-System",
                Items = new List<DeviceItemInfo>
                {
                    SelectableDeviceItem(
                        "PC_1", 0, "IE general_1", "OrderNumber:IE-General", 1, "if_1",
                        SelectableNode("PC_1", "Port A", "dup-node", "Ethernet", "192.168.0.20")),
                    SelectableDeviceItem(
                        "PC_1", 1, "IE general_2", "OrderNumber:IE-General", 2, "if_2",
                        SelectableNode("PC_1", "Port B", "dup-node", "Ethernet", "10.20.30.40")),
                },
            },
        },
    };

    string ConfigureQualifiedFixture(string line, HardwareConfigInfo state, string scenario)
    {
        var request = JsonSerializer.Deserialize<WorkerRequest>(line, WorkerJson.Envelope)!;
        NetworkObjectSelectorInfo target;
        try { target = NetworkConfigurationTargetBinding.Resolve(request); }
        catch (WorkerOperationException ex) { return WorkerJson.SerializePayload(new WorkerResponse { Success = false, FailureCategory = ex.FailureCategory, Error = ex.Message }); }
        var device = state.Devices.Single();
        if (!string.Equals(device.Name, target.DeviceName, StringComparison.OrdinalIgnoreCase))
            return WorkerJson.SerializePayload(new WorkerResponse { Success = false, FailureCategory = WorkerFailureCategories.TargetNotFound, Error = "Device not found." });
        if (target.InterfacePath is null)
            return WorkerJson.SerializePayload(new WorkerResponse { Success = false, FailureCategory = WorkerFailureCategories.TargetAmbiguous, Error = "Use qualified interfacePath." });
        var owner = NetworkInterfacePathMatcher.Match(device.Items, target.InterfacePath, x => x.Items, x => x.Name, x => x.PositionNumber, x => x.TypeIdentifier);
        if (!owner.Success) return WorkerJson.SerializePayload(new WorkerResponse { Success = false, FailureCategory = owner.FailureCategory, Error = owner.Error });
        var networkInterface = owner.Item!.NetworkInterfaces.Single();
        var node = NetworkNodeReadSelectorBuilder.MatchNode(networkInterface.Nodes, target.NodeId, target.NodeIndex, x => x.NodeId);
        if (!node.Success || target.InterfaceName is not null && target.InterfaceName != networkInterface.Name)
            return WorkerJson.SerializePayload(new WorkerResponse { Success = false, FailureCategory = node.FailureCategory ?? WorkerFailureCategories.TargetEvidenceMismatch, Error = node.Error ?? "Interface constraint mismatch." });
        // Preflight all dependency selectors before any scalar mutation.
        if (request.SubnetId is not null || request.IoSystemNumber is not null)
            return WorkerJson.SerializePayload(new WorkerResponse { Success = false, FailureCategory = WorkerFailureCategories.WorkerOperationFailed, Error = "Requested dependency was not found; no mutation." });
        if (scenario == "network-qualified-budget-known-observations")
        {
            // This scenario reports attempted assignments while retaining the old hardware state.
            guardedNetworkWrites++;
            var attempted = new Dictionary<string, string>
            { [NetworkSettingKeys.Address] = request.IpAddress!, [NetworkSettingKeys.SubnetMask] = request.SubnetMask!, [NetworkSettingKeys.PnDeviceName] = request.PnDeviceName! };
            return Success(WorkerJson.SerializePayload(new ConfigureNetworkDeviceResultInfo
            { DeviceName = request.DeviceName!, AppliedSettings = attempted }));
        }
        var applied = new Dictionary<string, string>();
        guardedNetworkWrites++;
        if (request.IpAddress is not null) { node.Item!.IpAddress = request.IpAddress; applied[NetworkSettingKeys.Address] = request.IpAddress; }
        if (request.SubnetMask is not null)
        {
            if (scenario != "network-qualified-partial") { node.Item!.SubnetMask = request.SubnetMask; applied[NetworkSettingKeys.SubnetMask] = request.SubnetMask; }
        }
        if (request.PnDeviceName is not null) { node.Item!.PnDeviceName = request.PnDeviceName; applied[NetworkSettingKeys.PnDeviceName] = request.PnDeviceName; }
        return Success(WorkerJson.SerializePayload(new ConfigureNetworkDeviceResultInfo { DeviceName = request.DeviceName!, AppliedSettings = applied }));
    }

    private static HardwareConfigInfo QualifiedHardwareFixture() => new()
    {
        RootDeviceCount = 1,
        DiscoveryEvidence = new() { Scope = "project", Complete = true },
        Devices = new() { new() { Name = "S7-1500/ET200MP station_1", Items = new() { new()
        {
            Name = "PLC_DP", PositionNumber = 1,
            SelectorDiagnostics = new() { "Generic item type evidence is unavailable." },
            Items = new()
            {
                new() { Name = "PROFINET interface_1", PositionNumber = 32768,
                    SelectorDiagnostics = new() { "Generic item type evidence is unavailable." }, NetworkInterfaces = new()
                    { new() { Name = "PROFINET interface_1", SelectorDiagnostics = new() { "Generic owner type evidence is unavailable." },
                        Nodes = new() { new() { NodeId = "E1", Name = "X1", IpAddress = "192.168.12.2", SubnetMask = "255.255.255.0" } } } } },
                new() { Name = "PROFINET interface_2", PositionNumber = 33024,
                    SelectorDiagnostics = new() { "Generic item type evidence is unavailable." }, NetworkInterfaces = new()
                    { new() { Name = "PROFINET interface_2", SelectorDiagnostics = new() { "Generic owner type evidence is unavailable." },
                        Nodes = new() { new() { NodeId = "E1", Name = "X2", IpAddress = "192.168.13.20" } } } } },
            },
        } } } },
    };

    private static DeviceItemInfo SelectableDeviceItem(
        string deviceName,
        int index,
        string itemName,
        string typeIdentifier,
        int positionNumber,
        string interfaceName,
        params NodeInfo[] nodes)
    {
        var path = new List<DeviceItemPathSegmentInfo>
        {
            new()
            {
                Index = index,
                Name = itemName,
                PositionNumber = positionNumber,
                TypeIdentifier = typeIdentifier,
            },
        };

        foreach (var node in nodes)
            node.Selector = NetworkSelectorFactory.QualifiedNode(deviceName, node.NodeId,
                path.Select(segment => new NetworkInterfacePathSegmentInfo { Name = segment.Name,
                    PositionNumber = segment.PositionNumber, TypeIdentifier = segment.TypeIdentifier }).ToList(), interfaceName);

        return new DeviceItemInfo
        {
            Name = itemName,
            TypeIdentifier = typeIdentifier,
            PositionNumber = positionNumber,
            Selectable = true,
            Selector = new NetworkObjectSelectorInfo
            {
                Kind = NetworkObjectKinds.DeviceItem,
                DeviceName = deviceName,
                ItemPath = path,
            },
            NetworkInterfaces = new List<NetworkInterfaceInfo>
            {
                new()
                {
                    Name = interfaceName,
                    Selectable = true,
                    Selector = new NetworkObjectSelectorInfo
                    {
                        Kind = NetworkObjectKinds.NetworkInterface,
                        DeviceName = deviceName,
                        ItemPath = path,
                        InterfaceName = interfaceName,
                    },
                    Nodes = nodes.ToList(),
                },
            },
        };
    }

    private static NodeInfo SelectableNode(
        string deviceName,
        string name,
        string nodeId,
        string? nodeType = null,
        string? ipAddress = null,
        string? subnetMask = null,
        string? pnDeviceName = null,
        string? subnetName = null,
        string? ioSystemName = null) => new()
    {
        Name = name,
        NodeId = nodeId,
        NodeType = nodeType,
        IpAddress = ipAddress,
        SubnetMask = subnetMask,
        PnDeviceName = pnDeviceName,
        SubnetName = subnetName,
        IoSystemName = ioSystemName,
        Selectable = true,
        Selector = new NetworkObjectSelectorInfo
        {
            Kind = NetworkObjectKinds.Node,
            DeviceName = deviceName,
            NodeId = nodeId,
        },
    };

    private static SubnetInfo SelectableSubnet(
        string name,
        string subnetId,
        string? networkType,
        string? typeIdentifier,
        IEnumerable<IoSystemInfo> ioSystems,
        IEnumerable<string> connectedNodeNames) => new()
    {
        Name = name,
        SubnetId = subnetId,
        NetworkType = networkType,
        TypeIdentifier = typeIdentifier,
        Selectable = true,
        Selector = new NetworkObjectSelectorInfo { Kind = NetworkObjectKinds.Subnet, SubnetId = subnetId },
        IoSystems = ioSystems.ToList(),
        ConnectedNodeNames = connectedNodeNames.ToList(),
    };

    private static IoSystemInfo SelectableIoSystem(string subnetId, string name, int number, string? controllerName) => new()
    {
        Name = name,
        Number = number,
        IoControllerName = controllerName,
        Selectable = true,
        Selector = new NetworkObjectSelectorInfo
        {
            Kind = NetworkObjectKinds.IoSystem,
            SubnetId = subnetId,
            Number = number,
        },
    };

    // Builds the current HardwareConfigInfo for the "multi-homed-network" scenario from the live
    // mutable node state, so a read after a configure_network_device call observes the mutation.
    private static HardwareConfigInfo MultiHomedHardwareConfig(MultiHomedNode plc, MultiHomedNode db) => new()
    {
        DiscoveryEvidence = new() { Scope = "project", Complete = true },
        Devices = new List<DeviceInfo>
        {
            new()
            {
                Name = "PC_1",
                TypeIdentifier = "OrderNumber:PC-System",
                Items = new List<DeviceItemInfo>
                {
                    SelectableDeviceItem(
                        "PC_1", 0, "IE general_1", "OrderNumber:IE-General", 1,
                        "PROFINET interface_1",
                        SelectableNode(
                            "PC_1", plc.Name, plc.NodeId, "Ethernet", plc.IpAddress, plc.SubnetMask,
                            plc.PnDeviceName, "PN/IE_1")),
                    SelectableDeviceItem(
                        "PC_1", 1, "IE general_2", "OrderNumber:IE-General", 2,
                        "PROFINET interface_2",
                        SelectableNode(
                            "PC_1", db.Name, db.NodeId, "Ethernet", db.IpAddress, db.SubnetMask,
                            db.PnDeviceName, "PN/IE_2")),
                },
            },
        },
        Subnets = new List<SubnetInfo>
        {
            SelectableSubnet(
                "PN/IE_1", "subnet-plc", "Ethernet", null,
                Array.Empty<IoSystemInfo>(), new[] { $"PC_1.{plc.Name}" }),
            SelectableSubnet(
                "PN/IE_2", "subnet-db", "Ethernet", null,
                Array.Empty<IoSystemInfo>(), new[] { $"PC_1.{db.Name}" }),
        },
        Messages = new List<string>(),
    };

    // Parses the forwarded nodeId and mutates ONLY the matching node's live state, so the OTHER node
    // stays byte-for-byte identical on a later read. Real Openness would resolve this same nodeId to a
    // specific Node object before applying any of these settings; this fixture mirrors that by keying
    // off the same exact identifier the host already resolved before ever sending this request.
    string ConfigureMultiHomedNode(string requestLine, MultiHomedNode plc, MultiHomedNode db)
    {
        var nodeId = ReadField(requestLine, "nodeId");
        var target = nodeId switch
        {
            "node-plc" => plc,
            "node-db" => db,
            _ => null,
        };

        if (target is null)
        {
            return $$"""{"success":false,"error":"multi-homed-network has no node with nodeId '{{nodeId}}'"}""";
        }

        var applied = new Dictionary<string, string>();

        var ipAddress = ReadField(requestLine, "ipAddress");
        if (ipAddress is not null)
        {
            target.IpAddress = ipAddress;
            applied[NetworkSettingKeys.Address] = ipAddress;
        }

        var subnetMask = ReadField(requestLine, "subnetMask");
        if (subnetMask is not null)
        {
            target.SubnetMask = subnetMask;
            applied[NetworkSettingKeys.SubnetMask] = subnetMask;
        }

        var pnDeviceName = ReadField(requestLine, "pnDeviceName");
        if (pnDeviceName is not null)
        {
            target.PnDeviceName = pnDeviceName;
            applied[NetworkSettingKeys.PnDeviceName] = pnDeviceName;
        }

        var result = new ConfigureNetworkDeviceResultInfo
        {
            DeviceName = "PC_1",
            AppliedSettings = applied,
            Messages = new List<string> { $"configured nodeId '{nodeId}'" },
        };

        return Success(WorkerJson.SerializePayload(result));
    }

    // Builds the Phase 3 list_network_objects fixture: one object of every kind (6 total), with the
    // communicationConnection entry unselectable (selector is null) because its connection index cannot
    // always be determined at list time. TotalCount matches Items.Count (no hidden items on this page).
    private static NetworkObjectListInfo ListNetworkObjectsFixture() => new()
    {
        Items = new List<NetworkObjectSummaryInfo>
        {
            new()
            {
                Kind = NetworkObjectKinds.DeviceItem,
                Selectable = true,
                Selector = new NetworkObjectSelectorInfo
                {
                    Kind = NetworkObjectKinds.DeviceItem,
                    DeviceName = "PLC_1",
                    ItemPath = new List<DeviceItemPathSegmentInfo>
                    {
                        new()
                        {
                            Index = 0,
                            Name = "PROFINET interface_1",
                            PositionNumber = 1,
                            TypeIdentifier = "OrderNumber:TEST",
                        },
                    },
                },
                Evidence = new NetworkObjectEvidenceInfo
                {
                    Name = "PROFINET interface_1",
                    TypeIdentifier = "OrderNumber:TEST",
                    PositionNumber = 1,
                    DeviceItemPath = new List<string> { "PROFINET interface_1" },
                },
            },
            new()
            {
                Kind = NetworkObjectKinds.NetworkInterface,
                Selectable = true,
                Selector = new NetworkObjectSelectorInfo
                {
                    Kind = NetworkObjectKinds.NetworkInterface,
                    DeviceName = "PLC_1",
                    ItemPath = new List<DeviceItemPathSegmentInfo>
                    {
                        new()
                        {
                            Index = 0,
                            Name = "PROFINET interface_1",
                            PositionNumber = 1,
                            TypeIdentifier = "OrderNumber:TEST",
                        },
                    },
                    InterfaceName = "PROFINET interface_1",
                },
                Evidence = new NetworkObjectEvidenceInfo
                {
                    Name = "PROFINET interface_1",
                    DeviceItemPath = new List<string> { "PROFINET interface_1" },
                    InterfaceName = "PROFINET interface_1",
                },
            },
            new()
            {
                Kind = NetworkObjectKinds.Node,
                Selectable = true,
                Selector = new NetworkObjectSelectorInfo
                {
                    Kind = NetworkObjectKinds.Node,
                    DeviceName = "PLC_1",
                    NodeId = "node-1",
                },
                Evidence = new NetworkObjectEvidenceInfo { Name = "X1", NodeName = "X1" },
            },
            new()
            {
                Kind = NetworkObjectKinds.Subnet,
                Selectable = true,
                Selector = new NetworkObjectSelectorInfo
                {
                    Kind = NetworkObjectKinds.Subnet,
                    SubnetId = "subnet-1",
                },
                Evidence = new NetworkObjectEvidenceInfo { Name = "PN/IE_1", SubnetName = "PN/IE_1" },
            },
            new()
            {
                Kind = NetworkObjectKinds.IoSystem,
                Selectable = true,
                Selector = new NetworkObjectSelectorInfo
                {
                    Kind = NetworkObjectKinds.IoSystem,
                    SubnetId = "subnet-1",
                    Number = 100,
                },
                Evidence = new NetworkObjectEvidenceInfo
                {
                    Name = "IO system_1",
                    SubnetName = "PN/IE_1",
                    IoSystemName = "IO system_1",
                },
            },
            new()
            {
                Kind = NetworkObjectKinds.CommunicationConnection,
                Selectable = false,
                Selector = null, // connection index not always determinable at list time
                Evidence = new NetworkObjectEvidenceInfo
                {
                    Name = "S7 connection_1",
                    TypeIdentifier = "S7",
                    ConnectionIsValid = false,
                },
                Diagnostics = new List<string>
                {
                    "Connection identity could not be read; selector not available.",
                },
            },
        },
        TotalCount = 6,
        ReturnedCount = 6,
        NextCursor = null,
    };

    // Builds the Phase 3 inspect_network_object fixture: attributes covering the full typed value
    // vocabulary. Each attribute carries source provenance, access classification, supportedTypes, and
    // availability. The special names unknownAttribute, readFailed, and unrepresentable exercise the
    // three non-available availability states so round-trip tests can assert all lifecycle paths.
    private static NetworkObjectInspectionInfo InspectNetworkObjectFixture() => new()
    {
        Target = new NetworkObjectSelectorInfo
        {
            Kind = NetworkObjectKinds.Node,
            DeviceName = "PLC_1",
            NodeId = "node-1",
        },
        Evidence = new NetworkObjectEvidenceInfo
        {
            Name = "X1",
            TypeIdentifier = "OrderNumber:TEST",
            PositionNumber = 1,
            Address = "192.168.0.10",
            DeviceItemPath = new List<string> { "PLC_1", "X1" },
            InterfaceName = "PROFINET interface_1",
            InterfaceType = "PROFINET",
            InterfaceOperatingMode = "IoController",
            NodeName = "X1",
            NodeType = "Ethernet",
            SubnetName = "PN/IE_1",
            NetworkType = "Ethernet",
            IoSystemName = "IO system_1",
            IoControllerName = "PLC_1",
            ConnectionIsValid = true,
            LocalEndpointName = "PLC_1.X1",
            PartnerEndpointName = "ET200SP_1.X1",
            LocalSubnetName = "PN/IE_1",
            PartnerSubnetName = "PN/IE_1",
        },
        Attributes = new List<NetworkAttributeInfo>
        {
            new()
            {
                Name = "nullAttribute",
                Source = "modeled",
                Access = "readOnly",
                SupportedTypes = new List<string>(),
                Availability = "available",
                Value = new NetworkAttributeValueInfo { Kind = "null", Value = null },
            },
            new()
            {
                Name = "stringAttribute",
                Source = "modeled",
                Access = "readOnly",
                SupportedTypes = new List<string> { "string" },
                Availability = "available",
                Value = new NetworkAttributeValueInfo { Kind = "string", Value = "192.168.0.10" },
            },
            new()
            {
                Name = "booleanAttribute",
                Source = "modeled",
                Access = "readOnly",
                SupportedTypes = new List<string> { "boolean" },
                Availability = "available",
                Value = new NetworkAttributeValueInfo { Kind = "boolean", Value = true },
            },
            new()
            {
                Name = "integerAttribute",
                Source = "modeled",
                Access = "readOnly",
                SupportedTypes = new List<string> { "integer" },
                Availability = "available",
                Value = new NetworkAttributeValueInfo { Kind = "integer", Value = 1500L },
            },
            new()
            {
                Name = "numberAttribute",
                Source = "modeled",
                Access = "readOnly",
                SupportedTypes = new List<string> { "number" },
                Availability = "available",
                Value = new NetworkAttributeValueInfo { Kind = "number", Value = 3.14 },
            },
            new()
            {
                Name = "enumAttribute",
                Source = "modeled",
                Access = "readOnly",
                SupportedTypes = new List<string> { "enum" },
                Availability = "available",
                Value = new NetworkAttributeValueInfo
                {
                    Kind = "enum",
                    Value = new NetworkEnumValueInfo { TypeName = "MediaType", Symbol = "Ethernet", NumericValue = 1 },
                },
            },
            new()
            {
                Name = "unknownAttribute",
                Source = null,
                Access = "unknown",
                SupportedTypes = new List<string>(),
                Availability = "unknownAttribute",
                Diagnostic = new NetworkAttributeDiagnosticInfo
                {
                    Category = "unknown_attribute",
                    Message = "Attribute was not recognized.",
                },
            },
            new()
            {
                Name = "readFailed",
                Source = "modeled",
                Access = "readOnly",
                SupportedTypes = new List<string>(),
                Availability = "readFailed",
                Diagnostic = new NetworkAttributeDiagnosticInfo { Category = "read_error", Message = "read failed" },
            },
            new()
            {
                Name = "unrepresentable",
                Source = "modeled",
                Access = "readOnly",
                SupportedTypes = new List<string>(),
                Availability = "unrepresentable",
                Diagnostic = new NetworkAttributeDiagnosticInfo { Category = "type_error", Message = "cannot represent value" },
            },
        },
        Messages = new List<string>(),
    };

    // Two disjoint scripted pages verify that the host unwraps the public continuation before dispatch.
    private static NetworkObjectListInfo LargeListNetworkObjectsFixture(bool secondPage = false)
    {
        var items = Enumerable.Range(secondPage ? 21 : 1, 20)
            .Select(i => new NetworkObjectSummaryInfo
            {
                Kind = NetworkObjectKinds.Node,
                Selectable = true,
                Selector = new NetworkObjectSelectorInfo
                {
                    Kind = NetworkObjectKinds.Node,
                    DeviceName = "LargeSwitch",
                    NodeId = $"node-{i:D3}",
                },
                Evidence = new NetworkObjectEvidenceInfo
                {
                    Name = $"Port_{i:D2}",
                    NodeName = $"Port_{i:D2}",
                },
            })
            .ToList();

        return new NetworkObjectListInfo
        {
            Items = items,
            TotalCount = 100,
            ReturnedCount = items.Count,
            NextCursor = secondPage ? "large-list-page-3" : "large-list-page-2",
        };
    }

    // Builds the "network-io-map" hardware fixture. ioDetails is attached only when the read opted
    // in; otherwise the DeviceItemInfo.IoDetails JsonIgnore attribute omits the member entirely, so
    // a default read is byte-identical to the pre-I/O-map shape.
    private static HardwareConfigInfo IoMapHardwareConfig(
        bool includeIoDetails,
        bool includeTagMatches,
        string? deviceName,
        string? plcName)
    {
        var selectedDevice = deviceName is null
            || string.Equals("PLC_1", deviceName, StringComparison.OrdinalIgnoreCase);
        var selectedPlc = !includeTagMatches
            || plcName is null
            || string.Equals("PLC_1", plcName, StringComparison.Ordinal);

        return new HardwareConfigInfo
        {
            Devices = selectedDevice
                ? new List<DeviceInfo>
                {
                    new()
                    {
                        Name = "PLC_1",
                        TypeIdentifier = "OrderNumber:TEST",
                        Items = new List<DeviceItemInfo>
                        {
                            IoMapDeviceItem("DI_16", includeIoDetails, includeTagMatches && selectedPlc),
                        },
                    },
                }
                : new List<DeviceInfo>(),
            Subnets = new List<SubnetInfo>(),
            Messages = includeTagMatches && !selectedPlc
                ? new List<string> { $"No PLC named '{plcName}' was found; no tag matches are reported." }
                : new List<string>(),
        };
    }

    private static DeviceItemInfo IoMapDeviceItem(string itemName, bool includeIoDetails, bool includeTagMatches)
    {
        var item = SelectableDeviceItem(
            "PLC_1", 0, itemName, "OrderNumber:TEST", 1, "PROFINET interface_1");
        if (!includeIoDetails)
        {
            return item;
        }

        item.IoDetails = new DeviceItemIoDetailsInfo
        {
            Addresses = new List<IoAddressInfo>
            {
                // Diagnosis-type addresses on PROFINET interfaces report StartAddress = -1 (and
                // Length = -1) on V21; the worker normalizes those to null, so the fixture models the
                // normalized shape. Ordinal IoType order ("Diagnosis" < "Input" < "Output") mirrors
                // the real worker's deterministic sort.
                new()
                {
                    IoType = "Diagnosis",
                    StartAddress = null,
                    Length = null,
                    Context = null,
                    ControllerNames = new List<string>(),
                },
                new()
                {
                    IoType = "Input",
                    StartAddress = 4,
                    Length = 2,
                    Context = "Device",
                    ControllerNames = new List<string> { "PLC_1" },
                },
                new()
                {
                    IoType = "Output",
                    StartAddress = 4,
                    Length = 2,
                    Context = "Device",
                    ControllerNames = new List<string> { "PLC_1" },
                },
            },
            Channels = new List<IoChannelInfo>
            {
                new()
                {
                    Number = 0,
                    IoType = "Input",
                    Type = "Digital",
                    ChannelAddressBits = 32,
                    ChannelWidthBits = 1,
                    LogicalAddress = "%I4.0",
                    TagMatches = includeTagMatches
                        ? new List<IoTagMatchInfo>
                        {
                            // Ordinal order (table, folder, name): mirrors the real worker's sort so the
                            // fixture and production agree on deterministic output.
                            new() { Name = "RunPermit", DataType = "Bool", LogicalAddress = "%I4.0", TableName = "Tag table_1", FolderPath = "/" },
                            new() { Name = "StartButton", DataType = "Bool", LogicalAddress = "%I4.0", TableName = "Tag table_1", FolderPath = "/" },
                        }
                        : new List<IoTagMatchInfo>(),
                },
                new()
                {
                    Number = 1,
                    IoType = "Input",
                    Type = "Analog",
                    ChannelAddressBits = 512,
                    ChannelWidthBits = 16,
                    LogicalAddress = "%IW64",
                    TagMatches = includeTagMatches
                        ? new List<IoTagMatchInfo>
                        {
                            new() { Name = "AnalogIn", DataType = "Int", LogicalAddress = "%IW64", TableName = "Tag table_1", FolderPath = "/" },
                        }
                        : new List<IoTagMatchInfo>(),
                },
            },
        };
        return item;
    }

    // Builds the "network-io-map-malformed" fixture: a structurally valid device item whose
    // ioDetails carries an EXPLICIT null addresses collection — the exact shape that must be
    // rejected as protocol_error by NetworkPayloadContract.
    private static HardwareConfigInfo IoMapMalformedHardwareConfig() => new()
    {
        Devices = new List<DeviceInfo>
        {
            new()
            {
                Name = "PLC_1",
                TypeIdentifier = "OrderNumber:TEST",
                Items = new List<DeviceItemInfo>
                {
                    new()
                    {
                        Name = "DI_16",
                        TypeIdentifier = "OrderNumber:TEST",
                        PositionNumber = 1,
                        Selectable = false,
                        SelectorDiagnostics = new List<string> { "No selector fixture for the malformed I/O-map item." },
                        NetworkInterfaces = new List<NetworkInterfaceInfo>(),
                        CommunicationConnections = new List<CommunicationConnectionInfo>(),
                        Items = new List<DeviceItemInfo>(),
                        IoDetails = new DeviceItemIoDetailsInfo
                        {
                            Addresses = null!, // explicit null collection -> protocol_error
                            Channels = new List<IoChannelInfo>(),
                        },
                    },
                },
            },
        },
        Subnets = new List<SubnetInfo>(),
        Messages = new List<string>(),
    };

}
