using System.Text.Json;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tests.Safety.Pipeline;

/// <summary>
/// One scripted write item. <see cref="DependsOn"/> makes the plan depend on an earlier item;
/// <see cref="Guards"/> fire in the planning pass, <see cref="LateGuards"/> in the re-plan;
/// <see cref="FailWith"/> makes the mutation fail with that category.
/// </summary>
public sealed record FakeWriteItem(
    string OperationId,
    string Operation = "fake_write",
    string? ProjectPath = null,
    string? DependsOn = null,
    IReadOnlyList<string>? Guards = null,
    IReadOnlyList<string>? LateGuards = null,
    string? FailWith = null) : IOperationBatchItem
{
    public string Target => $"target-{OperationId}";
}

public sealed record FakeEffect(string Target, string Change);

public sealed record FakeVerification(int MutationCount);

public sealed record FakeWriteResponse(
    string Tool,
    string ContractVersion,
    string Phase,
    bool Success,
    WriteToolError? Error,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<WriteGuardReport> Guards,
    IReadOnlyList<WriteEffect<FakeEffect>> Effects,
    StructuredOperationBatch? Batch,
    FakeVerification? Verification);

/// <summary>A scriptable write domain that records every call it receives, in order.</summary>
public sealed class FakeWriteDomain : IWriteDomain<FakeWriteItem, FakeEffect, FakeVerification, FakeWriteResponse>
{
    public const string AcknowledgeGuard = "test_acknowledge";
    public const string BlockGuard = "test_block";
    public const string InfoGuard = "test_info";
    public const string Precondition = "contentHash";

    public static readonly WriteGuardCatalog Catalog = new(new[]
    {
        new WriteGuardDefinition(AcknowledgeGuard, WriteGuardSeverities.Acknowledge, "Needs acknowledgement."),
        new WriteGuardDefinition(BlockGuard, WriteGuardSeverities.Block, "Never allowed."),
        new WriteGuardDefinition(InfoGuard, WriteGuardSeverities.Info, "Informational.")
    });

    private readonly FakeWriteBindingGate _gate;
    private readonly HashSet<string> _replanned = new(StringComparer.Ordinal);

    public FakeWriteDomain(FakeWriteBindingGate gate)
    {
        _gate = gate;
    }

    public string ToolName => "fake_write_tool";

    public string ContractVersion => "fake/1";

    public bool ConfirmsEveryCall { get; set; }

    public string DescribeForConfirmation(IReadOnlyList<FakeWriteItem> items, IReadOnlyList<ItemPlan<FakeEffect>> plans)
        => $"Confirm '{ToolName}': " + string.Join(", ", plans.Where(plan => plan.Effect is not null)
            .Select(plan => $"{plan.Effect!.Change} {plan.Effect.Target}"));

    public List<string> Calls { get; } = new();

    public WriteValidation Validation { get; set; } = WriteValidation.Valid();

    public WriteToolError? PlanFailure { get; set; }

    public WriteToolError? ReplanFailure { get; set; }

    public string EffectChange { get; set; } = "delete";

    public IReadOnlyList<CheckedPrecondition>? PlannedPreconditions { get; set; }

    public IReadOnlyList<string>? PlannedGuards { get; set; }

    /// <summary>A dependent item's plan also carries its simulated effect, as PLC planning does.</summary>
    public bool PlansDependentEffects { get; set; }

    public bool VerificationPasses { get; set; } = true;

    public Action? OnMutate { get; set; }

    /// <summary>The mutation of this operation throws after it was dispatched.</summary>
    public string? ThrowOnMutate { get; set; }

    public string? ThrowOnCall { get; set; }

    public InvalidOperationException ScriptedException { get; } = new("Scripted domain failure.");

    /// <summary>The plan returns a null entry in place of the first item's plan.</summary>
    public bool ReturnNullPlan { get; set; }

    public int MutationCount { get; private set; }

    public int MutationsOutsideLease { get; private set; }

    public WriteValidation Validate(IReadOnlyList<FakeWriteItem> items, McpAccessMode accessMode)
    {
        RecordCall("validate");
        return Validation;
    }

