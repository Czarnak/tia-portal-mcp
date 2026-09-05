using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;

namespace TiaMcpServer.Batch;

internal sealed record TagOperationSafetyDecodeResult(
    bool Success,
    string CanonicalState,
    string? Error = null,
    string? FailureCategory = null);

internal static class TagOperationSafetySnapshotContract
{
    public static TagOperationSafetyDecodeResult Decode(string operation, string payload)
    {
        try
        {
            var canonical = operation switch
            {
                "create_tag_table" => Decode<CreateTagTableSafetySnapshotInfo>(payload, ValidateCreateTagTable),
                "delete_tag_table" => Decode<DeleteTagTableSafetySnapshotInfo>(payload, ValidateDeleteTagTable),
                "create_tag" => Decode<CreateTagSafetySnapshotInfo>(payload, ValidateCreateTag),
                "update_tag" => Decode<UpdateTagSafetySnapshotInfo>(payload, ValidateUpdateTag),
                "delete_tag" => Decode<DeleteTagSafetySnapshotInfo>(payload, ValidateDeleteTag),
                "create_user_constant" => Decode<CreateUserConstantSafetySnapshotInfo>(payload, ValidateCreateUserConstant),
                "update_user_constant" => Decode<UpdateUserConstantSafetySnapshotInfo>(payload, ValidateUpdateUserConstant),
                "delete_user_constant" => Decode<DeleteUserConstantSafetySnapshotInfo>(payload, ValidateDeleteUserConstant),
                _ => throw new InvalidOperationException($"Unsupported tag safety operation '{operation}'.")
            };

            return new(true, canonical);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return new(false, string.Empty, ex.Message, WorkerFailureCategories.ProtocolError);
        }
    }

    private static string Decode<T>(string payload, Action<T> validate)
    {
        var snapshot = CanonicalJson.Deserialize<T>(payload);
        validate(snapshot);
        return CanonicalJson.Serialize(snapshot);
    }

    private static void ValidateCreateTagTable(CreateTagTableSafetySnapshotInfo snapshot)
    {
        RequireText(snapshot.PlcName, "PLC name");
        RequireText(snapshot.FolderPath, "folder path", allowEmpty: true);
        RequireText(snapshot.RequestedTableName, "requested table name");
        RequireCollisions(snapshot.TableNameCollisions, expectedKind: null);
    }

    private static void ValidateDeleteTagTable(DeleteTagTableSafetySnapshotInfo snapshot)
    {
        RequireTable(snapshot.TargetTable);
        RequireText(snapshot.ExportedSimaticMl, "exported Simatic ML");
        RequireText(snapshot.ExportSha256, "export SHA-256");
        if (snapshot.CharacterCount < 0 || snapshot.CharacterCount != snapshot.ExportedSimaticMl.Length)
        {
            throw new JsonException("The tag-table export character count is invalid.");
        }
    }

    private static void ValidateCreateTag(CreateTagSafetySnapshotInfo snapshot)
    {
        RequireTable(snapshot.TargetTable);
        RequireText(snapshot.EffectiveName, "effective tag name");
        RequireCollisions(snapshot.NameCollisions, "tag-name");
        RequireCollisions(snapshot.AddressCollisions, "logical-address");
    }

    private static void ValidateUpdateTag(UpdateTagSafetySnapshotInfo snapshot)
    {
        RequireTable(snapshot.TargetTable);
        RequireTag(snapshot.TargetTag, snapshot.TargetTable);
        RequireText(snapshot.EffectiveName, "effective tag name");
        RequireCollisions(snapshot.NameCollisions, "tag-name");
        RequireCollisions(snapshot.AddressCollisions, "logical-address");
    }

    private static void ValidateDeleteTag(DeleteTagSafetySnapshotInfo snapshot)
    {
        RequireTable(snapshot.TargetTable);
        RequireTag(snapshot.TargetTag, snapshot.TargetTable);
    }

