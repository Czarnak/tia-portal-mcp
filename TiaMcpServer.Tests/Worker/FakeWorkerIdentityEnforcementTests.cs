using System.Text.Json;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

[Collection(RealWorkerProcessCollection.Name)]
public sealed class FakeWorkerIdentityEnforcementTests
{
    [Fact]
    public async Task OrdinaryReadCannotSelectPortalProject()
    {
        const string source = @"C:\Fixture\guarded-lifecycle-source.ap21";
        const string target = @"C:\Fixture\Target.ap21";
        using var ui = new FakeWorkerUiOpenProject(source);
        using var portals = new FakeWorkerPortals(new(200, source), new(900, target));
        using var transport = new PersistentWorkerTransport(FakeWorkerLocator.Locate(),
            TimeSpan.FromSeconds(5), workerArgs: "--access-mode read-only");
        var before = await transport.SendAsync(new WorkerRequest { Method = "get_project_status" });
        Assert.True(before.Success, before.Error);
        var rejected = await transport.SendAsync(new WorkerRequest { Method = "get_project_status", ProjectPath = target });
        Assert.False(rejected.Success);
        Assert.Equal(WorkerFailureCategories.BindingConflict, rejected.FailureCategory);
        var after = await transport.SendAsync(new WorkerRequest { Method = "get_project_status" });
        Assert.True(after.Success, after.Error);
        Assert.Equal(before.SessionIdentity!.SessionGeneration, after.SessionIdentity!.SessionGeneration);
        Assert.Equal(200, after.SessionIdentity.PortalProcessId);
        Assert.Equal(source, after.SessionIdentity.ProjectPath);
    }

    [Fact]
    public async Task CannedStatusWithoutUiOpenProject_ReportsClosedWithoutRequestDerivedIdentity()
    {
        using var uiOpen = new FakeWorkerUiOpenProject(null);
        using var transport = CreateTransport();

        var response = await transport.SendAsync(new WorkerRequest
        {
            Method = "get_project_status", ProjectPath = "ok"
        });

        Assert.True(response.Success, response.Error);
        using var payload = JsonDocument.Parse(response.Payload!);
        Assert.False(payload.RootElement.GetProperty("project").GetProperty("isOpen").GetBoolean());
        Assert.Null(response.SessionIdentity?.ProjectPath);
    }

    [Fact]
    public async Task ProtectedCannedRead_RejectsForgedProjectIdentityWhenNothingIsOpen()
    {
        using var uiOpen = new FakeWorkerUiOpenProject(null);
        using var transport = CreateTransport();
        var requestedPath = Path.GetFullPath("ok");
        var status = await transport.SendAsync(new WorkerRequest { Method = "get_basic_project_status" });
        Assert.True(status.Success, status.Error);
        var empty = Assert.IsType<WorkerSessionIdentity>(status.SessionIdentity);

        var response = await transport.SendAsync(new WorkerRequest
        {
            Method = "read_hardware_config", ProjectPath = requestedPath,
            ExpectedSessionIdentity = new WorkerSessionIdentity
            {
                WorkerSessionId = empty.WorkerSessionId,
                SessionGeneration = empty.SessionGeneration,
                PortalProcessId = empty.PortalProcessId,
                ProjectPath = requestedPath
            }
        });

        Assert.False(response.Success);
        Assert.Equal(WorkerFailureCategories.BindingConflict, response.FailureCategory);
        Assert.Null(response.SessionIdentity?.ProjectPath);
    }

    [Fact]
    public async Task RebindProbe_WithNoOpenSource_ReturnsEmptySourceState()
    {
        const string source = "guarded-lifecycle-no-source";
        const string destination = "C:\\Fixture\\Destination.ap21";
        using var transport = CreateTransport();
        var status = await transport.SendAsync(new WorkerRequest
        {
            Method = "get_basic_project_status", ProjectPath = source
        });
        Assert.True(status.Success);
        Assert.NotNull(status.SessionIdentity);

        var probe = await transport.SendAsync(new WorkerRequest
        {
            Method = "probe_open_project_rebind",
            ProjectPath = source,
            RebindDestinationProjectPath = destination,
            ExpectedSessionIdentity = status.SessionIdentity
        });

        Assert.True(probe.Success, probe.Error);
        using var document = JsonDocument.Parse(probe.Payload!);
        var state = document.RootElement;
        Assert.Equal(JsonValueKind.Null, state.GetProperty("sourceProjectPath").ValueKind);
        Assert.Equal(JsonValueKind.Null, state.GetProperty("sourceIsModified").ValueKind);
        Assert.False(state.GetProperty("sourceOpenedByWorker").GetBoolean());
        Assert.False(state.GetProperty("willCloseSource").GetBoolean());
    }

