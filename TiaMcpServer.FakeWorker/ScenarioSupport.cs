using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.FakeWorker;

internal static class ScenarioSupport
{
    public static WorkerResponse BindingConflict(string error)
        => new()
        {
            Success = false,
            FailureCategory = WorkerFailureCategories.BindingConflict,
            Error = error
        };

    public static string Success(string payload)
        => JsonSerializer.Serialize(new WorkerResponse { Success = true, Payload = payload }, WorkerJson.Envelope);

    public static string SuccessWithResolvedPath(string payload, string resolvedProjectPath)
        => JsonSerializer.Serialize(new WorkerResponse
        {
            Success = true,
            Payload = payload,
            ResolvedProjectPath = resolvedProjectPath
        }, WorkerJson.Envelope);

    public static string? ReadMethod(string requestLine) => ReadField(requestLine, "method");

    public static string? ReadField(string requestLine, string propertyName)
    {
        try
        {
            using var doc = JsonDocument.Parse(requestLine);
            // ValueKind-guarded so a missing field, an explicit JSON null and a non-string all read as
            // null rather than throwing: a scenario that dispatches on an ABSENT field (format) must be
            // able to see its absence.
            return doc.RootElement.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static int? ReadIntField(string requestLine, string propertyName)
    {
        try
        {
            using var doc = JsonDocument.Parse(requestLine);
            return doc.RootElement.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
                ? value.GetInt32()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool? ReadBoolField(string requestLine, string propertyName)
    {
        try
        {
            using var doc = JsonDocument.Parse(requestLine);
            return doc.RootElement.TryGetProperty(propertyName, out var value)
                && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? value.GetBoolean()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
