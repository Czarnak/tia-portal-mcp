using System.Text.Json;
using TiaMcpServer.Contracts;
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

    public List<string> Calls { get; } = new();

    public WriteValidation Validation { get; set; } = WriteValidation.Valid();

    public WriteToolError? PlanFailure { get; set; }

    public WriteToolError? ReplanFailure { get; set; }

    public int MutationCount { get; private set; }

    public int MutationsOutsideLease { get; private set; }

    public WriteValidation Validate(IReadOnlyList<FakeWriteItem> items, McpAccessMode accessMode)
    {
        Calls.Add("validate");
        return Validation;
    }

    public Task<WritePlan<FakeEffect>> PlanAsync(string? projectPath, IReadOnlyList<FakeWriteItem> items)
    {
        Calls.Add("plan");
        if (PlanFailure is not null)
        {
            return Task.FromResult(WritePlan<FakeEffect>.Fail(PlanFailure.Category, PlanFailure.Message));
        }

        var plans = items
            .Select(item => item.DependsOn is null
                ? ItemPlan<FakeEffect>.Resolved(EffectFor(item), PreconditionsFor(item))
                : ItemPlan<FakeEffect>.DependsOnItem(item.DependsOn, PreconditionsFor(item)))
            .ToList();
        return Task.FromResult(WritePlan<FakeEffect>.Ok(plans));
    }

    public IReadOnlyList<FiredGuard> EvaluateGuards(
        IReadOnlyList<FakeWriteItem> items,
        IReadOnlyList<ItemPlan<FakeEffect>> plans)
    {
        Calls.Add("guards");
        var fired = new List<FiredGuard>();
        for (var i = 0; i < items.Count; i++)
        {
            if (plans[i].Effect is null)
            {
                continue;
            }

            var ids = _replanned.Contains(items[i].OperationId) ? items[i].LateGuards : items[i].Guards;
            fired.AddRange((ids ?? Array.Empty<string>())
                .Select(id => new FiredGuard(id, items[i].OperationId, $"{id} on {items[i].Target}.")));
        }

        return fired;
    }

    public Task<ItemReplan<FakeEffect>> ReplanAsync(string? projectPath, FakeWriteItem item)
    {
        Calls.Add($"replan:{item.OperationId}");
        _replanned.Add(item.OperationId);
        return Task.FromResult(ReplanFailure is null
            ? ItemReplan<FakeEffect>.Ok(ItemPlan<FakeEffect>.Resolved(EffectFor(item), PreconditionsFor(item)))
            : ItemReplan<FakeEffect>.Fail(ReplanFailure.Category, ReplanFailure.Message));
    }

    public Task<WorkerCallResult> MutateAsync(string? projectPath, FakeWriteItem item)
    {
        Calls.Add($"mutate:{item.OperationId}");
        if (!_gate.LeaseActive)
        {
            MutationsOutsideLease++;
        }

        MutationCount++;
        return Task.FromResult(item.FailWith is null
            ? WorkerCallResult.Ok("{\"done\":true}")
            : WorkerCallResult.Fail(item.FailWith, $"{item.OperationId} failed."));
    }

    public StructuredOperationItem Project(FakeWriteItem item, WorkerCallResult result)
    {
        Calls.Add($"project:{item.OperationId}");
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
        Calls.Add("verify");
        return Task.FromResult<FakeVerification?>(new FakeVerification(MutationCount));
    }

    public FakeWriteResponse Compose(WriteReport<FakeEffect, FakeVerification> report)
    {
        Calls.Add("compose");
        return new FakeWriteResponse(
            ToolName, ContractVersion, report.Phase, report.Success, report.Error, report.Warnings,
            report.Guards, report.Effects, report.Batch, report.Verification);
    }

    public static string HashFor(FakeWriteItem item) => $"source:sha256:{item.OperationId}";

    private static FakeEffect EffectFor(FakeWriteItem item) => new(item.Target, "delete");

    private static IReadOnlyList<CheckedPrecondition> PreconditionsFor(FakeWriteItem item)
        => new[] { new CheckedPrecondition(Precondition, HashFor(item), HashFor(item), true) };
}

/// <summary>A binding gate that records gate calls and whether its lease is currently held.</summary>
public sealed class FakeWriteBindingGate : IWriteBindingGate
{
    public const string ProjectPath = @"C:\Projects\Fake\Fake.ap21";

    public McpAccessMode AccessMode { get; set; } = McpAccessMode.ReadWrite;

    public ProjectBindingSnapshot CurrentBinding { get; } = new(
        ProjectBindingSnapshot.VerifiedState, "binding-1", 3, ProjectPath, "session-1", 1, 4242, null);

    public WorkerCallResult GateResult { get; set; } = WorkerCallResult.Ok("{}");

    public WriteToolError? LeaseRefusal { get; set; }

    public int GateCalls { get; private set; }

    public int LeaseCalls { get; private set; }

    public bool LeaseActive { get; private set; }

    public Task<WorkerCallResult> RequireVerifiedWriteBindingAsync(string? projectPath)
    {
        GateCalls++;
        return Task.FromResult(GateResult);
    }

    public async Task<WriteLeaseResult<T>> RunUnderLeaseAsync<T>(
        ProjectBindingSnapshot binding,
        Func<Task<T>> operation)
        where T : class
    {
        LeaseCalls++;
        if (LeaseRefusal is not null)
        {
            return WriteLeaseResult<T>.Fail(LeaseRefusal);
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
