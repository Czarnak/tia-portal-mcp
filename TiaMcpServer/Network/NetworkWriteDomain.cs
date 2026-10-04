using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Network;

/// <summary>One instance per call. Retains observations without introducing another execution loop.</summary>
public sealed class NetworkWriteDomain(OpennessWorkerClient client) : IWriteDomain<NetworkOperationRequest, NetworkWriteEffect, NetworkWriteVerification, NetworkGuardedWriteResponse>
{
    private readonly NetworkWritePlanner _planner = new(client);
    // Detached copies prevent later re-plans or presentation from rewriting initial observations.
    private readonly Dictionary<string, NetworkWriteEffect> _initial = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NetworkWriteEffect> _current = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NetworkMutationVerificationInfo> _immediate = new(StringComparer.Ordinal);
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
        if (plan.Success) for (var i = 0; i < items.Count; i++)
        {
            var effect = plan.Items[i].Effect!;
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
        var replan = await _planner.ReplanAsync(path, item).ConfigureAwait(false);
        if (replan.Success) _current[item.OperationId] = Copy(replan.Plan!.Effect!);
        return replan;
    }
    public Task<WorkerCallResult> MutateAsync(string? path, NetworkOperationRequest item)
    { _attempted.Add(item.OperationId); return NetworkWorkerInvoker.InvokeWriteAsync(client, item, path); }
    public StructuredOperationItem Project(NetworkOperationRequest item, WorkerCallResult result)
    {
        var projected = NetworkPayloadContract.Project(item, result, requireVerification: true);
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
