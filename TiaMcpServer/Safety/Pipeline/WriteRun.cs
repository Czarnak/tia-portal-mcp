using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Tools;

namespace TiaMcpServer.Safety.Pipeline;

/// <summary>
/// The state of one <see cref="WriteExecution"/> call: plans, fired guards, per-item outcomes and
/// durations. Used once and discarded; it keeps <see cref="WriteExecution"/> itself small.
/// </summary>
internal sealed class WriteRun<TItem, TEffect, TVerification, TResponse>
    where TItem : IOperationBatchItem
{
    private readonly IWriteDomain<TItem, TEffect, TVerification, TResponse> _domain;
    private readonly WriteCall<TItem> _call;
    private readonly IWriteBindingGate _gate;
    private readonly IWriteAuditSink _audit;
    private readonly WriteGuardCatalog _catalog;
    private readonly TimeProvider _time;
    private readonly DateTimeOffset _startedAt;
    private readonly long _startTimestamp;
    private readonly List<WriteGuardReport> _guards = new();
    private readonly ItemPlan<TEffect>?[] _plans;
    private readonly long?[] _durations;
    private StructuredOperationBatch? _batch;

    public WriteRun(
        IWriteDomain<TItem, TEffect, TVerification, TResponse> domain,
        WriteCall<TItem> call,
        IWriteBindingGate gate,
        IWriteAuditSink audit,
        WriteGuardCatalog catalog,
        TimeProvider time)
    {
        _domain = domain;
        _call = call;
        _gate = gate;
        _audit = audit;
        _catalog = catalog;
        _time = time;
        _startedAt = time.GetUtcNow();
        _startTimestamp = time.GetTimestamp();
        var count = call.Items?.Count ?? 0;
        _plans = new ItemPlan<TEffect>?[count];
        _durations = new long?[count];
    }

    private IReadOnlyList<TItem> Items => _call.Items ?? Array.Empty<TItem>();

    /// <summary>
    /// Validation, gate, and lease failures are reported and audited outside the lease; everything
    /// from planning to the audit append runs inside it.
    /// </summary>
    public async Task<CallToolResult> ExecuteAsync()
    {
        var rejection = ValidateInput();
        if (rejection is not null)
        {
            return Finish(Failure(WritePhases.Error, rejection));
        }

        var gate = await _gate.RequireVerifiedWriteBindingAsync(_call.ProjectPath).ConfigureAwait(false);
        if (!gate.Success)
        {
            return Finish(Failure(WritePhases.Error, new WriteToolError(
                WorkerFailureCategories.BindingConflict,
                gate.Error ?? "The session holds no verified binding for this project.")));
        }

        var lease = await _gate
            .RunUnderLeaseAsync(_gate.CurrentBinding, async () => Finish(await RunLeasedAsync().ConfigureAwait(false)))
            .ConfigureAwait(false);
        return lease.Success
            ? lease.Value!
            : Finish(Failure(WritePhases.Error, lease.Error ?? new WriteToolError(
                WorkerFailureCategories.BindingConflict, "The project binding changed before the write could run.")));
    }

    private WriteToolError? ValidateInput()
    {
        if (Items.Count == 0)
        {
            return Validation("The call must contain at least one operation.");
        }

        var duplicate = Items
            .GroupBy(item => item.OperationId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            return Validation($"operationId '{duplicate.Key}' appears more than once in the call.");
        }

        var validation = _domain.Validate(Items, _gate.AccessMode);
        if (!validation.IsValid)
        {
            return validation.Error ?? Validation("The write request is invalid.");
        }

        var acknowledge = GuardDecisions.ValidateAcknowledgeList(_call.Acknowledge, _catalog);
        return acknowledge is null ? null : Validation(acknowledge);
    }

    private async Task<WriteReport<TEffect, TVerification>> RunLeasedAsync()
    {
        var plan = await _domain.PlanAsync(_call.ProjectPath, Items).ConfigureAwait(false);
        if (!plan.Success)
        {
            return Failure(WritePhases.Error, plan.Error ?? new WriteToolError(
                WorkerFailureCategories.TargetNotFound, "The write targets could not be resolved."));
        }

        if (plan.Items.Count != Items.Count)
        {
            throw new InvalidOperationException(
                $"'{_domain.ToolName}' planned {plan.Items.Count} item(s) for {Items.Count} operation(s).");
        }

        plan.Items.ToArray().CopyTo(_plans, 0);
        var decision = GuardDecisions.Decide(
            _domain.EvaluateGuards(Items, plan.Items), _call.Acknowledge, _catalog, _call.DryRun);
        _guards.AddRange(decision.Guards);
        switch (decision.Kind)
        {
            case GuardDecisionKind.Invalid:
                return Failure(WritePhases.Error, Validation(decision.Message!));
            case GuardDecisionKind.Blocked:
                return Failure(WritePhases.Blocked, new WriteToolError(
                    WorkerFailureCategories.GuardBlocked, decision.Message!));
        }

        return _call.DryRun
            ? Report(WritePhases.Preview, success: true, error: null)
            : await MutateAndVerifyAsync().ConfigureAwait(false);
    }

    private async Task<WriteReport<TEffect, TVerification>> MutateAndVerifyAsync()
    {
        var items = new List<StructuredOperationItem>(Items.Count);
        var stopped = false;
        for (var index = 0; index < Items.Count; index++)
        {
            if (stopped)
            {
                items.Add(Skipped(Items[index]));
                continue;
            }

            var start = _time.GetTimestamp();
            var item = await RunItemAsync(index).ConfigureAwait(false);
            _durations[index] = ElapsedMs(start);
            items.Add(item);
            stopped = !string.Equals(item.Status, OperationBatchStatus.Succeeded, StringComparison.Ordinal);
        }

        _batch = MarkPartialWrite(items);
        var verification = await _domain.VerifyAsync(_call.ProjectPath, _batch).ConfigureAwait(false);
        var failure = _batch.Operations.FirstOrDefault(item => item.Failure is not null)?.Failure;
        var error = failure is null ? null : new WriteToolError(failure.Category, failure.Message);
        return Report(WritePhases.Applied, _batch.IsFullySuccessful, error, _batch, verification);
    }

    private async Task<StructuredOperationItem> RunItemAsync(int index)
    {
        var item = Items[index];
        if (_plans[index]!.DependsOn is not null)
        {
            var failure = await ReplanAsync(index).ConfigureAwait(false);
            if (failure is not null)
            {
                return Failed(item, failure);
            }
        }

        var result = await _domain.MutateAsync(_call.ProjectPath, item).ConfigureAwait(false);
        return _domain.Project(item, result);
    }

    /// <summary>Re-plans a dependent item and applies the late acknowledge rule to its guards.</summary>
    private async Task<WriteToolError?> ReplanAsync(int index)
    {
        var item = Items[index];
        var replan = await _domain.ReplanAsync(_call.ProjectPath, item).ConfigureAwait(false);
        if (!replan.Success)
        {
            return replan.Error ?? new WriteToolError(
                WorkerFailureCategories.TargetNotFound, $"Operation '{item.OperationId}' could not be re-resolved.");
        }

        var plan = replan.Plan ?? throw new InvalidOperationException(
            $"'{_domain.ToolName}' re-planned '{item.OperationId}' successfully without a plan.");
        _plans[index] = plan;
        var decision = GuardDecisions.DecideLate(
            _domain.EvaluateGuards(new[] { item }, new[] { plan }), _call.Acknowledge, _catalog);
        _guards.AddRange(decision.Guards);
        return decision.Kind == GuardDecisionKind.Blocked
            ? new WriteToolError(WorkerFailureCategories.GuardBlocked, decision.Message!)
            : null;
    }

    /// <summary>In a multi-item call, the item that failed carries the partial-write guard and warning.</summary>
    private StructuredOperationBatch MarkPartialWrite(List<StructuredOperationItem> items)
    {
        var index = items.FindIndex(
            item => string.Equals(item.Status, OperationBatchStatus.Failed, StringComparison.Ordinal));
        if (items.Count > 1 && index >= 0)
        {
            var failed = items[index];
            items[index] = failed with { Warnings = failed.Warnings.Append(WriteExecution.PartialWriteMessage).ToArray() };
            _guards.Add(new WriteGuardReport(
                WriteGuardCatalog.PartialWriteGuardId,
                _catalog.Get(WriteGuardCatalog.PartialWriteGuardId).Severity,
                failed.OperationId,
                WriteExecution.PartialWriteMessage,
                Acknowledged: null));
        }

        return StructuredOperationBatch.FromItems(items);
    }

    private WriteReport<TEffect, TVerification> Failure(string phase, WriteToolError error)
        => Report(phase, success: false, error);

    private WriteReport<TEffect, TVerification> Report(
        string phase,
        bool success,
        WriteToolError? error,
        StructuredOperationBatch? batch = null,
        TVerification? verification = default)
    {
        var warnings = _guards
            .Where(guard => guard.Severity == WriteGuardSeverities.Info)
            .Select(guard => guard.Message)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new WriteReport<TEffect, TVerification>(
            phase, success, error, warnings, _guards.ToArray(), Effects(), batch, verification);
    }

    private IReadOnlyList<WriteEffect<TEffect>> Effects()
        => Items
            .Select((item, index) => (item, plan: _plans.ElementAtOrDefault(index)))
            .Where(pair => HasEffect(pair.plan))
            .Select(pair => new WriteEffect<TEffect>(pair.item.OperationId, pair.plan!.Effect!))
            .ToArray();

    /// <summary>Composes the response once, audits it, and returns the same text as the tool result.</summary>
    private CallToolResult Finish(WriteReport<TEffect, TVerification> report)
    {
        var text = CanonicalJson.Serialize(_domain.Compose(report));
        _audit.Append(BuildAuditRecord(report, text));
        var isError = report.Phase is WritePhases.Error or WritePhases.Blocked;
        return StructuredToolResult.CreateCanonical(text, isError);
    }

    private WriteAuditRecord BuildAuditRecord(WriteReport<TEffect, TVerification> report, string text)
        => new(
            WriteAuditRecord.Kind,
            WriteAuditRecord.CurrentVersion,
            _startedAt,
            _domain.ToolName,
            _domain.ContractVersion,
            WriteAuditRecord.ModeName(_gate.AccessMode),
            _call.ProjectPath,
            WriteAuditBinding.From(_gate.CurrentBinding),
            CanonicalJson.ToElement(Items),
            report.Phase,
            text,
            "sha256:" + ContentHashes.Sha256Hex(text),
            report.Guards.Select(guard => AuditGuard(guard, report.Phase)).ToArray(),
            Items.Select(AuditItem).ToArray(),
            ElapsedMs(_startTimestamp));

    /// <summary>An acknowledge guard counts as satisfied by the agent only when the write was applied.</summary>
    private static WriteAuditGuard AuditGuard(WriteGuardReport guard, string phase)
        => new(
            guard.Id,
            guard.Severity,
            guard.OperationId,
            guard.Message,
            guard.Acknowledged,
            guard.Acknowledged == true && phase == WritePhases.Applied ? GuardSatisfactions.Agent : null);

    private WriteAuditItem AuditItem(TItem item, int index)
    {
        var outcome = _batch?.Operations[index];
        var plan = _plans.ElementAtOrDefault(index);
        return new WriteAuditItem(
            item.OperationId,
            item.Operation,
            HasEffect(plan) ? CanonicalJson.Serialize(plan!.Effect) : null,
            plan?.Preconditions ?? Array.Empty<CheckedPrecondition>(),
            outcome?.Status ?? OperationBatchStatus.Skipped,
            outcome?.Failure?.Category,
            outcome?.Failure?.Message,
            outcome?.Warnings ?? Array.Empty<string>(),
            _durations.ElementAtOrDefault(index));
    }

    private static bool HasEffect(ItemPlan<TEffect>? plan) => plan is not null && plan.Effect is not null;

    private long ElapsedMs(long startTimestamp)
        => (long)Math.Ceiling(_time.GetElapsedTime(startTimestamp).TotalMilliseconds);

    private static WriteToolError Validation(string message)
        => new(WorkerFailureCategories.ValidationError, message);

    private static StructuredOperationItem Skipped(TItem item)
        => new(
            item.OperationId, item.Operation, OperationBatchStatus.Skipped, Result: null, Failure: null,
            Omission: null, StructuredOperationSkipReasons.EarlierOperationFailed, Array.Empty<string>());

    private static StructuredOperationItem Failed(TItem item, WriteToolError error)
        => new(
            item.OperationId, item.Operation, OperationBatchStatus.Failed, Result: null,
            new StructuredOperationFailure(error.Category, error.Message), Omission: null, SkipReason: null,
            Array.Empty<string>());
}
