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
                "create_tag_table" => CanonicalJson.Serialize(CanonicalJson.Deserialize<CreateTagTableSafetySnapshotInfo>(payload)),
                "delete_tag_table" => CanonicalJson.Serialize(CanonicalJson.Deserialize<DeleteTagTableSafetySnapshotInfo>(payload)),
                "create_tag" => CanonicalJson.Serialize(CanonicalJson.Deserialize<CreateTagSafetySnapshotInfo>(payload)),
                "update_tag" => CanonicalJson.Serialize(CanonicalJson.Deserialize<UpdateTagSafetySnapshotInfo>(payload)),
                "delete_tag" => CanonicalJson.Serialize(CanonicalJson.Deserialize<DeleteTagSafetySnapshotInfo>(payload)),
                "create_user_constant" => CanonicalJson.Serialize(CanonicalJson.Deserialize<CreateUserConstantSafetySnapshotInfo>(payload)),
                "update_user_constant" => CanonicalJson.Serialize(CanonicalJson.Deserialize<UpdateUserConstantSafetySnapshotInfo>(payload)),
                "delete_user_constant" => CanonicalJson.Serialize(CanonicalJson.Deserialize<DeleteUserConstantSafetySnapshotInfo>(payload)),
                _ => throw new InvalidOperationException($"Unsupported tag safety operation '{operation}'.")
            };

            return new(true, canonical);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return new(false, string.Empty, ex.Message, WorkerFailureCategories.ProtocolError);
        }
    }
}
