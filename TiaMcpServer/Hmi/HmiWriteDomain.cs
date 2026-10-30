using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Safety;

namespace TiaMcpServer.Hmi;

/// <summary>
/// The hmi_write domain for the guarded pipeline; one instance per call. The planner's guards are
/// cached by operationId so <see cref="EvaluateGuards"/> stays pure; plans declare no dependencies,
/// so nothing is re-planned. Only block and info guards exist, so nothing ever asks the user.
/// </summary>
public sealed class HmiWriteDomain(OpennessWorkerClient client)
    : IWriteDomain<HmiWriteOperationRequest, HmiWriteEffect, WriteVerification, HmiGuardedWriteResponse>
{
    private readonly HmiWritePlanner _planner = new(client);
    private readonly Dictionary<string, IReadOnlyList<FiredGuard>> _guards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HmiWriteEffect> _effects = new(StringComparer.Ordinal);
    private readonly HashSet<string> _attempted = new(StringComparer.Ordinal);
    private IReadOnlyList<HmiWriteOperationRequest> _items = Array.Empty<HmiWriteOperationRequest>();

    public string ToolName => "hmi_write";

    public string ContractVersion => HmiWriteContractVersion.Current;

    public bool ConfirmsEveryCall => false;

    public WriteValidation Validate(IReadOnlyList<HmiWriteOperationRequest> items, McpAccessMode accessMode)
        => WriteValidation.Compose(
            HmiWriteCatalog.ValidateWrite(items),
            () => HmiWritePayloadBudget.MeasureProtectedCore(items),
            () => HmiWriteCatalog.ValidateAccessMode(items, accessMode),
            ToolName);

    public async Task<WritePlan<HmiWriteEffect>> PlanAsync(IReadOnlyList<HmiWriteOperationRequest> items)
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

    public IReadOnlyList<FiredGuard> EvaluateGuards(IReadOnlyList<HmiWriteOperationRequest> items, IReadOnlyList<ItemPlan<HmiWriteEffect>> plans)
        => items.SelectMany(item => _guards.GetValueOrDefault(item.OperationId) ?? Array.Empty<FiredGuard>()).ToArray();

    public Task<ItemReplan<HmiWriteEffect>> ReplanAsync(HmiWriteOperationRequest item)
        => throw new InvalidOperationException("hmi_write plans declare no dependencies; ReplanAsync must never be called.");

    public Task<WorkerCallResult> MutateAsync(HmiWriteOperationRequest item)
    {
        _attempted.Add(item.OperationId);
        return client.WriteHmiAsync(HmiOperationCatalog.WorkerMethod(item.Operation), HmiWriteCatalog.ToWorkerRequest(item, _planner.ResolvedHmiName(item.OperationId)));
    }

    public StructuredOperationItem Project(HmiWriteOperationRequest item, WorkerCallResult result)
        => HmiWritePayloadContract.Project(item, result);

    public async Task<WriteVerification?> VerifyAsync(StructuredOperationBatch batch)
        => await new TrustedWriteVerifier<HmiWriteOperationRequest>(_items, new HmiVerificationPolicy(_effects)).VerifyAsync(
            StructuredOperationBatch.FromItems(batch.Operations.Where(o => _attempted.Contains(o.OperationId)).ToArray())).ConfigureAwait(false);

    public bool VerificationSucceeded(WriteVerification? verification) => verification?.Success == true;

    public HmiGuardedWriteResponse Compose(WriteReport<HmiWriteEffect, WriteVerification> report)
        => HmiWritePayloadBudget.Apply(new HmiGuardedWriteResponse(ToolName, ContractVersion, report.Phase, report.Success,
            report.Error, report.Warnings, report.Guards,
            report.Effects.Select(e => new HmiWriteEffectPresentation(e.OperationId, e.Effect, null)).ToArray(),
            report.Batch, report.Verification, null));
}
