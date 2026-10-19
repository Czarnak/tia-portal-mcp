// Offline object-access doubles for the source-linked network selector resolver.
namespace Siemens.Engineering
{
    public abstract partial class ProjectBase
    {
        public Composition<HW.Subnet> Subnets { get; } = new();
    }
}

namespace Siemens.Engineering.HW
{
    public enum NetType { Ethernet }

    public sealed class Subnet : NamedObject
    {
        public object? SubnetId { get; set; } = "subnet-a";
        public Exception? IdentityFailure { get; set; }
        public NetType NetType { get; set; }
        public string TypeIdentifier { get; set; } = "System:Subnet.Ethernet";
        public Composition<Features.IoSystem> IoSystems { get; } = new();
        public override object GetAttribute(string name) => name == "SubnetId"
            ? IdentityFailure is null ? SubnetId! : throw IdentityFailure
            : base.GetAttribute(name);
    }
}

namespace Siemens.Engineering.HW.Features
{
    public enum InterfaceType { Ethernet }
    public enum InterfaceOperatingMode { IoController }
    public enum NodeType { Ethernet }

    public sealed class NetworkInterface : NamedObject
    {
        public InterfaceType InterfaceType { get; set; }
        public InterfaceOperatingMode InterfaceOperatingMode { get; set; }
        public Composition<Node> Nodes { get; } = new();
    }

    public sealed class Node : NamedObject
    {
        public string NodeId { get; set; } = "node-a";
        public NodeType NodeType { get; set; }
    }

    public sealed class IoSystem : NamedObject
    {
        private int number;
        public Exception? NumberFailure { get; set; }
        public int Number
        {
            get => NumberFailure is null ? number : throw NumberFailure;
            set => number = value;
        }
    }
}

namespace Siemens.Engineering.HW.CommunicationConnections
{
    public enum ConnectionType { S7Connection }
    public sealed class CommunicationManagement
    {
        public ConnectionComposition Connections { get; } = new();
    }
    public sealed class ConnectionComposition : Composition<Connection> { }
    public class Connection : NamedObject
    {
        public ConnectionType ConnectionType { get; set; }
        public bool IsValid { get; set; }
        public NamedObject? LocalInterface { get; set; }
        public NamedObject? LocalTarget { get; set; }
        public NamedObject? PartnerInterface { get; set; }
        public NamedObject? PartnerTarget { get; set; }
        public string? LocalSubnetName { get; set; }
        public string? PartnerSubnetName { get; set; }
    }
    public sealed class S7Connection : Connection
    {
        public string? LocalConnectionId { get; set; }
        public string? LocalConnectionName { get; set; }
        public long LocalConnectionResourceId { get; set; }
    }
    public sealed class FdlConnection : Connection
    {
        public string? LocalConnectionId { get; set; }
        public string? LocalConnectionName { get; set; }
    }
    public sealed class IsoConnection : Connection
    {
        public string? LocalConnectionId { get; set; }
        public string? LocalConnectionName { get; set; }
    }
    public sealed class IsoOnTcpConnection : Connection
    {
        public string? LocalConnectionId { get; set; }
        public string? LocalConnectionName { get; set; }
    }
    public sealed class PtpConnection : Connection
    {
        public string? LocalConnectionId { get; set; }
        public string? LocalConnectionName { get; set; }
    }
    public sealed class TcpConnection : Connection
    {
        public string? LocalConnectionId { get; set; }
        public string? LocalConnectionName { get; set; }
    }
    public sealed class UdpConnection : Connection
    {
        public string? LocalConnectionId { get; set; }
        public string? LocalConnectionName { get; set; }
    }
    public sealed class HmiConnection : Connection
    {
        public string? LocalConnectionName { get; set; }
    }
}
