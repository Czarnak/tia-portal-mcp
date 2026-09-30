using TiaMcpServer.Contracts;
using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.Tools;

public sealed record StandaloneToolOutcome<TPayload>(
    string Status,
    TPayload? Value,
    StructuredOperationFailure? Failure,
    StructuredOperationOmission? Omission) where TPayload : class;

public sealed record GetProjectStatusResponse(
    string ContractVersion,
    bool Success,
    StructuredOperationFailure? Error,
    IReadOnlyList<string> Warnings,
    StandaloneToolOutcome<ProjectStatusInfo>? Result)
{
    public string Tool => "get_project_status";
}

public sealed record CompileCheckResponse(
    string ContractVersion,
    bool Success,
    StructuredOperationFailure? Error,
    IReadOnlyList<string> Warnings,
    StandaloneToolOutcome<CompileCheckReport>? Result)
{
    public string Tool => "compile_check";
}
