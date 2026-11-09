using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Library;

/// <summary>One call's library plans, guards and attempted items for the shared guarded pipeline.</summary>
public sealed class LibraryWriteDomain(OpennessWorkerClient client)
    : IWriteDomain<LibraryWriteItem, LibraryWriteEffect, WriteVerification, LibraryGuardedWriteResponse>
{
    private readonly LibraryWritePlanner _planner = new(client);
    private readonly Dictionary<string, IReadOnlyList<FiredGuard>> _guards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LibraryWriteEffect> _effects = new(StringComparer.Ordinal);
    private readonly HashSet<string> _attempted = new(StringComparer.Ordinal);
    private IReadOnlyList<LibraryWriteItem> _items = [];

    public string ToolName => "library_write";
    public string ContractVersion => LibraryWriteContractVersion.Current;
    public bool PlansBeforeApply(IReadOnlyList<LibraryWriteItem> items) => false;

    public bool ConfirmsCall(IReadOnlyList<LibraryWriteItem> items)
        => items.Any(item => LibraryWriteMethods.LifecycleOperations.Contains(item.Operation, StringComparer.Ordinal));

    public IEnumerable<string> CapabilityOperations(IReadOnlyList<LibraryWriteItem> items)
        => items.Select(item => LibraryWriteMethods.ForItem(LibraryWriteCatalog.ToWorkerRequest(item)));

    public string DescribeForConfirmation(IReadOnlyList<LibraryWriteItem> items, IReadOnlyList<ItemPlan<LibraryWriteEffect>> plans)
        => string.Join("\n", items.Where(item => LibraryWriteMethods.LifecycleOperations.Contains(item.Operation, StringComparer.Ordinal))
            .Select(item => item.Operation switch
            {
                "create_global" => $"create_global: directory '{item.Directory}', name '{item.Name}'"
                    + (item.Author is null ? "." : $", author '{item.Author}'."),
                "open_global" => $"open_global: file '{item.FilePath}', mode '{item.OpenMode}'.",
                "retrieve_global" => $"retrieve_global: archive '{item.FilePath}', destination directory '{item.Directory}', mode '{item.OpenMode}'.",
                "save_global" => $"save_global: library '{item.LibraryPath ?? item.LibraryName}'.",
                "close_global" => $"close_global: library '{item.LibraryPath ?? item.LibraryName}', action '{item.CloseAction}'.",
                "archive_global" => $"archive_global: library '{item.LibraryPath ?? item.LibraryName}', destination directory '{item.Directory}', name '{item.Name}'.",
                _ => throw new InvalidOperationException("Unknown library lifecycle operation.")
            }));

    public WriteValidation Validate(IReadOnlyList<LibraryWriteItem> items, McpAccessMode accessMode)
    {
        _items = items.ToArray();
        _guards.Clear();
        _effects.Clear();
        _attempted.Clear();
        return WriteValidation.Compose(LibraryWriteCatalog.ValidateWrite(items),
            () => LibraryWritePayloadBudget.MeasureProtectedCore(items),
            () => LibraryWriteCatalog.ValidateAccessMode(items, accessMode), ToolName);
    }

    public async Task<WritePlan<LibraryWriteEffect>> PlanAsync(IReadOnlyList<LibraryWriteItem> items)
    {
        var result = await _planner.PlanAsync(items).ConfigureAwait(false);
        if (!result.Plan.Success) return result.Plan;
        _items = items.ToArray();
        _guards.Clear();
        for (var i = 0; i < items.Count; i++)
        {
            _guards[items[i].OperationId] = result.Guards.GetValueOrDefault(items[i].OperationId) ?? [];
        }
        return result.Plan;
    }

    public IReadOnlyList<FiredGuard> EvaluateGuards(IReadOnlyList<LibraryWriteItem> items, IReadOnlyList<ItemPlan<LibraryWriteEffect>> plans)
        => items.SelectMany(item => _guards.GetValueOrDefault(item.OperationId) ?? []).ToArray();

    public Task<ItemReplan<LibraryWriteEffect>> ReplanAsync(LibraryWriteItem item)
        => throw new InvalidOperationException("library_write plans declare no dependencies; ReplanAsync must never be called.");

    public Task<WorkerCallResult> MutateAsync(LibraryWriteItem item)
    {
        _attempted.Add(item.OperationId);
        var request = LibraryWriteCatalog.ToWorkerRequest(item);
        return client.WriteLibraryAsync(LibraryWriteMethods.ForItem(request), request);
    }

    public StructuredOperationItem Project(LibraryWriteItem item, WorkerCallResult result)
    {
        var projected = LibraryWritePayloadContract.Project(item, result);
        if (projected.Result is { } payload)
        {
            if (projected.Status == OperationBatchStatus.Succeeded)
            {
                var value = Json.CanonicalJson.DeserializeWorkerPayload<LibraryWriteResultInfo>(payload.GetRawText());
                _effects[item.OperationId] = new(value.Library, value.Target, value.AlreadyOpen, [], false) { Type = value.Type, MetadataChange = value.MetadataChange };
            }
            else if (result.LibraryWriteOutcome is { } outcome)
                _effects[item.OperationId] = new(outcome.Library, null, null, [], false) { Type = outcome.Type, MetadataChange = outcome.MetadataChange };
        }
        return projected;
    }

    public async Task<WriteVerification?> VerifyAsync(StructuredOperationBatch batch)
        => await new TrustedWriteVerifier<LibraryWriteItem>(_items, new LibraryVerificationPolicy(_effects, _items)).VerifyAsync(
            StructuredOperationBatch.FromItems(batch.Operations.Where(o => _attempted.Contains(o.OperationId)).ToArray())).ConfigureAwait(false);

    public bool VerificationSucceeded(WriteVerification? verification) => verification?.Success == true;

    public LibraryGuardedWriteResponse Compose(WriteReport<LibraryWriteEffect, WriteVerification> report)
        => LibraryWritePayloadBudget.Apply(new LibraryGuardedWriteResponse(ToolName, ContractVersion, report.Phase, report.Success,
            report.Error, report.Warnings, report.Guards,
            report.Effects.Select(e => new LibraryWriteEffectPresentation(e.OperationId, e.Effect, null)).ToArray(),
            report.Batch, report.Verification, null));
}
