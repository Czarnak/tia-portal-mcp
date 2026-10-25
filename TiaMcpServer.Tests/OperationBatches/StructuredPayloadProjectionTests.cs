using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Vci;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.OperationBatches;

public sealed class StructuredPayloadProjectionTests
{
    [Fact]
    public void TryWriteProtocolDiagnostic_DefaultTextContainsOnlyOperation()
    {
        string? diagnostic = null;

        StructuredPayloadProjection.TryWriteProtocolDiagnostic("list_workspaces", text => diagnostic = text);

        Assert.Equal("TiaMcpServer: worker payload contract rejection: operation=list_workspaces.", diagnostic);
    }

    [Fact]
    public void TryWriteProtocolDiagnostic_ThrowingWriterDoesNotEscape()
    {
        var exception = Record.Exception(() => StructuredPayloadProjection.TryWriteProtocolDiagnostic(
            "list_workspaces", _ => throw new IOException("private writer failure")));

        Assert.Null(exception);
    }

    [Fact]
    public void TryWriteProtocolDiagnostic_ThrowingFormatterDoesNotEscape()
    {
        var writerCalled = false;

        var exception = Record.Exception(() => StructuredPayloadProjection.TryWriteProtocolDiagnostic(
            "list_workspaces", _ => writerCalled = true,
            () => throw new InvalidOperationException("private formatter failure")));

        Assert.Null(exception);
        Assert.False(writerCalled);
    }

    [Fact]
    public void VciRead_MalformedPayloadAndThrowingDiagnosticStillReturnProtocolError()
    {
        const string payloadMarker = "private malformed payload";
        const string diagnosticFailure = "private diagnostic exception";
        var item = VciPayloadContract.Project(
            new() { OperationId = "read", Operation = "list_workspaces" },
            WorkerCallResult.Ok(payloadMarker),
            _ => throw new IOException(diagnosticFailure));

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
        Assert.Equal("The worker payload for 'list_workspaces' did not match its declared result contract and was rejected.", item.Failure.Message);
        var document = CanonicalJson.Serialize(item);
        Assert.DoesNotContain(payloadMarker, document);
        Assert.DoesNotContain(diagnosticFailure, document);
    }
}
