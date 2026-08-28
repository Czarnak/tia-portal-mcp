using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public sealed class NetworkVerifiedWriteFixtureTests
{
    [Fact]
    public async Task VerifyAsync_BindsTheExactIdentityReportedForTheTargetPath()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = CreateClient(binding);

        await NetworkVerifiedWriteFixture.VerifyAsync(
            client,
            binding,
            "network-roundtrip");

        var snapshot = binding.CaptureSnapshot();
        Assert.True(snapshot.IsVerified);
        Assert.Equal(
            ProjectPathNormalization.Canonicalize("network-roundtrip"),
            snapshot.ProjectPath);

        var followUp = await client.ReadHardwareConfigAsync("network-roundtrip");
        Assert.True(followUp.Success, followUp.Error);
        Assert.NotNull(followUp.SessionIdentity);
        Assert.Equal(snapshot.WorkerSessionId, followUp.SessionIdentity!.WorkerSessionId);
        Assert.Equal(snapshot.SessionGeneration, followUp.SessionIdentity.SessionGeneration);
        Assert.Equal(snapshot.PortalProcessId, followUp.SessionIdentity.PortalProcessId);
        Assert.Equal(snapshot.ProjectPath, followUp.SessionIdentity.ProjectPath);
    }

    [Fact]
    public async Task VerifyAsync_RejectsAWorkerReportedDifferentProjectWithoutBinding()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = CreateClient(binding);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NetworkVerifiedWriteFixture.VerifyAsync(
                client,
                binding,
                "network-binding-mismatch"));

        Assert.Contains("reported project", exception.Message, StringComparison.OrdinalIgnoreCase);
        var snapshot = binding.CaptureSnapshot();
        Assert.Equal(ProjectBindingSnapshot.UnboundState, snapshot.State);
        Assert.Null(snapshot.ProjectPath);
    }

    private static OpennessWorkerClient CreateClient(ProjectSessionBinding binding)
        => new(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate(),
            accessPolicy: new OperationAccessPolicy(McpAccessMode.ReadWrite));
}
