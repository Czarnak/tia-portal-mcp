namespace TiaMcpServer.OperationBatches;

internal static class StructuredPayloadProjection
{
    internal static void TryWriteProtocolDiagnostic(
        string operation,
        Action<string> writeDiagnostic,
        Func<string>? createDiagnostic = null)
    {
        try
        {
            writeDiagnostic(createDiagnostic is null
                ? $"TiaMcpServer: worker payload contract rejection: operation={operation}."
                : createDiagnostic());
        }
        catch
        {
            // Diagnostics must never replace the stable fail-closed protocol_error response.
        }
    }
}
