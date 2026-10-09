using System.Text.Json.Serialization;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.CrossReferences;
using TiaMcpServer.Contracts.Multiuser;
using TiaMcpServer.Contracts.Project;

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
    IReadOnlyList<PortalProcessInfo> Portals,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ProjectBindingInspectionInfo? Inspection = null);

public sealed record ProjectBindingInspectionInfo(
    string Action,
    int? PortalProcessId,
    MultiuserServerConnectionsInfo? ServerConnections,
    MultiuserServerGroupsInfo? ServerGroups,
    MultiuserServerProjectsInfo? ServerProjects,
    MultiuserLocalSessionsInfo? LocalSessions,
    MultiuserLockStateInfo? LockState);

public sealed record ProjectBindingInfo(
    string State,
    string? ProjectPath,
    int? PortalProcessId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ProjectContextInfo? Context = null);

public sealed record PortalProcessInfo(int ProcessId, string? ProjectPath, bool HasUserInterface, bool IsBound);
