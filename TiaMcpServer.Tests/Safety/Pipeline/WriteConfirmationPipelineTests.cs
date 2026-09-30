using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using Xunit;

namespace TiaMcpServer.Tests.Safety.Pipeline;

public sealed class WriteConfirmationPipelineTests
{
    private const string Ack = FakeWriteDomain.AcknowledgeGuard;
    private readonly FakeWriteBindingGate _gate = new();
    private readonly FakeWriteDomain _domain;
    private readonly RecordingAuditSink _audit;
    private readonly WriteExecution _execution;
    private readonly List<ElicitRequestParams> _prompts = new();

    public WriteConfirmationPipelineTests()
    {
        _domain = new FakeWriteDomain(_gate);
        _audit = new RecordingAuditSink(_gate);
        _execution = new WriteExecution(_gate, _audit, FakeWriteDomain.Catalog, TimeProvider.System);
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData(FakeWriteDomain.InfoGuard)]
    [InlineData(FakeWriteDomain.BlockGuard)]
    [InlineData(Ack)]
    public async Task DefaultOn_IgnoresMalformedOrSuppliedAgentIds(string agentId)
    {
        var (result, doc) = await RunAsync(Confirmed(), acknowledge: new[] { agentId, agentId });

        Assert.False(result.IsError == true);
        Assert.True(doc.GetProperty("success").GetBoolean());
        Assert.Single(_prompts);
        Assert.Equal(GuardSatisfactions.User, Assert.Single(_audit.Records[0].Guards).SatisfiedBy);
    }

    [Fact]
    public async Task AcceptWithConfirm_UsesOnePrompt_AndRechecksBeforeMutation()
    {
        var (result, doc) = await RunAsync(Confirmed());

        Assert.False(result.IsError == true);
        Assert.True(doc.GetProperty("success").GetBoolean());
        Assert.Contains("target-a", Assert.Single(_prompts).Message);
        Assert.Equal(new[] { "validate", "plan", "guards", "plan", "guards", "mutate:a", "project:a", "verify", "compose" }, _domain.Calls);
        Assert.Equal(1, _gate.LeaseCalls);
        Assert.True(Assert.Single(_audit.LeaseActiveAtAppend));
        Assert.Equal(GuardSatisfactions.User, Assert.Single(_audit.Records[0].Guards).SatisfiedBy);
    }

    [Theory]
    [InlineData("accept", null)]
    [InlineData("accept", false)]
    [InlineData("decline", true)]
    [InlineData("cancel", true)]
    public async Task UnconfirmedReply_IsAccessDenied(string action, bool? confirm)
    {
        var (result, doc) = await RunAsync(Reply(action, confirm), acknowledge: new[] { Ack });

        AssertDenied(result, doc, WorkerFailureCategories.AccessDenied);
        Assert.Equal(0, _domain.MutationCount);
        Assert.Null(Assert.Single(_audit.Records[0].Guards).SatisfiedBy);
    }

    [Fact]
    public async Task AbsentAdapter_IsAccessDenied_WithMissingCapabilityMessage()
    {
        var result = await _execution.RunAsync(_domain, Call(), new WriteConfirmationContext(new UserConfirmationOptions()));
        var doc = Document(result);

        AssertDenied(result, doc, WorkerFailureCategories.AccessDenied);
        Assert.Contains("elicitation", doc.GetProperty("error").GetProperty("message").GetString()!);
        Assert.Equal(0, _domain.MutationCount);
        Assert.Single(_audit.Records);
    }

    [Fact]
    public async Task UnsupportedCapability_IsAccessDenied_WithoutSendingRequest()
    {
        var (result, doc) = await RunAsync(Confirmed(), supported: false);

        AssertDenied(result, doc, WorkerFailureCategories.AccessDenied);
        Assert.Empty(_prompts);
        Assert.Equal(0, _domain.MutationCount);
    }

    [Fact]
    public async Task Timeout_IsAccessDenied_AndAuditedOnce()
    {
        var confirmation = new UserConfirmation(true, async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Confirmed();
        }, TimeSpan.FromMilliseconds(10));

        var result = await _execution.RunAsync(_domain, Call(), new WriteConfirmationContext(new UserConfirmationOptions(), confirmation));

