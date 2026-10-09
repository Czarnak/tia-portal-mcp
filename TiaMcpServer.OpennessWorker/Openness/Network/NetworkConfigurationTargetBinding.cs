using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Network;

/// <summary>Validates the IPC selector authority and freezes it before worker project activity.</summary>
public static class NetworkConfigurationTargetBinding
{
    public static NetworkObjectSelectorInfo Resolve(WorkerRequest request)
    {
        var supplied = request.NetworkObjectTarget;
        if (supplied is not null && (request.DeviceName is not null && !string.Equals(request.DeviceName, supplied.DeviceName, StringComparison.OrdinalIgnoreCase)
            || request.NodeId is not null && !string.Equals(request.NodeId, supplied.NodeId, StringComparison.Ordinal)))
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "Top-level device/node identity conflicts with networkObjectTarget.");
        var target = supplied ?? new NetworkObjectSelectorInfo { Kind = NetworkObjectKinds.Node, DeviceName = request.DeviceName, NodeId = request.NodeId };
        if (string.IsNullOrWhiteSpace(target.DeviceName) || string.IsNullOrWhiteSpace(target.NodeId)
            || (target.Kind is not null && target.Kind != NetworkObjectKinds.Node))
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "Configuration requires a deviceName and nodeId node selector.");
        return JsonSerializer.Deserialize<NetworkObjectSelectorInfo>(JsonSerializer.Serialize(target, WorkerJson.Envelope), WorkerJson.Envelope)!;
    }
}
