using TiaMcpServer.Contracts.Project;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Vci;

public sealed record VciWriteEffect(string Operation, string WorkspaceName, List<ProjectTreeSelectorSegment>? Scope,
    string? RootPath, int Leaves, int MappedLeaves, int FormatFallbacks);
public sealed record VciWriteEffectPresentation(string OperationId, VciWriteEffect? Effect, StructuredOperationOmission? Omission);
public sealed record VciGuardedWriteResponse(string Tool, string ContractVersion, string Phase, bool Success,
    WriteToolError? Error, IReadOnlyList<string> Warnings, IReadOnlyList<WriteGuardReport> Guards,
    IReadOnlyList<VciWriteEffectPresentation> Effects, StructuredOperationBatch? Batch, VciWriteVerification? Verification,
    StructuredOperationOmission? Omission);
public sealed record VciOperationVerification(string OperationId, bool Success, string Check, string? Expected, string? Actual, string? Message);
public sealed record VciWriteVerification(bool Success, IReadOnlyList<VciOperationVerification> Operations, StructuredOperationOmission? Omission);
public sealed record VciPlanResult(WritePlan<VciWriteEffect> Plan, IReadOnlyDictionary<string, IReadOnlyList<FiredGuard>> Guards);
public sealed record VciReplanResult(ItemReplan<VciWriteEffect> Replan, IReadOnlyList<FiredGuard> Guards);
