using System.Text.Json;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.Batch;

internal static class TagUpdateSafetyCurrentState
{
    internal static string? ValidateRequestedExternalFlags(BatchOperationRequest op, TagUpdateSafetySnapshot snapshot)
    {
        if (op.ExternalAccessible.HasValue && snapshot.ExternalAccessible is null) return "externalAccessible";
        if (op.ExternalVisible.HasValue && snapshot.ExternalVisible is null) return "externalVisible";
        if (op.ExternalWritable.HasValue && snapshot.ExternalWritable is null) return "externalWritable";
        return null;
    }

    internal static string Compose(TagUpdateSafetySnapshot snapshot, string broadTagTablesPayload)
    {
        using var broadTagTables = JsonDocument.Parse(broadTagTablesPayload);
        return JsonSerializer.Serialize(new
        {
            exactTarget = snapshot,
            broadTagTables = broadTagTables.RootElement
        });
    }
}