    [Fact]
    public async Task ContentReadsWithoutExpectedIdentityFailBeforeScenarioDispatch()
    {
        using var transport = CreateTransport();
        var observed = await PrimeAsync(transport);
        var reads = OperationPolicyCatalog.AllOperationNames.Where(operation =>
            OperationPolicyCatalog.GetCapability(operation) is
                OperationCapability.Observe or OperationCapability.TemporaryExport)
            .Except(new[]
            {
                "get_project_status", "list_tia_portal_processes", "list_server_connections",
                "list_server_groups", "list_server_projects", "list_local_sessions", "get_lock_state"
            });

        foreach (var method in reads)
        {
            var rejected = await transport.SendAsync(new WorkerRequest
            {
                Method = method, ProjectPath = "network-roundtrip"
            });
            Assert.False(rejected.Success, method);
            Assert.Equal(WorkerFailureCategories.BindingConflict, rejected.FailureCategory);
            Assert.Contains("requires expected worker/Portal/project session identity", rejected.Error);
            Assert.Null(rejected.SessionIdentity);
        }

        foreach (var method in new[] { "read_hardware_config", "search_equipment_catalog" })
        {
            var accepted = await transport.SendAsync(new WorkerRequest
            {
                Method = method, ProjectPath = "network-roundtrip", ExpectedSessionIdentity = observed
            });
            Assert.True(accepted.Success, accepted.Error);
            Assert.Equal(observed.WorkerSessionId, accepted.SessionIdentity!.WorkerSessionId);
            Assert.Equal(observed.SessionGeneration, accepted.SessionIdentity.SessionGeneration);
            Assert.Equal(observed.PortalProcessId, accepted.SessionIdentity.PortalProcessId);
            Assert.Equal(observed.ProjectPath, accepted.SessionIdentity.ProjectPath);
        }
    }

    [Theory]
    [InlineData("read_hardware_config")]
    [InlineData("get_block_content")]
    [InlineData("search_equipment_catalog")]
    [InlineData("hmi_list_tags")]
    [InlineData("vci_list_workspaces")]
    [InlineData("read_hardware_page_candidates")]
    [InlineData("get_project_status")]
    [InlineData("get_basic_project_status")]
    [InlineData("list_tia_portal_processes")]
    [InlineData("list_server_connections")]
    [InlineData("list_server_groups")]
    [InlineData("list_server_projects")]
    [InlineData("list_local_sessions")]
    [InlineData("get_lock_state")]
    [InlineData("select_portal_project")]
    [InlineData("open_project")]
    [InlineData("create_project")]
    public async Task SuppliedIdentityIsValidatedBeforeDispatchEvenForOptionalMethods(string method)
    {
        using var transport = CreateTransport();
        var observed = await PrimeAsync(transport);
        foreach (var field in new[] { "workerSessionId", "sessionGeneration", "portalProcessId", "projectPath" })
        {
            var response = await transport.SendAsync(new WorkerRequest
            {
                Method = method, ProjectPath = "network-roundtrip", ExpectedSessionIdentity = Change(observed, field)
            });
            Assert.False(response.Success);
            Assert.Equal(WorkerFailureCategories.BindingConflict, response.FailureCategory);
            Assert.Null(response.SessionIdentity);
        }
    }

    [Fact]
    public async Task ProtectedRequestWithoutExpectedIdentityFailsBeforeScenarioDispatch()
    {
        using var transport = CreateTransport();
        await PrimeAsync(transport);

        var response = await transport.SendAsync(new WorkerRequest
        {
            Method = "probe_project_status_for_lifecycle",
            ProjectPath = "network-roundtrip"
        });

        Assert.False(response.Success);
        Assert.Equal(WorkerFailureCategories.BindingConflict, response.FailureCategory);
    }

    [Theory]
    [InlineData("workerSessionId")]
    [InlineData("sessionGeneration")]
    [InlineData("portalProcessId")]
    [InlineData("projectPath")]
    public async Task ProtectedRequestRejectsEveryMismatchedIdentityField(string field)
    {
        using var transport = CreateTransport();
        var observed = await PrimeAsync(transport);

        var response = await transport.SendAsync(new WorkerRequest
        {
            Method = "probe_project_status_for_lifecycle",
            ProjectPath = "network-roundtrip",
            ExpectedSessionIdentity = Change(observed, field)
        });

        Assert.False(response.Success);
        Assert.Equal(WorkerFailureCategories.BindingConflict, response.FailureCategory);
    }

