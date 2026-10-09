using System.Text.Json;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Safety.Pipeline;

public sealed class WriteBindingStrategyTests
{
    private readonly FakeWriteBindingGate _gate = new() { AccessMode = McpAccessMode.Full };
    private readonly FakeWriteDomain _domain;
    private readonly RecordingAuditSink _audit;
    private readonly WriteExecution _execution;

    public WriteBindingStrategyTests()
    {
        _domain = new FakeWriteDomain(_gate);
        _audit = new RecordingAuditSink(_gate);
        _execution = new WriteExecution(_gate, _audit, FakeWriteDomain.Catalog, TimeProvider.System);
    }

    [Fact]
    public async Task ExplicitPreparation_PinsUnboundSnapshot_WithoutPretendingItIsVerified()
    {
        _gate.CurrentBinding = FakeWriteBindingGate.Snapshot(ProjectBindingSnapshot.UnboundState, "unbound", 0, null);
        _gate.GateResult = WorkerCallResult.Fail(WorkerFailureCategories.BindingConflict, "No project.");
        var strategy = new PreparedStrategy(_gate.CurrentBinding);

        var result = await _execution.RunAsync(_domain, Call(), bindingStrategy: strategy);

        Assert.False(result.IsError == true);
        Assert.Equal(1, strategy.Calls);
        Assert.Equal(0, _gate.GateCalls);
        Assert.Equal(ProjectBindingSnapshot.UnboundState, _gate.LeasedBinding!.State);
        Assert.Equal(1, _domain.MutationCount);
        Assert.True(Assert.Single(_audit.LeaseActiveAtAppend));
    }

    [Fact]
    public async Task ExplicitPreparation_StaleRevision_IsRefusedBeforePlanning()
    {
        var prepared = _gate.CurrentBinding;
        _gate.CurrentBinding = FakeWriteBindingGate.Snapshot(
            prepared.State, prepared.BindingId, prepared.Revision + 1, prepared.ProjectPath);

        var result = await _execution.RunAsync(_domain, Call(), bindingStrategy: new PreparedStrategy(prepared));

        Assert.True(result.IsError);
        Assert.Equal(0, _domain.MutationCount);
        Assert.DoesNotContain("plan", _domain.Calls);
        Assert.Equal(0, _gate.GateCalls);
        Assert.Single(_audit.Records);
    }

    [Fact]
    public async Task ExplicitPreparation_Refusal_IsAuditedWithoutLeaseOrPlanning()
    {
        var strategy = new PreparedStrategy(_gate.CurrentBinding)
        {
            Error = new WriteToolError(WorkerFailureCategories.AccessDenied, "Lifecycle requires full access.")
        };

        var result = await _execution.RunAsync(_domain, Call(), bindingStrategy: strategy);

        Assert.True(result.IsError);
        Assert.Equal(0, _gate.GateCalls);
        Assert.Equal(0, _gate.LeaseCalls);
        Assert.DoesNotContain("plan", _domain.Calls);
        Assert.Equal("access_denied", JsonDocument.Parse(_audit.Records[0].ResponseText)
            .RootElement.GetProperty("error").GetProperty("category").GetString());
    }

    [Fact]
    public async Task AccessValidation_StopsBeforeLifecyclePreparation()
    {
        _domain.Validation = WriteValidation.Invalid(WorkerFailureCategories.AccessDenied, "Full access required.");
        var strategy = new PreparedStrategy(_gate.CurrentBinding);

        var result = await _execution.RunAsync(_domain, Call(), bindingStrategy: strategy);

        Assert.True(result.IsError);
        Assert.Equal(0, strategy.Calls);
        Assert.Equal(0, _gate.LeaseCalls);
    }

    [Fact]
    public async Task DefaultPreparation_StillRejectsAnUnboundOrdinaryWrite()
    {
        _gate.CurrentBinding = FakeWriteBindingGate.Snapshot(ProjectBindingSnapshot.UnboundState, "unbound", 0, null);
        _gate.GateResult = WorkerCallResult.Fail(WorkerFailureCategories.BindingConflict, "No project.");

        var result = await _execution.RunAsync(_domain, Call());

        Assert.True(result.IsError);
        Assert.Equal(1, _gate.GateCalls);
        Assert.Equal(0, _gate.LeaseCalls);
        Assert.Equal(0, _domain.MutationCount);
    }

    private static WriteCall<FakeWriteItem> Call() => new(null, new[] { new FakeWriteItem("a") }, false);

    private sealed class PreparedStrategy(ProjectBindingSnapshot binding) : IWriteBindingStrategy<FakeWriteItem>
    {
        public int Calls { get; private set; }
        public WriteToolError? Error { get; init; }

        public Task<WriteBindingPreparation> PrepareAsync(WriteCall<FakeWriteItem> call, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Error is null
                ? WriteBindingPreparation.Prepared(binding)
                : WriteBindingPreparation.Rejected(Error));
        }
    }
}
