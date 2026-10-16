using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Vci;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Vci;

public sealed class VciWriteDomain(OpennessWorkerClient client, VciBreadcrumbLocation breadcrumb)
    : IWriteDomain<VciOperationRequest, VciWriteEffect, VciWriteVerification, VciGuardedWriteResponse>
{
    private readonly VciWritePlanner _planner = new(client);
    private readonly Dictionary<string, IReadOnlyList<FiredGuard>> _guards = new(StringComparer.Ordinal);
    private readonly HashSet<string> _deferred = new(StringComparer.Ordinal);
    private IReadOnlyList<VciOperationRequest> _items = [];
    public string ToolName => "vci_write";
    public string ContractVersion => VciContractVersion.Current;
    public bool ConfirmsEveryCall => false;

    public WriteValidation Validate(IReadOnlyList<VciOperationRequest> items, McpAccessMode accessMode)
    {
        var validation = VciWriteCatalog.ValidateWrite(items);
        if (!validation.IsValid) return WriteValidation.Invalid(validation.Category, validation.Error);
        if (VciWritePayloadBudget.MeasureProtectedCore(items) > StructuredOperationBatchPayloadBudget.MaxDocumentChars)
            return WriteValidation.Invalid(WorkerFailureCategories.ValidationError,
                "The encoded operation identities leave insufficient room for the required response summaries. Use smaller vci_write calls.");
        var errors = VciWriteCatalog.ValidateAccessMode(items, accessMode);
        return errors.Count == 0 ? WriteValidation.Valid() : WriteValidation.Invalid(WorkerFailureCategories.AccessDenied, string.Join("\n", errors));
    }

    public async Task<WritePlan<VciWriteEffect>> PlanAsync(string? projectPath, IReadOnlyList<VciOperationRequest> items)
    {
        _items = items.ToArray();
        var result = await _planner.PlanAsync(projectPath, items).ConfigureAwait(false);
        foreach (var pair in result.Guards) _guards[pair.Key] = pair.Value;
        if (result.Plan.Success)
            for (var i = 0; i < items.Count; i++)
                if (result.Plan.Items[i].DependsOn is not null) _deferred.Add(items[i].OperationId);
        return result.Plan;
    }

    public IReadOnlyList<FiredGuard> EvaluateGuards(IReadOnlyList<VciOperationRequest> items, IReadOnlyList<ItemPlan<VciWriteEffect>> plans)
        => items.SelectMany(i => _guards.GetValueOrDefault(i.OperationId) ?? []).ToArray();

    public async Task<ItemReplan<VciWriteEffect>> ReplanAsync(string? projectPath, VciOperationRequest item)
    {
        var result = await _planner.ReplanAsync(projectPath, item).ConfigureAwait(false);
        _guards[item.OperationId] = result.Guards;
        if (result.Replan.Success) _deferred.Remove(item.OperationId);
        return result.Replan;
    }

    public async Task<WorkerCallResult> MutateAsync(string? projectPath, VciOperationRequest item)
    {
        var requestId = Guid.NewGuid().ToString("N");
        WorkerCallResult result;
        VciLastAttemptedInfo? last;
        try
        {
            result = await client.WriteVciAsync(VciMethods.ForOperation(item.Operation),
                VciWriteCatalog.ToWorkerRequest(item, requestId, breadcrumb.Path), projectPath).ConfigureAwait(false);
        }
        finally { last = VciBreadcrumbFile.Take(breadcrumb.Path, requestId); }
        if (result.FailureCategory is WorkerFailureCategories.WorkerTimeout or WorkerFailureCategories.WorkerCrashed)
            result = result with { Error = result.Error + (last is null ? "" : " lastAttempted: " + CanonicalJson.Serialize(last))
                + " The leaf may still be completing inside TIA Portal; inspect with vci_read before retrying." };
        return result;
    }

    public StructuredOperationItem Project(VciOperationRequest item, WorkerCallResult result) => VciWritePayloadContract.Project(item, result);
    public async Task<VciWriteVerification?> VerifyAsync(string? projectPath, StructuredOperationBatch batch)
        => await new VciWriteVerifier(client, _items).VerifyAsync(projectPath, batch).ConfigureAwait(false);
    public bool VerificationSucceeded(VciWriteVerification? verification) => verification?.Success == true;

    public VciGuardedWriteResponse Compose(WriteReport<VciWriteEffect, VciWriteVerification> report)
    {
        var deferred = _items.Where(i => _deferred.Contains(i.OperationId)).Select(i => i.OperationId).ToArray();
        var warnings = deferred.Length == 0 ? report.Warnings : report.Warnings.Concat(new[] {
            "Deferred operations [" + string.Join(", ", deferred) + "]: inventory and guard checks await predecessor mutation; no inventory counts are confirmed." }).ToArray();
        return VciWritePayloadBudget.Apply(new(ToolName, ContractVersion, report.Phase, report.Success, report.Error,
            warnings, report.Guards, report.Effects.Select(e => new VciWriteEffectPresentation(e.OperationId, e.Effect, null)).ToArray(),
            report.Batch, report.Verification, null));
    }
}
