using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Hmi;

public static class HmiWriteContractVersion { public const string Current = "1.0"; }
public sealed record HmiTargetIdentity(string Kind, string HmiName, string Name, string? AlarmKind, string? TableName, string? GroupPath);
public sealed record HmiFieldChange(string Field, string? Current, string? Requested, bool Derived);
public sealed record HmiReference(string Tag, string Name, string TypeName, string? Path);
public sealed record HmiRemoval(IReadOnlyList<string> Tags, IReadOnlyList<HmiReference> References);
public sealed record HmiWriteEffect(string Operation, HmiTargetIdentity Target, IReadOnlyList<HmiFieldChange> Changes, HmiRemoval? Removes, string? DependsOn);
public sealed record HmiWriteEffectPresentation(string OperationId, HmiWriteEffect? Effect, StructuredOperationOmission? Omission);
public sealed record HmiGuardedWriteResponse(string Tool, string ContractVersion, string Phase, bool Success,
    WriteToolError? Error, IReadOnlyList<string> Warnings, IReadOnlyList<WriteGuardReport> Guards,
    IReadOnlyList<HmiWriteEffectPresentation> Effects, StructuredOperationBatch? Batch, HmiWriteVerification? Verification,
    StructuredOperationOmission? Omission);
public sealed record HmiOperationVerification(string OperationId, bool Success, string Check, string? Expected, string? Actual, string? Message);
public sealed record HmiWriteVerification(bool Success, IReadOnlyList<HmiOperationVerification> Operations, StructuredOperationOmission? Omission);
public sealed record HmiResolution(HmiWriteEffect? Effect, IReadOnlyList<FiredGuard> Guards, WriteToolError? Error);
public sealed record HmiTableEvidence(string Name, string? GroupPath);

/// <summary>Planning read evidence for one resolved HMI: update/delete targets and delete consequences.</summary>
public sealed record HmiEvidence
{
    public string HmiName { get; init; } = string.Empty;
    public IReadOnlyList<HmiTableEvidence> Tables { get; init; } = Array.Empty<HmiTableEvidence>();
    public IReadOnlyList<HmiTagRowInfo> Tags { get; init; } = Array.Empty<HmiTagRowInfo>();
    public IReadOnlyList<HmiAlarmRowInfo> Alarms { get; init; } = Array.Empty<HmiAlarmRowInfo>();
    public IReadOnlyList<HmiReference> References { get; init; } = Array.Empty<HmiReference>();
    public bool Complete { get; init; }
}
