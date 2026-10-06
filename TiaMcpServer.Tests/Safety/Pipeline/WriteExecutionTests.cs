using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Safety.Pipeline;

public sealed class WriteExecutionTests
{
    private const string Ack = FakeWriteDomain.AcknowledgeGuard;
    private const string Block = FakeWriteDomain.BlockGuard;
    private const string Info = FakeWriteDomain.InfoGuard;

    private static readonly string[] ApplyA =
        { "validate", "plan", "guards", "mutate:a", "project:a", "verify", "compose" };

    private readonly FakeWriteBindingGate _gate = new() { AccessMode = McpAccessMode.Full };
    private readonly FakeWriteDomain _domain;
    private readonly RecordingAuditSink _audit;
    private readonly WriteExecution _execution;

    public WriteExecutionTests()
    {
        _domain = new FakeWriteDomain(_gate);
        _audit = new RecordingAuditSink(_gate);
        _execution = new WriteExecution(
            _gate, _audit, FakeWriteDomain.Catalog, new SteppingTimeProvider(TimeSpan.FromMilliseconds(7)));
    }

    [Fact]
    public async Task EmptyCall_IsRejectedBeforeTheGate()
    {
        var (result, doc) = await RunAsync(Array.Empty<FakeWriteItem>());

        AssertOutcome(result, doc, WritePhases.Error, isError: true, WorkerFailureCategories.ValidationError);
        Assert.Equal(0, _gate.GateCalls);
        Assert.Equal(new[] { "compose" }, _domain.Calls);
        AssertAuditedOutsideLease();
    }

    [Fact]
    public async Task DuplicateOperationIds_AreRejectedBeforeTheGate()
    {
        var (result, doc) = await RunAsync(new[] { Item("a"), Item("a") });

        AssertOutcome(result, doc, WritePhases.Error, isError: true, WorkerFailureCategories.ValidationError);
        Assert.Contains("'a'", ErrorMessage(doc));
        Assert.Equal(0, _gate.GateCalls);
        Assert.Equal(new[] { "compose" }, _domain.Calls);
    }

    [Fact]
    public async Task NullItemOrBlankOperationId_IsRejectedBeforeTheGate()
    {
        var (nullResult, nullDoc) = await RunAsync(new[] { Item("a"), null! });
        AssertOutcome(nullResult, nullDoc, WritePhases.Error, isError: true, WorkerFailureCategories.ValidationError);
        Assert.Contains("index 1", ErrorMessage(nullDoc));

        var blank = new WriteExecutionTests();
        var (blankResult, blankDoc) = await blank.RunAsync(new[] { Item(" ") });
        AssertOutcome(blankResult, blankDoc, WritePhases.Error, isError: true, WorkerFailureCategories.ValidationError);

        Assert.Equal(0, _gate.GateCalls + blank._gate.GateCalls);
        Assert.Equal(new[] { "compose" }, _domain.Calls);
        Assert.Equal(new[] { "compose" }, blank._domain.Calls);
    }

    [Fact]
    public async Task DomainValidation_KeepsItsCategory()
    {
        _domain.Validation = WriteValidation.Invalid(WorkerFailureCategories.AccessDenied, "Read-only session.");

        var (result, doc) = await RunAsync(new[] { Item("a") });

        AssertOutcome(result, doc, WritePhases.Error, isError: true, WorkerFailureCategories.AccessDenied);
        Assert.Equal("Read-only session.", ErrorMessage(doc));
        Assert.Equal(0, _gate.GateCalls);
        Assert.Equal(new[] { "validate", "compose" }, _domain.Calls);
    }

    [Fact]
    public async Task GateFailure_StopsBeforePlanning_AsBindingConflict()
    {
        _gate.GateResult = WorkerCallResult.Fail(WorkerFailureCategories.ProtocolError, "No verified binding.");

        var (result, doc) = await RunAsync(new[] { Item("a") });

        AssertOutcome(result, doc, WritePhases.Error, isError: true, WorkerFailureCategories.BindingConflict);
        Assert.Equal("No verified binding.", ErrorMessage(doc));
        Assert.Equal(0, _gate.LeaseCalls);
        Assert.Equal(new[] { "validate", "compose" }, _domain.Calls);
        AssertAuditedOutsideLease();
    }

