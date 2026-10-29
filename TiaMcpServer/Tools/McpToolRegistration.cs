using ModelContextProtocol.Protocol;
using Microsoft.Extensions.DependencyInjection;
using TiaMcpServer.Json;
using TiaMcpServer.Network;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Plc;
using TiaMcpServer.ProjectLifecycle;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.CrossReferences;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Tools;

/// <summary>The public surface follows the same capability presets as host and worker dispatch.</summary>
public static class McpToolRegistration
{
    private const string ValidationError = WorkerFailureCategories.ValidationError;

    /// <summary>
    /// The one registration table: every tool type, its canonical argument-shape rejection, and whether nested
    /// schemas are closed. <c>closeNestedSchemas</c> also closes the output schema, so it matches each tool on main.
    /// </summary>
    private static readonly IReadOnlyDictionary<Type, Func<IMcpServerBuilder, IMcpServerBuilder>> ProductionTools =
        new Dictionary<Type, Func<IMcpServerBuilder, IMcpServerBuilder>>
        {
            [typeof(ProjectReadTools)] = builder => builder.WithClosedTools(typeof(ProjectReadTools),
                (_, tool, message) => tool == "browse_project_tree"
                    ? ProjectTreeArgumentValidatingTool.ValidationFailure(ValidationError, message)
                    : Standalone<ProjectStatusInfo>(tool, message),
                closeNestedSchemas: true, transformSchemaNode: ProjectReadToolRegistration.AlignSelectorSchemaNullability,
                decorate: (tool, strict) => tool == "browse_project_tree" ? new ProjectTreeArgumentValidatingTool(strict) : strict),
            [typeof(ProjectBindingTools)] = builder => builder.WithClosedTools(typeof(ProjectBindingTools),
                (_, tool, message) => Standalone<ProjectBindingResult>(tool, message),
                closeNestedSchemas: true, transformSchemaNode: ProjectBindingToolRegistration.AllowNullForceRebind,
                decorate: (_, strict) => new BindProjectArgumentValidatingTool(strict)),
            [typeof(NetworkReadTools)] = builder => builder.WithClosedTools(typeof(NetworkReadTools),
                (_, _, message) => NetworkReadTools.Error(ValidationError, message), closeNestedSchemas: false),
            [typeof(PlcReadTools)] = builder => builder.WithClosedTools(typeof(PlcReadTools),
                (_, _, message) => PlcReadTools.Error(ValidationError, message), closeNestedSchemas: false),
            [typeof(Hmi.HmiReadTools)] = builder => builder.WithClosedTools(typeof(Hmi.HmiReadTools),
                (_, _, message) => Hmi.HmiReadTools.Error(ValidationError, message), closeNestedSchemas: false),
            [typeof(CrossReferences.CrossReferenceReadTools)] = builder => builder.WithClosedTools(
                typeof(CrossReferences.CrossReferenceReadTools),
                (_, tool, message) => Standalone<CrossReferenceReport>(tool, message), closeNestedSchemas: false),
            [typeof(Vci.VciReadTools)] = builder => builder.WithClosedTools(typeof(Vci.VciReadTools),
                (_, _, message) => Vci.VciReadTools.Error(ValidationError, message), closeNestedSchemas: true),
            [typeof(ProjectEngineeringTools)] = builder => builder.WithClosedTools(typeof(ProjectEngineeringTools),
                (_, tool, message) => Standalone<CompileCheckReport>(tool, message), closeNestedSchemas: false),
            [typeof(NetworkWriteTools)] = builder => builder.WithClosedTools(typeof(NetworkWriteTools),
                (services, _, message) => WriteRejection(new NetworkWriteDomain(Client(services)), message),
                closeNestedSchemas: true, transformSchemaNode: NetworkWriteToolRegistration.AllowNullCurrentSettings),
            [typeof(PlcWriteTools)] = builder => builder.WithClosedTools(typeof(PlcWriteTools),
                (services, _, message) => WriteRejection(new PlcWriteDomain(Client(services)), message),
                closeNestedSchemas: true, transformSchemaNode: PlcWriteToolRegistration.AddObEventClassEnum,
                retiredItemMembers: PlcWriteToolRegistration.RetiredItemMembers),
            [typeof(Hmi.HmiWriteTools)] = builder => builder.WithClosedTools(typeof(Hmi.HmiWriteTools),
                (services, _, message) => WriteRejection(new Hmi.HmiWriteDomain(Client(services)), message),
                closeNestedSchemas: true),
            [typeof(Vci.VciWriteTools)] = builder => builder.WithClosedTools(typeof(Vci.VciWriteTools),
                (services, _, message) => WriteRejection(new Vci.VciWriteDomain(Client(services),
                    services.GetRequiredService<Vci.VciBreadcrumbLocation>()), message),
                closeNestedSchemas: true),
            [typeof(ProjectWriteTools)] = builder => builder.WithClosedTools(typeof(ProjectWriteTools),
                (services, tool, message) => WriteRejection(new LifecycleWriteDomain(Client(services), new LifecycleWriteItem(tool)), message),
                closeNestedSchemas: false),
        };

