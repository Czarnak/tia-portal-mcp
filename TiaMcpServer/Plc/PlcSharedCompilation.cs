using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Plc;

internal static class PlcSharedCompilationPolicy
{
    public static IReadOnlySet<string> DeferredOperationIds(IReadOnlyList<PlcOperationRequest> items)
    {
        var eligible = items.Where(IsSourceBlockImport).ToArray();
        return eligible.Length < 2
            ? new HashSet<string>(StringComparer.Ordinal)
            : eligible.Select(item => item.OperationId).ToHashSet(StringComparer.Ordinal);
    }

    public static string? Selector(string blockPath) => BlockAddress.Parse(blockPath).PlcName;

    public static string StageOf(StandaloneToolOutcome<CompileCheckReport> decoded)
        => decoded.Value is not { } report ? "unavailable"
            : StandaloneCompileOutcome.IsPassed(report) ? "succeeded"
            : report.TotalErrorCount > 0 ? "failed" : "unavailable";

    internal static bool IsSourceBlockImport(PlcOperationRequest item)
        => item.Operation is "update_block_logic" or "patch_block_logic"
            && PlcFormatNames.TryNormalize(item.Operation, item.Format, out var format, out _)
            && format == SourceFormatNames.Source;
}

internal sealed class PlcSharedCompilationTracker
{
    private sealed record CompilationGroup(string? PlcName, List<string> OperationIds);

    private readonly List<CompilationGroup> _groups = [];

    public void Record(PlcOperationRequest executionItem, WorkerCallResult result)
    {
        if (result.BlockImportOutcome?.TargetMutationCommitted != true
            || !PlcSharedCompilationPolicy.IsSourceBlockImport(executionItem))
            return;

        var selector = PlcSharedCompilationPolicy.Selector(executionItem.BlockPath!);
        var group = _groups.FirstOrDefault(entry => string.Equals(entry.PlcName, selector, StringComparison.OrdinalIgnoreCase));
        if (group is null)
        {
            group = new(selector, []);
            _groups.Add(group);
        }
        group.OperationIds.Add(executionItem.OperationId);
    }

    public async Task<IReadOnlyList<PlcSharedCompilation>> CompileAsync(Func<string?, Task<WorkerCallResult>> compile,
        StructuredOperationBatch batch)
    {
        var stopped = batch.Operations.FirstOrDefault(item => item.Status != OperationBatchStatus.Succeeded);
        var skip = stopped is { Status: OperationBatchStatus.Failed, Failure.Category:
            WorkerFailureCategories.PortalLost or WorkerFailureCategories.WorkerTimeout
            or WorkerFailureCategories.WorkerCrashed or WorkerFailureCategories.ProtocolError
            or WorkerFailureCategories.BindingConflict };
        var entries = new List<PlcSharedCompilation>(_groups.Count);
        foreach (var group in _groups)
        {
            if (skip)
            {
                entries.Add(new(group.PlcName, group.OperationIds.ToArray(), "skipped", null, null, null));
                continue;
            }

            var decoded = StandalonePayloadContract.DecodeCompile(await compile(group.PlcName).ConfigureAwait(false));
            entries.Add(new(group.PlcName, group.OperationIds.ToArray(), PlcSharedCompilationPolicy.StageOf(decoded),
                decoded.Value, decoded.Failure, decoded.Omission));
        }
        return entries;
    }
}