    [Fact]
    public async Task RefusedLease_StopsBeforePlanning_WithTheLeaseCategory()
    {
        _gate.LeaseRefusal = new WriteToolError(WorkerFailureCategories.BindingConflict, "Binding changed.");

        var (result, doc) = await RunAsync(new[] { Item("a") });

        AssertOutcome(result, doc, WritePhases.Error, isError: true, WorkerFailureCategories.BindingConflict);
        Assert.Equal("Binding changed.", ErrorMessage(doc));
        Assert.Equal(1, _gate.GateCalls);
        Assert.Equal(new[] { "validate", "compose" }, _domain.Calls);
        AssertAuditedOutsideLease();
    }

    [Fact]
    public async Task RebindBetweenGateAndLease_IsRefusedBeforeTheLease()
    {
        var rebound = FakeWriteBindingGate.Snapshot(
            ProjectBindingSnapshot.VerifiedState, "binding-2", 4, @"C:\Projects\Other\Other.ap21");
        _gate.OnGate = gate => gate.CurrentBinding = rebound;

        var (result, doc) = await RunAsync(new[] { Item("a") });

        AssertOutcome(result, doc, WritePhases.Error, isError: true, WorkerFailureCategories.BindingConflict);
        Assert.Equal(0, _gate.LeaseCalls);
        Assert.Equal(new[] { "validate", "compose" }, _domain.Calls);
        Assert.Equal("binding-2", _audit.Records[0].Binding!.BindingId);
        AssertAuditedOutsideLease();
    }

    [Fact]
    public async Task GateThatVerifiesTheStartupBinding_PinsTheVerifiedSnapshot()
    {
        _gate.CurrentBinding = FakeWriteBindingGate.Snapshot(
            ProjectBindingSnapshot.ConfiguredUnverifiedState, "binding-0", 1, FakeWriteBindingGate.ProjectPath);
        _gate.OnGate = gate => gate.CurrentBinding = FakeWriteBindingGate.Snapshot(
            ProjectBindingSnapshot.VerifiedState, "binding-1", 2, FakeWriteBindingGate.ProjectPath);

        var (result, doc) = await RunAsync(new[] { Item("a") });

        AssertOutcome(result, doc, WritePhases.Applied, isError: false, category: null);
        Assert.Equal(ApplyA, _domain.Calls);
        Assert.Equal("binding-1", _gate.LeasedBinding!.BindingId);
        Assert.Equal(2, _audit.Records[0].Binding!.Revision);
    }

    [Fact]
    public async Task GateThatVerifiesADifferentProject_IsRefused()
    {
        _gate.CurrentBinding = FakeWriteBindingGate.Snapshot(
            ProjectBindingSnapshot.ConfiguredUnverifiedState, "binding-0", 1, FakeWriteBindingGate.ProjectPath);
        _gate.OnGate = gate => gate.CurrentBinding = FakeWriteBindingGate.Snapshot(
            ProjectBindingSnapshot.VerifiedState, "binding-1", 2, @"C:\Projects\Other\Other.ap21");

        var (result, doc) = await RunAsync(new[] { Item("a") });

        AssertOutcome(result, doc, WritePhases.Error, isError: true, WorkerFailureCategories.BindingConflict);
        Assert.Equal(0, _gate.LeaseCalls);
        Assert.Equal(new[] { "validate", "compose" }, _domain.Calls);
    }

    [Fact]
    public async Task PlanFailure_MutatesNothing()
    {
        _domain.PlanFailure = new WriteToolError(WorkerFailureCategories.TargetNotFound, "No such target.");

        var (result, doc) = await RunAsync(new[] { Item("a") });

        AssertOutcome(result, doc, WritePhases.Error, isError: true, WorkerFailureCategories.TargetNotFound);
        Assert.Equal(new[] { "validate", "plan", "compose" }, _domain.Calls);
        AssertAuditedInsideLease();
    }

    [Fact]
    public async Task NullItemPlan_Throws_AndIsAudited()
    {
        _domain.ReturnNullPlan = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _execution.RunAsync(_domain, Call(new[] { Item("a") })));

