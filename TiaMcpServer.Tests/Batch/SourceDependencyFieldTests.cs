using TiaMcpServer.Batch;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public class SourceDependencyFieldTests
{
    private static BatchOperationRequest BlockRead() => new()
    {
        OperationId = "r1",
        Operation = "get_block_content",
        BlockPath = "PLC_1/Blocks/DamperDigital",
        Format = "source",
    };

    private static BatchOperationRequest TypeRead() => new()
    {
        OperationId = "r2",
        Operation = "get_type_content",
        TypePath = "PLC_1/Types/AnalogInputSettings",
    };

    [Fact]
    public void get_block_content_accepts_withDependencies()
    {
        var op = BlockRead();
        op.WithDependencies = true;

        var result = BatchOperationCatalog.ValidateReadBatch(new[] { op });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void get_type_content_accepts_withDependencies()
    {
        var op = TypeRead();
        op.WithDependencies = true;

        var result = BatchOperationCatalog.ValidateReadBatch(new[] { op });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void update_block_logic_rejects_withDependencies()
    {
        var op = new BatchOperationRequest
        {
            OperationId = "w1",
            Operation = "update_block_logic",
            BlockPath = "PLC_1/Blocks/DamperDigital",
            YamlContent = "FUNCTION_BLOCK \"DamperDigital\"\r\nEND_FUNCTION_BLOCK\r\n",
            WithDependencies = true,
        };

        var result = BatchOperationCatalog.ValidateWriteBatch(new[] { op });

        Assert.False(result.IsValid);
        Assert.Contains("withDependencies", result.Error);
    }

    [Fact]
    public void BuildRequest_forwards_withDependencies_for_get_block_content()
    {
        var op = BlockRead();
        op.WithDependencies = true;

        var request = BatchWorkerInvoker.BuildRequest(op);

        Assert.True(request.WithDependencies);
    }

    [Fact]
    public void BuildRequest_forwards_withDependencies_for_get_type_content()
    {
        var op = TypeRead();
        op.WithDependencies = true;

        var request = BatchWorkerInvoker.BuildRequest(op);

        Assert.True(request.WithDependencies);
    }

    [Fact]
    public void BuildRequest_leaves_withDependencies_null_when_not_supplied()
    {
        var request = BatchWorkerInvoker.BuildRequest(BlockRead());

        Assert.Null(request.WithDependencies);
    }

    [Fact]
    public void BuildRequest_never_forwards_withDependencies_on_a_write()
    {
        // The safety token binds to the single-object form of the block; a dependency-bearing
        // current-state read would bind the token to a document a write can never accept.
        var op = new BatchOperationRequest
        {
            OperationId = "w1",
            Operation = "update_block_logic",
            BlockPath = "PLC_1/Blocks/DamperDigital",
            YamlContent = "FUNCTION_BLOCK \"DamperDigital\"\r\nEND_FUNCTION_BLOCK\r\n",
        };

        var request = BatchWorkerInvoker.BuildRequest(op);

        Assert.Null(request.WithDependencies);
    }

    [Fact]
    public void A_source_format_block_write_preview_says_so()
    {
        var op = new BatchOperationRequest
        {
            OperationId = "w1",
            Operation = "update_block_logic",
            BlockPath = "PLC_1/Blocks/DamperDigital",
            YamlContent = "FUNCTION_BLOCK \"DamperDigital\"\r\nEND_FUNCTION_BLOCK\r\n",
            Format = "source",
        };

        var description = BatchSafetySnapshot.DescribeOperation(op);

        Assert.Contains("PLC_1/Blocks/DamperDigital", description);
        Assert.Contains("source format", description);
    }

    [Fact]
    public void An_xml_block_write_preview_is_unchanged()
    {
        var op = new BatchOperationRequest
        {
            OperationId = "w1",
            Operation = "update_block_logic",
            BlockPath = "PLC_1/Blocks/Main",
            YamlContent = "<Document />",
        };

        Assert.Equal("Update PLC block 'PLC_1/Blocks/Main'.", BatchSafetySnapshot.DescribeOperation(op));
    }
}
