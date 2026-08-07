using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public sealed class CommunicationConnectionSelectorFactoryTests
{
    private static readonly DeviceItemPathSegmentInfo[] OwnerPath =
    {
        new()
        {
            Index = 0,
            Name = "CPU_1",
            PositionNumber = 1,
            TypeIdentifier = "OrderNumber:CPU",
        },
    };

    [Fact]
    public void Create_SameTypeAndNameConnectionsAreDistinguishedByCompositionIndex()
    {
        var first = Create(connectionIndex: 0);
        var second = Create(connectionIndex: 1);

        Assert.True(first.Selectable);
        Assert.True(second.Selectable);
        Assert.Equal(0, first.Selector!.ConnectionIndex);
        Assert.Equal(1, second.Selector!.ConnectionIndex);
        Assert.Equal(first.ConnectionType, second.ConnectionType);
        Assert.Equal(first.LocalConnectionName, second.LocalConnectionName);
    }

    [Fact]
    public void Create_S7ConnectionIncludesOptionalLocalIdentityEvidence()
    {
        var summary = Create(localConnectionId: "16#1001");

        Assert.Equal(NetworkObjectKinds.CommunicationConnection, summary.Selector!.Kind);
        Assert.Equal("PLC_1", summary.Selector.DeviceName);
        Assert.Equal("S7Connection", summary.Selector.ConnectionType);
        Assert.Equal("S7_Connection_1", summary.Selector.LocalConnectionName);
        Assert.Equal("16#1001", summary.Selector.LocalConnectionId);
        Assert.Equal("PLC_2", summary.PartnerName);
        Assert.True(summary.IsValid);
        Assert.Empty(summary.SelectorDiagnostics);
    }

    [Fact]
    public void Create_HmiConnectionDoesNotInventUnavailableLocalConnectionId()
    {
        var summary = CommunicationConnectionSelectorFactory.Create(
            "HMI_1",
            OwnerPath,
            connectionIndex: 0,
            connectionType: "HmiConnection",
            localConnectionName: "HMI_Connection_1",
            localConnectionId: null,
            partnerName: "PLC_1",
            isValid: true);

        Assert.True(summary.Selectable);
        Assert.Null(summary.LocalConnectionId);
        Assert.Null(summary.Selector!.LocalConnectionId);
    }

    public static TheoryData<string> NonHmiConnectionTypes() => new()
    {
        "S7Connection",
        "FdlConnection",
        "IsoConnection",
        "IsoOnTcpConnection",
        "PtpConnection",
        "TcpConnection",
        "UdpConnection",
    };

    [Theory]
    [MemberData(nameof(NonHmiConnectionTypes))]
    public void Create_EveryNonHmiTypeRequiresNonblankLocalConnectionId(string connectionType)
    {
        foreach (var localConnectionId in new string?[] { null, string.Empty, " " })
        {
            var summary = CommunicationConnectionSelectorFactory.Create(
                "PLC_1",
                OwnerPath,
                connectionIndex: 0,
                connectionType,
                localConnectionName: "Connection_1",
                localConnectionId,
                partnerName: null,
                isValid: true);

            Assert.False(summary.Selectable);
            Assert.Null(summary.Selector);
            Assert.Contains(
                summary.SelectorDiagnostics,
                diagnostic => diagnostic.Contains("local connection ID", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Create_HmiTypeRejectsSuppliedLocalConnectionId()
    {
        var summary = CommunicationConnectionSelectorFactory.Create(
            "HMI_1",
            OwnerPath,
            connectionIndex: 0,
            connectionType: "HmiConnection",
            localConnectionName: "HMI_Connection_1",
            localConnectionId: "not-applicable",
            partnerName: null,
            isValid: true);

        Assert.False(summary.Selectable);
        Assert.Null(summary.Selector);
        Assert.Contains(
            summary.SelectorDiagnostics,
            diagnostic => diagnostic.Contains("does not expose", StringComparison.OrdinalIgnoreCase));
    }

    public static TheoryData<string> InstalledConnectionTypes() => new()
    {
        "S7Connection",
        "FdlConnection",
        "IsoConnection",
        "IsoOnTcpConnection",
        "PtpConnection",
        "TcpConnection",
        "UdpConnection",
        "HmiConnection",
    };

    [Theory]
    [MemberData(nameof(InstalledConnectionTypes))]
    public void Create_AcceptsEveryInstalledV21ConnectionType(string connectionType)
    {
        var summary = CommunicationConnectionSelectorFactory.Create(
            "PLC_1",
            OwnerPath,
            connectionIndex: 0,
            connectionType,
            localConnectionName: "Connection_1",
            localConnectionId: connectionType == "HmiConnection" ? null : "1",
            partnerName: null,
            isValid: true);

        Assert.True(summary.Selectable);
        Assert.Equal(connectionType, summary.Selector!.ConnectionType);
    }

    public static TheoryData<string?, IReadOnlyList<DeviceItemPathSegmentInfo>, int, string?, string?> MissingEvidence() => new()
    {
        { null, OwnerPath, 0, "S7Connection", "S7_Connection_1" },
        { " ", OwnerPath, 0, "S7Connection", "S7_Connection_1" },
        { "PLC_1", Array.Empty<DeviceItemPathSegmentInfo>(), 0, "S7Connection", "S7_Connection_1" },
        { "PLC_1", OwnerPath, -1, "S7Connection", "S7_Connection_1" },
        { "PLC_1", OwnerPath, 0, null, "S7_Connection_1" },
        { "PLC_1", OwnerPath, 0, "UnknownConnection", "S7_Connection_1" },
        { "PLC_1", OwnerPath, 0, "S7Connection", null },
        { "PLC_1", OwnerPath, 0, "S7Connection", " " },
    };

    [Theory]
    [MemberData(nameof(MissingEvidence))]
    public void Create_MissingRequiredEvidenceProducesUnselectableSummary(
        string? deviceName,
        IReadOnlyList<DeviceItemPathSegmentInfo> itemPath,
        int connectionIndex,
        string? connectionType,
        string? localConnectionName)
    {
        var summary = CommunicationConnectionSelectorFactory.Create(
            deviceName,
            itemPath,
            connectionIndex,
            connectionType,
            localConnectionName,
            localConnectionId: null,
            partnerName: null,
            isValid: false);

        Assert.False(summary.Selectable);
        Assert.Null(summary.Selector);
        Assert.NotEmpty(summary.SelectorDiagnostics);
        Assert.All(summary.SelectorDiagnostics, diagnostic => Assert.False(string.IsNullOrWhiteSpace(diagnostic)));
    }

    [Fact]
    public void Create_ClonesOwnerPathEvidence()
    {
        var path = OwnerPath.ToList();
        var summary = CommunicationConnectionSelectorFactory.Create(
            "PLC_1", path, 0, "S7Connection", "S7_Connection_1", "1", null, true);

        path[0].Name = "mutated";

        Assert.Equal("CPU_1", summary.Selector!.ItemPath![0].Name);
    }

    private static CommunicationConnectionInfo Create(int connectionIndex = 0, string? localConnectionId = "16#1001")
        => CommunicationConnectionSelectorFactory.Create(
            "PLC_1",
            OwnerPath,
            connectionIndex,
            connectionType: "S7Connection",
            localConnectionName: "S7_Connection_1",
            localConnectionId,
            partnerName: "PLC_2",
            isValid: true);
}
