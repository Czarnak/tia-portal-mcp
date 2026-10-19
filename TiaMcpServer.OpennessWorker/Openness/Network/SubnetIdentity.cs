using Siemens.Engineering;

namespace TiaMcpServer.OpennessWorker.Openness.Network;

internal static class SubnetIdentity
{
    public static NetworkObjectDiscoveryEvidenceValue<string> Read(
        IEngineeringObject subnet, string field = "Subnet identity")
        => NetworkOpennessRead.ReadExactStringIdentityAttribute(subnet, "SubnetId", field);
}
