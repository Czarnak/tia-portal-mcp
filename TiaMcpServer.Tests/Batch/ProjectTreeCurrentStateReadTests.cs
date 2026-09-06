using System;
using System.Threading.Tasks;
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public sealed class ProjectTreeCurrentStateReadTests
{
    [Fact]
    public async Task CreateBlock_CurrentStateRead_CanonicalizesSoftwareUnitRootParentPath()
    {
        using var client = new OpennessWorkerClient(new ProjectSessionBinding(null), logger: null, workerExecutablePath: FakeWorkerLocator.Locate());
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
        using var client = new OpennessWorkerClient(new ProjectSessionBinding(null), logger: null, workerExecutablePath: FakeWorkerLocator.Locate());
        var result = await BatchWorkerInvoker.ReadCurrentStateAsync(client, new BatchOperationRequest
        {
            OperationId = "delete-nested-unit",
            Operation = "delete_block_group",
            BlockPath = "PLC_1/Units/Line1/Blocks/Motion/AreaA",
            ProjectPath = "tree-safety-unit-nested"
        });

        Assert.True(result.Success, result.Error);
        Assert.Contains("\"parentPath\":\"PLC_1/Units/Line1/Blocks/Motion\"", result.Payload, StringComparison.Ordinal);
        Assert.Contains("\"path\":\"PLC_1/Units/Line1/Blocks/Motion/AreaA\"", result.Payload, StringComparison.Ordinal);
    }
}
