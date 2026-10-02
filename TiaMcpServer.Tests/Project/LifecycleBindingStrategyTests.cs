using TiaMcpServer.Contracts;
using TiaMcpServer.ProjectLifecycle;
using TiaMcpServer.Safety;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.Worker;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Project;

/// <summary>Concrete lifecycle preparation over the existing worker-client source and lease rules.</summary>
[Collection(RealWorkerProcessCollection.Name)]
public sealed class LifecycleBindingStrategyTests
{
    [Theory]
    [InlineData(McpAccessMode.ReadWrite, "open_project")]
    [InlineData(McpAccessMode.ReadWrite, "create_project")]
    [InlineData(McpAccessMode.Full, "open_project")]
    [InlineData(McpAccessMode.Full, "create_project")]
    public async Task UnboundOpenAndCreate_PrepareTheExactUnboundSource(McpAccessMode mode, string operation)
    {
        var binding = new ProjectSessionBinding(null);
        using var client = Client(binding, mode);
        var before = client.BindingSnapshot;

        var prepared = await new LifecycleBindingStrategy(client).PrepareAsync(Call(Item(operation, Destination())));

        Assert.True(prepared.Success, prepared.Error?.Message);
        Assert.Equal(ProjectBindingSnapshot.UnboundState, prepared.Binding!.State);
        Assert.Null(prepared.Binding.ProjectPath);
        Assert.True(before.SameBinding(prepared.Binding));
        Assert.True(before.SameBinding(client.BindingSnapshot));
    }

    [Fact]
    public async Task VerifiedSameProjectOpen_PreparesTheOriginalRevision()
    {
        var source = Source();
        using var uiOpen = new FakeWorkerUiOpenProject(source);
        var binding = new ProjectSessionBinding(null);
        using var client = Client(binding);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, source);
        var before = client.BindingSnapshot;

        var prepared = await new LifecycleBindingStrategy(client).PrepareAsync(Call(Item("open_project", source)));

