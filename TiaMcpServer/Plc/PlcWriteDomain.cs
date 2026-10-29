using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Safety;

namespace TiaMcpServer.Plc;

/// <summary>
/// The plc_write domain for the guarded pipeline; one instance per call. The planner's guards are
/// cached by operationId so <see cref="EvaluateGuards"/> stays pure, and a late re-plan refreshes
/// its item's entry. Only block and info guards exist, so nothing ever asks the user.
/// </summary>
public sealed class PlcWriteDomain(OpennessWorkerClient client)
    : IWriteDomain<PlcOperationRequest, PlcWriteEffect, PlcWriteVerification, PlcGuardedWriteResponse>
{
    private readonly PlcWritePlanner _planner = new(client);
    private readonly Dictionary<string, IReadOnlyList<FiredGuard>> _guards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PlcWriteEffect> _effects = new(StringComparer.Ordinal);
    private readonly HashSet<string> _attempted = new(StringComparer.Ordinal);
    private IReadOnlyList<PlcOperationRequest> _items = Array.Empty<PlcOperationRequest>();

    public string ToolName => "plc_write";

    public string ContractVersion => PlcWriteContractVersion.Current;

    public bool ConfirmsEveryCall => false;

    public WriteValidation Validate(IReadOnlyList<PlcOperationRequest> items, McpAccessMode accessMode)
        => WriteValidation.Compose(
            PlcOperationCatalog.ValidateWrite(items),
            () => PlcWritePayloadBudget.MeasureProtectedCore(items),
            () => PlcOperationCatalog.ValidateAccessMode(items, accessMode),
            ToolName);

    public async Task<WritePlan<PlcWriteEffect>> PlanAsync(IReadOnlyList<PlcOperationRequest> items)
    {
        var result = await _planner.PlanAsync(items).ConfigureAwait(false);
        if (!result.Plan.Success) return result.Plan;
        _items = items.ToArray();
        for (var i = 0; i < items.Count; i++)
        {
            _guards[items[i].OperationId] = result.Guards.GetValueOrDefault(items[i].OperationId) ?? Array.Empty<FiredGuard>();
            _effects[items[i].OperationId] = result.Plan.Items[i].Effect!;
        }

        return result.Plan;
    }

    public IReadOnlyList<FiredGuard> EvaluateGuards(IReadOnlyList<PlcOperationRequest> items, IReadOnlyList<ItemPlan<PlcWriteEffect>> plans)
        => items.SelectMany(item => _guards.GetValueOrDefault(item.OperationId) ?? Array.Empty<FiredGuard>()).ToArray();

    public async Task<ItemReplan<PlcWriteEffect>> ReplanAsync(PlcOperationRequest item)
    {
        var result = await _planner.ReplanAsync(item).ConfigureAwait(false);
        if (!result.Replan.Success) return result.Replan;
        _guards[item.OperationId] = result.Guards;
        // The re-plan reads real state, so it knows no dependency; the presented effect keeps the planned one.
        var plan = result.Replan.Plan!;
        var effect = plan.Effect! with { DependsOn = _effects.GetValueOrDefault(item.OperationId)?.DependsOn };
        _effects[item.OperationId] = effect;
        return ItemReplan<PlcWriteEffect>.Ok(plan with { Effect = effect });
    }

    public Task<WorkerCallResult> MutateAsync(PlcOperationRequest item)
    {
        _attempted.Add(item.OperationId);
        return PlcWorkerInvoker.InvokeWriteAsync(client, item);
    }

    public StructuredOperationItem Project(PlcOperationRequest item, WorkerCallResult result)
        => PlcPayloadContract.ProjectWrite(item, result);

    public async Task<PlcWriteVerification?> VerifyAsync(StructuredOperationBatch batch)
        => await PlcVerificationPolicy.VerifyAsync(client, _items, _effects,
            StructuredOperationBatch.FromItems(batch.Operations.Where(o => _attempted.Contains(o.OperationId)).ToArray())).ConfigureAwait(false);

    public bool VerificationSucceeded(PlcWriteVerification? verification) => verification?.Success == true;

    public PlcGuardedWriteResponse Compose(WriteReport<PlcWriteEffect, PlcWriteVerification> report)
        => PlcWritePayloadBudget.Apply(new PlcGuardedWriteResponse(ToolName, ContractVersion, report.Phase, report.Success,
            report.Error, report.Warnings, report.Guards,
            report.Effects.Select(e => new PlcWriteEffectPresentation(e.OperationId, e.Effect, null)).ToArray(),
            report.Batch, report.Verification, null));
}
