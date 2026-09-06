using System;
using System.Text.Json;
using System.Threading.Tasks;
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using TiaMcpServer.Tests.Worker;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public sealed class ProjectTreeCurrentStateReadTests
{
    [Theory]
    [InlineData("create_block", "tree-safety-route-create-block", "Mixer", "parentPath", "PLC_1/Blocks/Main")]
    [InlineData("create_block_group", "tree-safety-route-create-block-group", "AreaA", "parentPath", "PLC_1/Blocks/Main")]
    [InlineData("delete_block_group", "tree-safety-route-delete-block-group", "AreaA", "groupPath", "PLC_1/Blocks/Main/AreaA")]
    public async Task CurrentStateRead_UsesExactInternalSnapshotMethod(
        string operation, string scenario, string name, string property, string expectedPath)
    {
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate());
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, scenario);
        var result = await BatchWorkerInvoker.ReadCurrentStateAsync(client, new BatchOperationRequest
        {
            OperationId = "route",
            Operation = operation,
            BlockPath = "PLC_1/Blocks/Main/" + name,
            BlockType = operation == "create_block" ? "FB" : null,
            Language = operation == "create_block" ? "SCL" : null,
            ProjectPath = scenario
        });

        Assert.True(result.Success, result.Error);
        Assert.Contains($"\"{property}\":\"{expectedPath}\"", result.Payload, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("create_block", "read_create_block_safety_snapshot")]
    [InlineData("create_block_group", "read_create_block_group_safety_snapshot")]
    [InlineData("delete_block_group", "read_delete_block_group_safety_snapshot")]
    public async Task CurrentStateRead_SendsExpectedSessionIdentity(string operation, string method)
    {
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate());
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, "echo");
        var result = await (operation switch
        {
            "create_block" => client.ReadCreateBlockSafetySnapshotAsync("PLC_1/Blocks/Main/Mixer", "FB", "SCL", null, "echo"),
            "create_block_group" => client.ReadCreateBlockGroupSafetySnapshotAsync("PLC_1/Blocks/Main/AreaA", "echo"),
            _ => client.ReadDeleteBlockGroupSafetySnapshotAsync("PLC_1/Blocks/Main/AreaA", "echo")
        });

        Assert.True(result.Success, result.Error);
        using var request = JsonDocument.Parse(result.Payload);
        Assert.Equal(method, request.RootElement.GetProperty("method").GetString());
        Assert.Equal(operation == "create_block" ? "PLC_1/Blocks/Main/Mixer" : "PLC_1/Blocks/Main/AreaA",
            request.RootElement.GetProperty("blockPath").GetString());
        if (operation == "create_block")
        {
            Assert.Equal("FB", request.RootElement.GetProperty("blockType").GetString());
            Assert.Equal("SCL", request.RootElement.GetProperty("language").GetString());
        }
        var identity = request.RootElement.GetProperty("expectedSessionIdentity");
        var bound = binding.CaptureSnapshot().ToWorkerIdentity();
        Assert.NotNull(bound);
        Assert.Equal(bound.WorkerSessionId, identity.GetProperty("workerSessionId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(identity.GetProperty("workerSessionId").GetString()));
        Assert.Equal(bound.SessionGeneration, identity.GetProperty("sessionGeneration").GetInt64());
        Assert.Equal(bound.PortalProcessId, identity.GetProperty("portalProcessId").GetInt32());
        Assert.Equal(bound.ProjectPath, identity.GetProperty("projectPath").GetString(), ignoreCase: true);
    }

    [Fact]
    public async Task CreateBlock_CurrentStateRead_CanonicalizesSoftwareUnitRootParentPath()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate());
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, "tree-safety-unit-root");
        var result = await BatchWorkerInvoker.ReadCurrentStateAsync(client, new BatchOperationRequest
        {
            OperationId = "create-root-unit",
            Operation = "create_block",
            BlockPath = "PLC_1/Units/Line1/Blocks/Main",
            BlockType = "FB",
            Language = "SCL",
            ProjectPath = "tree-safety-unit-root"
        });

        Assert.True(result.Success, result.Error);
        Assert.Contains("\"softwareUnitName\":\"Line1\"", result.Payload, StringComparison.Ordinal);
        Assert.Contains("\"rootBlocksPath\":\"PLC_1/Units/Line1/Blocks\"", result.Payload, StringComparison.Ordinal);
        Assert.Contains("\"parentPath\":\"PLC_1/Units/Line1/Blocks\"", result.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteBlockGroup_CurrentStateRead_CanonicalizesNestedSoftwareUnitAncestorPath()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate());
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, "tree-safety-unit-nested");
        var result = await BatchWorkerInvoker.ReadCurrentStateAsync(client, new BatchOperationRequest
        {
            OperationId = "delete-nested-unit",
            Operation = "delete_block_group",
            BlockPath = "PLC_1/Units/Line1/Blocks/Motion/AreaA",
            ProjectPath = "tree-safety-unit-nested"
        });

        Assert.True(result.Success, result.Error);
        Assert.Contains("\"parentPath\":\"PLC_1/Units/Line1/Blocks/Motion\"", result.Payload, StringComparison.Ordinal);
        using var snapshot = JsonDocument.Parse(result.Payload);
        var ancestor = Assert.Single(snapshot.RootElement.GetProperty("ancestors").EnumerateArray());
        Assert.Equal("Motion", ancestor.GetProperty("name").GetString());
        Assert.Equal("PLC_1/Units/Line1/Blocks/Motion", ancestor.GetProperty("path").GetString());
        Assert.Equal("UserBlockGroup", ancestor.GetProperty("kind").GetString());
        Assert.Contains("\"groupPath\":\"PLC_1/Units/Line1/Blocks/Motion/AreaA\"", result.Payload, StringComparison.Ordinal);
    }
}
