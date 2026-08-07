using System.Text.Json;
using TiaMcpServer.Json;

namespace TiaMcpServer.OperationBatches;

public static class OperationBatchResultFormatter
{
    public static string Error(string toolName, string error)
        => JsonSerializer.Serialize(new { tool = toolName, success = false, error }, TiaJson.Presentation);

    public static string Read(string toolName, IReadOnlyList<OperationBatchResult> results)
    {
        var failed = Count(results, OperationBatchStatus.Failed);
        var omitted = Count(results, OperationBatchStatus.Omitted);
        return JsonSerializer.Serialize(
            new
            {
                tool = toolName,
                success = failed == 0 && omitted == 0,
                operationCount = results.Count,
                succeeded = Count(results, OperationBatchStatus.Succeeded),
                failed,
                omitted,
                operations = Project(results)
            },
            TiaJson.Presentation);
    }

    public static string Apply(string toolName, IReadOnlyList<OperationBatchResult> results)
    {
        var failed = Count(results, OperationBatchStatus.Failed);
        var skipped = Count(results, OperationBatchStatus.Skipped);
        return JsonSerializer.Serialize(
            new
            {
                tool = toolName,
                success = failed == 0 && skipped == 0,
                operationCount = results.Count,
                succeeded = Count(results, OperationBatchStatus.Succeeded),
                failed,
                skipped,
                operations = Project(results)
            },
            TiaJson.Presentation);
    }

    private static int Count(IReadOnlyList<OperationBatchResult> results, string status)
        => results.Count(result => string.Equals(result.Status, status, StringComparison.Ordinal));

    private static IReadOnlyList<object> Project(IReadOnlyList<OperationBatchResult> results)
        => results
            .Select(result => (object)new
            {
                operationId = result.OperationId,
                operation = result.Operation,
                status = result.Status,
                result = result.Result,
                warnings = result.Warnings
            })
            .ToArray();
}