    public static IMcpServerBuilder WithAccessModeTools(this IMcpServerBuilder builder, McpAccessMode mode)
    {
        _ = McpAccessModeNames.ToName(mode); // Reject an invalid configuration before registration.
        foreach (var type in new[] { typeof(ProjectReadTools), typeof(ProjectBindingTools), typeof(NetworkReadTools), typeof(PlcReadTools),
                     typeof(Hmi.HmiReadTools), typeof(CrossReferences.CrossReferenceReadTools), typeof(Vci.VciReadTools) })
            builder.WithProductionTools(type);
        if (OperationPolicyCatalog.IsCapabilityAllowed(mode, OperationCapability.Compile))
            builder.WithProductionTools(typeof(ProjectEngineeringTools));
        if (OperationPolicyCatalog.IsCapabilityAllowed(mode, OperationCapability.ProjectMutation))
            foreach (var type in new[] { typeof(NetworkWriteTools), typeof(PlcWriteTools), typeof(Hmi.HmiWriteTools), typeof(Vci.VciWriteTools) })
                builder.WithProductionTools(type);
        if (OperationPolicyCatalog.IsCapabilityAllowed(mode, OperationCapability.ProjectLifecycle))
            builder.WithProductionTools(typeof(ProjectWriteTools));
        return builder;
    }

    /// <summary>Registers one production tool type with closed arguments and its canonical rejection.</summary>
    public static IMcpServerBuilder WithProductionTools(this IMcpServerBuilder builder, Type toolType)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return ProductionTools.TryGetValue(toolType, out var register)
            ? register(builder)
            : throw new ArgumentOutOfRangeException(nameof(toolType), toolType, "Not a production tool type.");
    }

    private static OpennessWorkerClient Client(IServiceProvider services) => services.GetRequiredService<OpennessWorkerClient>();

    private static CallToolResult Standalone<TPayload>(string tool, string message) where TPayload : class
        => StructuredStandaloneResult.Create<TPayload>(tool, null, [], new StructuredOperationFailure(ValidationError, message));

    /// <summary>The document WriteRun.Finish returns for an error report, without its audit append.</summary>
    private static CallToolResult WriteRejection<TItem, TEffect, TVerification, TResponse>(
        IWriteDomain<TItem, TEffect, TVerification, TResponse> domain, string message) where TItem : IOperationBatchItem
        => StructuredToolResult.CreateCanonical(CanonicalJson.Serialize(domain.Compose(new WriteReport<TEffect, TVerification>(
            WritePhases.Error, false, new WriteToolError(ValidationError, message), [], [], [], null, default))), isError: true);
}

/// <summary>The one production guard catalog: lifecycle, Network, PLC, HMI and VCI guard definitions.</summary>
public static class WriteGuardRegistration
{
    public static WriteGuardCatalog ProductionCatalog()
        => new(LifecycleWriteDomain.GuardDefinitions.Concat(NetworkGuardDefinitions.Definitions).Concat(PlcGuardDefinitions.Definitions).Concat(Hmi.HmiGuardDefinitions.Definitions).Concat(Vci.VciGuardDefinitions.Definitions));
}
