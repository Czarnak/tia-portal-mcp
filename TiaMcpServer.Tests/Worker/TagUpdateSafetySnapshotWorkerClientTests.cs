using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Tests.Worker;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

public sealed class TagUpdateSafetySnapshotWorkerClientTests
{
    [Fact]
    public async Task BoundSnapshotRead_RequestJsonStillCarriesExpectedSessionIdentity()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate());
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, "echo");

        var call = await client.ReadUpdateTagSafetySnapshotAsync(
            plcName: null,
            tableName: "Default tag table",
            folderPath: "/",
            name: "MotorReady",
            projectPath: "echo");

        Assert.True(call.Success, call.Error);
        using var echoed = JsonDocument.Parse(call.Payload);
        var expected = echoed.RootElement.GetProperty("expectedSessionIdentity");
        Assert.Equal(JsonValueKind.Object, expected.ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(expected.GetProperty("workerSessionId").GetString()));
        Assert.True(expected.GetProperty("sessionGeneration").GetInt64() >= 0);
        Assert.True(expected.GetProperty("portalProcessId").GetInt32() > 0);
        Assert.False(string.IsNullOrWhiteSpace(expected.GetProperty("projectPath").GetString()));
    }
}
