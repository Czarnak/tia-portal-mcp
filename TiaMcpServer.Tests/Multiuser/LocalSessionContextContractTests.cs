using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.Network;
using TiaMcpServer.Plc;
using TiaMcpServer.Tools;
using Xunit;

namespace TiaMcpServer.Tests.Multiuser;

public sealed class LocalSessionContextContractTests
{
    private const string AmcPath = @"C:\Sessions\Alpha\LS\Alpha.amc21";
    private const string AlsPath = @"C:\Sessions\Alpha\Alpha.als21";

    private static ProjectContextInfo LocalContext(string? containerPath = null) => new()
    {
        ContainerKind = ProjectContainerKinds.LocalSession,
        SessionMode = MultiuserSessionModes.Unknown,
        Capabilities = ProjectCapabilityCatalog.Describe(ProjectContainerKinds.LocalSession).ToList(),
        RemoteIdentity = null,
        ConnectionObservation = null,
        EngineeringProjectPath = AmcPath,
        SessionContainerPath = containerPath,
        OpenedByWorker = containerPath is not null
    };

    private static WorkerSessionIdentity Identity(ProjectContextInfo context) => new()
    {
        WorkerSessionId = "worker-a",
        SessionGeneration = 7,
        PortalProcessId = 28144,
        ProjectPath = AmcPath,
        Context = context
    };

    private static MultiuserRemoteIdentity ScopedRemote(int sessionId = 2, string alias = "fixture") => new()
    {
        ServerAlias = alias,
        Host = "fixture-host",
        Port = 8735,
        Group = new ProjectServerGroupIdentity { IsRoot = false, Name = "group" },
        ServerProjectName = "fixture-project",
        LocalSessionId = sessionId,
        LocalSessionPath = @"C:\Sessions\Alpha"
    };

    [Fact]
    public void LocalContext_RoundTripsRequiredExplicitNulls()
    {
        var json = WorkerJson.SerializePayload(Identity(LocalContext()));
        using var document = JsonDocument.Parse(json);
        var context = document.RootElement.GetProperty("context");

        Assert.Equal(AmcPath, document.RootElement.GetProperty("projectPath").GetString());
        Assert.Equal("localSession", context.GetProperty("containerKind").GetString());
        Assert.Equal("unknown", context.GetProperty("sessionMode").GetString());
        Assert.Equal(AmcPath, context.GetProperty("engineeringProjectPath").GetString());
        Assert.Equal(JsonValueKind.Null, context.GetProperty("sessionContainerPath").ValueKind);
        Assert.Equal(JsonValueKind.Null, context.GetProperty("remoteIdentity").ValueKind);
        Assert.Equal(JsonValueKind.Null, context.GetProperty("connectionObservation").ValueKind);
        Assert.False(context.GetProperty("openedByWorker").GetBoolean());
        Assert.NotEmpty(context.GetProperty("capabilities").EnumerateArray());
        Assert.Equal(8, context.EnumerateObject().Count());

        var decoded = CanonicalJson.DeserializeWorkerPayload<WorkerSessionIdentity>(json);
        Assert.Equal(AmcPath, decoded.Context!.EngineeringProjectPath);
        Assert.Null(decoded.Context.SessionContainerPath);
        Assert.Throws<JsonException>(() => CanonicalJson.DeserializeWorkerPayload<WorkerSessionIdentity>(
            json.Replace("\"sessionContainerPath\":null,", string.Empty, StringComparison.Ordinal)));
    }

