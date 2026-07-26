using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests;

public class TypeOperationInvokerTests
{
    [Fact]
    public void Update_type_content_forwards_format_and_source_content()
    {
        var op = new BatchOperationRequest
        {
            OperationId = "w1",
            Operation = "update_type_content",
            TypePath = "PLC_1/Types/AnalogInputSettings",
            SourceContent = "TYPE \"AnalogInputSettings\"\r\nEND_TYPE\r\n",
            Format = "source",
        };

        var request = BatchWorkerInvoker.BuildRequest(op);

        Assert.Equal("update_type_content", request.Method);
        Assert.Equal("PLC_1/Types/AnalogInputSettings", request.TypePath);
        Assert.Equal("TYPE \"AnalogInputSettings\"\r\nEND_TYPE\r\n", request.SourceContent);
        Assert.Equal("source", request.Format);
    }

    [Fact]
    public void Type_operations_default_format_to_source_when_omitted()
    {
        var op = new BatchOperationRequest
        {
            OperationId = "r1",
            Operation = "get_type_content",
            TypePath = "PLC_1/Types/AnalogInputSettings",
        };

        var request = BatchWorkerInvoker.BuildRequest(op);

        Assert.Equal("source", request.Format);
    }

    [Fact]
    public void Block_operations_default_format_to_xml_when_omitted()
    {
        var op = new BatchOperationRequest
        {
            OperationId = "r2",
            Operation = "get_block_content",
            BlockPath = "PLC_1/Blocks/Main",
        };

        var request = BatchWorkerInvoker.BuildRequest(op);

        Assert.Equal("xml", request.Format);
    }

    [Fact]
    public void An_invalid_format_is_rejected_before_the_session_binds()
    {
        var op = new BatchOperationRequest
        {
            OperationId = "r3",
            Operation = "get_type_content",
            TypePath = "PLC_1/Types/AnalogInputSettings",
            Format = "s7dcl",
        };

        var ex = Assert.Throws<ArgumentException>(() => BatchWorkerInvoker.BuildRequest(op));

        Assert.Contains("s7dcl", ex.Message);
    }
}
