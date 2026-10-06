using ModelContextProtocol.Server;
using Microsoft.Extensions.DependencyInjection;
using TiaMcpServer.Contracts;
using TiaMcpServer.Network;
using TiaMcpServer.Plc;
using TiaMcpServer.ProjectLifecycle;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Tools;

/// <summary>The public surface follows the same capability presets as host and worker dispatch.</summary>
public static class McpToolRegistration
{
    public static IMcpServerBuilder WithAccessModeTools(this IMcpServerBuilder builder, McpAccessMode mode)
    {
        _ = McpAccessModeNames.ToName(mode); // Reject an invalid configuration before registration.
        builder.WithProjectReadTools().WithProjectBindingTools().WithTools<NetworkReadTools>().WithTools<Plc.PlcReadTools>().WithTools<CrossReferences.CrossReferenceReadTools>();
        if (OperationPolicyCatalog.IsCapabilityAllowed(mode, OperationCapability.Compile))
            builder.WithTools<ProjectEngineeringTools>();
        if (OperationPolicyCatalog.IsCapabilityAllowed(mode, OperationCapability.ProjectMutation))
            builder.WithNetworkWriteTools().WithPlcWriteTools();
        if (OperationPolicyCatalog.IsCapabilityAllowed(mode, OperationCapability.ProjectLifecycle))
            builder.WithTools<ProjectWriteTools>();
        return builder;
    }
}

/// <summary>The one production guard catalog: lifecycle, Network and PLC guard definitions.</summary>
public static class WriteGuardRegistration
{
    public static WriteGuardCatalog ProductionCatalog()
        => new(LifecycleWriteDomain.GuardDefinitions.Concat(NetworkGuardDefinitions.Definitions).Concat(PlcGuardDefinitions.Definitions));
}
