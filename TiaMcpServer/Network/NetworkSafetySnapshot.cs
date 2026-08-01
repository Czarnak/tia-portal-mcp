using TiaMcpServer.Contracts;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Network;

/// <summary>Stable preview targets and current-state acquisition for dedicated network writes.</summary>
public static class NetworkSafetySnapshot
{
    public static IReadOnlyList<OperationBatchTarget> BuildTargets(
        IReadOnlyList<NetworkOperationRequest> operations)
        => operations.Select(operation => new OperationBatchTarget(
            operation.OperationId,
            operation.Operation,
            DescribeOperation(operation))).ToArray();

    public static string? ResolveProjectPath(IReadOnlyList<NetworkOperationRequest> operations)
        => operations.FirstOrDefault(operation => !string.IsNullOrWhiteSpace(operation.ProjectPath))?.ProjectPath;

    public static Task<WorkerCallResult> ReadCurrentStateAsync(
        OpennessWorkerClient client,
        string? projectPath)
        => client.ReadHardwareConfigAsync(projectPath);

    private static string DescribeOperation(NetworkOperationRequest operation) => operation.Operation switch
    {
        "add_network_device" => $"Add network device '{operation.DeviceName}' using catalog type '{operation.TypeIdentifier}'.",
        "configure_network_device" => $"Configure network properties for device '{operation.DeviceName}'.",
        _ => $"{operation.Operation}.",
    };
}
