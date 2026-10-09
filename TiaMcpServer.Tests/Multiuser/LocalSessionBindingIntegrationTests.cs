using System.Text.Json;
using TiaMcpServer.Contracts.Multiuser;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Tools;
using Xunit;

namespace TiaMcpServer.Tests.Multiuser;

[Collection("Mcp protocol serial")]
public sealed class LocalSessionBindingIntegrationTests
{
    private const string Amc = "C:/Projects/Local.amc21";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VerifiedSource_ForeignPidWithoutPathRequiresForceAndSelectsSoleOwner(bool force)
    {
        const string source = "C:/Projects/A.ap21";
        using var portals = new FakeWorkerPortals(
            new FakeWorkerPortals.Entry(41, source), new FakeWorkerPortals.Entry(42, Amc));
        using var ui = new FakeWorkerUiOpenProject(source);
        using var directory = new TempAuditDirectory();
        Directory.CreateDirectory(directory.Path);
        using var log = new FakeWorkerRequestLog(directory.Path);
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectBindingTools>(accessMode: McpAccessMode.ReadOnly);
        var first = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?> { ["projectPath"] = source });
        Assert.True(first.StructuredContent!.Value.GetProperty("success").GetBoolean());
        var before = harness.WorkerClient.BindingSnapshot;
        var selections = log.Methods().Count(method => method == "select_portal_project");

