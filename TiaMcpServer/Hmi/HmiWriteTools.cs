using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Hmi;

[McpServerToolType]
public class HmiWriteTools
{
    [McpServerTool(Name = "hmi_write", ReadOnly = false, Destructive = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(HmiGuardedWriteResponse))]
    [Description("Execute up to 50 ordered Unified HMI writes against one verified already-open project. Omitted dryRun or dryRun:false executes; dryRun:true previews effects, dependencies and guards. No confirmation prompt is sent. Operations: create_tag_table, update_tag_table, delete_tag_table, create_tag, update_tag, delete_tag, create_alarm, update_alarm, delete_alarm. Tags use connection plus plcTag for PLC binding, or dataType for an internal tag. Alarms require alarmKind (discrete or analog). Supply multilingual text as culture/text pairs using plain text; eventText must have no line breaks and the server wraps it as markup. Never send pre-wrapped EventText. Later items may depend on earlier writes. Stops on first failure without rollback or automatic replay; failed items retain appliedProperties, failedProperty and create cleanup in their typed result. Inspect affected objects with hmi_read before retrying. This tool never saves, downloads or controls a device.")]
    public static Task<CallToolResult> HmiWrite(OpennessWorkerClient workerClient, WriteExecution execution,
        [Description("Ordered Unified HMI writes.")] HmiWriteOperationRequest[] operations,
        [Description("True previews without mutation; omitted or false executes.")] bool dryRun = false,
        CancellationToken cancellationToken = default)
        => execution.RunAsync(new HmiWriteDomain(workerClient), new WriteCall<HmiWriteOperationRequest>(
            operations ?? [], dryRun), cancellationToken: cancellationToken);
}