        Assert.True(prepared.Success, prepared.Error?.Message);
        Assert.True(before.SameBinding(prepared.Binding!));
        Assert.Equal(source, prepared.Binding!.ProjectPath);
    }

    [Fact]
    public async Task ConfiguredSource_IsGroundedAsSource_WithoutAdoptingDestination()
    {
        var source = Source();
        using var uiOpen = new FakeWorkerUiOpenProject(source);
        var destination = Destination();
        var binding = new ProjectSessionBinding(source);
        using var client = Client(binding);
        Assert.Equal(ProjectBindingSnapshot.ConfiguredUnverifiedState, client.BindingSnapshot.State);

        var prepared = await new LifecycleBindingStrategy(client).PrepareAsync(Call(Item("open_project", destination, force: true)));

        Assert.True(prepared.Success, prepared.Error?.Message);
        Assert.True(prepared.Binding!.IsVerified);
        Assert.Equal(source, prepared.Binding.ProjectPath);
        Assert.True(prepared.Binding.SameBinding(client.BindingSnapshot));
    }

    [Fact]
    public async Task InvalidatedSourceRecovery_PinsItsFreshVerifiedRevision()
    {
        var source = Source();
        using var uiOpen = new FakeWorkerUiOpenProject(source);
        var binding = new ProjectSessionBinding(null);
        using var client = Client(binding);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, source);
        binding.Invalidate("The connection was lost.");
        var invalidated = client.BindingSnapshot;

        var prepared = await new LifecycleBindingStrategy(client).PrepareAsync(Call(Item("open_project", Destination(), force: true)));

        Assert.True(prepared.Success, prepared.Error?.Message);
        Assert.True(prepared.Binding!.IsVerified);
        Assert.True(prepared.Binding.Revision > invalidated.Revision);
        Assert.Equal(source, prepared.Binding.ProjectPath);
        Assert.True(prepared.Binding.SameBinding(client.BindingSnapshot));
    }

    [Fact]
    public async Task DestinationSwitchWithoutForce_IsRefusedBeforeAnyRebinding()
    {
        var source = Source();
        using var uiOpen = new FakeWorkerUiOpenProject(source);
        var binding = new ProjectSessionBinding(null);
        using var client = Client(binding);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, source);
        var before = client.BindingSnapshot;

        var prepared = await new LifecycleBindingStrategy(client).PrepareAsync(Call(Item("open_project", Destination())));

        Assert.False(prepared.Success);
        Assert.Equal(WorkerFailureCategories.BindingConflict, prepared.Error!.Category);
        Assert.True(before.SameBinding(client.BindingSnapshot));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourcePreparation_PreservesBothWorkerOwnedAndExternallyOwnedProjects(bool externallyOwned)
    {
        var source = Source(externallyOwned ? "Source-ui-owned" : "Source");
        using var uiOpen = new FakeWorkerUiOpenProject(source);
        var destination = Destination();
        var binding = new ProjectSessionBinding(null);
        using var client = Client(binding);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, source);
        var before = client.BindingSnapshot;

        var prepared = await new LifecycleBindingStrategy(client).PrepareAsync(Call(Item("open_project", destination, force: true)));
        var probe = await client.ProbeOpenProjectRebindAsync(source, destination);
        Assert.True(prepared.Success, prepared.Error?.Message);
        Assert.True(probe.Success, probe.Error);
        var state = ProjectRebindStatePayloadContract.Decode(probe.Payload, source, destination);
        Assert.True(before.SameBinding(client.BindingSnapshot));
        Assert.Equal(source, state.SourceProjectPath);
        Assert.Equal(!externallyOwned, state.SourceOpenedByWorker);
        Assert.Equal(!externallyOwned, state.WillCloseSource);
    }

    [Fact]
    public async Task CreateWithActiveSource_PreparesSource_WithoutOpeningDestination()
    {
        var source = Source();
        using var uiOpen = new FakeWorkerUiOpenProject(source);
        var binding = new ProjectSessionBinding(null);
        using var client = Client(binding);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, source);
        var before = client.BindingSnapshot;

        var prepared = await new LifecycleBindingStrategy(client).PrepareAsync(Call(Item("create_project", Destination())));

        Assert.True(prepared.Success, prepared.Error?.Message);
        Assert.Equal(source, prepared.Binding!.ProjectPath);
        Assert.True(before.SameBinding(client.BindingSnapshot));
    }

    [Fact]
    public async Task PreparedRevisionBecomesStale_LeaseRefusesBeforeDispatch()
    {
        var source = Source();
        using var uiOpen = new FakeWorkerUiOpenProject(source);
        var binding = new ProjectSessionBinding(null);
        using var client = Client(binding);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, source);
        var prepared = await new LifecycleBindingStrategy(client).PrepareAsync(Call(Item("save_project", source)));
        binding.Invalidate("Another operation changed the session.");
        var invoked = false;

        var lease = await new OpennessWriteBindingGate(client).RunUnderLeaseAsync(prepared.Binding!, () =>
        {
            invoked = true;
            return Task.FromResult("mutated");
        });

        Assert.True(prepared.Success, prepared.Error?.Message);
        Assert.False(lease.Success);
        Assert.Equal(WorkerFailureCategories.BindingConflict, lease.Error!.Category);
        Assert.False(invoked);
    }

    [Theory]
    [InlineData(McpAccessMode.ReadOnly, "open_project")]
    [InlineData(McpAccessMode.ReadOnly, "create_project")]
    public async Task ReadOnly_CannotPrepareLifecycleOrGroundAConfiguredSource(McpAccessMode mode, string operation)
    {
        var binding = new ProjectSessionBinding(Source());
        using var client = Client(binding, mode);
        var before = client.BindingSnapshot;

        var prepared = await new LifecycleBindingStrategy(client).PrepareAsync(Call(Item(operation, Destination(), force: true)));

        Assert.False(prepared.Success);
        Assert.Equal(WorkerFailureCategories.AccessDenied, prepared.Error!.Category);
        Assert.True(before.SameBinding(client.BindingSnapshot));
        Assert.Equal(ProjectBindingSnapshot.ConfiguredUnverifiedState, client.BindingSnapshot.State);
    }

    private static OpennessWorkerClient Client(ProjectSessionBinding binding, McpAccessMode mode = McpAccessMode.Full)
        => new(binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate(), accessPolicy: new OperationAccessPolicy(mode));

    private static LifecycleWriteItem Item(string operation, string target, bool force = false) => new(operation)
    {
        ProjectPath = operation == "create_project" ? null : target,
        ForceRebind = force,
        ProjectDirectory = operation == "create_project" ? Path.GetDirectoryName(target) : null,
        ProjectName = operation == "create_project" ? Path.GetFileNameWithoutExtension(target) : null
    };

    private static WriteCall<LifecycleWriteItem> Call(LifecycleWriteItem item) => new(item.ProjectPath, new[] { item }, false);

    private static string Source(string name = "Source") => FixturePath(name);

    private static string Destination() => FixturePath("Destination");

    private static string FixturePath(string name)
        => Path.Combine(Path.GetTempPath(), "guarded-lifecycle-binding-" + Guid.NewGuid().ToString("N"), name + ".ap21");
}