    [Fact]
    public async Task ProtectedRequestRejectsARequestPathOutsideTheExpectedProject()
    {
        using var transport = CreateTransport();
        var observed = await PrimeAsync(transport);

        var response = await transport.SendAsync(new WorkerRequest
        {
            Method = "probe_project_status_for_lifecycle",
            ProjectPath = "network-roundtrip-other",
            ExpectedSessionIdentity = observed
        });

        Assert.False(response.Success);
        Assert.Equal(WorkerFailureCategories.BindingConflict, response.FailureCategory);
    }

    [Fact]
    public async Task OptionalObserveRequestStillRejectsASuppliedMismatchedIdentity()
    {
        using var transport = CreateTransport();
        var observed = await PrimeAsync(transport);

        var response = await transport.SendAsync(new WorkerRequest
        {
            Method = "get_project_status",
            ProjectPath = "network-roundtrip",
            ExpectedSessionIdentity = Change(observed, "workerSessionId")
        });

        Assert.False(response.Success);
        Assert.Equal(WorkerFailureCategories.BindingConflict, response.FailureCategory);
    }

    [Fact]
    public async Task GetTypeContent_SuppliedMismatchedExpectedIdentity_ReturnsBindingConflict()
    {
        using var transport = CreateTransport();
        var observed = await PrimeAsync(transport);

        var response = await transport.SendAsync(new WorkerRequest
        {
            Method = "get_type_content",
            ProjectPath = "network-roundtrip",
            TypePath = "PLC_1/Types/AnalogInputSettings",
            ExpectedSessionIdentity = Change(observed, "workerSessionId")
        });

        Assert.False(response.Success);
        Assert.Equal(WorkerFailureCategories.BindingConflict, response.FailureCategory);
        Assert.Contains("does not match the FakeWorker session", response.Error);
        Assert.Null(response.SessionIdentity);
    }

    [Fact]
    public async Task RejectedRequestDoesNotStampResponseOrMutateTheFakeWorkerSession()
    {
        using var transport = CreateTransport();
        var observed = await PrimeAsync(transport);

        var rejected = await transport.SendAsync(new WorkerRequest
        {
            Method = "probe_project_status_for_lifecycle",
            ProjectPath = "network-roundtrip-other",
            ExpectedSessionIdentity = observed
        });

        Assert.False(rejected.Success);
        Assert.Equal(WorkerFailureCategories.BindingConflict, rejected.FailureCategory);
        Assert.Null(rejected.SessionIdentity);

        var afterRejection = await transport.SendAsync(new WorkerRequest
        {
            Method = "read_hardware_config",
            ProjectPath = "network-roundtrip",
            ExpectedSessionIdentity = observed
        });

        Assert.True(afterRejection.Success, afterRejection.Error);
        var identityAfterRejection = Assert.IsType<WorkerSessionIdentity>(
            afterRejection.SessionIdentity);
        Assert.Equal(observed.WorkerSessionId, identityAfterRejection.WorkerSessionId);
        Assert.Equal(observed.SessionGeneration, identityAfterRejection.SessionGeneration);
        Assert.Equal(observed.PortalProcessId, identityAfterRejection.PortalProcessId);
        Assert.Equal(observed.ProjectPath, identityAfterRejection.ProjectPath);
    }

    private static PersistentWorkerTransport CreateTransport()
        => new(FakeWorkerLocator.Locate(), TimeSpan.FromSeconds(5));

    private static async Task<WorkerSessionIdentity> PrimeAsync(
        PersistentWorkerTransport transport)
    {
        using var uiOpen = new FakeWorkerUiOpenProject(
            Path.Combine(Path.GetDirectoryName(FakeWorkerLocator.Locate())!, "network-roundtrip"),
            groundsStatus: true);
        var response = await transport.SendAsync(new WorkerRequest
        {
            Method = "get_project_status",
            ProjectPath = "network-roundtrip"
        });

        Assert.True(response.Success, response.Error);
        return Assert.IsType<WorkerSessionIdentity>(response.SessionIdentity);
    }

    private static WorkerSessionIdentity Change(
        WorkerSessionIdentity source,
        string field)
        => new()
        {
            WorkerSessionId = field == "workerSessionId"
                ? source.WorkerSessionId + "-different"
                : source.WorkerSessionId,
            SessionGeneration = field == "sessionGeneration"
                ? source.SessionGeneration + 1
                : source.SessionGeneration,
            PortalProcessId = field == "portalProcessId"
                ? source.PortalProcessId + 1
                : source.PortalProcessId,
            ProjectPath = field == "projectPath"
                ? source.ProjectPath + ".different"
                : source.ProjectPath
        };
}