    [Fact]
    public void ColdAdoption_HasAmcBindingAndNullAlsProvenance()
    {
        var binding = new ProjectSessionBinding(null);
        Assert.True(binding.BindVerified(Identity(LocalContext()), false, out var error), error);

        var snapshot = binding.CaptureSnapshot();
        Assert.Equal(AmcPath, snapshot.ProjectPath);
        Assert.Equal(AmcPath, snapshot.Context!.EngineeringProjectPath);
        Assert.Null(snapshot.Context.SessionContainerPath);
        Assert.Null(snapshot.Context.RemoteIdentity);

        var status = new ProjectStatusInfo { IsOpen = true, Path = AmcPath, Metadata = null, Context = snapshot.Context };
        using var document = JsonDocument.Parse(WorkerJson.SerializePayload(status));
        Assert.Equal(AmcPath, document.RootElement.GetProperty("path").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("metadata").ValueKind);
        Assert.Equal(JsonValueKind.Null,
            document.RootElement.GetProperty("context").GetProperty("sessionContainerPath").ValueKind);
    }

    [Fact]
    public void SuccessfulAlsOpen_RecordsProvenanceForReturnedOwner()
    {
        var binding = new ProjectSessionBinding(null);
        Assert.True(binding.BindVerified(Identity(LocalContext(AlsPath)), false, out var error), error);

        var snapshot = binding.CaptureSnapshot();
        Assert.Equal(AmcPath, snapshot.ProjectPath);
        Assert.Equal(AlsPath, snapshot.Context!.SessionContainerPath);
        Assert.True(snapshot.Context.OpenedByWorker);
        Assert.Equal(AlsPath, snapshot.ToWorkerIdentity()!.Context!.SessionContainerPath);
    }

    [Fact]
    public void StandalonePayload_OmitsContext()
    {
        var identity = new WorkerSessionIdentity
        {
            WorkerSessionId = "standalone",
            SessionGeneration = 1,
            PortalProcessId = 123,
            ProjectPath = @"C:\Projects\Standalone.ap21"
        };
        using var workerDocument = JsonDocument.Parse(WorkerJson.SerializePayload(identity));
        Assert.False(workerDocument.RootElement.TryGetProperty("context", out _));
        Assert.Null(CanonicalJson.DeserializeWorkerPayload<WorkerSessionIdentity>(
            workerDocument.RootElement.GetRawText()).Context);

        using var statusDocument = JsonDocument.Parse(WorkerJson.SerializePayload(new ProjectStatusInfo()));
        Assert.False(statusDocument.RootElement.TryGetProperty("context", out _));
        Assert.Equal(JsonValueKind.Null, statusDocument.RootElement.GetProperty("metadata").ValueKind);

        using var bindingDocument = JsonDocument.Parse(CanonicalJson.Serialize(
            new ProjectBindingInfo(ProjectBindingSnapshot.UnboundState, null, null)));
        Assert.False(bindingDocument.RootElement.TryGetProperty("context", out _));
    }

    [Fact]
    public void LocalBindingSnapshot_DeepCopiesContext()
    {
        var context = LocalContext();
        context.RemoteIdentity = ScopedRemote();
        context.ConnectionObservation = new ProjectServerConnectionObservation
        {
            State = ProjectServerConnectionStates.Connected,
            ObservationSource = ProjectServerConnectionObservationSources.ExplicitRead,
            PreviousState = ProjectServerConnectionStates.Unknown
        };
        var binding = new ProjectSessionBinding(null);
        Assert.True(binding.BindVerified(Identity(context), false, out var error), error);
        var first = binding.CaptureSnapshot();

        context.Capabilities[0].Operation = "corrupted";
        context.RemoteIdentity.Group!.Name = "corrupted";
        context.ConnectionObservation.State = ProjectServerConnectionStates.Unknown;
        first.Context!.Capabilities[0].Operation = "also-corrupted";
        first.Context.RemoteIdentity!.LocalSessionId = 99;
        first.ToWorkerIdentity()!.Context!.ConnectionObservation!.State = ProjectServerConnectionStates.Unknown;

        Assert.NotEqual("also-corrupted", first.Context.Capabilities[0].Operation);
        Assert.Equal(2, first.Context.RemoteIdentity.LocalSessionId);
        Assert.Equal(ProjectServerConnectionStates.Connected,
            first.ToWorkerIdentity()!.Context!.ConnectionObservation!.State);

        var second = binding.CaptureSnapshot();
        Assert.NotEqual("corrupted", second.Context!.Capabilities[0].Operation);
        Assert.NotEqual("also-corrupted", second.Context.Capabilities[0].Operation);
        Assert.Equal("group", second.Context.RemoteIdentity!.Group!.Name);
        Assert.Equal(2, second.Context.RemoteIdentity.LocalSessionId);
        Assert.Equal(ProjectServerConnectionStates.Connected, second.Context.ConnectionObservation!.State);
    }

