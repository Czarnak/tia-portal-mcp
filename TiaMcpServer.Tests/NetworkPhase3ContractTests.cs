using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.Network;
using Xunit;

namespace TiaMcpServer.Tests;

/// <summary>
/// Contract tests for the Phase 3 network-introspection public surface:
/// DTO round-trips, selector validation, and catalog operation registration.
/// </summary>
public class NetworkPhase3ContractTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static NetworkOperationRequest Op(
        string id,
        string operation,
        Action<NetworkOperationRequest>? configure = null)
    {
        var req = new NetworkOperationRequest { OperationId = id, Operation = operation };
        configure?.Invoke(req);
        return req;
    }

    private static NetworkOperationRequest ListOp(
        string id = "lst",
        IReadOnlyList<string>? kinds = null,
        Action<NetworkOperationRequest>? adjust = null)
    {
        var req = new NetworkOperationRequest
        {
            OperationId = id,
            Operation = "list_network_objects",
            ObjectKinds = kinds ?? new[] { NetworkObjectKinds.Node },
        };
        adjust?.Invoke(req);
        return req;
    }

    private static NetworkOperationRequest InspectOp(
        string id = "ins",
        NetworkObjectTarget? target = null,
        Action<NetworkOperationRequest>? adjust = null)
    {
        var req = new NetworkOperationRequest
        {
            OperationId = id,
            Operation = "inspect_network_object",
            Target = target ?? Phase3Fixtures.ValidTarget(NetworkObjectKinds.Node),
        };
        adjust?.Invoke(req);
        return req;
    }

    [Fact]
    public void InspectionResultDtos_MatchLockedPublicContract()
    {
        Assert.True(typeof(NetworkObjectInspectionInfo).IsSealed);
        Assert.Equal(
            new[] { "Attributes", "Evidence", "Messages", "Target" },
            typeof(NetworkObjectInspectionInfo)
                .GetProperties()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));

        Assert.Equal(typeof(NetworkObjectSelectorInfo),
            typeof(NetworkObjectInspectionInfo).GetProperty("Target")!.PropertyType);
        Assert.Equal(typeof(NetworkObjectEvidenceInfo),
            typeof(NetworkObjectInspectionInfo).GetProperty("Evidence")!.PropertyType);

        Assert.True(typeof(NetworkObjectEvidenceInfo).IsSealed);
        Assert.Equal(
            new[]
            {
                "Address",
                "ConnectionIsValid",
                "DeviceItemPath",
                "InterfaceName",
                "InterfaceOperatingMode",
                "InterfaceType",
                "IoControllerName",
                "IoSystemName",
                "LocalEndpointName",
                "LocalSubnetName",
                "Name",
                "NetworkType",
                "NodeName",
                "NodeType",
                "PartnerEndpointName",
                "PartnerSubnetName",
                "PositionNumber",
                "SubnetName",
                "TypeIdentifier",
            },
            typeof(NetworkObjectEvidenceInfo)
                .GetProperties()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(typeof(int?),
            typeof(NetworkObjectEvidenceInfo).GetProperty("PositionNumber")!.PropertyType);
        Assert.Equal(typeof(bool?),
            typeof(NetworkObjectEvidenceInfo).GetProperty("ConnectionIsValid")!.PropertyType);
        Assert.Equal(typeof(List<string>),
            typeof(NetworkObjectEvidenceInfo).GetProperty("DeviceItemPath")!.PropertyType);
    }

    [Fact]
    public void DiscoveryResultDtos_MatchLockedPublicContractExactly()
    {
        Assert.Equal(
            new[] { "Diagnostics", "Evidence", "Kind", "Selectable", "Selector" },
            typeof(NetworkObjectSummaryInfo)
                .GetProperties()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(typeof(NetworkObjectEvidenceInfo),
            typeof(NetworkObjectSummaryInfo).GetProperty("Evidence")!.PropertyType);
        Assert.Equal(typeof(List<string>),
            typeof(NetworkObjectSummaryInfo).GetProperty("Diagnostics")!.PropertyType);

        Assert.Equal(
            new[] { "Items", "NextCursor", "ReturnedCount", "TotalCount" },
            typeof(NetworkObjectListInfo)
                .GetProperties()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(typeof(int),
            typeof(NetworkObjectListInfo).GetProperty("TotalCount")!.PropertyType);

        Assert.Equal(
            new[] { "Index", "Name", "PositionNumber", "TypeIdentifier" },
            typeof(DeviceItemPathSegmentInfo)
                .GetProperties()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(typeof(int),
            typeof(DeviceItemPathSegmentInfo).GetProperty("PositionNumber")!.PropertyType);

        Assert.Null(typeof(NetworkObjectSelectorInfo).Assembly.GetType(
            "TiaMcpServer.Contracts.NetworkDeviceItemPathSegmentInfo",
            throwOnError: false,
            ignoreCase: false));
    }

    [Fact]
    public void DiscoveryResultJson_UsesOnlyLockedCamelCaseProperties()
    {
        var summary = new NetworkObjectSummaryInfo();
        using var summaryDocument = JsonDocument.Parse(CanonicalJson.Serialize(summary));
        Assert.Equal(
            new[] { "diagnostics", "evidence", "kind", "selectable", "selector" },
            summaryDocument.RootElement
                .EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));

        var list = new NetworkObjectListInfo();
        using var listDocument = JsonDocument.Parse(CanonicalJson.Serialize(list));
        Assert.Equal(
            new[] { "items", "nextCursor", "returnedCount", "totalCount" },
            listDocument.RootElement
                .EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(JsonValueKind.Number, listDocument.RootElement.GetProperty("totalCount").ValueKind);
    }

    // ---------------------------------------------------------------------------
    // Selector round-trip: every kind serialises to JSON that NetworkObjectTarget
    // accepts without unmapped-member rejection.
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("deviceItem")]
    [InlineData("networkInterface")]
    [InlineData("node")]
    [InlineData("subnet")]
    [InlineData("ioSystem")]
    [InlineData("communicationConnection")]
    public void Selector_output_round_trips_into_strict_request_target(string kind)
    {
        var selector = Phase3Fixtures.ValidSelector(kind);
        var json = CanonicalJson.Serialize(selector);
        var target = CanonicalJson.Deserialize<NetworkObjectTarget>(json);

        Assert.NotNull(target);
        Assert.Equal(kind, target!.Kind);
    }

    // ---------------------------------------------------------------------------
    // list_network_objects — objectKinds validation
    // ---------------------------------------------------------------------------

    [Fact]
    public void ListNetworkObjects_MissingObjectKinds_IsRejected()
    {
        var result = NetworkOperationCatalog.ValidateRead(new[]
        {
            Op("lst", "list_network_objects"),
        });

        Assert.False(result.IsValid);
        Assert.Contains("objectKinds", result.Error);
    }

    [Fact]
    public void ListNetworkObjects_EmptyObjectKinds_IsRejected()
    {
        var result = NetworkOperationCatalog.ValidateRead(new[]
        {
            ListOp(adjust: req => req.ObjectKinds = Array.Empty<string>()),
        });

        Assert.False(result.IsValid);
        Assert.Contains("objectKinds", result.Error);
    }

    [Fact]
    public void ListNetworkObjects_DuplicateObjectKinds_IsRejected()
    {
        var result = NetworkOperationCatalog.ValidateRead(new[]
        {
            ListOp(adjust: req => req.ObjectKinds = new[] { NetworkObjectKinds.Node, NetworkObjectKinds.Node }),
        });

        Assert.False(result.IsValid);
        Assert.Contains("objectKinds", result.Error);
    }

    [Fact]
    public void ListNetworkObjects_UnknownObjectKind_IsRejected()
    {
        var result = NetworkOperationCatalog.ValidateRead(new[]
        {
            ListOp(adjust: req => req.ObjectKinds = new[] { "widget" }),
        });

        Assert.False(result.IsValid);
        Assert.Contains("widget", result.Error);
    }

    [Fact]
    public void ListNetworkObjects_BlankGlobalDeviceName_IsRejected()
    {
        var result = NetworkOperationCatalog.ValidateRead(new[]
        {
            ListOp(adjust: req => req.DeviceName = "   "),
        });

        Assert.False(result.IsValid);
        Assert.Contains("deviceName", result.Error);
    }

    [Theory]
    [InlineData(NetworkObjectKinds.Subnet)]
    [InlineData(NetworkObjectKinds.IoSystem)]
    public void ListNetworkObjects_GlobalKindWithDeviceName_IsRejected(string globalKind)
    {
        var result = NetworkOperationCatalog.ValidateRead(new[]
        {
            ListOp(kinds: new[] { globalKind }, adjust: request => request.DeviceName = "PLC_1"),
        });

        Assert.False(result.IsValid);
        Assert.Contains("deviceName", result.Error);
        Assert.Contains(globalKind, result.Error);
    }

    [Fact]
    public void ListNetworkObjects_MixedDeviceAndGlobalKindsWithDeviceName_IsRejected()
    {
        var result = NetworkOperationCatalog.ValidateRead(new[]
        {
            ListOp(
                kinds: new[] { NetworkObjectKinds.Node, NetworkObjectKinds.Subnet },
                adjust: request => request.DeviceName = "PLC_1"),
        });

        Assert.False(result.IsValid);
        Assert.Contains("deviceName", result.Error);
        Assert.Contains(NetworkObjectKinds.Subnet, result.Error);
    }

    [Fact]
    public void ListNetworkObjects_AllDeviceScopedKindsWithDeviceName_IsAccepted()
    {
        var result = NetworkOperationCatalog.ValidateRead(new[]
        {
            ListOp(
                kinds: new[]
                {
                    NetworkObjectKinds.DeviceItem,
                    NetworkObjectKinds.NetworkInterface,
                    NetworkObjectKinds.Node,
                    NetworkObjectKinds.CommunicationConnection,
                },
                adjust: request => request.DeviceName = "PLC_1"),
        });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void ListNetworkObjects_GlobalKindsWithoutDeviceName_AreAccepted()
    {
        var result = NetworkOperationCatalog.ValidateRead(new[]
        {
            ListOp(kinds: new[] { NetworkObjectKinds.Subnet, NetworkObjectKinds.IoSystem }),
        });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void ListNetworkObjects_PageSizeZero_IsRejected()
    {
        var result = NetworkOperationCatalog.ValidateRead(new[]
        {
            ListOp(adjust: req => req.PageSize = 0),
        });

        Assert.False(result.IsValid);
        Assert.Contains("pageSize", result.Error);
    }

    [Fact]
    public void ListNetworkObjects_PageSizeOver200_IsRejected()
    {
        var result = NetworkOperationCatalog.ValidateRead(new[]
        {
            ListOp(adjust: req => req.PageSize = 201),
        });

        Assert.False(result.IsValid);
        Assert.Contains("pageSize", result.Error);
    }

    [Fact]
    public void ListNetworkObjects_CursorWithoutObjectKinds_IsCoveredByRequiredFieldCheck()
    {
        // cursor requires an objectKinds list to page within; without objectKinds the
        // required-field check fires first.
        var result = NetworkOperationCatalog.ValidateRead(new[]
        {
            Op("lst", "list_network_objects", req => req.Cursor = "page2"),
        });

        Assert.False(result.IsValid);
        Assert.Contains("objectKinds", result.Error);
    }

    [Fact]
    public void ListNetworkObjects_AllKinds_IsAccepted()
    {
        var result = NetworkOperationCatalog.ValidateRead(new[]
        {
            ListOp(kinds: NetworkObjectKinds.All.ToArray()),
        });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void ListNetworkObjects_WithAllOptionalFields_IsAccepted()
    {
        var result = NetworkOperationCatalog.ValidateRead(new[]
        {
            ListOp(adjust: req =>
            {
                req.DeviceName = "PLC_1";
                req.PageSize = 50;
                req.Cursor = "tok";
            }),
        });

        Assert.True(result.IsValid, result.Error);
    }

    // ---------------------------------------------------------------------------
    // inspect_network_object — target and attributeNames validation
    // ---------------------------------------------------------------------------

    [Fact]
    public void InspectNetworkObject_MissingTarget_IsRejected()
    {
        var result = NetworkOperationCatalog.ValidateRead(new[]
        {
            Op("ins", "inspect_network_object"),
        });

        Assert.False(result.IsValid);
        Assert.Contains("target", result.Error);
    }

    [Fact]
    public void InspectNetworkObject_EmptyAttributeNames_IsRejected()
    {
        var result = NetworkOperationCatalog.ValidateRead(new[]
        {
            InspectOp(adjust: req => req.AttributeNames = Array.Empty<string>()),
        });

        Assert.False(result.IsValid);
        Assert.Contains("attributeNames", result.Error);
    }

    [Fact]
    public void InspectNetworkObject_DuplicateAttributeNames_IsRejected()
    {
        var result = NetworkOperationCatalog.ValidateRead(new[]
        {
            InspectOp(adjust: req => req.AttributeNames = new[] { "IpAddress", "IpAddress" }),
        });

        Assert.False(result.IsValid);
        Assert.Contains("attributeNames", result.Error);
    }

    [Fact]
    public void InspectNetworkObject_Over200AttributeNames_IsRejected()
    {
        var names = Enumerable.Range(0, 201).Select(i => $"Attr{i}").ToArray();

        var result = NetworkOperationCatalog.ValidateRead(new[]
        {
            InspectOp(adjust: req => req.AttributeNames = names),
        });

        Assert.False(result.IsValid);
        Assert.Contains("attributeNames", result.Error);
    }

    // ---------------------------------------------------------------------------
    // inspect_network_object — valid selectors for every kind
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("deviceItem")]
    [InlineData("networkInterface")]
    [InlineData("node")]
    [InlineData("subnet")]
    [InlineData("ioSystem")]
    [InlineData("communicationConnection")]
    public void InspectNetworkObject_ValidSelector_IsAccepted(string kind)
    {
        var result = NetworkOperationCatalog.ValidateRead(new[]
        {
            InspectOp(target: Phase3Fixtures.ValidTarget(kind)),
        });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void InspectNetworkObject_IoSystemNameEvidence_IsAccepted()
    {
        var target = Phase3Fixtures.ValidTarget(NetworkObjectKinds.IoSystem);
        target.IoSystemIndex = 1;
        target.IoSystemName = "IO system_1";

        var result = NetworkOperationCatalog.ValidateRead(new[] { InspectOp(target: target) });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void InspectNetworkObject_NodePathAndIndexEvidence_IsAccepted()
    {
        var target = Phase3Fixtures.ValidTarget(NetworkObjectKinds.DeviceItem);
        target.Kind = NetworkObjectKinds.Node;
        target.NodeId = "IE1";
        target.NodeIndex = 1;

        var result = NetworkOperationCatalog.ValidateRead(new[] { InspectOp(target: target) });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void InspectNetworkObject_NodeIndexWithoutPath_IsRejected()
    {
        var target = Phase3Fixtures.ValidTarget(NetworkObjectKinds.Node);
        target.NodeIndex = 1;

        var result = NetworkOperationCatalog.ValidateRead(new[] { InspectOp(target: target) });

        Assert.False(result.IsValid);
        Assert.Contains("itemPath", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void InspectNetworkObject_BlankIoSystemNameEvidence_IsRejected()
    {
        var target = Phase3Fixtures.ValidTarget(NetworkObjectKinds.IoSystem);
        target.IoSystemName = "   ";

        var result = NetworkOperationCatalog.ValidateRead(new[] { InspectOp(target: target) });

        Assert.False(result.IsValid);
        Assert.Contains("ioSystemName", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void InspectNetworkObject_NegativeIoSystemIndexEvidence_IsRejected()
    {
        var target = Phase3Fixtures.ValidTarget(NetworkObjectKinds.IoSystem);
        target.IoSystemIndex = -1;

        var result = NetworkOperationCatalog.ValidateRead(new[] { InspectOp(target: target) });

        Assert.False(result.IsValid);
        Assert.Contains("ioSystemIndex", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(NetworkObjectKinds.DeviceItem)]
    [InlineData(NetworkObjectKinds.NetworkInterface)]
    [InlineData(NetworkObjectKinds.CommunicationConnection)]
    public void InspectNetworkObject_EmptyItemPath_IsRejected(string kind)
    {
        var target = Phase3Fixtures.ValidTarget(kind);
        target.ItemPath = Array.Empty<NetworkDeviceItemPathSegment>();

        var result = NetworkOperationCatalog.ValidateRead(new[] { InspectOp(target: target) });

        Assert.False(result.IsValid);
        Assert.Contains("itemPath", result.Error);
    }

    [Theory]
    [InlineData("index", -1)]
    [InlineData("positionNumber", -1)]
    public void InspectNetworkObject_NegativeItemPathNumber_IsRejected(string field, int value)
    {
        var target = Phase3Fixtures.ValidTarget(NetworkObjectKinds.DeviceItem);
        var segment = Assert.Single(target.ItemPath!);
        if (field == "index")
        {
            segment.Index = value;
        }
        else
        {
            segment.PositionNumber = value;
        }

        var result = NetworkOperationCatalog.ValidateRead(new[] { InspectOp(target: target) });

        Assert.False(result.IsValid);
        Assert.Contains(field, result.Error);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("typeIdentifier")]
    public void InspectNetworkObject_BlankItemPathText_IsRejected(string field)
    {
        var target = Phase3Fixtures.ValidTarget(NetworkObjectKinds.DeviceItem);
        var segment = Assert.Single(target.ItemPath!);
        if (field == "name")
        {
            segment.Name = "   ";
        }
        else
        {
            segment.TypeIdentifier = "   ";
        }

        var result = NetworkOperationCatalog.ValidateRead(new[] { InspectOp(target: target) });

        Assert.False(result.IsValid);
        Assert.Contains(field, result.Error);
    }

    [Fact]
    public void InspectNetworkObject_MissingItemPathPosition_IsRejected()
    {
        var target = Phase3Fixtures.ValidTarget(NetworkObjectKinds.DeviceItem);
        Assert.Single(target.ItemPath!).PositionNumber = null;

        var result = NetworkOperationCatalog.ValidateRead(new[] { InspectOp(target: target) });

        Assert.False(result.IsValid);
        Assert.Contains("positionNumber", result.Error);
    }

    [Theory]
    [InlineData(NetworkObjectKinds.IoSystem, "number")]
    [InlineData(NetworkObjectKinds.CommunicationConnection, "connectionIndex")]
    public void InspectNetworkObject_NegativeSelectorNumber_IsRejected(string kind, string field)
    {
        var target = Phase3Fixtures.ValidTarget(kind);
        if (field == "number")
        {
            target.Number = -1;
        }
        else
        {
            target.ConnectionIndex = -1;
        }

        var result = NetworkOperationCatalog.ValidateRead(new[] { InspectOp(target: target) });

        Assert.False(result.IsValid);
        Assert.Contains(field, result.Error);
    }

    [Fact]
    public void InspectNetworkObject_BlankOptionalInterfaceEvidence_IsRejected()
    {
        var target = Phase3Fixtures.ValidTarget(NetworkObjectKinds.NetworkInterface);
        target.InterfaceType = "   ";

        var result = NetworkOperationCatalog.ValidateRead(new[] { InspectOp(target: target) });

        Assert.False(result.IsValid);
        Assert.Contains("interfaceType", result.Error);
    }

    [Fact]
    public void InspectNetworkObject_BlankInapplicableField_IsStillRejected()
    {
        var target = Phase3Fixtures.ValidTarget(NetworkObjectKinds.Node);
        target.SubnetId = "   ";

        var result = NetworkOperationCatalog.ValidateRead(new[] { InspectOp(target: target) });

        Assert.False(result.IsValid);
        Assert.Contains("subnetId", result.Error);
    }

    [Theory]
    [InlineData("S7Connection")]
    [InlineData("FdlConnection")]
    [InlineData("IsoConnection")]
    [InlineData("IsoOnTcpConnection")]
    [InlineData("PtpConnection")]
    [InlineData("TcpConnection")]
    [InlineData("UdpConnection")]
    public void InspectNetworkObject_NonHmiConnectionRequiresLocalConnectionId(string connectionType)
    {
        var target = Phase3Fixtures.ValidTarget(NetworkObjectKinds.CommunicationConnection);
        target.ConnectionType = connectionType;
        target.LocalConnectionId = null;

        var result = NetworkOperationCatalog.ValidateRead(new[] { InspectOp(target: target) });

        Assert.False(result.IsValid);
        Assert.Contains("localConnectionId", result.Error);
    }

    [Fact]
    public void InspectNetworkObject_HmiConnectionForbidsLocalConnectionId()
    {
        var target = Phase3Fixtures.ValidTarget(NetworkObjectKinds.CommunicationConnection);
        target.ConnectionType = "HmiConnection";
        target.LocalConnectionId = "not-applicable";

        var result = NetworkOperationCatalog.ValidateRead(new[] { InspectOp(target: target) });

        Assert.False(result.IsValid);
        Assert.Contains("localConnectionId", result.Error);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("s7connection")]
    [InlineData("S7")]
    public void InspectNetworkObject_UnsupportedConnectionType_IsRejected(string connectionType)
    {
        var target = Phase3Fixtures.ValidTarget(NetworkObjectKinds.CommunicationConnection);
        target.ConnectionType = connectionType;

        var result = NetworkOperationCatalog.ValidateRead(new[] { InspectOp(target: target) });

        Assert.False(result.IsValid);
        Assert.Contains("connectionType", result.Error);
    }

    // ---------------------------------------------------------------------------
    // inspect_network_object — missing required selector fields
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("deviceItem", "deviceName")]
    [InlineData("deviceItem", "itemPath")]
    [InlineData("networkInterface", "deviceName")]
    [InlineData("networkInterface", "itemPath")]
    [InlineData("node", "deviceName")]
    [InlineData("node", "nodeId")]
    [InlineData("subnet", "subnetId")]
    [InlineData("ioSystem", "subnetId")]
    [InlineData("ioSystem", "number")]
    [InlineData("communicationConnection", "deviceName")]
    [InlineData("communicationConnection", "itemPath")]
    [InlineData("communicationConnection", "connectionIndex")]
    [InlineData("communicationConnection", "connectionType")]
    [InlineData("communicationConnection", "localConnectionName")]
    public void InspectNetworkObject_MissingRequiredSelectorField_IsRejected(string kind, string missingField)
    {
        var target = Phase3Fixtures.ValidTarget(kind);
        Phase3Fixtures.ClearField(target, missingField);

        var result = NetworkOperationCatalog.ValidateRead(new[]
        {
            InspectOp(target: target),
        });

        Assert.False(result.IsValid);
        Assert.Contains(missingField, result.Error);
    }

    // ---------------------------------------------------------------------------
    // inspect_network_object — inapplicable selector fields
    // ---------------------------------------------------------------------------

    [Theory]
    // deviceItem does not accept node/subnet/ioSystem/connection fields
    [InlineData("deviceItem", "nodeId", "node-7")]
    [InlineData("deviceItem", "subnetId", "S1")]
    // networkInterface does not accept itemPath/node/subnet/ioSystem/connection
    [InlineData("networkInterface", "nodeId", "node-7")]
    [InlineData("networkInterface", "subnetId", "S1")]
    // node does not accept itemPath/interface/subnet/ioSystem/connection fields
    [InlineData("node", "subnetId", "S1")]
    [InlineData("node", "interfaceName", "PROFINET")]
    // subnet does not accept device/itemPath/node/connection fields
    [InlineData("subnet", "deviceName", "PLC_1")]
    [InlineData("subnet", "nodeId", "7")]
    // ioSystem does not accept device/itemPath/interface/node/connection fields
    [InlineData("ioSystem", "deviceName", "PLC_1")]
    [InlineData("ioSystem", "nodeId", "7")]
    // communicationConnection does not accept subnet/ioSystem/node fields
    [InlineData("communicationConnection", "subnetId", "S1")]
    [InlineData("communicationConnection", "nodeId", "7")]
    public void InspectNetworkObject_InapplicableSelectorField_IsRejected(string kind, string field, string value)
    {
        var target = Phase3Fixtures.ValidTarget(kind);
        Phase3Fixtures.SetStringField(target, field, value);

        var result = NetworkOperationCatalog.ValidateRead(new[]
        {
            InspectOp(target: target),
        });

        Assert.False(result.IsValid);
        Assert.Contains(field, result.Error);
    }

    // ---------------------------------------------------------------------------
    // configure_network_device — backward compatibility with new target fields
    // ---------------------------------------------------------------------------

    [Fact]
    public void ConfigureNetworkDevice_TargetWithExplicitNodeKind_IsAccepted()
    {
        var result = NetworkOperationCatalog.ValidateWrite(new[]
        {
            new NetworkOperationRequest
            {
                OperationId = "cfg",
                Operation = "configure_network_device",
                Target = new NetworkObjectTarget { Kind = NetworkObjectKinds.Node, DeviceName = "PLC_1", NodeId = "7" },
                Changes = new NetworkDeviceChanges { IpAddress = "192.168.0.10" },
            },
        });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void ConfigureNetworkDevice_TargetWithNonNodeKind_IsRejected()
    {
        var result = NetworkOperationCatalog.ValidateWrite(new[]
        {
            new NetworkOperationRequest
            {
                OperationId = "cfg",
                Operation = "configure_network_device",
                Target = new NetworkObjectTarget { Kind = NetworkObjectKinds.Subnet, DeviceName = "PLC_1", NodeId = "7" },
                Changes = new NetworkDeviceChanges { IpAddress = "192.168.0.10" },
            },
        });

        Assert.False(result.IsValid);
        Assert.Contains("kind", result.Error);
    }

    [Fact]
    public void ConfigureNetworkDevice_TargetWithNewSelectorField_IsRejected()
    {
        var result = NetworkOperationCatalog.ValidateWrite(new[]
        {
            new NetworkOperationRequest
            {
                OperationId = "cfg",
                Operation = "configure_network_device",
                Target = new NetworkObjectTarget { DeviceName = "PLC_1", NodeId = "7", SubnetId = "S1" },
                Changes = new NetworkDeviceChanges { IpAddress = "192.168.0.10" },
            },
        });

        Assert.False(result.IsValid);
        Assert.Contains("subnetId", result.Error);
    }
}

/// <summary>
/// Test-only fixture factory for Phase 3 selector objects.
/// Returns a fully-populated selector for the given kind so all required fields are present.
/// </summary>
internal static class Phase3Fixtures
{
    public static NetworkObjectSelectorInfo ValidSelector(string kind) => kind switch
    {
        NetworkObjectKinds.DeviceItem => new NetworkObjectSelectorInfo
        {
            Kind = kind,
            DeviceName = "PLC_1",
            ItemPath = new List<DeviceItemPathSegmentInfo> { ValidSelectorPathSegment() },
        },
        NetworkObjectKinds.NetworkInterface => new NetworkObjectSelectorInfo
        {
            Kind = kind,
            DeviceName = "PLC_1",
            ItemPath = new List<DeviceItemPathSegmentInfo> { ValidSelectorPathSegment() },
            InterfaceName = "PROFINET interface_1",
        },
        NetworkObjectKinds.Node => new NetworkObjectSelectorInfo
        {
            Kind = kind,
            DeviceName = "PLC_1",
            NodeId = "7",
        },
        NetworkObjectKinds.Subnet => new NetworkObjectSelectorInfo
        {
            Kind = kind,
            SubnetId = "PN/IE_1",
        },
        NetworkObjectKinds.IoSystem => new NetworkObjectSelectorInfo
        {
            Kind = kind,
            SubnetId = "PN/IE_1",
            Number = 1,
        },
        NetworkObjectKinds.CommunicationConnection => new NetworkObjectSelectorInfo
        {
            Kind = kind,
            DeviceName = "PLC_1",
            ItemPath = new List<DeviceItemPathSegmentInfo> { ValidSelectorPathSegment() },
            ConnectionIndex = 0,
            ConnectionType = "S7Connection",
            LocalConnectionName = "Connection_1",
            LocalConnectionId = "1",
        },
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>Returns a NetworkObjectTarget with all required fields set for the given kind.</summary>
    public static NetworkObjectTarget ValidTarget(string kind) => kind switch
    {
        NetworkObjectKinds.DeviceItem => new NetworkObjectTarget
        {
            Kind = kind,
            DeviceName = "PLC_1",
            ItemPath = new[] { ValidRequestPathSegment() },
        },
        NetworkObjectKinds.NetworkInterface => new NetworkObjectTarget
        {
            Kind = kind,
            DeviceName = "PLC_1",
            ItemPath = new[] { ValidRequestPathSegment() },
            InterfaceName = "PROFINET interface_1",
        },
        NetworkObjectKinds.Node => new NetworkObjectTarget
        {
            Kind = kind,
            DeviceName = "PLC_1",
            NodeId = "7",
        },
        NetworkObjectKinds.Subnet => new NetworkObjectTarget
        {
            Kind = kind,
            SubnetId = "PN/IE_1",
        },
        NetworkObjectKinds.IoSystem => new NetworkObjectTarget
        {
            Kind = kind,
            SubnetId = "PN/IE_1",
            Number = 1,
        },
        NetworkObjectKinds.CommunicationConnection => new NetworkObjectTarget
        {
            Kind = kind,
            DeviceName = "PLC_1",
            ItemPath = new[] { ValidRequestPathSegment() },
            ConnectionIndex = 0,
            ConnectionType = "S7Connection",
            LocalConnectionName = "Connection_1",
            LocalConnectionId = "1",
        },
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static DeviceItemPathSegmentInfo ValidSelectorPathSegment() => new()
    {
        Index = 0,
        Name = "CPU",
        PositionNumber = 1,
        TypeIdentifier = "OrderNumber:CPU",
    };

    private static NetworkDeviceItemPathSegment ValidRequestPathSegment() => new()
    {
        Index = 0,
        Name = "CPU",
        PositionNumber = 1,
        TypeIdentifier = "OrderNumber:CPU",
    };

    /// <summary>Clears a named selector field on the target (sets it to null / default).</summary>
    public static void ClearField(NetworkObjectTarget target, string field)
    {
        switch (field)
        {
            case "deviceName": target.DeviceName = null; break;
            case "itemPath": target.ItemPath = null; break;
            case "interfaceName": target.InterfaceName = null; break;
            case "interfaceType": target.InterfaceType = null; break;
            case "interfaceOperatingMode": target.InterfaceOperatingMode = null; break;
            case "nodeId": target.NodeId = null; break;
            case "subnetId": target.SubnetId = null; break;
            case "number": target.Number = null; break;
            case "connectionIndex": target.ConnectionIndex = null; break;
            case "connectionType": target.ConnectionType = null; break;
            case "localConnectionName": target.LocalConnectionName = null; break;
            case "localConnectionId": target.LocalConnectionId = null; break;
            default: throw new ArgumentOutOfRangeException(nameof(field), field, null);
        }
    }

    /// <summary>Sets a named string selector field on the target.</summary>
    public static void SetStringField(NetworkObjectTarget target, string field, string value)
    {
        switch (field)
        {
            case "deviceName": target.DeviceName = value; break;
            case "interfaceName": target.InterfaceName = value; break;
            case "interfaceType": target.InterfaceType = value; break;
            case "interfaceOperatingMode": target.InterfaceOperatingMode = value; break;
            case "nodeId": target.NodeId = value; break;
            case "subnetId": target.SubnetId = value; break;
            case "connectionType": target.ConnectionType = value; break;
            case "localConnectionName": target.LocalConnectionName = value; break;
            case "localConnectionId": target.LocalConnectionId = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(field), field, null);
        }
    }
}