        Assert.Equal(new[] { "validate", "plan" }, _domain.Calls);
        var record = Assert.Single(_audit.Records);
        Assert.Equal(WritePhases.Error, record.Phase);
        AssertAuditedInsideLease();
    }

    [Fact]
    public async Task DryRun_ReportsEffectsAndGuards_AndMutatesNothing()
    {
        _gate.AccessMode = McpAccessMode.ReadWrite;
        var (result, doc) = await RunAsync(
            new[] { Item("a", guards: new[] { Ack, Info }), Item("b") }, dryRun: true);

        AssertOutcome(result, doc, WritePhases.Preview, isError: false, category: null);
        Assert.True(doc.GetProperty("success").GetBoolean());
        Assert.Equal(new[] { "validate", "plan", "guards", "compose" }, _domain.Calls);
        Assert.Equal(new[] { "a", "b" }, Strings(doc.GetProperty("effects"), "operationId"));
        var guard = GuardById(doc, Ack);
        Assert.False(guard.GetProperty("acknowledged").GetBoolean());
        Assert.Equal(JsonValueKind.Null, doc.GetProperty("batch").ValueKind);
        Assert.Equal(new[] { $"{Info} on target-a." }, Strings(doc.GetProperty("warnings")));
        Assert.All(_audit.Records[0].Guards, g => Assert.Null(g.SatisfiedBy));
    }

    [Fact]
    public async Task UnacknowledgedGuard_IsBlocked()
    {
        _gate.AccessMode = McpAccessMode.ReadWrite;
        var (result, doc) = await RunAsync(new[] { Item("a", guards: new[] { Ack }) });

        AssertOutcome(result, doc, WritePhases.Blocked, isError: true, WorkerFailureCategories.AccessDenied);
        Assert.Equal(new[] { "validate", "plan", "guards", "compose" }, _domain.Calls);
        Assert.False(GuardById(doc, Ack).GetProperty("acknowledged").GetBoolean());
        Assert.Null(Assert.Single(_audit.Records[0].Guards).SatisfiedBy);
    }

    [Fact]
    public async Task AcknowledgedGuard_IsApplied_AndAuditedAsSatisfiedByPolicy()
    {
        var (result, doc) = await RunAsync(new[] { Item("a", guards: new[] { Ack }) });

        AssertOutcome(result, doc, WritePhases.Applied, isError: false, category: null);
        Assert.True(doc.GetProperty("success").GetBoolean());
        Assert.Equal(ApplyA, _domain.Calls);
        Assert.True(GuardById(doc, Ack).GetProperty("acknowledged").GetBoolean());
        Assert.Equal(GuardSatisfactions.Policy, Assert.Single(_audit.Records[0].Guards).SatisfiedBy);
    }

    [Fact]
    public async Task BlockGuard_RefusesDespiteAcknowledgements()
    {
        var (result, doc) = await RunAsync(
            new[] { Item("a", guards: new[] { Ack, Block }) });

        AssertOutcome(result, doc, WritePhases.Blocked, isError: true, WorkerFailureCategories.GuardBlocked);
        Assert.Equal(new[] { "validate", "plan", "guards", "compose" }, _domain.Calls);
        Assert.Equal(0, _domain.MutationCount);
        Assert.Contains(Block, ErrorMessage(doc));
    }

    [Fact]
    public async Task InfoGuard_LandsInWarnings_AndDoesNotStopTheWrite()
    {
        var (result, doc) = await RunAsync(new[] { Item("a", guards: new[] { Info }) });

        AssertOutcome(result, doc, WritePhases.Applied, isError: false, category: null);
        Assert.Equal(new[] { $"{Info} on target-a." }, Strings(doc.GetProperty("warnings")));
        Assert.Equal(ApplyA, _domain.Calls);
    }

    [Fact]
    public async Task FailedMiddleItem_StopsTheCall_WithPartialWriteGuard_AndStillVerifies()
    {
        var (result, doc) = await RunAsync(new[]
        {
            Item("a"), Item("b", failWith: WorkerFailureCategories.WorkerOperationFailed), Item("c")
        });

        AssertOutcome(result, doc, WritePhases.Applied, isError: false, category: null);
        Assert.False(doc.GetProperty("success").GetBoolean());
        var operations = doc.GetProperty("batch").GetProperty("operations");
        Assert.Equal(
            new[] { OperationBatchStatus.Succeeded, OperationBatchStatus.Failed, OperationBatchStatus.Skipped },
            Strings(operations, "status"));
        Assert.Contains(WriteExecution.PartialWriteMessage, Strings(operations[1].GetProperty("warnings")));
        var partial = GuardById(doc, WriteGuardCatalog.PartialWriteGuardId);
        Assert.Equal("b", partial.GetProperty("operationId").GetString());
        Assert.Equal(WriteGuardSeverities.Info, partial.GetProperty("severity").GetString());
        Assert.Contains(WriteExecution.PartialWriteMessage, Strings(doc.GetProperty("warnings")));
        Assert.Equal(
            new[] { "validate", "plan", "guards", "mutate:a", "project:a", "mutate:b", "project:b", "verify", "compose" },
            _domain.Calls);
        Assert.Equal(2, doc.GetProperty("verification").GetProperty("mutationCount").GetInt32());
    }

    [Fact]
    public async Task SingleItemFailure_HasNoPartialWriteGuard()
    {
        var (result, doc) = await RunAsync(new[] { Item("a", failWith: WorkerFailureCategories.WorkerOperationFailed) });

        AssertOutcome(result, doc, WritePhases.Applied, isError: false, category: null);
        Assert.False(doc.GetProperty("success").GetBoolean());
        Assert.Empty(doc.GetProperty("guards").EnumerateArray());
        Assert.Empty(doc.GetProperty("warnings").EnumerateArray());
        Assert.Equal(ApplyA, _domain.Calls);
    }

    [Fact]
    public async Task DependentItem_IsReplannedAfterTheEarlierMutation()
    {
        var (result, doc) = await RunAsync(new[] { Item("a"), Item("b", dependsOn: "a") });

        AssertOutcome(result, doc, WritePhases.Applied, isError: false, category: null);
        Assert.Equal(
            new[]
            {
                "validate", "plan", "guards", "mutate:a", "project:a",
                "replan:b", "guards", "mutate:b", "project:b", "verify", "compose"
            },
            _domain.Calls);
        Assert.Equal(new[] { "a", "b" }, Strings(doc.GetProperty("effects"), "operationId"));
    }

    [Fact]
    public async Task DependentItemGuard_FiredAtPlanAndReplan_IsReportedOnce()
    {
        _domain.PlansDependentEffects = true;
        var (result, doc) = await RunAsync(new[]
        {
            Item("a"),
            Item("b", dependsOn: "a", guards: new[] { FakeWriteDomain.InfoGuard }, lateGuards: new[] { FakeWriteDomain.InfoGuard })
        });

        AssertOutcome(result, doc, WritePhases.Applied, isError: false, category: null);
        Assert.Equal(new[] { "b" }, Strings(doc.GetProperty("guards"), "operationId"));
        Assert.Single(Assert.Single(_audit.Records).Guards);
    }

    [Fact]
    public async Task PolicyGuard_CoversItsLateFiring_AndTheDependentItemIsApplied()
    {
        var (result, doc) = await RunAsync(
            new[] { Item("a", guards: new[] { Ack }), Item("b", dependsOn: "a", lateGuards: new[] { Ack }) });

        AssertOutcome(result, doc, WritePhases.Applied, isError: false, category: null);
        Assert.True(doc.GetProperty("success").GetBoolean());
        Assert.Equal(
            new[]
            {
                "validate", "plan", "guards", "mutate:a", "project:a",
                "replan:b", "guards", "mutate:b", "project:b", "verify", "compose"
            },
            _domain.Calls);
        var guards = doc.GetProperty("guards");
        Assert.Equal(new[] { "a", "b" }, Strings(guards, "operationId"));
        Assert.All(guards.EnumerateArray(), guard =>
        {
            Assert.Equal(Ack, guard.GetProperty("id").GetString());
            Assert.True(guard.GetProperty("acknowledged").GetBoolean());
        });
        Assert.All(Assert.Single(_audit.Records).Guards,
            guard => Assert.Equal(GuardSatisfactions.Policy, guard.SatisfiedBy));
    }

    [Fact]
    public async Task LateUnacknowledgedGuard_FailsTheDependentItem_WithGuardBlocked()
    {
        _gate.AccessMode = McpAccessMode.ReadWrite;
        var (result, doc) = await RunAsync(new[]
        {
            Item("a"), Item("b", dependsOn: "a", lateGuards: new[] { Ack }), Item("c")
        });

        AssertOutcome(result, doc, WritePhases.Applied, isError: false, category: null);
        var operations = doc.GetProperty("batch").GetProperty("operations");
        Assert.Equal(
            new[] { OperationBatchStatus.Succeeded, OperationBatchStatus.Failed, OperationBatchStatus.Skipped },
            Strings(operations, "status"));
        Assert.Equal(
            WorkerFailureCategories.GuardBlocked,
            operations[1].GetProperty("failure").GetProperty("category").GetString());
        var warnings = Strings(operations[1].GetProperty("warnings"));
        Assert.Contains(WriteExecution.PartialWriteNotMutatedMessage, warnings);
        Assert.DoesNotContain(WriteExecution.PartialWriteMessage, warnings);
        Assert.False(GuardById(doc, Ack).GetProperty("acknowledged").GetBoolean());
        Assert.Equal(
            new[]
            {
                "validate", "plan", "guards", "mutate:a", "project:a",
                "replan:b", "guards", "verify", "compose"
            },
            _domain.Calls);
    }

    [Fact]
    public async Task FailedReplan_FailsTheDependentItem_WithItsCategory()
    {
        _domain.ReplanFailure = new WriteToolError(WorkerFailureCategories.TargetNotFound, "Gone.");

        var (result, doc) = await RunAsync(new[] { Item("a"), Item("b", dependsOn: "a") });

        AssertOutcome(result, doc, WritePhases.Applied, isError: false, category: null);
        Assert.False(doc.GetProperty("success").GetBoolean());
        Assert.Equal(
            new[] { "validate", "plan", "guards", "mutate:a", "project:a", "replan:b", "verify", "compose" },
            _domain.Calls);
        Assert.Equal(
            WriteExecution.PartialWriteNotMutatedMessage,
            GuardById(doc, WriteGuardCatalog.PartialWriteGuardId).GetProperty("message").GetString());
    }

    [Fact]
    public async Task EveryMutation_AndTheAudit_RunInsideTheLease()
    {
        var (result, doc) = await RunAsync(new[] { Item("a"), Item("b", dependsOn: "a"), Item("c") });

        AssertOutcome(result, doc, WritePhases.Applied, isError: false, category: null);
        Assert.Equal(
            new[]
            {
                "validate", "plan", "guards", "mutate:a", "project:a", "replan:b", "guards",
                "mutate:b", "project:b", "mutate:c", "project:c", "verify", "compose"
            },
            _domain.Calls);
        Assert.Equal(3, _domain.MutationCount);
        Assert.Equal(0, _domain.MutationsOutsideLease);
        Assert.Equal(1, _gate.LeaseCalls);
        AssertAuditedInsideLease();
    }

    [Fact]
    public async Task ExceptionAfterAMutation_IsAuditedInsideTheLease_AndRethrown()
    {
        _domain.ThrowOnMutate = "b";

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _execution.RunAsync(_domain, Call(new[] { Item("a"), Item("b"), Item("c") })));

        Assert.Equal("b exploded.", thrown.Message);
        Assert.Equal(new[] { "validate", "plan", "guards", "mutate:a", "project:a", "mutate:b" }, _domain.Calls);
        var record = Assert.Single(_audit.Records);
        Assert.Equal(WritePhases.Error, record.Phase);
        Assert.Equal(
            new[] { OperationBatchStatus.Succeeded, OperationBatchStatus.Failed, OperationBatchStatus.Skipped },
            record.Items.Select(i => i.Status));
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, record.Items[1].FailureCategory);
        Assert.Contains("b exploded.", record.Items[1].FailureMessage);
        Assert.True(record.Items[1].DurationMs > 0);
        Assert.Contains("b exploded.", record.ResponseText);
        AssertAuditedInsideLease();
    }

    [Fact]
    public async Task AuditRecord_CarriesTheCallAndPerItemDetail()
    {
        var items = new[] { Item("a"), Item("b", failWith: WorkerFailureCategories.WorkerOperationFailed), Item("c") };
        var (result, _) = await RunAsync(items);

        var record = Assert.Single(_audit.Records);
        Assert.Equal(WriteAuditRecord.Kind, record.RecordKind);
        Assert.Equal(WriteAuditRecord.CurrentVersion, record.RecordVersion);
        Assert.Equal(SteppingTimeProvider.Start, record.Timestamp);
        Assert.Equal("fake_write_tool", record.Tool);
        Assert.Equal("fake/1", record.ContractVersion);
        Assert.Equal("full", record.AccessMode);
        Assert.Equal(FakeWriteBindingGate.ProjectPath, record.ProjectPath);
        Assert.Equal("binding-1", record.Binding!.BindingId);
        Assert.Equal(WritePhases.Applied, record.Phase);
        Assert.Equal(new[] { "a", "b", "c" }, Strings(record.RequestedOperations!.Value, "operationId"));
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal(text, record.ResponseText);
        Assert.Equal("sha256:" + ContentHashes.Sha256Hex(text), record.ResponseHash);
        Assert.True(record.DurationMs > 0);

        Assert.Equal(new[] { "a", "b", "c" }, record.Items.Select(i => i.OperationId));
        Assert.Equal(
            new[] { OperationBatchStatus.Succeeded, OperationBatchStatus.Failed, OperationBatchStatus.Skipped },
            record.Items.Select(i => i.Status));
        Assert.Contains("target-a", record.Items[0].Target);
        var precondition = Assert.Single(record.Items[0].Preconditions);
        Assert.Equal(FakeWriteDomain.Precondition, precondition.Name);
        Assert.Equal(FakeWriteDomain.HashFor(items[0]), precondition.Actual);
        Assert.True(record.Items[0].DurationMs > 0);
        Assert.True(record.Items[1].DurationMs > 0);
        Assert.Null(record.Items[2].DurationMs);
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, record.Items[1].FailureCategory);
        Assert.Equal("b failed.", record.Items[1].FailureMessage);
        Assert.Contains(WriteExecution.PartialWriteMessage, record.Items[1].Warnings);
    }

    [Fact]
    public async Task PreGateRejection_IsAuditedWithEveryItemSkipped()
    {
        var (result, doc) = await RunAsync(new[] { Item("a"), Item("a") });

        AssertOutcome(result, doc, WritePhases.Error, isError: true, WorkerFailureCategories.ValidationError);
        Assert.Equal(new[] { "compose" }, _domain.Calls);
        var record = Assert.Single(_audit.Records);
        Assert.Equal(WritePhases.Error, record.Phase);
        Assert.All(record.Items, item => Assert.Equal(OperationBatchStatus.Skipped, item.Status));
        Assert.All(record.Items, item => Assert.Null(item.DurationMs));
        AssertAuditedOutsideLease();
    }

    private static FakeWriteItem Item(
        string id,
        string? dependsOn = null,
        string[]? guards = null,
        string[]? lateGuards = null,
        string? failWith = null)
        => new(id, DependsOn: dependsOn, Guards: guards, LateGuards: lateGuards, FailWith: failWith);

    private static WriteCall<FakeWriteItem> Call(
        IReadOnlyList<FakeWriteItem> items, bool dryRun = false)
        => new(FakeWriteBindingGate.ProjectPath, items, dryRun);

    private async Task<(CallToolResult Result, JsonElement Document)> RunAsync(
        IReadOnlyList<FakeWriteItem> items,
        bool dryRun = false)
    {
        var recordsBefore = _audit.Records.Count;
        var result = await _execution.RunAsync(_domain, Call(items, dryRun));
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        var structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal(text, structured.GetRawText());
        Assert.Equal(recordsBefore + 1, _audit.Records.Count);
        return (result, JsonDocument.Parse(text).RootElement.Clone());
    }

    private static void AssertOutcome(
        CallToolResult result, JsonElement doc, string phase, bool isError, string? category)
    {
        Assert.Equal(phase, doc.GetProperty("phase").GetString());
        Assert.Equal(isError, result.IsError == true);
        var error = doc.GetProperty("error");
        if (category is null)
        {
            Assert.Equal(JsonValueKind.Null, error.ValueKind);
            return;
        }

        Assert.Equal(category, error.GetProperty("category").GetString());
    }

    private void AssertAuditedOutsideLease() => Assert.False(Assert.Single(_audit.LeaseActiveAtAppend));

    private void AssertAuditedInsideLease() => Assert.True(Assert.Single(_audit.LeaseActiveAtAppend));

    private static string ErrorMessage(JsonElement doc)
        => doc.GetProperty("error").GetProperty("message").GetString()!;

    private static JsonElement GuardById(JsonElement doc, string id)
        => Assert.Single(doc.GetProperty("guards").EnumerateArray(), g => g.GetProperty("id").GetString() == id);

    private static string[] Strings(JsonElement array)
        => array.EnumerateArray().Select(e => e.GetString()!).ToArray();

    private static string[] Strings(JsonElement array, string member)
        => array.EnumerateArray().Select(e => e.GetProperty(member).GetString()!).ToArray();
}
