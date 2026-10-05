using ModelContextProtocol.Server;
using Microsoft.Extensions.DependencyInjection;
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using TiaMcpServer.Network;

namespace TiaMcpServer.Tools;

/// <summary>The public surface follows the same capability presets as host and worker dispatch.</summary>
public static class McpToolRegistration
{
    public static IMcpServerBuilder WithAccessModeTools(this IMcpServerBuilder builder, McpAccessMode mode)
    {
        _ = McpAccessModeNames.ToName(mode); // Reject an invalid configuration before registration.
        builder.WithProjectReadTools().WithProjectBindingTools().WithTools<ReadBatchTools>().WithTools<NetworkReadTools>().WithTools<Plc.PlcReadTools>();
        if (OperationPolicyCatalog.IsCapabilityAllowed(mode, OperationCapability.Compile))
            builder.WithTools<ProjectEngineeringTools>();
        if (OperationPolicyCatalog.IsCapabilityAllowed(mode, OperationCapability.ProjectMutation))
            builder.WithTools<WriteBatchTools>().WithNetworkWriteTools();
        if (OperationPolicyCatalog.IsCapabilityAllowed(mode, OperationCapability.ProjectLifecycle))
            builder.WithTools<ProjectWriteTools>();
        return builder;
    }
}
