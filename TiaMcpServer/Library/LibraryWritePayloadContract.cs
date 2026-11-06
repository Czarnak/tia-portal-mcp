using System.Text.Json;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Json;

namespace TiaMcpServer.Library;

public static class LibraryWritePayloadContract
{
    internal static void ValidateEnvelopeOutcome(JsonElement envelope)
    {
        if (envelope.ValueKind != JsonValueKind.Object) return;
        var members = envelope.EnumerateObject()
            .Where(p => string.Equals(p.Name, "libraryWriteOutcome", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (members.Length == 0) return;
        // Validate raw evidence before the permissive legacy envelope reader normalizes it.
        if (members.Length != 1 || members[0].Name != "libraryWriteOutcome") throw new JsonException();
        if (members[0].Value.ValueKind != JsonValueKind.Null)
            CanonicalJson.DeserializeWorkerPayload<LibraryWriteOutcomeInfo>(members[0].Value.GetRawText());
    }
}