    public Task<WritePlan<FakeEffect>> PlanAsync(string? projectPath, IReadOnlyList<FakeWriteItem> items)
    {
        RecordCall("plan");
        if (PlanFailure is not null)
        {
            return Task.FromResult(WritePlan<FakeEffect>.Fail(PlanFailure.Category, PlanFailure.Message));
        }

        var plans = items
            .Select(item => item.DependsOn is null
                ? ItemPlan<FakeEffect>.Resolved(EffectFor(item), PreconditionsFor(item))
                : PlansDependentEffects
                    ? new ItemPlan<FakeEffect>(EffectFor(item), item.DependsOn, PreconditionsFor(item))
                    : ItemPlan<FakeEffect>.DependsOnItem(item.DependsOn, PreconditionsFor(item)))
            .ToList();
        if (ReturnNullPlan)
        {
            plans[0] = null!;
        }

        return Task.FromResult(WritePlan<FakeEffect>.Ok(plans));
    }

    public IReadOnlyList<FiredGuard> EvaluateGuards(
        IReadOnlyList<FakeWriteItem> items,
        IReadOnlyList<ItemPlan<FakeEffect>> plans)
    {
        RecordCall("guards");
        var fired = new List<FiredGuard>();
        for (var i = 0; i < items.Count; i++)
        {
            if (plans[i].Effect is null)
            {
                continue;
            }

            var ids = PlannedGuards ?? (_replanned.Contains(items[i].OperationId) ? items[i].LateGuards : items[i].Guards);
            fired.AddRange((ids ?? Array.Empty<string>())
                .Select(id => new FiredGuard(id, items[i].OperationId, $"{id} on {items[i].Target}.")));
        }

        return fired;
    }

    public Task<ItemReplan<FakeEffect>> ReplanAsync(string? projectPath, FakeWriteItem item)
    {
        RecordCall($"replan:{item.OperationId}");
        _replanned.Add(item.OperationId);
        return Task.FromResult(ReplanFailure is null
            ? ItemReplan<FakeEffect>.Ok(ItemPlan<FakeEffect>.Resolved(EffectFor(item), PreconditionsFor(item)))
            : ItemReplan<FakeEffect>.Fail(ReplanFailure.Category, ReplanFailure.Message));
    }

    public Task<WorkerCallResult> MutateAsync(string? projectPath, FakeWriteItem item)
    {
        RecordCall($"mutate:{item.OperationId}");
        if (!_gate.LeaseActive)
        {
            MutationsOutsideLease++;
        }

        MutationCount++;
        OnMutate?.Invoke();
        if (ThrowOnMutate == item.OperationId)
        {
            throw new InvalidOperationException($"{item.OperationId} exploded.");
        }

        return Task.FromResult(item.FailWith is null
            ? WorkerCallResult.Ok("{\"done\":true}")
            : WorkerCallResult.Fail(item.FailWith, $"{item.OperationId} failed."));
    }

    public StructuredOperationItem Project(FakeWriteItem item, WorkerCallResult result)
    {
        RecordCall($"project:{item.OperationId}");
        return result.Success
            ? new StructuredOperationItem(
                item.OperationId, item.Operation, OperationBatchStatus.Succeeded,
                JsonDocument.Parse(result.Payload).RootElement.Clone(), null, null, null, Array.Empty<string>())
            : new StructuredOperationItem(
                item.OperationId, item.Operation, OperationBatchStatus.Failed, null,
                new StructuredOperationFailure(result.FailureCategory!, result.Error!), null, null, Array.Empty<string>());
    }

    public Task<FakeVerification?> VerifyAsync(string? projectPath, StructuredOperationBatch batch)
    {
        RecordCall("verify");
        return Task.FromResult<FakeVerification?>(new FakeVerification(MutationCount));
    }

    public bool VerificationSucceeded(FakeVerification? verification) => VerificationPasses;

    public FakeWriteResponse Compose(WriteReport<FakeEffect, FakeVerification> report)
    {
        RecordCall("compose");
        return new FakeWriteResponse(
            ToolName, ContractVersion, report.Phase, report.Success, report.Error, report.Warnings,
            report.Guards, report.Effects, report.Batch, report.Verification);
    }

