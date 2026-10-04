using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Network;

[McpServerToolType]
public class NetworkWriteTools
{
    [McpServerTool(Name = "network_write", ReadOnly = false, Destructive = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(NetworkGuardedWriteResponse))]
    [Description("Execute up to 50 ordered network writes against one verified already-open project. Omitted dryRun or dryRun:false executes; use dryRun:true to preview effects and guards without mutation. No confirmation prompt is sent. Operations: add_network_device (typeIdentifier, deviceName), configure_network_device (exact target.deviceName and target.nodeId, changes), create_subnet (subnet.name, subnet.networkType), update_subnet (target.kind='subnet', exact target.subnetId, subnetChanges), delete_subnet (target.kind='subnet', exact target.subnetId). Omitted change members stay unchanged. Connected subnet deletion removes the listed connections while preserving devices and nodes; networkDeviceCountUnchanged compares the project's root device count. Unknown consequence evidence blocks execution. Phases: preview, applied, blocked, error. Stops on first failure, with no rollback or automatic replay. Re-read affected objects with network_read before retrying. This tool never saves, compiles, downloads, or controls a PLC.")]
    public static Task<CallToolResult> NetworkWrite(
        OpennessWorkerClient workerClient,
        WriteExecution execution,
        [Description("Ordered network write operations, all constrained to the same project.")] NetworkOperationRequest[] operations,
        [Description("True previews effects and guards without mutation; omitted or false executes.")] bool dryRun = false,
        CancellationToken cancellationToken = default)
        => execution.RunAsync(new NetworkWriteDomain(workerClient),
            new WriteCall<NetworkOperationRequest>(
                operations?.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item?.ProjectPath))?.ProjectPath,
                operations ?? [], dryRun), cancellationToken: cancellationToken);

}
