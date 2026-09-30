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
    private readonly IWriteBindingStrategy<TItem>? _bindingStrategy;
    private readonly DateTimeOffset _startedAt;
    private readonly long _startTimestamp;
    private readonly List<WriteGuardReport> _guards = new();
    private readonly ItemPlan<TEffect>?[] _plans;
    private readonly StructuredOperationItem?[] _outcomes;
    private readonly bool[] _mutated;
    private readonly long?[] _durations;
    private ProjectBindingSnapshot _binding;
    private StructuredOperationBatch? _batch;
    private bool _applyStarted;
    private bool _audited;
    private int _current = -1;
    private long _itemStart;

    public WriteRun(
        IWriteDomain<TItem, TEffect, TVerification, TResponse> domain,
        WriteCall<TItem> call,
        IWriteBindingGate gate,
        IWriteAuditSink audit,
        WriteGuardCatalog catalog,
        TimeProvider time,
        IWriteBindingStrategy<TItem>? bindingStrategy = null)
    {
        _domain = domain;
        _call = call;
        _gate = gate;
        _audit = audit;
        _catalog = catalog;
        _time = time;
        _bindingStrategy = bindingStrategy;
        _startedAt = time.GetUtcNow();
        _startTimestamp = time.GetTimestamp();
        _binding = gate.CurrentBinding;
        var count = call.Items?.Count ?? 0;
        _plans = new ItemPlan<TEffect>?[count];
        _outcomes = new StructuredOperationItem?[count];
        _mutated = new bool[count];
        _durations = new long?[count];
    }

    private IReadOnlyList<TItem> Items => _call.Items ?? Array.Empty<TItem>();

    /// <summary>
    /// Validation, gate, pin, and lease failures are reported and audited outside the lease;
    /// everything from planning to the audit append runs inside it, under the pinned snapshot.
    /// </summary>
    public async Task<CallToolResult> ExecuteAsync()
    {
        var rejection = ValidateInput();
        if (rejection is not null)
        {
            return Finish(Failure(WritePhases.Error, rejection));
        }

        if (_bindingStrategy is not null)
        {
            var prepared = await _bindingStrategy.PrepareAsync(_call).ConfigureAwait(false);
            if (!prepared.Success)
            {
                return Finish(Failure(WritePhases.Error, prepared.Error ?? new WriteToolError(
                    WorkerFailureCategories.BindingConflict, "The lifecycle binding could not be prepared.")));
            }

            _binding = prepared.Binding ?? throw new InvalidOperationException(
                "A successful write preparation must return the exact snapshot to pin.");
        }
        else
        {
            var beforeGate = _gate.CurrentBinding;
            var gate = await _gate.RequireVerifiedWriteBindingAsync(_call.ProjectPath).ConfigureAwait(false);
            if (!gate.Success)
            {
                return Finish(Failure(WritePhases.Error, new WriteToolError(
                    WorkerFailureCategories.BindingConflict,
                    gate.Error ?? "The session holds no verified binding for this project.")));
            }

            var pinFailure = PinBinding(beforeGate);
            if (pinFailure is not null)
            {
                return Finish(Failure(WritePhases.Error, pinFailure));
            }
        }

        var lease = await _gate.RunUnderLeaseAsync(_binding, RunLeasedAndFinishAsync).ConfigureAwait(false);
        return lease.Success
            ? lease.Value!
            : Finish(Failure(WritePhases.Error, lease.Error ?? new WriteToolError(
                WorkerFailureCategories.BindingConflict, "The project binding changed before the write could run.")));
    }

    /// <summary>
    /// Pins the snapshot the lease runs under. A binding that was already verified must be unchanged
    /// by the gate; a binding the gate itself verified must now be verified for the same project.
    /// Anything else means the binding moved between the gate and the lease.
    /// </summary>
    private WriteToolError? PinBinding(ProjectBindingSnapshot beforeGate)
    {
        var pinned = _gate.CurrentBinding;
        _binding = pinned;
        var consistent = string.Equals(beforeGate.State, ProjectBindingSnapshot.VerifiedState, StringComparison.Ordinal)
            ? string.Equals(beforeGate.BindingId, pinned.BindingId, StringComparison.Ordinal)
                && beforeGate.Revision == pinned.Revision
            : string.Equals(pinned.State, ProjectBindingSnapshot.VerifiedState, StringComparison.Ordinal)
                && string.Equals(beforeGate.ProjectPath, pinned.ProjectPath, StringComparison.OrdinalIgnoreCase);
        return consistent
            ? null
            : new WriteToolError(
                WorkerFailureCategories.BindingConflict,
                "The project binding changed while the write was being checked. Check the project status and retry.");
    }

    private WriteToolError? ValidateInput()
    {
        if (Items.Count == 0)
        {
            return Validation("The call must contain at least one operation.");
        }

        for (var index = 0; index < Items.Count; index++)
        {
            if (Items[index] is null || string.IsNullOrWhiteSpace(Items[index].OperationId))
            {
                return Validation($"The operation at index {index} needs a non-blank operationId.");
            }
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

    /// <summary>
    /// Runs the leased stages. An exception after the lease started is audited (a mutation may
    /// already have happened) and then rethrown unchanged.
    /// </summary>
    private async Task<CallToolResult> RunLeasedAndFinishAsync()
    {
        try
        {
            return Finish(await RunLeasedAsync().ConfigureAwait(false));
        }
        catch (Exception ex) when (!_audited)
        {
            AuditUnexpectedFailure(ex);
            throw;
        }
    }

    private async Task<WriteReport<TEffect, TVerification>> RunLeasedAsync()
    {
        var plan = await _domain.PlanAsync(_call.ProjectPath, Items).ConfigureAwait(false);
        if (!plan.Success)
        {
            return Failure(WritePhases.Error, plan.Error ?? new WriteToolError(
                WorkerFailureCategories.TargetNotFound, "The write targets could not be resolved."));
        }

        if (plan.Items.Count != Items.Count || plan.Items.Any(item => item is null))
        {
            throw new InvalidOperationException(
                $"'{_domain.ToolName}' must plan exactly one non-null item per operation ({Items.Count}).");
        }

        plan.Items.ToArray().CopyTo(_plans, 0);
        var decision = GuardDecisions.Decide(
            _domain.EvaluateGuards(Items, plan.Items),
            _call.Acknowledge,
            _catalog,
            _call.DryRun);
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
        _applyStarted = true;
        var stopped = false;
        for (var index = 0; index < Items.Count; index++)
        {
            if (stopped)
            {
                _outcomes[index] = Skipped(Items[index]);
                continue;
            }

            _current = index;
            _itemStart = _time.GetTimestamp();
            var item = await RunItemAsync(index).ConfigureAwait(false);
            _durations[index] = ElapsedMs(_itemStart);
            _outcomes[index] = item;
            stopped = !string.Equals(item.Status, OperationBatchStatus.Succeeded, StringComparison.Ordinal);
        }

        _current = -1;
        _batch = FinalizeBatch();
        var verification = await _domain.VerifyAsync(_call.ProjectPath, _batch).ConfigureAwait(false);
        // Dispatch failures belong to the typed outcomes, not the call rejection field.
        return Report(WritePhases.Applied, _batch.IsFullySuccessful, error: null, _batch, verification);
    }

    /// <summary>Snapshots the outcomes, including operations skipped after an unexpected failure.</summary>
    private StructuredOperationBatch FinalizeBatch()
    {
        var items = Items.Select((item, index) => _outcomes[index] ?? Skipped(item)).ToList();
        var batch = MarkPartialWrite(items);
        batch.Operations.ToArray().CopyTo(_outcomes, 0);
        return batch;
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

        _mutated[index] = true;
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

    /// <summary>
    /// In a multi-item call, the item that stopped the call carries the partial-write guard and
    /// warning. Its wording depends on whether that item's own mutation was attempted.
    /// </summary>
    private StructuredOperationBatch MarkPartialWrite(List<StructuredOperationItem> items)
    {
        var index = items.FindIndex(
            item => string.Equals(item.Status, OperationBatchStatus.Failed, StringComparison.Ordinal));
        var message = items.Count < 2 || index < 0 ? null
            : _mutated[index] ? WriteExecution.PartialWriteMessage
            : index > 0 ? WriteExecution.PartialWriteNotMutatedMessage
            : null;
        if (message is null)
        {
            return StructuredOperationBatch.FromItems(items);
        }

        var failed = items[index];
        items[index] = failed with { Warnings = failed.Warnings.Append(message).ToArray() };
        _guards.Add(new WriteGuardReport(
            WriteGuardCatalog.PartialWriteGuardId,
            _catalog.Get(WriteGuardCatalog.PartialWriteGuardId).Severity,
            failed.OperationId,
            message,
            Acknowledged: null));
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
        _audited = true;
        var isError = report.Phase is WritePhases.Error or WritePhases.Blocked;
        return StructuredToolResult.CreateCanonical(text, isError);
    }

    /// <summary>
    /// Audits a call that threw inside the lease. The domain may be what failed, so the recorded
    /// response is the pipeline's own report rather than a composed one. Never throws.
    /// </summary>
    private void AuditUnexpectedFailure(Exception ex)
    {
        try
        {
            var error = new WriteToolError(
                WorkerFailureCategories.WorkerOperationFailed,
                $"The write pipeline failed unexpectedly: {ex.GetType().Name}: {ex.Message}");
            if (_current >= 0 && _outcomes[_current] is null)
            {
                _outcomes[_current] = Failed(Items[_current], error);
                _durations[_current] = ElapsedMs(_itemStart);
            }

            if (_applyStarted && _batch is null)
            {
                _batch = FinalizeBatch();
            }

            var report = Report(WritePhases.Error, success: false, error, _batch);
            _audit.Append(BuildAuditRecord(report, CanonicalJson.Serialize(report)));
            _audited = true;
        }
        catch (Exception auditEx)
        {
            Console.Error.WriteLine(
                $"TiaMcpServer: failed to audit a failed '{_domain.ToolName}' call: {auditEx.Message}");
        }
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
            WriteAuditBinding.From(_binding),
            CanonicalJson.ToElement(Items),
            report.Phase,
            text,
            "sha256:" + ContentHashes.Sha256Hex(text),
            report.Guards.Select(AuditGuard).ToArray(),
            Items.Select(AuditItem).ToArray(),
            ElapsedMs(_startTimestamp));

    /// <summary>Preserves accepted live acknowledgements even if a later stage throws.</summary>
    private WriteAuditGuard AuditGuard(WriteGuardReport guard)
        => new(
            guard.Id,
            guard.Severity,
            guard.OperationId,
            guard.Message,
            guard.Acknowledged,
            guard.Acknowledged == true && _applyStarted ? GuardSatisfactions.Agent : null);

    /// <summary>One audit item; an operation that never ran is recorded as skipped with no duration.</summary>
    private WriteAuditItem AuditItem(TItem item, int index)
    {
        var outcome = _outcomes.ElementAtOrDefault(index);
        var plan = _plans.ElementAtOrDefault(index);
        return new WriteAuditItem(
            item?.OperationId ?? string.Empty,
            item?.Operation ?? string.Empty,
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
