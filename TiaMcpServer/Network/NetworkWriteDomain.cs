using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Network;

/// <summary>One instance per call. Retains observations without introducing another execution loop.</summary>
public sealed class NetworkWriteDomain(OpennessWorkerClient client) : IWriteDomain<NetworkOperationRequest, NetworkWriteEffect, WriteVerification, NetworkGuardedWriteResponse>
{
    private readonly NetworkWritePlanner _planner = new(client);
    private readonly Dictionary<string, NetworkWriteEffect> _effects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NetworkOperationRequest> _prepared = new(StringComparer.Ordinal);
    private IReadOnlyList<NetworkOperationRequest> _items = Array.Empty<NetworkOperationRequest>();
    private const string BudgetRefusal = "The prepared Network recovery evidence exceeds the response budget. Use smaller network_write calls; no mutation was dispatched for this item.";
    private readonly HashSet<string> _attempted = new(StringComparer.Ordinal);
    public string ToolName => "network_write";
    public string ContractVersion => NetworkWriteContractVersion.Current;
    public bool ConfirmsEveryCall => false;
    public WriteValidation Validate(IReadOnlyList<NetworkOperationRequest> items, McpAccessMode accessMode)
        => WriteValidation.Compose(
            NetworkOperationCatalog.ValidateWrite(items),
            () => NetworkWritePayloadBudget.MeasureProtectedCore(items),
            () => NetworkOperationCatalog.ValidateAccessMode(items, accessMode),
            ToolName);
    public async Task<WritePlan<NetworkWriteEffect>> PlanAsync(IReadOnlyList<NetworkOperationRequest> items)
    {
        var plan = await _planner.PlanAsync(items).ConfigureAwait(false);
        if (plan.Success && NetworkWritePayloadBudget.MeasurePreparedCore(items, plan.Items) > StructuredOperationBatchPayloadBudget.MaxDocumentChars)
            return WritePlan<NetworkWriteEffect>.Fail(WorkerFailureCategories.ValidationError, BudgetRefusal);
        if (plan.Success) _items = items.ToArray();
        if (plan.Success) for (var i = 0; i < items.Count; i++)
        {
            var effect = plan.Items[i].Effect!;
            _prepared.TryAdd(items[i].OperationId, NetworkIdentityResolver.BindPreparedTarget(items[i], effect.Target));
            _effects[items[i].OperationId] = Copy(effect);
        }
        return plan;
    }
    public IReadOnlyList<FiredGuard> EvaluateGuards(IReadOnlyList<NetworkOperationRequest> items, IReadOnlyList<ItemPlan<NetworkWriteEffect>> plans)
    {
        var guards = new List<FiredGuard>();
        for (var i = 0; i < items.Count; i++)
        {
            var effect = plans[i].Effect;
            if (effect?.ConnectionsComplete != true)
                guards.Add(new(NetworkGuardDefinitions.Unverifiable, items[i].OperationId, "Required Network consequence inventory or root device count is unavailable. Inspect current hardware state."));
            else if (effect.Operation == "delete_subnet" && effect.AffectedNodes.Count > 0)
                guards.Add(new(NetworkGuardDefinitions.ConnectedDelete, items[i].OperationId,
                    $"Deleting the selected subnet removes connections for {effect.AffectedNodes.Count} nodes; devices and nodes remain. Inspect the operation's effect for exact identities."));
        }
        return guards;
    }
    public async Task<ItemReplan<NetworkWriteEffect>> ReplanAsync(NetworkOperationRequest item)
    {
        var replan = await _planner.ReplanAsync(_prepared.GetValueOrDefault(item.OperationId) ?? item).ConfigureAwait(false);
        if (replan.Success)
        {
            var updated = replan.Plan!.Effect!;
            var reservation = _items.Select(operation => ItemPlan<NetworkWriteEffect>.Resolved(
                operation.OperationId == item.OperationId ? updated : _effects[operation.OperationId])).ToArray();
            if (NetworkWritePayloadBudget.MeasurePreparedCore(_items, reservation) > StructuredOperationBatchPayloadBudget.MaxDocumentChars)
                return ItemReplan<NetworkWriteEffect>.Fail(WorkerFailureCategories.ValidationError, BudgetRefusal);
            _effects[item.OperationId] = Copy(updated);
        }
        return replan;
    }
    public Task<WorkerCallResult> MutateAsync(NetworkOperationRequest item)
    { _attempted.Add(item.OperationId); return NetworkWorkerInvoker.InvokeWriteAsync(client, _prepared.GetValueOrDefault(item.OperationId) ?? item); }
    public StructuredOperationItem Project(NetworkOperationRequest item, WorkerCallResult result)
        => NetworkPayloadContract.Project(_prepared.GetValueOrDefault(item.OperationId) ?? item, result);
    public async Task<WriteVerification?> VerifyAsync(StructuredOperationBatch batch)
        => await new TrustedWriteVerifier<NetworkOperationRequest>(_items, new NetworkVerificationPolicy(_effects)).VerifyAsync(
            StructuredOperationBatch.FromItems(batch.Operations.Where(item => _attempted.Contains(item.OperationId)).ToArray())).ConfigureAwait(false);
    public bool VerificationSucceeded(WriteVerification? verification) => verification?.Success == true;
    public NetworkGuardedWriteResponse Compose(WriteReport<NetworkWriteEffect, WriteVerification> report) => NetworkWritePayloadBudget.Apply(new(
        ToolName, ContractVersion, report.Phase, report.Success, report.Error, report.Warnings, report.Guards,
        report.Effects.Select(e => new NetworkWriteEffectPresentation(e.OperationId, e.Effect, null)).ToArray(), report.Batch, report.Verification));
    private static NetworkWriteEffect Copy(NetworkWriteEffect effect) => CanonicalJson.Deserialize<NetworkWriteEffect>(CanonicalJson.Serialize(effect));
}