    [Fact]
    public void SameOwner_NewRemoteFactsRefreshWithoutAdvancingBinding()
    {
        var binding = new ProjectSessionBinding(null);
        Assert.True(binding.BindVerified(Identity(LocalContext()), false, out _));
        var before = binding.CaptureSnapshot();
        var observed = LocalContext();
        observed.RemoteIdentity = ScopedRemote();
        observed.ConnectionObservation = new ProjectServerConnectionObservation
        {
            State = ProjectServerConnectionStates.Connected,
            ObservationSource = ProjectServerConnectionObservationSources.ExplicitRead,
            ObservedAt = DateTimeOffset.UtcNow
        };

        Assert.True(binding.TryAdoptVerified(before, Identity(observed), out var error), error);
        var after = binding.CaptureSnapshot();
        Assert.True(before.SameBinding(after));
        Assert.Equal(2, after.Context!.RemoteIdentity!.LocalSessionId);
        Assert.Equal(ProjectServerConnectionStates.Connected, after.Context.ConnectionObservation!.State);

        observed.RemoteIdentity.LocalSessionId = 3;
        Assert.False(binding.TryAdoptVerified(before, Identity(observed), out _));
        var contradicted = binding.CaptureSnapshot();
        Assert.True(before.SameBinding(contradicted));
        Assert.Equal(2, contradicted.Context!.RemoteIdentity!.LocalSessionId);
        Assert.Null(contradicted.Context.ConnectionObservation);
    }

    [Fact]
    public void LocalIdentity_MissingOrContradictoryTypedContextIsRejected()
    {
        var binding = new ProjectSessionBinding(null);
        var absent = Identity(LocalContext());
        absent.Context = null;
        Assert.False(binding.BindVerified(absent, false, out _));

        var wrong = LocalContext();
        wrong.EngineeringProjectPath = @"C:\Sessions\Other\LS\Other.amc21";
        Assert.False(binding.BindVerified(Identity(wrong), false, out _));
        Assert.False(binding.IsVerified);
    }

    [Fact]
    public void UnjoinedObservation_NeverAdvertisesConnectionOrReplacesPinnedEvidence()
    {
        var unjoined = LocalContext();
        unjoined.ConnectionObservation = new ProjectServerConnectionObservation
        {
            State = ProjectServerConnectionStates.Connected,
            ObservationSource = ProjectServerConnectionObservationSources.SessionBind,
            ObservedAt = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero)
        };
        var empty = new ProjectSessionBinding(null);
        Assert.False(empty.BindVerified(Identity(unjoined), false, out _));
        Assert.False(empty.IsVerified);

        unjoined.RemoteIdentity = new MultiuserRemoteIdentity { ServerAlias = "fixture" };
        Assert.False(empty.BindVerified(Identity(unjoined), false, out _));

