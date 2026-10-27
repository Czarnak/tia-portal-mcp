using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Plc;

/// <summary>
/// What one plc_write item does, or would do: the resolved target, current → requested field
/// changes, where a created object goes, what a delete removes, bounded content diff evidence, and
/// the earlier item it depends on.
/// </summary>
public sealed record PlcWriteEffect(
    string Operation,
    PlcTargetIdentity Target,
    IReadOnlyList<PlcFieldChange> Changes,
    string? Placement,
    PlcRemoval? Removes,
    PlcContentDiffEvidence? ContentDiff,
    string? DependsOn);

/// <summary>Kind is Tag, UserConstant, TagTable, Block, BlockGroup or Type; inapplicable members are null.</summary>
public sealed record PlcTargetIdentity(
    string Kind,
    string PlcName,
    string? DeviceName,
    string? FolderPath,
    string? TableName,
    string? Name,
    string? BlockPath,
    string? TypePath);

public sealed record PlcFieldChange(string Field, string? Current, string? Requested);

public sealed record PlcRemoval(
    int TagCount,
    int UserConstantCount,
    IReadOnlyList<string> Blocks,
    IReadOnlyList<string> Groups,
    string? BlockType,
    string? Language);

public sealed record PlcWriteEffectPresentation(string OperationId, PlcWriteEffect? Effect, StructuredOperationOmission? Omission);

/// <summary>The plc_write response document; <see cref="Omission"/> is set when diagnostics were compacted.</summary>
public sealed record PlcGuardedWriteResponse(string Tool, string ContractVersion, string Phase, bool Success,
    WriteToolError? Error, IReadOnlyList<string> Warnings, IReadOnlyList<WriteGuardReport> Guards,
    IReadOnlyList<PlcWriteEffectPresentation> Effects, StructuredOperationBatch? Batch, PlcWriteVerification? Verification,
    StructuredOperationOmission? Omission);

/// <summary>One item resolved against the working state: an effect with its guards, or a call-stopping error.</summary>
public sealed record PlcResolution(PlcWriteEffect? Effect, IReadOnlyList<FiredGuard> Guards, WriteToolError? Error);

/// <summary>
/// One post-write check of one attempted item. <see cref="Check"/> is <c>contentHash</c> (a content
/// item reports its new <see cref="ContentHash"/>), <c>trusted</c> (the Openness call completed
/// without rejection; nothing was read back) or <c>superseded</c>.
/// </summary>
public sealed record PlcOperationVerification(string OperationId, bool Success, string Check,
    string? Expected, string? Actual, string? ContentHash, string? Message);

public sealed record PlcWriteVerification(bool Success, IReadOnlyList<PlcOperationVerification> Operations,
    StructuredOperationOmission? Omission)
{
    /// <summary>ContentHash = Actual for "contentHash" entries, null otherwise.</summary>
    public static PlcWriteVerification From(WriteVerification shared)
        => new(shared.Success, shared.Operations.Select(o => new PlcOperationVerification(
            o.OperationId, o.Success, o.Check, o.Expected, o.Actual,
            o.Check == "contentHash" ? o.Actual : null, o.Message)).ToArray(), shared.Omission);
}