    private static void ValidateCreateUserConstant(CreateUserConstantSafetySnapshotInfo snapshot)
    {
        RequireTable(snapshot.TargetTable);
        RequireText(snapshot.EffectiveName, "effective user-constant name");
        RequireCollisions(snapshot.NameCollisions, expectedKind: null);
    }

    private static void ValidateUpdateUserConstant(UpdateUserConstantSafetySnapshotInfo snapshot)
    {
        RequireTable(snapshot.TargetTable);
        RequireConstant(snapshot.TargetConstant, snapshot.TargetTable);
        RequireText(snapshot.EffectiveName, "effective user-constant name");
        RequireCollisions(snapshot.NameCollisions, expectedKind: null);
    }

    private static void ValidateDeleteUserConstant(DeleteUserConstantSafetySnapshotInfo snapshot)
    {
        RequireTable(snapshot.TargetTable);
        RequireConstant(snapshot.TargetConstant, snapshot.TargetTable);
    }

    private static void RequireTable(TagTableSafetyIdentityInfo? table)
    {
        ArgumentNullException.ThrowIfNull(table);
        RequireText(table.PlcName, "table PLC name");
        RequireText(table.FolderPath, "table folder path", allowEmpty: true);
        RequireText(table.TableName, "table name");
        RequireText(table.CanonicalPath, "table canonical path");
    }

    private static void RequireTag(TagSafetyIdentityInfo? tag, TagTableSafetyIdentityInfo table)
    {
        ArgumentNullException.ThrowIfNull(tag);
        RequireText(tag.PlcName, "tag PLC name");
        RequireText(tag.FolderPath, "tag folder path", allowEmpty: true);
        RequireText(tag.TableName, "tag table name");
        RequireText(tag.TagName, "tag name");
        RequireText(tag.CanonicalPath, "tag canonical path");
        RequireText(tag.DataType, "tag data type");
        RequireSameTable(tag.PlcName, tag.FolderPath, tag.TableName, table);
    }

    private static void RequireConstant(UserConstantSafetyIdentityInfo? constant, TagTableSafetyIdentityInfo table)
    {
        ArgumentNullException.ThrowIfNull(constant);
        RequireText(constant.PlcName, "user-constant PLC name");
        RequireText(constant.FolderPath, "user-constant folder path", allowEmpty: true);
        RequireText(constant.TableName, "user-constant table name");
        RequireText(constant.ConstantName, "user-constant name");
        RequireText(constant.CanonicalPath, "user-constant canonical path");
        RequireText(constant.DataType, "user-constant data type");
        RequireText(constant.Value, "user-constant value", allowEmpty: true);
        RequireSameTable(constant.PlcName, constant.FolderPath, constant.TableName, table);
    }

    private static void RequireCollisions(IReadOnlyList<TagCollisionProbeInfo>? collisions, string? expectedKind)
    {
        ArgumentNullException.ThrowIfNull(collisions);
        foreach (var collision in collisions)
        {
            ArgumentNullException.ThrowIfNull(collision);
            RequireText(collision.Kind, "collision kind");
            RequireText(collision.CandidateName, "collision candidate name");
            RequireText(collision.CanonicalPath, "collision canonical path");
            if (expectedKind is not null && !string.Equals(collision.Kind, expectedKind, StringComparison.Ordinal))
            {
                throw new JsonException($"Unsupported collision kind '{collision.Kind}'.");
            }
        }
    }

    private static void RequireSameTable(string plcName, string folderPath, string tableName, TagTableSafetyIdentityInfo table)
    {
        if (!string.Equals(plcName, table.PlcName, StringComparison.Ordinal) ||
            !string.Equals(folderPath, table.FolderPath, StringComparison.Ordinal) ||
            !string.Equals(tableName, table.TableName, StringComparison.Ordinal))
        {
            throw new JsonException("The target object does not belong to the target tag table.");
        }
    }

    private static void RequireText(string? value, string name, bool allowEmpty = false)
    {
        if (value is null || (!allowEmpty && string.IsNullOrWhiteSpace(value)))
        {
            throw new JsonException($"The snapshot {name} is required.");
        }
    }
}
