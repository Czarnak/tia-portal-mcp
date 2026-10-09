using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Network;

/// <summary>One instance per call. Retains observations without introducing another execution loop.</summary>
public sealed class NetworkWriteDomain(OpennessWorkerClient client) : IWriteDomain<NetworkOperationRequest, NetworkWriteEffect, NetworkWriteVerification, NetworkGuardedWriteResponse>
{
    private readonly NetworkWritePlanner _planner = new(client);
    // Detached copies prevent later re-plans or presentation from rewriting initial observations.
    private readonly Dictionary<string, NetworkWriteEffect> _initial = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NetworkWriteEffect> _current = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NetworkMutationVerificationInfo> _immediate = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NetworkOperationRequest> _prepared = new(StringComparer.Ordinal);
    private IReadOnlyList<NetworkOperationRequest> _items = Array.Empty<NetworkOperationRequest>();
    private const string BudgetRefusal = "The prepared Network recovery evidence exceeds the response budget. Use smaller network_write calls; no mutation was dispatched for this item.";
    private readonly HashSet<string> _attempted = new(StringComparer.Ordinal);
    public string ToolName => "network_write";
    public string ContractVersion => NetworkContractVersion.Current;
    public bool ConfirmsEveryCall => false;
    public WriteValidation Validate(IReadOnlyList<NetworkOperationRequest> items, McpAccessMode accessMode)
    {
        var validation = NetworkOperationCatalog.ValidateWrite(items);
        if (!validation.IsValid) return WriteValidation.Invalid(WorkerFailureCategories.ValidationError, validation.Error);
        if (NetworkWritePayloadBudget.MeasureProtectedCore(items) > StructuredOperationBatchPayloadBudget.MaxDocumentChars)
            return WriteValidation.Invalid(WorkerFailureCategories.ValidationError,
                "The encoded operation identities leave insufficient room for the required response summaries. Use smaller network_write calls.");
        var errors = NetworkOperationCatalog.ValidateAccessMode(items, accessMode);
        return errors.Count == 0 ? WriteValidation.Valid() : WriteValidation.Invalid(WorkerFailureCategories.AccessDenied, string.Join("\n", errors));
    }
    public async Task<WritePlan<NetworkWriteEffect>> PlanAsync(string? path, IReadOnlyList<NetworkOperationRequest> items)
    {
        var plan = await _planner.PlanAsync(path, items).ConfigureAwait(false);
        if (plan.Success && NetworkWritePayloadBudget.MeasurePreparedCore(items, plan.Items) > StructuredOperationBatchPayloadBudget.MaxDocumentChars)
            return WritePlan<NetworkWriteEffect>.Fail(WorkerFailureCategories.ValidationError, BudgetRefusal);
        if (plan.Success) _items = items.ToArray();
        if (plan.Success) for (var i = 0; i < items.Count; i++)
        {
            var effect = plan.Items[i].Effect!;
            _prepared.TryAdd(items[i].OperationId, NetworkIdentityResolver.BindPreparedTarget(items[i], effect.Target));
            _initial.TryAdd(items[i].OperationId, Copy(effect));
            _current[items[i].OperationId] = Copy(effect);
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
    public async Task<ItemReplan<NetworkWriteEffect>> ReplanAsync(string? path, NetworkOperationRequest item)
    {
        var replan = await _planner.ReplanAsync(path, _prepared.GetValueOrDefault(item.OperationId) ?? item).ConfigureAwait(false);
        if (replan.Success)
        {
            var updated = replan.Plan!.Effect!;
            var reservation = _items.Select(operation =>
            {
                var initial = _initial[operation.OperationId];
                var current = operation.OperationId == item.OperationId ? updated : _current[operation.OperationId];
                // Final preservation includes both initial and late observations. The selected
                // target stays fixed to preparation, even when consequences grow later.
                return ItemPlan<NetworkWriteEffect>.Resolved(current with
                {
                    Target = initial.Target,
                    AffectedNodes = initial.AffectedNodes.Concat(current.AffectedNodes)
                        .Distinct(NetworkNodeIdentityComparer.Instance).ToArray()
                });
            }).ToArray();
            if (NetworkWritePayloadBudget.MeasurePreparedCore(_items, reservation) > StructuredOperationBatchPayloadBudget.MaxDocumentChars)
                return ItemReplan<NetworkWriteEffect>.Fail(WorkerFailureCategories.ValidationError, BudgetRefusal);
            _current[item.OperationId] = Copy(updated);
        }
        return replan;
    }
    public Task<WorkerCallResult> MutateAsync(string? path, NetworkOperationRequest item)
    { _attempted.Add(item.OperationId); return NetworkWorkerInvoker.InvokeWriteAsync(client, _prepared.GetValueOrDefault(item.OperationId) ?? item, path); }
    public StructuredOperationItem Project(NetworkOperationRequest item, WorkerCallResult result)
    {
        var projected = NetworkPayloadContract.Project(_prepared.GetValueOrDefault(item.OperationId) ?? item, result, requireVerification: true);
        if (projected.Result is { } typed && typed.TryGetProperty("verification", out var verification))
            _immediate[item.OperationId] = CanonicalJson.Deserialize<NetworkMutationVerificationInfo>(verification.GetRawText());
        return projected;
    }
    public async Task<NetworkWriteVerification?> VerifyAsync(string? path, StructuredOperationBatch batch)
        => await new NetworkWriteVerifier(client, _initial, _current).VerifyAsync(path,
            StructuredOperationBatch.FromItems(batch.Operations.Where(item => _attempted.Contains(item.OperationId)).ToArray()), _immediate).ConfigureAwait(false);
    public bool VerificationSucceeded(NetworkWriteVerification? verification) => verification?.Success == true;
    public NetworkGuardedWriteResponse Compose(WriteReport<NetworkWriteEffect, NetworkWriteVerification> report) => NetworkWritePayloadBudget.Apply(new(
        ToolName, ContractVersion, report.Phase, report.Success, report.Error, report.Warnings, report.Guards,
        report.Effects.Select(e => new NetworkWriteEffectPresentation(e.OperationId, e.Effect, null)).ToArray(), report.Batch, report.Verification));
    private static NetworkWriteEffect Copy(NetworkWriteEffect effect) => CanonicalJson.Deserialize<NetworkWriteEffect>(CanonicalJson.Serialize(effect));
}
