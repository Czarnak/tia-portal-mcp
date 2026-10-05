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

public sealed record ReadCrossReferencesResponse(
    string ContractVersion,
    bool Success,
    StructuredOperationFailure? Error,
    IReadOnlyList<string> Warnings,
    StandaloneToolOutcome<CrossReferenceReport>? Result)
{
    public string Tool => "read_cross_references";
}

public sealed record BindProjectResponse(
    string ContractVersion,
    bool Success,
    StructuredOperationFailure? Error,
    IReadOnlyList<string> Warnings,
    StandaloneToolOutcome<ProjectBindingResult>? Result)
{
    public string Tool => "bind_project";
}

public sealed record ProjectBindingResult(
    string Transition,
    ProjectBindingInfo Binding,
    ProjectBindingInfo PreviousBinding,
    ProjectStatusInfo? Project,
    IReadOnlyList<PortalProcessInfo> Portals);

public sealed record ProjectBindingInfo(string State, string? ProjectPath, int? PortalProcessId);

public sealed record PortalProcessInfo(int ProcessId, string? ProjectPath, bool HasUserInterface, bool IsBound);
