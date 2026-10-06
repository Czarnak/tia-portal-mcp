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

/// <summary>One item resolved against the working state: an effect with its guards, or a call-stopping error.</summary>
public sealed record PlcResolution(PlcWriteEffect? Effect, IReadOnlyList<FiredGuard> Guards, WriteToolError? Error);

public sealed record PlcPlanResult(WritePlan<PlcWriteEffect> Plan, IReadOnlyDictionary<string, IReadOnlyList<FiredGuard>> Guards);

public sealed record PlcReplanResult(ItemReplan<PlcWriteEffect> Replan, IReadOnlyList<FiredGuard> Guards);
