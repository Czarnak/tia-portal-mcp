using System.Text.Json.Serialization;

namespace TiaMcpServer.Contracts;

public sealed record TagTableSafetyIdentityInfo(
    [property: JsonRequired] string PlcName,
    [property: JsonRequired] string FolderPath,
    [property: JsonRequired] string TableName,
    [property: JsonRequired] string CanonicalPath);

public sealed record TagSafetyIdentityInfo(
    [property: JsonRequired] string PlcName,
    [property: JsonRequired] string FolderPath,
    [property: JsonRequired] string TableName,
    [property: JsonRequired] string TagName,
    [property: JsonRequired] string CanonicalPath,
    [property: JsonRequired] string DataType,
    string? LogicalAddress,
    bool? ExternalAccessible,
    bool? ExternalVisible,
    bool? ExternalWritable);

public sealed record UserConstantSafetyIdentityInfo(
    [property: JsonRequired] string PlcName,
    [property: JsonRequired] string FolderPath,
    [property: JsonRequired] string TableName,
    [property: JsonRequired] string ConstantName,
    [property: JsonRequired] string CanonicalPath,
    [property: JsonRequired] string DataType,
    [property: JsonRequired] string Value);

public sealed record TagCollisionProbeInfo(
    [property: JsonRequired] string Kind,
    [property: JsonRequired] string CandidateName,
    [property: JsonRequired] string CanonicalPath,
    string? LogicalAddress,
    [property: JsonRequired] bool IsTarget);

public sealed record CreateTagTableSafetySnapshotInfo(
    [property: JsonRequired] string PlcName,
    [property: JsonRequired] string FolderPath,
    [property: JsonRequired] string RequestedTableName,
    [property: JsonRequired] IReadOnlyList<TagCollisionProbeInfo> TableNameCollisions);

public sealed record DeleteTagTableSafetySnapshotInfo(
    [property: JsonRequired] TagTableSafetyIdentityInfo TargetTable,
    [property: JsonRequired] string ExportedSimaticMl,
    [property: JsonRequired] string ExportSha256,
    [property: JsonRequired] int CharacterCount);

public sealed record CreateTagSafetySnapshotInfo(
    [property: JsonRequired] TagTableSafetyIdentityInfo TargetTable,
    [property: JsonRequired] string EffectiveName,
    string? EffectiveLogicalAddress,
    [property: JsonRequired] IReadOnlyList<TagCollisionProbeInfo> NameCollisions,
    [property: JsonRequired] IReadOnlyList<TagCollisionProbeInfo> AddressCollisions);

public sealed record UpdateTagSafetySnapshotInfo(
    [property: JsonRequired] TagTableSafetyIdentityInfo TargetTable,
    [property: JsonRequired] TagSafetyIdentityInfo TargetTag,
    [property: JsonRequired] string EffectiveName,
    string? EffectiveLogicalAddress,
    [property: JsonRequired] IReadOnlyList<TagCollisionProbeInfo> NameCollisions,
    [property: JsonRequired] IReadOnlyList<TagCollisionProbeInfo> AddressCollisions);

public sealed record DeleteTagSafetySnapshotInfo(
    [property: JsonRequired] TagTableSafetyIdentityInfo TargetTable,
    [property: JsonRequired] TagSafetyIdentityInfo TargetTag);

public sealed record CreateUserConstantSafetySnapshotInfo(
    [property: JsonRequired] TagTableSafetyIdentityInfo TargetTable,
    [property: JsonRequired] string EffectiveName,
    [property: JsonRequired] IReadOnlyList<TagCollisionProbeInfo> NameCollisions);

public sealed record UpdateUserConstantSafetySnapshotInfo(
    [property: JsonRequired] TagTableSafetyIdentityInfo TargetTable,
    [property: JsonRequired] UserConstantSafetyIdentityInfo TargetConstant,
    [property: JsonRequired] string EffectiveName,
    [property: JsonRequired] IReadOnlyList<TagCollisionProbeInfo> NameCollisions);

public sealed record DeleteUserConstantSafetySnapshotInfo(
    [property: JsonRequired] TagTableSafetyIdentityInfo TargetTable,
    [property: JsonRequired] UserConstantSafetyIdentityInfo TargetConstant);