        AssertDenied(result, Document(result), WorkerFailureCategories.AccessDenied);
        Assert.Single(_audit.Records);
        Assert.Equal(0, _domain.MutationCount);
    }

    [Fact]
    public async Task RequestFailure_IsAccessDenied_WithoutEchoingTransportDetails()
    {
        var confirmation = new UserConfirmation(true, (_, _) => throw new IOException("private transport detail"), TimeSpan.FromSeconds(1));
        var result = await _execution.RunAsync(_domain, Call(), new WriteConfirmationContext(new UserConfirmationOptions(), confirmation));

        AssertDenied(result, Document(result), WorkerFailureCategories.AccessDenied);
        Assert.DoesNotContain("private transport detail", _audit.Records[0].ResponseText);
        Assert.Equal(0, _domain.MutationCount);
    }

    [Fact]
    public async Task CallerCancellation_DuringPrompt_IsAuditedOnce_AndPropagated()
    {
        using var source = new CancellationTokenSource();
        var confirmation = new UserConfirmation(true, (_, ct) =>
        {
            source.Cancel();
            ct.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Confirmed());
        }, TimeSpan.FromSeconds(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _execution.RunAsync(
            _domain, Call(), new WriteConfirmationContext(new UserConfirmationOptions(), confirmation), cancellationToken: source.Token));

        Assert.Single(_audit.Records);
        Assert.True(Assert.Single(_audit.LeaseActiveAtAppend));
        Assert.Equal(0, _domain.MutationCount);
        Assert.Null(Assert.Single(_audit.Records[0].Guards).SatisfiedBy);
    }

    [Fact]
    public async Task CallerCancellation_BeforeBinding_IsAuditedOnce_AndPropagated()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _execution.RunAsync(
            _domain, Call(), new WriteConfirmationContext(new UserConfirmationOptions()), cancellationToken: source.Token));

        Assert.Single(_audit.Records);
        Assert.Equal(0, _gate.GateCalls);
        Assert.Equal(0, _domain.MutationCount);
    }

    [Fact]
    public async Task DryRun_NeverPrompts_ReportsUnacknowledgedGuards()
    {
        var (result, doc) = await RunAsync(Confirmed(), dryRun: true, acknowledge: new[] { Ack });

        Assert.False(result.IsError == true);
        Assert.Equal(WritePhases.Preview, doc.GetProperty("phase").GetString());
        Assert.Empty(_prompts);
        Assert.Equal(0, _domain.MutationCount);
        Assert.False(Assert.Single(doc.GetProperty("guards").EnumerateArray()).GetProperty("acknowledged").GetBoolean());
        Assert.Null(Assert.Single(_audit.Records[0].Guards).SatisfiedBy);
    }

    [Fact]
    public async Task InfoOnlyWrite_NeverPrompts()
    {
        var (result, _) = await RunAsync(Confirmed(), guards: new[] { FakeWriteDomain.InfoGuard });

        Assert.False(result.IsError == true);
        Assert.Empty(_prompts);
        Assert.Equal(1, _domain.MutationCount);
    }

    [Fact]
    public async Task HardBlock_NeverPromptsOrMutates()
    {
        var (result, doc) = await RunAsync(Confirmed(), guards: new[] { Ack, FakeWriteDomain.BlockGuard });

        AssertDenied(result, doc, WorkerFailureCategories.GuardBlocked);
        Assert.Empty(_prompts);
        Assert.Equal(0, _domain.MutationCount);
    }

    [Fact]
    public async Task SwitchOff_AcknowledgesExactSet_AndAuditsAgentProvenance()
    {
        var result = await _execution.RunAsync(_domain, Call(acknowledge: new[] { Ack }),
            new WriteConfirmationContext(new UserConfirmationOptions(false)));

        Assert.False(result.IsError == true);
        Assert.Equal(GuardSatisfactions.Agent, Assert.Single(_audit.Records[0].Guards).SatisfiedBy);
    }

    [Theory]
    [InlineData("effect")]
    [InlineData("precondition")]
    [InlineData("guard")]
    [InlineData("binding")]
    public async Task StateChangedWhilePromptWasOpen_IsRefusedWithoutMutation(string change)
    {
        var (result, doc) = await RunAsync(Confirmed(), duringPrompt: () =>
        {
            switch (change)
            {
                case "effect": _domain.EffectChange = "different consequence"; break;
                case "precondition": _domain.PlannedPreconditions = new[] { new CheckedPrecondition("state", "old", "new", false) }; break;
                case "guard": _domain.PlannedGuards = new[] { Ack, FakeWriteDomain.InfoGuard }; break;
                case "binding": _gate.CurrentBinding = FakeWriteBindingGate.Snapshot(ProjectBindingSnapshot.VerifiedState, "different", 99, FakeWriteBindingGate.ProjectPath); break;
            }
        });

        AssertDenied(result, doc, WorkerFailureCategories.BindingConflict);
        Assert.Single(_prompts);
        Assert.Equal(0, _domain.MutationCount);
        Assert.Equal(GuardSatisfactions.User, Assert.Single(_audit.Records[0].Guards).SatisfiedBy);
    }

    [Fact]
    public async Task ResolutionFailsAfterAcceptance_IsRefusedWithoutMutation()
    {
        var (result, doc) = await RunAsync(Confirmed(), duringPrompt: () =>
            _domain.PlanFailure = new WriteToolError(WorkerFailureCategories.TargetNotFound, "The source disappeared."));

        AssertDenied(result, doc, WorkerFailureCategories.BindingConflict);
        Assert.Single(_prompts);
        Assert.Equal(0, _domain.MutationCount);
        Assert.Equal(GuardSatisfactions.User, Assert.Single(_audit.Records[0].Guards).SatisfiedBy);
    }

    [Fact]
    public async Task ExceptionAfterAcceptance_PreservesUserProvenance_AndAuditsOnce()
    {
        _domain.ThrowOnMutate = "a";
        var confirmation = new UserConfirmation(true, (_, _) => ValueTask.FromResult(Confirmed()), TimeSpan.FromSeconds(1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _execution.RunAsync(
            _domain, Call(), new WriteConfirmationContext(new UserConfirmationOptions(), confirmation)));

        Assert.Equal(GuardSatisfactions.User, Assert.Single(Assert.Single(_audit.Records).Guards).SatisfiedBy);
        Assert.True(Assert.Single(_audit.LeaseActiveAtAppend));
        Assert.Equal(1, _domain.MutationCount);
    }

    [Fact]
    public async Task LateGuardForDifferentTarget_CannotReuseUserApprovalById()
    {
        var confirmation = new UserConfirmation(true, (_, _) => ValueTask.FromResult(Confirmed()), TimeSpan.FromSeconds(1));
        var call = new WriteCall<FakeWriteItem>(FakeWriteBindingGate.ProjectPath,
            new[] { new FakeWriteItem("a", Guards: new[] { Ack }), new FakeWriteItem("b", DependsOn: "a", LateGuards: new[] { Ack }) }, false, null);

        var result = await _execution.RunAsync(_domain, call, new WriteConfirmationContext(new UserConfirmationOptions(), confirmation));
        var doc = Document(result);

        Assert.False(result.IsError == true);
        Assert.False(doc.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, doc.GetProperty("error").ValueKind);
        Assert.Equal(1, _domain.MutationCount);
        Assert.Equal(WorkerFailureCategories.GuardBlocked, doc.GetProperty("batch").GetProperty("operations")[1].GetProperty("failure").GetProperty("category").GetString());
    }

    [Fact]
    public async Task VerificationFailure_MakesCallUnsuccessful_PreservesMutationAndNoReplayWarning()
    {
        _domain.VerificationPasses = false;
        var (result, doc) = await RunAsync(Confirmed(), guards: Array.Empty<string>());

        Assert.False(result.IsError == true);
        Assert.False(doc.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, doc.GetProperty("error").ValueKind);
        Assert.Equal(OperationBatchStatus.Succeeded, doc.GetProperty("batch").GetProperty("operations")[0].GetProperty("status").GetString());
        Assert.Equal(1, doc.GetProperty("verification").GetProperty("mutationCount").GetInt32());
        Assert.Contains(doc.GetProperty("warnings").EnumerateArray(), warning => warning.GetString()!.Contains("before retrying"));
    }

    private async Task<(CallToolResult Result, JsonElement Document)> RunAsync(
        ElicitResult reply,
        IReadOnlyList<string>? acknowledge = null,
        IReadOnlyList<string>? guards = null,
        bool dryRun = false,
        bool supported = true,
        Action? duringPrompt = null)
    {
        var confirmation = new UserConfirmation(supported, (request, _) =>
        {
            Assert.True(_gate.LeaseActive);
            _prompts.Add(request);
            duringPrompt?.Invoke();
            return ValueTask.FromResult(reply);
        }, TimeSpan.FromSeconds(1));
        var result = await _execution.RunAsync(_domain, Call(acknowledge, guards, dryRun),
            new WriteConfirmationContext(new UserConfirmationOptions(), confirmation));
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal(text, Assert.IsType<JsonElement>(result.StructuredContent).GetRawText());
        var record = Assert.Single(_audit.Records);
        Assert.Equal(text, record.ResponseText);
        Assert.Equal("sha256:" + ContentHashes.Sha256Hex(text), record.ResponseHash);
        return (result, Document(result));
    }

    private static WriteCall<FakeWriteItem> Call(IReadOnlyList<string>? acknowledge = null, IReadOnlyList<string>? guards = null, bool dryRun = false)
        => new(FakeWriteBindingGate.ProjectPath, new[] { new FakeWriteItem("a", Guards: guards ?? new[] { Ack }) }, dryRun, acknowledge);

    private static ElicitResult Confirmed() => Reply("accept", true);

    private static ElicitResult Reply(string action, bool? confirm) => new()
    {
        Action = action,
        Content = confirm.HasValue ? new Dictionary<string, JsonElement> { ["confirm"] = JsonSerializer.SerializeToElement(confirm.Value) } : new()
    };

    private static JsonElement Document(CallToolResult result)
        => JsonDocument.Parse(Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text).RootElement.Clone();

    private static void AssertDenied(CallToolResult result, JsonElement doc, string category)
    {
        Assert.True(result.IsError);
        Assert.False(doc.GetProperty("success").GetBoolean());
        Assert.Equal(category, doc.GetProperty("error").GetProperty("category").GetString());
    }
}
