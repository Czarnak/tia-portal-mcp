using TiaMcpServer.Batch;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public class SourceDependencyFieldTests
{
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
