using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Tools;
using Xunit;

namespace TiaMcpServer.Tests.Multiuser;

[Collection("Mcp protocol serial")]
public sealed class LocalSessionBindingIntegrationTests
{
    private const string Amc = "C:/Projects/Local.amc21";

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
        var second = await harness.Client.CallToolAsync("bind_project", new Dictionary<string, object?>
        {
            ["projectPath"] = path, ["portalProcessId"] = 42
        });
        Assert.True(second.StructuredContent!.Value.GetProperty("success").GetBoolean(), second.StructuredContent.Value.GetRawText());
        var after = harness.WorkerClient.BindingSnapshot;
        Assert.Equal(before.BindingId, after.BindingId);
        Assert.Equal(before.Revision, after.Revision);
        Assert.False(after.Context!.OpenedByWorker);
        Assert.Null(after.Context.SessionContainerPath);
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
