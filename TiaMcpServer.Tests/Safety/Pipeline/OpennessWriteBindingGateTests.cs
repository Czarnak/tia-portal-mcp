using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Safety;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Safety.Pipeline;

/// <summary>
/// The binding gate over the real pinned project-binding lease, driven by the stateful
/// <c>network-subnet-lifecycle</c> FakeWorker scenario.
/// </summary>
[Collection(RealWorkerProcessCollection.Name)]
public sealed class OpennessWriteBindingGateTests
{
    private const string Scenario = "network-subnet-lifecycle";

    private static OpennessWorkerClient CreateClient(
        ProjectSessionBinding binding,
        OperationAccessPolicy? accessPolicy = null)
        => new(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate(),
            accessPolicy: accessPolicy);

    [Fact]
    public async Task VerifiedBinding_PassesTheGateAndRunsUnderTheCurrentSnapshot()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = CreateClient(binding);
        await NetworkVerifiedWriteFixture.VerifyAsync(client, binding, Scenario);
        var gate = new OpennessWriteBindingGate(client);

        var required = await gate.RequireVerifiedWriteBindingAsync(Scenario);
        var invoked = 0;
        var lease = await gate.RunUnderLeaseAsync(gate.CurrentBinding, async () =>
        {
            invoked++;
            var read = await client.ReadHardwareConfigAsync(Scenario);
            return read.Success ? "ran" : "read failed";
        });

        Assert.True(required.Success, required.Error);
        Assert.True(gate.CurrentBinding.IsVerified);
        Assert.True(lease.Success);
        Assert.Null(lease.Error);
        Assert.Equal("ran", lease.Value);
        Assert.Equal(1, invoked);
    }

    [Fact]
    public async Task SnapshotTakenBeforeBinding_IsRefusedWithoutRunningTheOperation()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = CreateClient(binding);
        var gate = new OpennessWriteBindingGate(client);
        var stale = gate.CurrentBinding;
        await NetworkVerifiedWriteFixture.VerifyAsync(client, binding, Scenario);

        var invoked = 0;
        var lease = await gate.RunUnderLeaseAsync(stale, () =>
        {
            invoked++;
            return Task.FromResult("ran");
        });

        Assert.False(lease.Success);
        Assert.Null(lease.Value);
        Assert.NotNull(lease.Error);
        Assert.Equal(WorkerFailureCategories.BindingConflict, lease.Error!.Category);
        Assert.False(string.IsNullOrWhiteSpace(lease.Error.Message));
        Assert.Equal(0, invoked);
    }

    [Fact]
    public async Task UnboundSession_FailsTheGateWithBindingConflict()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = CreateClient(binding);
        var gate = new OpennessWriteBindingGate(client);

        var required = await gate.RequireVerifiedWriteBindingAsync(Scenario);

        Assert.False(required.Success);
        Assert.Equal(WorkerFailureCategories.BindingConflict, required.FailureCategory);
    }

    [Fact]
    public async Task CancellationWhileWaitingForLease_DoesNotRunTheCancelledOperation()
    {
        using var client = CreateClient(new ProjectSessionBinding(null));
        var gate = new OpennessWriteBindingGate(client);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holder = gate.RunUnderLeaseAsync(gate.CurrentBinding, async () =>
        {
            entered.SetResult();
            await release.Task;
            return "holder";
        });
        await entered.Task;
        using var cancellation = new CancellationTokenSource();
        var invoked = false;
        var waiting = gate.RunUnderLeaseAsync(gate.CurrentBinding, () =>
        {
            invoked = true;
            return Task.FromResult("cancelled");
        }, cancellation.Token);
        cancellation.Cancel();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.False(invoked);
        }
        finally
        {
            release.SetResult();
            await holder;
        }
    }

    [Fact]
    public void AccessMode_IsReadWriteWithoutPolicyAndFollowsThePolicyOtherwise()
    {
        using var unrestricted = CreateClient(new ProjectSessionBinding(null));
        using var readOnly = CreateClient(
            new ProjectSessionBinding(null),
            new OperationAccessPolicy(McpAccessMode.ReadOnly));

        Assert.Equal(McpAccessMode.ReadWrite, new OpennessWriteBindingGate(unrestricted).AccessMode);
        Assert.Equal(McpAccessMode.ReadOnly, new OpennessWriteBindingGate(readOnly).AccessMode);
    }
}