        var joined = LocalContext();
        joined.RemoteIdentity = ScopedRemote();
        joined.ConnectionObservation = new ProjectServerConnectionObservation
        {
            State = ProjectServerConnectionStates.Connected,
            ObservationSource = ProjectServerConnectionObservationSources.ExplicitRead,
            ObservedAt = new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.Zero)
        };
        var binding = new ProjectSessionBinding(null);
        Assert.True(binding.BindVerified(Identity(joined), false, out _));
        var before = binding.CaptureSnapshot();

        unjoined.RemoteIdentity = null;
        Assert.False(binding.TryAdoptVerified(before, Identity(unjoined), out _));
        var after = binding.CaptureSnapshot();
        Assert.True(before.SameBinding(after));
        Assert.Equal(joined.ConnectionObservation.ObservedAt, after.Context!.ConnectionObservation!.ObservedAt);
        Assert.Equal(2, after.Context.RemoteIdentity!.LocalSessionId);

        unjoined.RemoteIdentity = ScopedRemote(alias: "other-endpoint");
        Assert.False(binding.TryAdoptVerified(before, Identity(unjoined), out _));
        var contradicted = binding.CaptureSnapshot();
        Assert.Equal(2, contradicted.Context!.RemoteIdentity!.LocalSessionId);
        Assert.Null(contradicted.Context.ConnectionObservation);
    }

    [Fact]
    public void UnjoinedInitialUnknown_PromotesAndRefreshesWithoutBindingRevisionChurn()
    {
        var observedAt = new DateTimeOffset(2026, 10, 8, 8, 0, 0, TimeSpan.Zero);
        var local = LocalContext();
        local.ConnectionObservation = new ProjectServerConnectionObservation
        {
            State = ProjectServerConnectionStates.Unknown,
            ObservationSource = ProjectServerConnectionObservationSources.SessionBind,
            ObservedAt = observedAt,
            PreviousState = null,
            Transition = false
        };
        var binding = new ProjectSessionBinding(null);
        Assert.True(binding.BindVerified(Identity(local), false, out var bindError), bindError);
        var before = binding.CaptureSnapshot();
        Assert.Null(before.Context!.RemoteIdentity);
        Assert.Equal(observedAt, before.Context.ConnectionObservation!.ObservedAt);

        Assert.True(binding.TryAdoptVerified(before, Identity(local), out var refreshError), refreshError);
        var after = binding.CaptureSnapshot();
        Assert.True(before.SameBinding(after));
        Assert.Null(after.Context!.RemoteIdentity);
        Assert.Equal(observedAt, after.Context.ConnectionObservation!.ObservedAt);

        local.ConnectionObservation.State = ProjectServerConnectionStates.Connected;
        Assert.False(binding.TryAdoptVerified(before, Identity(local), out _));
        var rejected = binding.CaptureSnapshot();
        Assert.True(before.SameBinding(rejected));
        Assert.Equal(ProjectServerConnectionStates.Unknown,
            rejected.Context!.ConnectionObservation!.State);
        Assert.Null(rejected.Context.RemoteIdentity);
    }

    [Fact]
    public void UnjoinedUnknownWithRemoteReadSource_DoesNotClaimInitialSessionEvidence()
    {
        var local = LocalContext();
        local.ConnectionObservation = new ProjectServerConnectionObservation
        {
            State = ProjectServerConnectionStates.Unknown,
            ObservationSource = ProjectServerConnectionObservationSources.ExplicitRead,
            ObservedAt = new DateTimeOffset(2026, 10, 8, 8, 0, 0, TimeSpan.Zero)
        };
        var binding = new ProjectSessionBinding(null);
        Assert.False(binding.BindVerified(Identity(local), false, out _));
    }

    [Fact]
    public void NullCapabilityEntry_IsRejectedWithoutAnException()
    {
        var malformed = LocalContext();
        malformed.Capabilities.Add(null!);
        var binding = new ProjectSessionBinding(null);
        Assert.False(binding.BindVerified(Identity(malformed), false, out var error));
        Assert.NotNull(error);
        Assert.False(binding.IsVerified);

        var healthy = LocalContext();
        Assert.True(binding.BindVerified(Identity(healthy), false, out _));
        var before = binding.CaptureSnapshot();
        Assert.False(binding.TryAdoptVerified(before, Identity(malformed), out error));
        Assert.NotNull(error);
        Assert.True(before.SameBinding(binding.CaptureSnapshot()));
    }

    [Fact]
    public void CapabilityCatalog_CoversEveryRegisteredProjectScopedOperation()
    {
        var publicTools = new[]
        {
            "bind_project", "get_project_status", "browse_project_tree", "plc_read", "plc_write",
            "network_read", "network_write", "read_cross_references", "compile_check",
            "open_project", "create_project", "save_project", "save_project_as", "archive_project", "close_project"
        };
        var projectItems = PlcOperationCatalog.ReadOperationNames
            .Concat(PlcOperationCatalog.WriteOperationNames)
            .Concat(NetworkOperationCatalog.ReadOperationNames)
            .Concat(NetworkOperationCatalog.WriteOperationNames)
            .Where(name => name != "search_equipment_catalog");
        var advertised = ProjectCapabilityCatalog.Describe(ProjectContainerKinds.LocalSession)
            .Select(capability => capability.Operation).ToHashSet(StringComparer.Ordinal);

        Assert.Subset(publicTools.Concat(projectItems).ToHashSet(StringComparer.Ordinal), advertised);
        Assert.DoesNotContain("search_equipment_catalog", advertised); // independent hardware-catalog query
        Assert.DoesNotContain("probe_open_project_rebind", advertised);
        Assert.DoesNotContain("get_basic_project_status", advertised);
    }

    [Fact]
    public void LocalContentAndTerminalOperations_AreNotDelivered()
    {
        Assert.True(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.LocalSession, "bind_project"));
        Assert.True(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.LocalSession, "open_project"));
        Assert.True(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.LocalSession, "get_project_status"));
        Assert.False(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.LocalSession, "save_project"));
        Assert.False(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.LocalSession, "close_project"));
        Assert.False(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.LocalSession, "get_block_content"));
        var capabilities = ProjectCapabilityCatalog.Describe(ProjectContainerKinds.LocalSession)
            .ToDictionary(item => item.Operation, StringComparer.Ordinal);
        Assert.Equal(ProjectCapabilityApplicabilities.NotYetDelivered, capabilities["save_project"].Applicability);
        Assert.Equal(ProjectCapabilityApplicabilities.StandaloneOnly, capabilities["close_project"].Applicability);
        Assert.Equal(ProjectCapabilityApplicabilities.NotYetDelivered, capabilities["get_block_content"].Applicability);
    }

    [Theory]
    [InlineData("probe_network_object_attributes", ProjectContainerKinds.StandaloneProject, true)]
    [InlineData("probe_io_system_qualification", ProjectContainerKinds.StandaloneProject, true)]
    [InlineData("probe_subnet_lifecycle_mutations", ProjectContainerKinds.StandaloneProject, true)]
    [InlineData("probe_network_object_attributes", ProjectContainerKinds.LocalSession, false)]
    [InlineData("probe_io_system_qualification", ProjectContainerKinds.LocalSession, false)]
    [InlineData("probe_subnet_lifecycle_mutations", ProjectContainerKinds.LocalSession, false)]
    public void InternalNetworkProbe_CapabilityDecisionPreservesStandaloneOnlySupport(
        string method, string containerKind, bool supported)
    {
        // WithSession passes these unmapped method names to this production decision.
        Assert.Equal(supported, ProjectCapabilityCatalog.Supports(containerKind, method));
        Assert.DoesNotContain(ProjectCapabilityCatalog.Describe(containerKind), item => item.Operation == method);
        Assert.Equal(method != "probe_network_object_attributes",
            OperationPolicyCatalog.RequiresExpectedSessionIdentity(method));
        Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.Full, method));
        Assert.Equal(method == "probe_network_object_attributes",
            OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, method));
    }

    [Fact]
    public void RebindState_LocalOwnerNeverImpliesGenericClose()
    {
        var state = ProjectRebindStateInfo.Create(AmcPath, @"C:\Projects\Other.ap21", false, true, LocalContext(AlsPath));
        Assert.True(state.SourceOpenedByWorker);
        Assert.False(state.WillCloseSource);
        Assert.Equal(ProjectContainerKinds.LocalSession, state.SourceContext!.ContainerKind);
        Assert.Equal(AlsPath, state.SourceContext.SessionContainerPath);
    }

    [Fact]
    public void UnsupportedCapability_IsKnown()
        => Assert.True(WorkerFailureCategories.IsKnown("unsupported_capability"));
}
