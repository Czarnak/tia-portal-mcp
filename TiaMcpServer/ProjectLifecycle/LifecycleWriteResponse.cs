using TiaMcpServer.Contracts;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tools;

namespace TiaMcpServer.ProjectLifecycle;

public sealed record LifecycleWriteResponse(
    string Tool,
    string ContractVersion,
    bool Success,
    WriteToolError? Error,
    IReadOnlyList<string> Warnings,
    string Phase,
    IReadOnlyList<WriteGuardReport> Guards,
    LifecycleEffects? Effects,
    StandaloneToolOutcome<ProjectLifecycleResultInfo>? Result,
    StandaloneToolOutcome<ProjectStatusInfo>? Verification);
