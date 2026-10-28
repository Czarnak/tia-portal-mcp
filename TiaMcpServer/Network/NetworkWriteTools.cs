using System.ComponentModel;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
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
    [Description("Execute up to 50 ordered network writes against one verified already-open project. Omitted dryRun or dryRun:false executes; use dryRun:true to preview effects and guards without mutation. No confirmation prompt is sent. Operations: add_network_device (typeIdentifier, deviceName), configure_network_device (exact target.deviceName and target.nodeId, changes), create_subnet (subnet.name, subnet.networkType), update_subnet (target.kind='subnet', exact target.subnetId, subnetChanges), delete_subnet (target.kind='subnet', exact target.subnetId). Omitted change members stay unchanged. Connected subnet deletion removes the listed connections while preserving devices and nodes; listed identities reflect the planning snapshot only. Incomplete deletion consequence evidence blocks execution. Phases: preview, applied, blocked, error. Stops on the first configuration rejection or other failure, with no rollback or automatic replay. Native configuration rejections return engineering_rejected and any carried appliedSettings; earlier settings may remain applied. Verification reports trusted or superseded from completed operations without read-back. Re-read affected objects with network_read before retrying. This tool never saves, compiles, downloads, or controls a PLC. A PROFINET device name is only settable once changes.pnDeviceNameAutoGeneration is false; pass both in the same item.")]
    public static Task<CallToolResult> NetworkWrite(
        OpennessWorkerClient workerClient,
        WriteExecution execution,
        [Description("Ordered network write operations.")] NetworkOperationRequest[] operations,
        [Description("True previews effects and guards without mutation; omitted or false executes.")] bool dryRun = false,
        CancellationToken cancellationToken = default)
        => execution.RunAsync(new NetworkWriteDomain(workerClient),
            new WriteCall<NetworkOperationRequest>(
                operations ?? [], dryRun), cancellationToken: cancellationToken);

}

internal static class NetworkWriteToolRegistration
{
    internal static JsonNode AllowNullCurrentSettings(AIJsonSchemaCreateContext context, JsonNode schema)
    {
        if (context.TypeInfo.Type == typeof(NetworkWriteEffect)
            && schema is JsonObject effectSchema
            && effectSchema["properties"]?["currentSettings"]?["additionalProperties"] is JsonObject values)
            values["type"] = new JsonArray("string", "null");
        return schema;
    }
}
