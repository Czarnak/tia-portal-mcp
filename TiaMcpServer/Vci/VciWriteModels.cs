using TiaMcpServer.Contracts.Project;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Vci;

public sealed record VciWriteEffect(string Operation, string WorkspaceName, List<ProjectTreeSelectorSegment>? Scope,
    string? RootPath, int Leaves, int MappedLeaves, int FormatFallbacks);
public sealed record VciWriteEffectPresentation(string OperationId, VciWriteEffect? Effect, StructuredOperationOmission? Omission);
public sealed record VciGuardedWriteResponse(string Tool, string ContractVersion, string Phase, bool Success,
    WriteToolError? Error, IReadOnlyList<string> Warnings, IReadOnlyList<WriteGuardReport> Guards,
    IReadOnlyList<VciWriteEffectPresentation> Effects, StructuredOperationBatch? Batch, WriteVerification? Verification,
    StructuredOperationOmission? Omission);
