using System.Text.Json;

namespace TiaMcpServer.Batch;

/// <summary>Builds the JSON envelopes returned by the batch MCP tools. Pure and unit-testable.</summary>
public static class BatchResultFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string Error(string tool, string error)
        => JsonSerializer.Serialize(new { tool, success = false, error }, JsonOptions);

    public static string ReadBatch(IReadOnlyList<BatchOperationResult> results)
    {
        var failed = Count(results, BatchOperationStatus.Failed);
        return JsonSerializer.Serialize(
            new
            {
                tool = "execute_read_batch",
                success = failed == 0,
                operationCount = results.Count,
                succeeded = Count(results, BatchOperationStatus.Succeeded),
                failed,
                operations = Project(results)
            },
            JsonOptions);
    }

    public static string ApplyBatch(IReadOnlyList<BatchOperationResult> results)
    {
        var failed = Count(results, BatchOperationStatus.Failed);
        var skipped = Count(results, BatchOperationStatus.Skipped);
        return JsonSerializer.Serialize(
            new
            {
                tool = "apply_write_batch",
                success = failed == 0 && skipped == 0,
                operationCount = results.Count,
                succeeded = Count(results, BatchOperationStatus.Succeeded),
                failed,
                skipped,
                operations = Project(results)
            },
            JsonOptions);
    }

    private static int Count(IReadOnlyList<BatchOperationResult> results, string status)
        => results.Count(r => string.Equals(r.Status, status, StringComparison.Ordinal));

    private static IReadOnlyList<object> Project(IReadOnlyList<BatchOperationResult> results)
        => results
            .Select(r => (object)new
            {
                operationId = r.OperationId,
                operation = r.Operation,
                status = r.Status,
                result = r.Result
            })
            .ToArray();
}
