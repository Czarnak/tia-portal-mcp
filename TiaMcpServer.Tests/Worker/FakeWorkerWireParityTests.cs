using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

/// <summary>
/// The FakeWorker must render payload contracts exactly as the real worker does. Before
/// WorkerJson, its DTO fixtures wrote explicit nulls where production omits them, so IPC tests
/// exercised a wire shape production never sends.
/// </summary>
[Collection("Mcp protocol serial")]
public sealed class FakeWorkerWireParityTests
{
    [Fact]
    public async Task FakeWorker_OmitsNullMembersOfABatchPayloadLikeTheRealWorker()
    {
        const string scenario = "tag-update-snapshot-unavailable-visible";
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate());
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, scenario);

        var result = await client.ReadUpdateTagSafetySnapshotAsync("PLC_1", "Inputs", folderPath: null, "Start", scenario);

        Assert.True(result.Success, result.Error);
        Assert.True(WorkerJson.OmitsNullMembers(typeof(UpdateTagSafetySnapshotInfo)));
        Assert.DoesNotContain("\"externalVisible\"", result.Payload, StringComparison.Ordinal);
    }
}