        var result = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?>
        {
            ["portalProcessId"] = 42, ["forceRebind"] = force
        });
        var document = result.StructuredContent!.Value;
        Assert.Equal(force, document.GetProperty("success").GetBoolean());
        if (force)
        {
            Assert.Equal("switched", document.GetProperty("result").GetProperty("value").GetProperty("transition").GetString());
            Assert.Equal(ProjectPathNormalization.Canonicalize(Amc), harness.WorkerClient.BindingSnapshot.ProjectPath);
            Assert.Equal(42, harness.WorkerClient.BindingSnapshot.PortalProcessId);
            Assert.False(before.SameBinding(harness.WorkerClient.BindingSnapshot));
        }
        else
        {
            Assert.True(before.SameBinding(harness.WorkerClient.BindingSnapshot));
            Assert.Equal(selections, log.Methods().Count(method => method == "select_portal_project"));
        }
        Assert.DoesNotContain("open_project", log.Methods());
    }

    [Theory]
    [InlineData(McpAccessMode.ReadOnly)]
    [InlineData(McpAccessMode.ReadWrite)]
    [InlineData(McpAccessMode.Full)]
    public async Task AllModes_AdoptAlreadyOpenAmcWithoutOpenDispatch(McpAccessMode mode)
    {
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, Amc));
        using var ui = new FakeWorkerUiOpenProject(Amc);
        using var directory = new TempAuditDirectory();
        Directory.CreateDirectory(directory.Path);
        using var log = new FakeWorkerRequestLog(directory.Path);
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectBindingTools>(accessMode: mode);

        var response = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?>
        {
            ["projectPath"] = Amc
        });

        var document = response.StructuredContent!.Value;
        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        var value = document.GetProperty("result").GetProperty("value");
        Assert.Equal("verified", value.GetProperty("binding").GetProperty("state").GetString());
        Assert.Equal(ProjectPathNormalization.Canonicalize(Amc), value.GetProperty("binding").GetProperty("projectPath").GetString());
        var context = value.GetProperty("binding").GetProperty("context");
        Assert.Equal(ProjectContainerKinds.LocalSession, context.GetProperty("containerKind").GetString());
        Assert.Equal(JsonValueKind.Null, context.GetProperty("sessionContainerPath").ValueKind);
        Assert.DoesNotContain("open_project", log.Methods());
    }

    [Fact]
    public async Task ExactPid_NoPath_SelectsSoleTypedOwnerWhenProcessMetadataOmitsPath()
    {
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, null));
        using var ui = new FakeWorkerUiOpenProject(Amc);
        using var directory = new TempAuditDirectory();
        Directory.CreateDirectory(directory.Path);
        using var log = new FakeWorkerRequestLog(directory.Path);
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectBindingTools>(accessMode: McpAccessMode.ReadOnly);

        var response = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?>
        {
            ["portalProcessId"] = 42
        });

        var document = response.StructuredContent!.Value;
        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        var binding = document.GetProperty("result").GetProperty("value").GetProperty("binding");
        Assert.Equal(42, binding.GetProperty("portalProcessId").GetInt32());
        Assert.Equal(ProjectPathNormalization.Canonicalize(Amc), binding.GetProperty("projectPath").GetString());
        Assert.DoesNotContain("open_project", log.Methods());
    }

    [Fact]
    public async Task NoSelector_TwoUnadvertisedPortalsAreAmbiguousBeforeSelection()
    {
        using var portals = new FakeWorkerPortals(
            new FakeWorkerPortals.Entry(41, null), new FakeWorkerPortals.Entry(42, null));
        using var ui = new FakeWorkerUiOpenProject(null);
        using var directory = new TempAuditDirectory();
        Directory.CreateDirectory(directory.Path);
        using var log = new FakeWorkerRequestLog(directory.Path);
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectBindingTools>(
            accessMode: McpAccessMode.ReadOnly);

        var response = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?>());
        var document = response.StructuredContent!.Value;
        Assert.False(document.GetProperty("success").GetBoolean());
        Assert.Equal(WorkerFailureCategories.TargetAmbiguous,
            document.GetProperty("result").GetProperty("failure").GetProperty("category").GetString());
        Assert.Equal(1, log.Methods().Count(method => method == "list_tia_portal_processes"));
        Assert.DoesNotContain("select_portal_project", log.Methods());
        Assert.False(harness.WorkerClient.BindingSnapshot.IsVerified);
    }

    [Fact]
    public async Task MissingExplicitPid_TwoOtherPortalsReturnNotFoundBeforeSelection()
    {
        using var portals = new FakeWorkerPortals(
            new FakeWorkerPortals.Entry(41, null), new FakeWorkerPortals.Entry(42, null));
        using var ui = new FakeWorkerUiOpenProject(null);
        using var directory = new TempAuditDirectory();
        Directory.CreateDirectory(directory.Path);
        using var log = new FakeWorkerRequestLog(directory.Path);
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectBindingTools>(
            accessMode: McpAccessMode.ReadOnly);

        var response = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?>
        {
            ["portalProcessId"] = 999
        });
        var document = response.StructuredContent!.Value;
        Assert.False(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.Equal(WorkerFailureCategories.TargetNotFound,
            document.GetProperty("result").GetProperty("failure").GetProperty("category").GetString());
        Assert.Equal(1, log.Methods().Count(method => method == "list_tia_portal_processes"));
        Assert.DoesNotContain("select_portal_project", log.Methods());
        Assert.False(harness.WorkerClient.BindingSnapshot.IsVerified);
    }

    [Theory]
    [InlineData(false, WorkerFailureCategories.TargetNotFound)]
    [InlineData(true, WorkerFailureCategories.TargetAmbiguous)]
    public async Task ExactAmcSelector_RejectsMissingOrAmbiguousOwnerWithoutAdoption(
        bool duplicateOwner, string expectedCategory)
    {
        var advertised = duplicateOwner ? Amc : "C:/Projects/Other.amc21";
        using var portals = new FakeWorkerPortals(
            new FakeWorkerPortals.Entry(42, advertised),
            new FakeWorkerPortals.Entry(43, duplicateOwner ? Amc : "C:/Projects/Third.amc21"));
        using var ui = new FakeWorkerUiOpenProject(advertised);
        using var directory = new TempAuditDirectory();
        Directory.CreateDirectory(directory.Path);
        using var log = new FakeWorkerRequestLog(directory.Path);
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectBindingTools>(
            accessMode: McpAccessMode.ReadOnly);

        var result = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?>
        {
            ["projectPath"] = Amc
        });

        var document = result.StructuredContent!.Value;
        Assert.False(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.Equal(expectedCategory,
            document.GetProperty("result").GetProperty("failure").GetProperty("category").GetString());
        Assert.False(harness.WorkerClient.BindingSnapshot.IsVerified);
        Assert.DoesNotContain("open_project", log.Methods());
    }

    [Theory]
    [InlineData("worker")]
    [InlineData("portal")]
    [InlineData("generation")]
    public void TypedLocalOwner_RefreshPreservesEpochButReplacementRotates(string replacement)
    {
        var binding = new ProjectSessionBinding(null);
        var first = LocalIdentity("worker-a", 42, 1, "plc_read");
        Assert.True(binding.BindVerified(first, forceRebind: false, out var error), error);
        var before = binding.CaptureSnapshot();
        var refreshed = LocalIdentity("worker-a", 42, 1, "browse_project_tree");

        Assert.True(binding.BindVerified(refreshed, forceRebind: false, out error), error);
        var afterRefresh = binding.CaptureSnapshot();
        Assert.Equal(before.BindingId, afterRefresh.BindingId);
        Assert.Equal(before.Revision, afterRefresh.Revision);
        Assert.Equal("browse_project_tree", Assert.Single(afterRefresh.Context!.Capabilities).Operation);

        var changed = LocalIdentity(
            replacement == "worker" ? "worker-b" : "worker-a",
            replacement == "portal" ? 43 : 42,
            replacement == "generation" ? 2 : 1,
            "browse_project_tree");
        Assert.True(binding.BindVerified(changed, forceRebind: true, out error), error);
        var afterReplacement = binding.CaptureSnapshot();
        Assert.NotEqual(before.BindingId, afterReplacement.BindingId);
        Assert.True(afterReplacement.Revision > before.Revision);
        Assert.Equal(changed.WorkerSessionId, afterReplacement.WorkerSessionId);
        Assert.Equal(changed.PortalProcessId, afterReplacement.PortalProcessId);
        Assert.Equal(changed.SessionGeneration, afterReplacement.SessionGeneration);
    }

    private static WorkerSessionIdentity LocalIdentity(string worker, int portal, long generation, string capability)
        => new()
        {
            WorkerSessionId = worker,
            PortalProcessId = portal,
            SessionGeneration = generation,
            ProjectPath = Amc,
            Context = new ProjectContextInfo
            {
                ContainerKind = ProjectContainerKinds.LocalSession,
                SessionMode = MultiuserSessionModes.Unknown,
                EngineeringProjectPath = Amc,
                Capabilities = [new ProjectCapabilityInfo { Operation = capability, Applicability = "supported" }]
            }
        };

    [Theory]
    [InlineData("C:/Projects/Próba Line.AMC21")]
    [InlineData("C:/Projects/Local.amc21")]
    public async Task ConfiguredAmcAssertion_ReverifyPreservesBindingAndNeverOpens(string path)
    {
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, path));
        using var ui = new FakeWorkerUiOpenProject(path);
        using var directory = new TempAuditDirectory();
        Directory.CreateDirectory(directory.Path);
        using var log = new FakeWorkerRequestLog(directory.Path);
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectBindingTools>(
            startupProjectPath: path, accessMode: McpAccessMode.ReadOnly,
            skipHardwarePreverification: true);

        var first = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?>());
        Assert.True(first.StructuredContent!.Value.GetProperty("success").GetBoolean(), first.StructuredContent.Value.GetRawText());
        var before = harness.WorkerClient.BindingSnapshot;
        var observedBefore = before.Context!.ConnectionObservation!.ObservedAt;
        await Task.Delay(20);
        var second = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?>
        {
            ["projectPath"] = path, ["portalProcessId"] = 42
        });
        Assert.True(second.StructuredContent!.Value.GetProperty("success").GetBoolean(), second.StructuredContent.Value.GetRawText());
        var after = harness.WorkerClient.BindingSnapshot;
        Assert.Equal(before.BindingId, after.BindingId);
        Assert.Equal(before.Revision, after.Revision);
        Assert.True(after.Context!.ConnectionObservation!.ObservedAt > observedBefore);
        Assert.False(after.Context!.OpenedByWorker);
        Assert.Null(after.Context.SessionContainerPath);
        Assert.DoesNotContain("open_project", log.Methods());
    }

    [Fact]
    public async Task SameAmcReplacementDuringStatus_InvalidatesVerifiedBindingWithoutOpening()
    {
        const string path = "C:/Projects/local-owner-replaced.amc21";
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, path));
        using var ui = new FakeWorkerUiOpenProject(path);
        using var directory = new TempAuditDirectory();
        Directory.CreateDirectory(directory.Path);
        using var log = new FakeWorkerRequestLog(directory.Path);
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectBindingTools>(
            accessMode: McpAccessMode.ReadOnly);
        var bound = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?>
        {
            ["projectPath"] = path
        });
        Assert.True(bound.StructuredContent!.Value.GetProperty("success").GetBoolean(), bound.StructuredContent.Value.GetRawText());
        var before = harness.WorkerClient.BindingSnapshot;

        var replaced = await harness.WorkerClient.GetProjectStatusAsync(path);

        Assert.False(replaced.Success);
        Assert.Equal(WorkerFailureCategories.BindingConflict, replaced.FailureCategory);
        var after = harness.WorkerClient.BindingSnapshot;
        Assert.Equal(ProjectBindingSnapshot.InvalidatedState, after.State);
        Assert.NotEqual(before.BindingId, after.BindingId);
        Assert.Equal(ProjectPathNormalization.Canonicalize(path), before.ProjectPath);
        Assert.DoesNotContain("open_project", log.Methods());
    }

    [Fact]
    public async Task AlsAssertion_CannotAdoptColdAmcOwner()
    {
        const string als = "C:/Projects/Local.als21";
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, Amc));
        using var ui = new FakeWorkerUiOpenProject(Amc);
        using var directory = new TempAuditDirectory();
        Directory.CreateDirectory(directory.Path);
        using var log = new FakeWorkerRequestLog(directory.Path);
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectBindingTools>(
            startupProjectPath: als, accessMode: McpAccessMode.ReadOnly,
            skipHardwarePreverification: true);

        var result = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?>());
        var document = result.StructuredContent!.Value;
        Assert.False(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.Contains(WorkerFailureCategories.ValidationError, document.GetRawText());
        Assert.DoesNotContain("open_project", log.Methods());
        Assert.False(harness.WorkerClient.BindingSnapshot.IsVerified);
    }

    [Fact]
    public async Task DifferentPidRequiresForceAndMismatchPreservesVerifiedSource()
    {
        using var portals = new FakeWorkerPortals(
            new FakeWorkerPortals.Entry(42, Amc),
            new FakeWorkerPortals.Entry(43, "C:/Projects/Other.amc21"));
        using var ui = new FakeWorkerUiOpenProject(Amc);
        using var directory = new TempAuditDirectory();
        Directory.CreateDirectory(directory.Path);
        using var log = new FakeWorkerRequestLog(directory.Path);
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectBindingTools>(accessMode: McpAccessMode.ReadOnly);
        var bound = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?>
        {
            ["projectPath"] = Amc, ["portalProcessId"] = 42
        });
        Assert.True(bound.StructuredContent!.Value.GetProperty("success").GetBoolean(), bound.StructuredContent.Value.GetRawText());
        var before = harness.WorkerClient.BindingSnapshot;
        var selectionsBefore = log.Methods().Count(method => method == "select_portal_project");

        var noForce = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?>
        {
            ["projectPath"] = Amc, ["portalProcessId"] = 43
        });
        Assert.Contains(WorkerFailureCategories.BindingConflict, noForce.StructuredContent!.Value.GetRawText());
        Assert.Equal(selectionsBefore, log.Methods().Count(method => method == "select_portal_project"));
        Assert.Equal(before.BindingId, harness.WorkerClient.BindingSnapshot.BindingId);

        var mismatch = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?>
        {
            ["projectPath"] = Amc, ["portalProcessId"] = 43, ["forceRebind"] = true
        });
        Assert.Contains(WorkerFailureCategories.TargetNotFound, mismatch.StructuredContent!.Value.GetRawText());
        Assert.Equal(before.BindingId, harness.WorkerClient.BindingSnapshot.BindingId);
        Assert.Equal(before.Revision, harness.WorkerClient.BindingSnapshot.Revision);
        Assert.DoesNotContain("open_project", log.Methods());
    }

    [Theory]
    [InlineData("local-envelope-secret", "PRIVATE_IDENTITY_CONTEXT_MEMBER", "PRIVATE_IDENTITY_CONTEXT_VALUE")]
    [InlineData("local-envelope-missing-null", "sessionContainerPath", "PRIVATE_IDENTITY_CONTEXT_VALUE")]
    [InlineData("local-envelope-invalid-mode", "invalid_session_mode", "PRIVATE_IDENTITY_CONTEXT_VALUE")]
    [InlineData("local-envelope-mismatched-path", "PRIVATE_OTHER_ENGINEERING_PATH", "PRIVATE_IDENTITY_CONTEXT_VALUE")]
    [InlineData("local-envelope-forged-remote", "PRIVATE_FORGED_SERVER_ALIAS", "PRIVATE_IDENTITY_CONTEXT_VALUE")]
    [InlineData("local-status-secret", "PRIVATE_STATUS_CONTEXT_MEMBER", "PRIVATE_STATUS_CONTEXT_VALUE")]
    public async Task MalformedLocalContext_IsSanitizedProtocolError(string scenario, string member, string value)
    {
        var path = $"C:/Projects/{scenario}.amc21";
        using var portals = new FakeWorkerPortals(new FakeWorkerPortals.Entry(42, path));
        using var ui = new FakeWorkerUiOpenProject(path);
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectBindingTools>(accessMode: McpAccessMode.ReadOnly);

        var response = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?>
        {
            ["projectPath"] = path
        });
        var text = response.StructuredContent!.Value.GetRawText();
        Assert.False(response.StructuredContent.Value.GetProperty("success").GetBoolean(), text);
        Assert.Contains(WorkerFailureCategories.ProtocolError, text);
        Assert.DoesNotContain(member, text, StringComparison.Ordinal);
        Assert.DoesNotContain(value, text, StringComparison.Ordinal);
    }
}
