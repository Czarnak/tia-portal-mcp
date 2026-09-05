namespace TiaMcpServer.Contracts;

public sealed record TagTableSafetyIdentityInfo(
    string PlcName,
    string FolderPath,
    string TableName,
    string CanonicalPath);

public sealed record TagSafetyIdentityInfo(
    string PlcName,
    string FolderPath,
    string TableName,
    string TagName,
    string CanonicalPath,
    string DataType,
    string? LogicalAddress,
    bool? ExternalAccessible,
    bool? ExternalVisible,
    bool? ExternalWritable);

public sealed record UserConstantSafetyIdentityInfo(
    string PlcName,
    string FolderPath,
    string TableName,
    string ConstantName,
    string CanonicalPath,
    string DataType,
    string Value);

public sealed record TagCollisionProbeInfo(
    string Kind,
    string CandidateName,
    string CanonicalPath,
    string? LogicalAddress,
    bool IsTarget);

public sealed record CreateTagTableSafetySnapshotInfo(
    string PlcName,
    string FolderPath,
    string RequestedTableName,
    IReadOnlyList<TagCollisionProbeInfo> TableNameCollisions);

public sealed record DeleteTagTableSafetySnapshotInfo(
    TagTableSafetyIdentityInfo TargetTable,
    string ExportedSimaticMl,
    string ExportSha256,
    int CharacterCount);

public sealed record CreateTagSafetySnapshotInfo(
    TagTableSafetyIdentityInfo TargetTable,
    string EffectiveName,
    string? EffectiveLogicalAddress,
    IReadOnlyList<TagCollisionProbeInfo> NameCollisions,
    IReadOnlyList<TagCollisionProbeInfo> AddressCollisions);

public sealed record UpdateTagSafetySnapshotInfo(
    TagTableSafetyIdentityInfo TargetTable,
    TagSafetyIdentityInfo TargetTag,
    string EffectiveName,
    string? EffectiveLogicalAddress,
    IReadOnlyList<TagCollisionProbeInfo> NameCollisions,
    IReadOnlyList<TagCollisionProbeInfo> AddressCollisions);

public sealed record DeleteTagSafetySnapshotInfo(
    TagTableSafetyIdentityInfo TargetTable,
    TagSafetyIdentityInfo TargetTag);

public sealed record CreateUserConstantSafetySnapshotInfo(
    TagTableSafetyIdentityInfo TargetTable,
    string EffectiveName,
    IReadOnlyList<TagCollisionProbeInfo> NameCollisions);

public sealed record UpdateUserConstantSafetySnapshotInfo(
    TagTableSafetyIdentityInfo TargetTable,
    UserConstantSafetyIdentityInfo TargetConstant,
    string EffectiveName,
    IReadOnlyList<TagCollisionProbeInfo> NameCollisions);

public sealed record DeleteUserConstantSafetySnapshotInfo(
    TagTableSafetyIdentityInfo TargetTable,
    UserConstantSafetyIdentityInfo TargetConstant);
