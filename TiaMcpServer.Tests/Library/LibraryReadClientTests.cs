using System.Text.Json;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Library;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Tests.Worker;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Library;

[Collection(RealWorkerProcessCollection.Name)]
public sealed class LibraryReadClientTests
{
    private const string Scenario = "library-read-roundtrip";
    private const string LocalSession = "C:/Projects/Local.amc21";
    private const string GlobalInspect = "library_global_inspect_library";
    private const string ProjectInspect = "library_project_inspect_library";

    private static LibraryQueryInfo Global() => new() { Scope = "global", LibraryName = "Fixture" };

    private static LibraryQueryInfo Project() => new() { Scope = "project" };

    [Theory]
    [InlineData(GlobalInspect)]
    [InlineData(ProjectInspect)]
    public async Task Unbound_BindingConflictNotSent(string method)
    {
        using var directory = new TempAuditDirectory();
        Directory.CreateDirectory(directory.Path);
        using var log = new FakeWorkerRequestLog(directory.Path);
        using var client = FakeWorkerClients.Create(McpAccessMode.ReadOnly, new ProjectSessionBinding(null));

        var result = await client.ReadLibraryAsync(method, method == GlobalInspect ? Global() : Project());

        Assert.False(result.Success);
        Assert.Equal(WorkerFailureCategories.BindingConflict, result.FailureCategory);
        Assert.Equal(WorkerDispatchState.NotSent, result.DispatchState);
        Assert.Empty(log.Methods());
    }

    [Fact]
    public async Task ConfiguredUnverifiedProject_GroundedFirst()
    {
        var source = ProjectPathNormalization.Canonicalize(Scenario)!;
        using var uiOpen = new FakeWorkerUiOpenProject(source, groundsStatus: true);
        using var directory = new TempAuditDirectory();
        Directory.CreateDirectory(directory.Path);
        using var log = new FakeWorkerRequestLog(directory.Path);
        using var client = FakeWorkerClients.Create(McpAccessMode.ReadOnly, new ProjectSessionBinding(source));
        Assert.Equal(ProjectBindingSnapshot.ConfiguredUnverifiedState, client.BindingSnapshot.State);

        var result = await client.ReadLibraryAsync(GlobalInspect, Global());

        Assert.True(result.Success, result.Error);
        Assert.True(client.BindingSnapshot.IsVerified);
        var methods = log.Methods();
        Assert.Equal(new[] { "hello", "get_project_status", GlobalInspect }, methods);
    }

    [Fact]
    public async Task GlobalRead_StandaloneBound_SendsExpectedIdentity()
    {
        using var directory = new TempAuditDirectory();
        Directory.CreateDirectory(directory.Path);
        using var log = new FakeWorkerRequestLog(directory.Path);
        using var client = await FakeWorkerClients.CreateBoundAsync(Scenario, McpAccessMode.ReadOnly);

        var result = await client.ReadLibraryAsync(GlobalInspect, Global());

        // The fake worker rejects a request whose expected session identity does not match its own.
        Assert.True(result.Success, result.Error);
        Assert.Equal(WorkerDispatchState.Sent, result.DispatchState);
        Assert.Equal(GlobalInspect, log.Methods()[^1]);
        Assert.Equal(client.BindingSnapshot.ProjectPath, result.SessionIdentity!.ProjectPath);
        using var payload = JsonDocument.Parse(result.Payload!);
        Assert.Equal("global", payload.RootElement.GetProperty("items")[0].GetProperty("identity").GetProperty("scope").GetString());
    }

    [Fact]
    public async Task GlobalRead_LocalSessionBound_Succeeds()
    {
        using var directory = new TempAuditDirectory();
        Directory.CreateDirectory(directory.Path);
        using var log = new FakeWorkerRequestLog(directory.Path);
        using var client = await FakeWorkerClients.CreateBoundAsync(LocalSession, McpAccessMode.ReadOnly);
        Assert.Equal(ProjectContainerKinds.LocalSession, client.BindingSnapshot.Context!.ContainerKind);

        var result = await client.ReadLibraryAsync("library_list_global_libraries", new LibraryQueryInfo { Offset = 0, Limit = 100 });

        Assert.True(result.Success, result.Error);
        Assert.Equal("library_list_global_libraries", log.Methods()[^1]);
    }

    [Fact]
    public async Task ProjectRead_LocalSession_UnsupportedCapabilityNotSent()
    {
        using var directory = new TempAuditDirectory();
        Directory.CreateDirectory(directory.Path);
        using var log = new FakeWorkerRequestLog(directory.Path);
        using var client = await FakeWorkerClients.CreateBoundAsync(LocalSession, McpAccessMode.ReadOnly);

        var result = await client.ReadLibraryAsync(ProjectInspect, Project());

        Assert.False(result.Success);
        Assert.Equal(WorkerFailureCategories.UnsupportedCapability, result.FailureCategory);
        Assert.Equal(WorkerDispatchState.NotSent, result.DispatchState);
        Assert.DoesNotContain(ProjectInspect, log.Methods());
        Assert.True(client.BindingSnapshot.IsVerified);
    }
}