    public static string HashFor(FakeWriteItem item) => $"source:sha256:{item.OperationId}";

    private void RecordCall(string call)
    {
        Calls.Add(call);
        if (ThrowOnCall == call)
        {
            throw ScriptedException;
        }
    }

    private FakeEffect EffectFor(FakeWriteItem item) => new(item.Target, EffectChange);

    private IReadOnlyList<CheckedPrecondition> PreconditionsFor(FakeWriteItem item)
        => PlannedPreconditions ?? new[] { new CheckedPrecondition(Precondition, HashFor(item), HashFor(item), true) };
}

/// <summary>A binding gate that records gate calls and whether its lease is currently held.</summary>
public sealed class FakeWriteBindingGate : IWriteBindingGate
{
    public const string ProjectPath = @"C:\Projects\Fake\Fake.ap21";

    public McpAccessMode AccessMode { get; set; }

    public ProjectBindingSnapshot CurrentBinding { get; set; } = Snapshot(
        ProjectBindingSnapshot.VerifiedState, "binding-1", 3, ProjectPath);

    public WorkerCallResult GateResult { get; set; } = WorkerCallResult.Ok("{}");

    /// <summary>Runs inside the gate call, e.g. to simulate a verification or a rebind.</summary>
    public Action<FakeWriteBindingGate>? OnGate { get; set; }

    public WriteToolError? LeaseRefusal { get; set; }

    public ProjectBindingSnapshot? LeasedBinding { get; private set; }

    public int GateCalls { get; private set; }

    public int LeaseCalls { get; private set; }

    public bool LeaseActive { get; private set; }

    public static ProjectBindingSnapshot Snapshot(string state, string bindingId, long revision, string? path)
        => new(state, bindingId, revision, path, "session-1", 1, 4242, null);

    public Task<WorkerCallResult> RequireVerifiedWriteBindingAsync(string? projectPath)
    {
        GateCalls++;
        OnGate?.Invoke(this);
        return Task.FromResult(GateResult);
    }

    /// <summary>Refuses a snapshot that is not the current binding, like the real pinned lease.</summary>
    public async Task<WriteLeaseResult<T>> RunUnderLeaseAsync<T>(
        ProjectBindingSnapshot binding,
        Func<Task<T>> operation)
        where T : class
    {
        LeaseCalls++;
        LeasedBinding = binding;
        if (LeaseRefusal is not null)
        {
            return WriteLeaseResult<T>.Fail(LeaseRefusal);
        }

        if (binding.BindingId != CurrentBinding.BindingId || binding.Revision != CurrentBinding.Revision)
        {
            return WriteLeaseResult<T>.Fail(new WriteToolError(
                WorkerFailureCategories.BindingConflict, "Stale binding snapshot."));
        }

        LeaseActive = true;
        try
        {
            return WriteLeaseResult<T>.Ok(await operation());
        }
        finally
        {
            LeaseActive = false;
        }
    }
}

/// <summary>Keeps every appended record and whether the gate's lease was held at append time.</summary>
public sealed class RecordingAuditSink : IWriteAuditSink
{
    private readonly FakeWriteBindingGate _gate;

    public RecordingAuditSink(FakeWriteBindingGate gate)
    {
        _gate = gate;
    }

    public List<WriteAuditRecord> Records { get; } = new();

    public List<bool> LeaseActiveAtAppend { get; } = new();

    public void Append(WriteAuditRecord record)
    {
        Records.Add(record);
        LeaseActiveAtAppend.Add(_gate.LeaseActive);
    }
}

/// <summary>A clock whose timestamp advances by a fixed step on every read.</summary>
public sealed class SteppingTimeProvider : TimeProvider
{
    public static readonly DateTimeOffset Start = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

    private readonly long _step;
    private long _ticks;

    public SteppingTimeProvider(TimeSpan step)
    {
        _step = step.Ticks;
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _ticks += _step;

    public override DateTimeOffset GetUtcNow() => Start + TimeSpan.FromTicks(_ticks);
}
