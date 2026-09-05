using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

public sealed class TagOperationSafetyClientIdentityTests
{
    [Theory]
    [InlineData("create_tag_table")]
    [InlineData("delete_tag_table")]
    [InlineData("create_tag")]
    [InlineData("update_tag")]
    [InlineData("delete_tag")]
    [InlineData("create_user_constant")]
    [InlineData("update_user_constant")]
    [InlineData("delete_user_constant")]
    public async Task EveryTagSafetyClientMethod_SendsExpectedSessionIdentityInTheWorkerRequest(string selectorKind)
    {
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(binding, logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate(),
            accessPolicy: new OperationAccessPolicy(McpAccessMode.ReadWrite));
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, "echo");
        var before = client.BindingSnapshot.ToWorkerIdentity()!;
        var result = selectorKind switch
        {
            "create_tag_table" => await client.ReadCreateTagTableSafetySnapshotAsync("PLC_1", "Inputs", null, "echo"),
            "delete_tag_table" => await client.ReadDeleteTagTableSafetySnapshotAsync("PLC_1", "Inputs", null, "echo"),
            "create_tag" => await client.ReadCreateTagSafetySnapshotAsync("PLC_1", "Inputs", null, "Start", "Bool", "%I0.0", "echo"),
            "update_tag" => await client.ReadUpdateTagSafetySnapshotAsync("PLC_1", "Inputs", null, "Start", "Start_1", "%I0.1", "echo"),
            "delete_tag" => await client.ReadDeleteTagSafetySnapshotAsync("PLC_1", "Inputs", null, "Start", "echo"),
            "create_user_constant" => await client.ReadCreateUserConstantSafetySnapshotAsync("PLC_1", "Inputs", null, "DebounceMs", "echo"),
            "update_user_constant" => await client.ReadUpdateUserConstantSafetySnapshotAsync("PLC_1", "Inputs", null, "DebounceMs", "echo"),
            "delete_user_constant" => await client.ReadDeleteUserConstantSafetySnapshotAsync("PLC_1", "Inputs", null, "DebounceMs", "echo"),
            _ => throw new ArgumentOutOfRangeException(nameof(selectorKind))
        };
        Assert.True(result.Success, result.Error);
        using var document = JsonDocument.Parse(result.Payload);
        var request = document.RootElement;
        Assert.Equal("read_" + selectorKind + "_safety_snapshot", request.GetProperty("method").GetString());
        Assert.Equal("PLC_1", request.GetProperty("plcName").GetString());
        Assert.Equal("Inputs", request.GetProperty("tableName").GetString());
        var expected = request.GetProperty("expectedSessionIdentity");
        Assert.Equal(before.WorkerSessionId, expected.GetProperty("workerSessionId").GetString());
        Assert.Equal(before.SessionGeneration, expected.GetProperty("sessionGeneration").GetInt64());
        Assert.Equal(before.PortalProcessId, expected.GetProperty("portalProcessId").GetInt32());
        Assert.Equal(before.ProjectPath, expected.GetProperty("projectPath").GetString());
        if (selectorKind == "create_tag")
        {
            Assert.Equal("Start", request.GetProperty("name").GetString());
            Assert.Equal("Bool", request.GetProperty("dataType").GetString());
            Assert.Equal("%I0.0", request.GetProperty("logicalAddress").GetString());
        }
        if (selectorKind == "update_tag")
        {
            Assert.Equal("Start", request.GetProperty("name").GetString());
            Assert.Equal("Start_1", request.GetProperty("newName").GetString());
            Assert.Equal("%I0.1", request.GetProperty("logicalAddress").GetString());
        }
        if (selectorKind.Contains("user_constant", StringComparison.Ordinal))
            Assert.Equal("DebounceMs", request.GetProperty("name").GetString());
    }
}
