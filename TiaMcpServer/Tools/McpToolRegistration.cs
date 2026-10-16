using ModelContextProtocol.Server;
using Microsoft.Extensions.DependencyInjection;
using TiaMcpServer.Network;
using TiaMcpServer.Plc;
using TiaMcpServer.ProjectLifecycle;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Contracts.Safety;

namespace TiaMcpServer.Tools;

/// <summary>The public surface follows the same capability presets as host and worker dispatch.</summary>
public static class McpToolRegistration
{
    public static IMcpServerBuilder WithAccessModeTools(this IMcpServerBuilder builder, McpAccessMode mode)
    {
        _ = McpAccessModeNames.ToName(mode); // Reject an invalid configuration before registration.
        builder.WithProjectReadTools().WithProjectBindingTools().WithTools<NetworkReadTools>().WithTools<Plc.PlcReadTools>().WithTools<Hmi.HmiReadTools>().WithTools<CrossReferences.CrossReferenceReadTools>().WithVciReadTools();
        if (OperationPolicyCatalog.IsCapabilityAllowed(mode, OperationCapability.Compile))
            builder.WithTools<ProjectEngineeringTools>();
        if (OperationPolicyCatalog.IsCapabilityAllowed(mode, OperationCapability.ProjectMutation))
            builder.WithNetworkWriteTools().WithPlcWriteTools().WithHmiWriteTools();
        if (OperationPolicyCatalog.IsCapabilityAllowed(mode, OperationCapability.ProjectLifecycle))
            builder.WithTools<ProjectWriteTools>();
        return builder;
    }
}

/// <summary>The one production guard catalog: lifecycle, Network, PLC, HMI and VCI guard definitions.</summary>
public static class WriteGuardRegistration
{
    public static WriteGuardCatalog ProductionCatalog()
        => new(LifecycleWriteDomain.GuardDefinitions.Concat(NetworkGuardDefinitions.Definitions).Concat(PlcGuardDefinitions.Definitions).Concat(Hmi.HmiGuardDefinitions.Definitions).Concat(Vci.VciGuardDefinitions.Definitions));
}
