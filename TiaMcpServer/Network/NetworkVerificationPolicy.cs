using TiaMcpServer.Contracts.Network;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Network;

public sealed class NetworkVerificationPolicy(IReadOnlyDictionary<string, NetworkWriteEffect> effects)
    : IWriteVerificationPolicy<NetworkOperationRequest>
{
    public string ReadTool => "network_read";
    public bool HasReadBack(NetworkOperationRequest item) => false;
    public Task<WriteOperationVerification> ReadBackAsync(NetworkOperationRequest item, StructuredOperationFailure? failure)
        => throw new NotSupportedException();

    public IReadOnlyList<string> ObjectKeys(NetworkOperationRequest item, bool asLater)
    {
        var target = effects[item.OperationId].Target;
        return item.Operation switch
        {
            "add_network_device" => [$"device\n{target.DeviceName!.ToUpperInvariant()}"],
            "configure_network_device" => [$"device\n{target.DeviceName!.ToUpperInvariant()}\ninterface\n{Owner(target)}\nnode\n{target.NodeId}"],
            "update_subnet" or "delete_subnet" => [$"subnet\n{target.SubnetId}"],
            "create_subnet" => [],
            _ => throw new NotSupportedException(),
        };
    }

    // Optional type evidence constrains selection but does not change the node's identity.
    private static string Owner(NetworkWriteTargetEvidence target)
        => NetworkInterfacePathEncoding.Encode((target.InterfacePath
            ?? throw new InvalidOperationException("A prepared Network node must have a qualified owner path."))
            .Select(segment => new NetworkInterfacePathSegmentInfo
            { Name = segment.Name, PositionNumber = segment.PositionNumber }).ToArray());
}
