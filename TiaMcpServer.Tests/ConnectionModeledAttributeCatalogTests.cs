using TiaMcpServer.OpennessWorker;
using Xunit;

namespace TiaMcpServer.Tests;

public sealed class ConnectionModeledAttributeCatalogTests
{
    private static readonly string[] InstalledConnectionTypes =
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

    [Fact]
    public void SupportedConnectionTypes_MatchesInstalledV21EnumExactly()
    {
        Assert.Equal(InstalledConnectionTypes, ConnectionModeledAttributeCatalog.SupportedConnectionTypes);
    }

    [Theory]
    [MemberData(nameof(ConnectionTypes))]
    public void ForConnectionType_ReturnsBaseTypedConnectionFields(string connectionType)
    {
        var descriptors = ConnectionModeledAttributeCatalog.ForConnectionType(connectionType);

        Assert.Contains(descriptors, descriptor =>
            Matches(descriptor, "ConnectionType", "Siemens.Engineering.HW.CommunicationConnections.ConnectionType", "connection.ConnectionType"));
        Assert.Contains(descriptors, descriptor =>
            Matches(descriptor, "IsValid", "System.Boolean", "connection.IsValid"));
        Assert.Contains(descriptors, descriptor =>
            Matches(descriptor, "LocalEndpointName", "System.String", "connection.LocalEndpointName"));
        Assert.Contains(descriptors, descriptor =>
            Matches(descriptor, "LocalSubnetName", "System.String", "connection.LocalSubnetName"));
        Assert.Contains(descriptors, descriptor =>
            Matches(descriptor, "LocalTargetName", "System.String", "connection.LocalTargetName"));
        Assert.Contains(descriptors, descriptor =>
            Matches(descriptor, "PartnerEndpointName", "System.String", "connection.PartnerEndpointName"));
        Assert.Contains(descriptors, descriptor =>
            Matches(descriptor, "PartnerSubnetName", "System.String", "connection.PartnerSubnetName"));
        Assert.Contains(descriptors, descriptor =>
            Matches(descriptor, "PartnerTargetName", "System.String", "connection.PartnerTargetName"));
    }

    [Theory]
    [InlineData("S7Connection", "s7Connection.LocalConnectionName")]
    [InlineData("FdlConnection", "fdlConnection.LocalConnectionName")]
    [InlineData("IsoConnection", "isoConnection.LocalConnectionName")]
    [InlineData("IsoOnTcpConnection", "isoOnTcpConnection.LocalConnectionName")]
    [InlineData("PtpConnection", "ptpConnection.LocalConnectionName")]
    [InlineData("TcpConnection", "tcpConnection.LocalConnectionName")]
    [InlineData("UdpConnection", "udpConnection.LocalConnectionName")]
    [InlineData("HmiConnection", "hmiConnection.LocalConnectionName")]
    public void ForConnectionType_DeclaresTypedLocalConnectionName(
        string connectionType,
        string adapterKey)
    {
        Assert.Contains(
            ConnectionModeledAttributeCatalog.ForConnectionType(connectionType),
            descriptor => Matches(descriptor, "LocalConnectionName", "System.String", adapterKey));
    }

    [Theory]
    [InlineData("S7Connection", "s7Connection.LocalConnectionId")]
    [InlineData("FdlConnection", "fdlConnection.LocalConnectionId")]
    [InlineData("IsoConnection", "isoConnection.LocalConnectionId")]
    [InlineData("IsoOnTcpConnection", "isoOnTcpConnection.LocalConnectionId")]
    [InlineData("PtpConnection", "ptpConnection.LocalConnectionId")]
    [InlineData("TcpConnection", "tcpConnection.LocalConnectionId")]
    [InlineData("UdpConnection", "udpConnection.LocalConnectionId")]
    public void ForConnectionType_DeclaresTypedLocalConnectionIdWhenInstalledTypeExposesIt(
        string connectionType,
        string adapterKey)
    {
        Assert.Contains(
            ConnectionModeledAttributeCatalog.ForConnectionType(connectionType),
            descriptor => Matches(descriptor, "LocalConnectionId", "System.String", adapterKey));
    }

    [Fact]
    public void ForConnectionType_S7AddsLocalConnectionResourceId()
    {
        Assert.Contains(
            ConnectionModeledAttributeCatalog.ForConnectionType("S7Connection"),
            descriptor => Matches(
                descriptor,
                "LocalConnectionResourceId",
                "System.Int64",
                "s7Connection.LocalConnectionResourceId"));
    }

    [Fact]
    public void ForConnectionType_HmiDoesNotClaimUnavailableLocalConnectionId()
    {
        var descriptors = ConnectionModeledAttributeCatalog.ForConnectionType("HmiConnection");

        Assert.DoesNotContain(descriptors, descriptor => descriptor.Name == "LocalConnectionId");
        Assert.DoesNotContain(descriptors, descriptor => descriptor.Name == "LocalConnectionResourceId");
    }

    [Theory]
    [InlineData("S7Connection", true)]
    [InlineData("FdlConnection", true)]
    [InlineData("IsoConnection", true)]
    [InlineData("IsoOnTcpConnection", true)]
    [InlineData("PtpConnection", true)]
    [InlineData("TcpConnection", true)]
    [InlineData("UdpConnection", true)]
    [InlineData("HmiConnection", false)]
    [InlineData("UnknownConnection", false)]
    public void RequiresLocalConnectionId_MatchesConcreteV21TypeContract(
        string connectionType,
        bool expected)
    {
        Assert.Equal(
            expected,
            ConnectionModeledAttributeCatalog.RequiresLocalConnectionId(connectionType));
    }

    [Theory]
    [MemberData(nameof(ConnectionTypes))]
    public void ForConnectionType_IsOrdinallySortedAndHasOneReaderPerName(string connectionType)
    {
        var descriptors = ConnectionModeledAttributeCatalog.ForConnectionType(connectionType);

        Assert.Equal(
            descriptors.Select(descriptor => descriptor.Name).OrderBy(name => name, StringComparer.Ordinal),
            descriptors.Select(descriptor => descriptor.Name));
        Assert.Equal(
            descriptors.Count,
            descriptors.Select(descriptor => descriptor.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.All(descriptors, descriptor =>
        {
            Assert.False(string.IsNullOrWhiteSpace(descriptor.ExpectedClrTypeName));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.AdapterKey));
        });
    }

    [Fact]
    public void ForConnectionType_UnknownTypeHasNoModeledContract()
    {
        Assert.Empty(ConnectionModeledAttributeCatalog.ForConnectionType("UnknownConnection"));
    }

    public static TheoryData<string> ConnectionTypes()
    {
        var values = new TheoryData<string>();
        foreach (var connectionType in InstalledConnectionTypes)
        {
            values.Add(connectionType);
        }

        return values;
    }

    private static bool Matches(
        NetworkModeledAttributeDescriptor descriptor,
        string name,
        string expectedClrTypeName,
        string adapterKey)
        => descriptor.Name == name
            && descriptor.ExpectedClrTypeName == expectedClrTypeName
            && descriptor.AdapterKey == adapterKey;
}
