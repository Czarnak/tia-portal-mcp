using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiConnections;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.Openness.Hmi;
using Xunit;
using static TiaMcpServer.Tests.Hmi.HmiTestProjects;

namespace TiaMcpServer.Tests.Hmi;

public class HmiConnectionReaderTests
{
    private const string Address = "Version=16.0.0.0;HostAddress=1.2.3.4;PlcAddress=1.2.3.5;";

    private static HmiConnection Connection(string name, params (string Name, string Value, string Info)[] driverProperties)
    {
        var connection = new HmiConnection
        {
            Name = name,
            CommunicationDriver = "SIMATIC S7 1200/1500",
            InitialAddress = Address,
            Partner = "PLC_1",
            Station = "S1",
            Node = "N1",
            DisabledAtStartup = true,
            Comment = "plain comment",
        };
        foreach (var (propertyName, value, info) in driverProperties)
        {
            connection.DriverProperties.Items.Add(new DriverProperty { PropertyName = propertyName, Value = value, Info = info });
        }

        return connection;
    }

    private static HmiSoftware SoftwareWith(params HmiConnection[] connections)
    {
        var software = Unified("Panel_RT");
        software.Connections.Items.AddRange(connections);
        return software;
    }

    [Fact]
    public void DriverPropertiesListedSortedByName()
    {
        var software = SoftwareWith(
            Connection("zeta"),
            Connection("Alpha", ("Timeout", "5", "ms"), ("alias", "x", "i1"), ("Baud", "9600", "i2")));

        var info = HmiConnectionReader.ListConnections(software);

        Assert.True(info.IsComplete);
        Assert.Empty(info.Messages);
        Assert.Equal(new[] { "Alpha", "zeta" }, info.Connections.Select(c => c.Name));
        var alpha = info.Connections[0];
        Assert.Equal(new[] { "alias", "Baud", "Timeout" }, alpha.DriverProperties.Select(p => p.PropertyName));
        Assert.Equal("9600", alpha.DriverProperties[1].Value);
        Assert.Equal("i2", alpha.DriverProperties[1].Info);
        Assert.Empty(info.Connections[1].DriverProperties);
    }

    [Fact]
    public void ScalarsAndParsedAddressAreCarried()
    {
        var info = HmiConnectionReader.ListConnections(SoftwareWith(Connection("c")));

        var row = Assert.Single(info.Connections);
        Assert.Equal("SIMATIC S7 1200/1500", row.CommunicationDriver);
        Assert.Equal("PLC_1", row.Partner);
        Assert.Equal("S1", row.Station);
        Assert.Equal("N1", row.Node);
        Assert.Equal(true, row.DisabledAtStartup);
        Assert.Equal("plain comment", row.Comment);
        Assert.Equal(Address, row.InitialAddress!.Raw);
        Assert.Equal("1.2.3.4", row.InitialAddress.Parsed["HostAddress"]);
    }

    [Fact]
    public void UnparsableAddressKeepsRawWithEmptyMap()
    {
        var connection = Connection("c");
        connection.InitialAddress = "opc.tcp://host:4840";

        var row = Assert.Single(HmiConnectionReader.ListConnections(SoftwareWith(connection)).Connections);

        Assert.Equal("opc.tcp://host:4840", row.InitialAddress!.Raw);
        Assert.Empty(row.InitialAddress.Parsed);
    }

    [Fact]
    public void NullCommentIsNullWithoutMessage()
    {
        var connection = Connection("c");
        connection.Comment = null!;

        var info = HmiConnectionReader.ListConnections(SoftwareWith(connection));

        Assert.Null(Assert.Single(info.Connections).Comment);
        Assert.True(info.IsComplete);
    }

    [Fact]
    public void RecoverableUnreadablePropertyIsNullWithMessageAndIncomplete()
    {
        var connection = Connection("c");
        connection.Failures["Node"] = new EngineeringTargetInvocationException("no node");
        connection.Failures["InitialAddress"] = new EngineeringNotSupportedException("no address");

        var info = HmiConnectionReader.ListConnections(SoftwareWith(connection));

        var row = Assert.Single(info.Connections);
        Assert.Null(row.Node);
        Assert.Null(row.InitialAddress);
        Assert.False(info.IsComplete);
        Assert.Equal(2, info.Messages.Count);
        Assert.Contains(info.Messages, m => m.Contains("Node") && m.Contains("'c'"));
    }

    [Fact]
    public void UnreadableDriverPropertiesFailTheItem()
    {
        var connection = Connection("c", ("a", "1", "i"));
        connection.Failures["DriverProperties"] = new EngineeringTargetInvocationException("no drivers");

        var ex = Assert.Throws<WorkerOperationException>(() => HmiConnectionReader.ListConnections(SoftwareWith(connection)));

        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, ex.FailureCategory);
    }

    [Fact]
    public void UnreadableDriverPropertyScalarIsNullWithMessageAndIncomplete()
    {
        var connection = Connection("c", ("a", "1", "i"));
        connection.DriverProperties.Items[0].Failures["Value"] = new EngineeringTargetInvocationException("no value");

        var info = HmiConnectionReader.ListConnections(SoftwareWith(connection));

        var property = Assert.Single(Assert.Single(info.Connections).DriverProperties);
        Assert.Null(property.Value);
        Assert.Equal("a", property.PropertyName);
        Assert.False(info.IsComplete);
    }

    [Fact]
    public void NonRecoverablePropertyFailureFailsTheItem()
    {
        var connection = Connection("c");
        connection.Failures["Partner"] = new EngineeringObjectDisposedException("TIA Portal has been disposed.");

        var ex = Assert.Throws<WorkerOperationException>(() => HmiConnectionReader.ListConnections(SoftwareWith(connection)));

        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, ex.FailureCategory);
    }

    [Fact]
    public void UnreadableCompositionFailsTheItem()
    {
        var software = SoftwareWith(Connection("c"));
        software.Connections.EnumerationFailure = new EngineeringTargetInvocationException("connections unavailable");

        var ex = Assert.Throws<WorkerOperationException>(() => HmiConnectionReader.ListConnections(software));

        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, ex.FailureCategory);
    }

    [Fact]
    public void EachPropertyIsReadOnce()
    {
        var connection = Connection("c", ("a", "1", "i"));
        connection.Reads.Clear(); // building the fixture read DriverProperties

        HmiConnectionReader.ListConnections(SoftwareWith(connection));

        Assert.Equal(1, connection.Reads.Count(r => r == "InitialAddress"));
        Assert.Equal(1, connection.Reads.Count(r => r == "DriverProperties"));
    }
}
