using System.Text.Json.Nodes;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Plc;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

[Collection(RealWorkerProcessCollection.Name)]
public sealed class PlcWriteBudgetTests
{
    private static readonly string Marker = "UNIQUE_EXCERPT_MARKER";

    private static PlcWriteEffect ContentEffect(string id)
    {
        var current = string.Join("\n", Enumerable.Range(0, 60).Select(i => $"{Marker} current {id} line {i} " + new string('a', 200)));
        var requested = string.Join("\n", Enumerable.Range(0, 60).Select(i => $"{Marker} requested {id} line {i} " + new string('b', 200)));
        return new PlcWriteEffect("update_block_logic",
            new PlcTargetIdentity("Block", "PLC_1", "Station_1", null, null, "Main", "PLC_1/Blocks/Main", null),
            Array.Empty<PlcFieldChange>(), null, null, PlcContentDiff.Build(current, requested), null);
    }

    private static PlcGuardedWriteResponse Preview(params PlcWriteEffect[] effects)
        => new("plc_write", "2.1", "preview", true, null, Array.Empty<string>(), Array.Empty<WriteGuardReport>(),
            effects.Select((e, i) => new PlcWriteEffectPresentation("op" + i, e, null)).ToArray(), null, null, "per-operation", null, null);

    [Fact]
    public void OversizedDiffEvidenceOmittedWhole()
    {
        // Item limit: the diff alone is dropped; the effect and its identity remain.
        var single = PlcWritePayloadBudget.Apply(Preview(ContentEffect("a")), maxItemChars: 4_000);
        var presented = Assert.Single(single.Effects);
        Assert.NotNull(presented.Effect);
        Assert.Null(presented.Effect!.ContentDiff);
        Assert.Equal("PLC_1/Blocks/Main", presented.Effect.Target.BlockPath);
        Assert.Equal(StructuredOperationBatchPayloadBudget.ItemLimitReason, presented.Omission!.Reason);
        Assert.Equal("plc_read", presented.Omission.RetryTool);
        Assert.False(single.Success);
        Assert.DoesNotContain(Marker, CanonicalJson.Serialize(single));

        // Document limit: whole diffs go first, largest first, until the document fits.
        var effects = Enumerable.Range(0, 4).Select(i => ContentEffect("d" + i)).ToArray();
        var original = CanonicalJson.Serialize(Preview(effects)).Length;
        var bounded = PlcWritePayloadBudget.Apply(Preview(effects), maxDocumentChars: original * 3 / 4);
        Assert.True(CanonicalJson.Serialize(bounded).Length <= original * 3 / 4);
        Assert.All(bounded.Effects, e => Assert.NotNull(e.Effect));
        Assert.Contains(bounded.Effects, e => e.Effect!.ContentDiff is null
            && e.Omission!.Reason == StructuredOperationBatchPayloadBudget.DocumentLimitReason);
        Assert.Contains(bounded.Effects, e => e.Effect!.ContentDiff is not null && e.Omission is null);
    }

    [Fact]
    public void FailureImportOutcomeOmittedWholeBeforeDocumentOverflow()
    {
        var outcome = new BlockImportOutcomeInfo
        {
            ImportStage = "completed", ImportResultState = "success", TargetMutationCommitted = true, CompileStage = "unavailable",
            FinalReadStage = "unavailable", TemporarySourceState = "not_applicable", ContentRelation = new string('r', 5_000),
        };
        var failed = new StructuredOperationItem("content", "update_block_logic", OperationBatchStatus.Failed, null,
            new StructuredOperationFailure(WorkerFailureCategories.WorkerOperationFailed, "Compile failed.", outcome), null, null, Array.Empty<string>());
        var response = new PlcGuardedWriteResponse("plc_write", "2.1", "applied", false, null, Array.Empty<string>(), Array.Empty<WriteGuardReport>(),
            Array.Empty<PlcWriteEffectPresentation>(), StructuredOperationBatch.FromItems(new[] { failed }), new(true, Array.Empty<PlcOperationVerification>(), null), "per-operation", null, null);

        var bounded = PlcWritePayloadBudget.Apply(response, maxDocumentChars: 3_000);

        var item = Assert.Single(bounded.Batch!.Operations);
        Assert.Equal("failed", item.Status);
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, item.Failure!.Category);
        Assert.Null(item.Failure.BlockImportOutcome);
        Assert.Equal(StructuredOperationBatchPayloadBudget.DocumentLimitReason, item.Omission!.Reason);
        Assert.True(CanonicalJson.Serialize(bounded).Length <= 3_000);
    }

    private static PlcGuardedWriteResponse WithDiagnostics(string phase, bool success, WriteToolError? error, params WriteGuardReport[] guards)
        => new("plc_write", "2.1", phase, success, error, Array.Empty<string>(), guards,
            Array.Empty<PlcWriteEffectPresentation>(), null, null, "per-operation", null, null);

    [Fact]
    public void MultiErrorValidationMessageDeliveredIntactWhenWithinBudget()
    {
        var message = string.Join("\n", Enumerable.Range(0, 4).Select(i => $"Operation 'op{i}': " + new string('e', 300)));
        Assert.True(message.Length > 512);

        var bounded = PlcWritePayloadBudget.Apply(WithDiagnostics("error", false, new(WorkerFailureCategories.ValidationError, message)));

        Assert.Equal(message, bounded.Error!.Message);
        Assert.Null(bounded.Omission);
    }

    [Fact]
    public void AppliedResponseWithLongGuardMessageKeepsSuccess()
    {
        var guard = new WriteGuardReport("plc_deletes_group_contents", "info", "op0", new string('g', 2_000), null);

        var bounded = PlcWritePayloadBudget.Apply(WithDiagnostics("applied", true, null, guard));

        Assert.True(bounded.Success);
        Assert.Null(bounded.Omission);
        Assert.Equal(guard.Message, Assert.Single(bounded.Guards).Message);
    }

    [Fact]
    public void OverBudgetDocumentStillCompactsDiagnostics()
    {
        var guard = new WriteGuardReport("plc_deletes_group_contents", "info", "op0", new string('g', 2_000), null);

        var bounded = PlcWritePayloadBudget.Apply(WithDiagnostics("applied", true, null, guard), maxDocumentChars: 1_000);

        Assert.DoesNotContain("ggggg", Assert.Single(bounded.Guards).Message);
        Assert.Equal("diagnosticDetailsOmitted", bounded.Omission!.Reason);
        Assert.False(bounded.Success);
    }

    [Theory]
    [InlineData(100_000, 3_000)]
    [InlineData(1_000, 100_000)]
    public void DiagnosticOmissionReportsEffectiveDocumentLimit(int itemLimit, int documentLimit)
    {
        var response = WithDiagnostics("applied", true, new("worker_operation_failed", new string('x', 6_000))) with
        {
            // PLC enters compaction on oversized values, rather than on diagnostic messages alone.
            Effects = [new("op0", ContentEffect("a"), null)],
        };
        var original = CanonicalJson.Serialize(response);

        var bounded = PlcWritePayloadBudget.Apply(response, itemLimit, documentLimit);

        Assert.Equal("diagnosticDetailsOmitted", bounded.Omission!.Reason);
        Assert.Equal(documentLimit, bounded.Omission.LimitChars);
        Assert.Equal(original.Length, bounded.Omission.OriginalChars);
        Assert.Equal("plc_read", bounded.Omission.RetryTool);
        Assert.False(bounded.Success);
        Assert.Equal(original, CanonicalJson.Serialize(response));
        Assert.InRange(CanonicalJson.Serialize(bounded).Length, 0, documentLimit);
    }

    [Fact]
    public async Task ProtectedCoreOverDocumentLimitRejectedBeforeWorker()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip");
        var before = requests.Methods().Length;
        // U+0001 remains escaped to six characters in every identity copy.
        var items = Enumerable.Range(0, 50)
            .Select(i => PlcGuardedWriteFixture.CreateTag(i.ToString("D3") + new string('\u0001', 253), "Tag" + i)).ToArray();
        Assert.Equal(50, items.Select(item => item.OperationId).Distinct().Count());
        Assert.All(items, item => Assert.Equal(256, item.OperationId.Length));
        Assert.True(PlcWritePayloadBudget.MeasureProtectedCore(items) > StructuredOperationBatchPayloadBudget.MaxDocumentChars);

        var response = await fixture.RunAsync(false, items);

        Assert.Equal("error", response.Phase);
        Assert.Equal(WorkerFailureCategories.ValidationError, response.Error!.Category);
        Assert.Contains("smaller plc_write calls", response.Error.Message);
        Assert.Equal(before, requests.Methods().Length);
        Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path));
    }

    [Theory]
    [InlineData('<')]
    [InlineData('\u4e00')]
    public void ProtectedCoreWithLiteralIdentitiesIsAdmitted(char identityCharacter)
    {
        var items = Enumerable.Range(0, 50)
            .Select(i => PlcGuardedWriteFixture.CreateTag(i.ToString("D3") + new string(identityCharacter, 253), "Tag" + i)).ToArray();
        Assert.Equal(50, items.Select(item => item.OperationId).Distinct().Count());
        Assert.All(items, item => Assert.Equal(256, item.OperationId.Length));
        Assert.True(PlcWritePayloadBudget.MeasureProtectedCore(items) <= StructuredOperationBatchPayloadBudget.MaxDocumentChars);
        using var client = new OpennessWorkerClient(new ProjectSessionBinding(null));
        Assert.True(new PlcWriteDomain(client).Validate(items, McpAccessMode.ReadWrite).IsValid);
    }

    [Fact]
    public async Task EligibleLargeBase_ShortPatchRetainsExecutionWhenDiffIsOmitted()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip");
        const string path = "PLC_2/Main";
        var content = "<Document>\n<Network>old</Network>\n<!--" + new string('x', 45_000) + "-->\n</Document>\n";
        var seeded = await fixture.Client.UpdateBlockLogicAsync(path, content, format: "xml",
            expectedContentHash: await fixture.ReadHashAsync(path, "xml"));
        Assert.True(seeded.Success, seeded.Error);
        var hash = await fixture.ReadHashAsync(path, "xml");
        var patch = new PlcOperationRequest
        {
            OperationId = "patch", Operation = "patch_block_logic", BlockPath = path, ExpectedContentHash = hash,
            Edits = [new PlcTextEdit { OldText = "<Network>old</Network>", NewText = "<Network>new</Network>" }],
        };
        // The DTO retains ordinary explicit-null members; measure reduction against the base.
        Assert.True(CanonicalJson.Serialize(patch).Length < content.Length / 10);

        var response = await fixture.RunAsync(false, patch);

        Assert.True(response.Success, CanonicalJson.Serialize(response));
        var diff = Assert.Single(response.Effects).Effect!.ContentDiff!;
        Assert.Equal(1, diff.CurrentChangedLineCount);
        Assert.Equal(1, diff.RequestedChangedLineCount);
        var preservedSize = Math.Max(CanonicalJson.Serialize(Assert.Single(response.Effects).Effect! with { ContentDiff = null }).Length,
            CanonicalJson.Serialize(response.Verification!.Operations).Length);
        var bounded = PlcWritePayloadBudget.Apply(response, maxItemChars: preservedSize + 1);
        var execution = Assert.Single(bounded.Batch!.Operations);
        Assert.Equal("patch_block_logic", execution.Operation);
        Assert.Equal("succeeded", execution.Status);
        Assert.Null(Assert.Single(bounded.Effects).Effect!.ContentDiff);
        Assert.NotNull(Assert.Single(bounded.Effects).Omission);
        Assert.False(bounded.Success);
        Assert.True(bounded.Verification!.Success);
        Assert.True(Assert.Single(bounded.Verification.Operations).Success);
    }

    private static CompileCheckReport SharedReport(int detailChars) => new()
    {
        Plcs = [new() { PlcName = "PLC_1", State = "Success", DiagnosticNotes = [Marker + new string('c', detailChars)] }],
    };

    private static PlcGuardedWriteResponse SharedResponse(CompileCheckReport? report, StructuredOperationFailure? failure = null)
        => WithDiagnostics("applied", true, null) with
        {
            CompilationMode = "shared-per-plc",
            SharedCompilations = [new("PLC_1", ["one", "two"], failure is null ? "succeeded" : "unavailable", report, failure, null)],
            Verification = new(true, [new("one", true, "contentHash", null, "verified-hash", "verified-hash", null)], null),
        };

    [Fact]
    public void OversizedCompileReport_OmittedBeforeVerificationChecks()
    {
        var outcome = new BlockImportOutcomeInfo { ContentRelation = "OUTCOME_MARKER" + new string('r', 5_000) };
        var failed = new StructuredOperationItem("failed", "update_block_logic", OperationBatchStatus.Failed, null,
            new(WorkerFailureCategories.WorkerOperationFailed, "Mutation failed.", outcome), null, null, []);
        var response = SharedResponse(SharedReport(12_000)) with
        {
            Effects = [new("one", ContentEffect("one"), null)],
            Batch = StructuredOperationBatch.FromItems([failed]),
        };
        // Leave room for omission records, while requiring the diff and report to yield.
        var withoutLargeValues = response with
        {
            Effects = [response.Effects[0] with { Effect = response.Effects[0].Effect! with { ContentDiff = null } }],
            SharedCompilations = [response.SharedCompilations![0] with { Report = null }],
        };
        var documentLimit = CanonicalJson.Serialize(withoutLargeValues).Length + 1_500;

        var bounded = PlcWritePayloadBudget.Apply(response, maxDocumentChars: documentLimit);

        var compilation = Assert.Single(bounded.SharedCompilations!);
        Assert.Null(compilation.Report);
        Assert.Equal(StructuredOperationBatchPayloadBudget.DocumentLimitReason, compilation.Omission!.Reason);
        Assert.Equal("compile_check", compilation.Omission.RetryTool);
        Assert.Null(Assert.Single(bounded.Effects).Effect!.ContentDiff);
        Assert.NotNull(Assert.Single(bounded.Batch!.Operations).Failure!.BlockImportOutcome);
        Assert.Equal("verified-hash", Assert.Single(bounded.Verification!.Operations).ContentHash);
        Assert.Null(bounded.Verification.Omission);
        Assert.True(bounded.Verification.Success);
        Assert.False(bounded.Success);
        Assert.InRange(CanonicalJson.Serialize(bounded).Length, 0, documentLimit);
    }

    [Fact]
    public void CompileReportOverItemLimit_OmittedWholeWithOmissionRecord()
    {
        var response = SharedResponse(SharedReport(12_000));
        var original = CanonicalJson.Serialize(response);
        var reportChars = CanonicalJson.Serialize(response.SharedCompilations![0].Report).Length;

        var bounded = PlcWritePayloadBudget.Apply(response, maxItemChars: 2_000);

        var compilation = Assert.Single(bounded.SharedCompilations!);
        Assert.Null(compilation.Report);
        Assert.Equal("succeeded", compilation.Stage);
        Assert.Equal(new[] { "one", "two" }, compilation.OperationIds);
        Assert.Equal(StructuredOperationBatchPayloadBudget.ItemLimitReason, compilation.Omission!.Reason);
        Assert.Equal(2_000, compilation.Omission.LimitChars);
        Assert.Equal(reportChars, compilation.Omission.OriginalChars);
        Assert.Equal("compile_check", compilation.Omission.RetryTool);
        Assert.Contains("selector", compilation.Omission.Guidance, StringComparison.OrdinalIgnoreCase);
        Assert.False(bounded.Success);
        Assert.True(bounded.Verification!.Success);
        Assert.Equal("verified-hash", Assert.Single(bounded.Verification.Operations).ContentHash);
        Assert.DoesNotContain(Marker, CanonicalJson.Serialize(bounded));
        Assert.Equal(original, CanonicalJson.Serialize(response));
    }

    [Theory]
    [InlineData(100_000, 2_000)]
    [InlineData(1_000, 100_000)]
    public void Compact_SummarizesCompileFailureMessages(int itemLimit, int documentLimit)
    {
        var response = SharedResponse(null, new(WorkerFailureCategories.WorkerOperationFailed, Marker + new string('f', 6_000)));

        var bounded = PlcWritePayloadBudget.Apply(response, itemLimit, documentLimit);

        var failure = Assert.Single(bounded.SharedCompilations!).Failure!;
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, failure.Category);
        Assert.Equal("Diagnostic details omitted. Inspect current state with plc_read.", failure.Message);
        Assert.Equal(StructuredOperationBatchPayloadBudget.DiagnosticOmissionReason, bounded.Omission!.Reason);
        Assert.Equal(documentLimit, bounded.Omission.LimitChars);
        Assert.False(bounded.Success);
        Assert.Equal("verified-hash", Assert.Single(bounded.Verification!.Operations).ContentHash);
        Assert.DoesNotContain(Marker, CanonicalJson.Serialize(bounded));
        Assert.Contains(Marker, CanonicalJson.Serialize(response));
        Assert.InRange(CanonicalJson.Serialize(bounded).Length, 0, documentLimit);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProtectedCore_ReservesSharedCompilationEntries(bool escapedSelector)
    {
        var selector = escapedSelector ? "PLC_" + new string('\u0001', 100) : "PLC_1";
        var items = Enumerable.Range(0, 2).Select(i => new PlcOperationRequest
        {
            OperationId = i + new string('\u0001', 255), Operation = "update_block_logic", Format = "source",
            BlockPath = selector + "/Main", Content = "source", ExpectedContentHash = new string('a', 64),
        }).ToArray();
        var perOperation = items.Select(item => new PlcOperationRequest
        {
            OperationId = item.OperationId, Operation = item.Operation, Format = "xml", BlockPath = item.BlockPath,
            Content = item.Content, ExpectedContentHash = item.ExpectedContentHash,
        }).ToArray();
        // The call will group into one selector at runtime, but admission reserves one entry per import.
        var minimumEntry = new PlcSharedCompilation(selector, [items[0].OperationId], "unavailable", null,
            new(new string('x', 64), "Diagnostic details omitted. Inspect current state with plc_read."),
            new(StructuredOperationBatchPayloadBudget.DocumentLimitReason, int.MaxValue, int.MaxValue, "compile_check", ""));
        var minimumReserved = 2 * CanonicalJson.Serialize(minimumEntry).Length;

        var addedReservation = PlcWritePayloadBudget.MeasureProtectedCore(items) - PlcWritePayloadBudget.MeasureProtectedCore(perOperation);

        Assert.True(addedReservation >= minimumReserved, $"Reserved {addedReservation} characters; expected at least {minimumReserved}.");
    }

    [Fact]
    public void ProtectedCore_LargeSharedSelectorsAreRejectedBeforeWorker()
    {
        var selector = "PLC_" + new string('p', 4_000);
        var items = Enumerable.Range(0, 50).Select(i => new PlcOperationRequest
        {
            OperationId = "op" + i, Operation = "update_block_logic", Format = "source", BlockPath = selector + "/Main",
            Content = "source", ExpectedContentHash = new string('a', 64),
        }).ToArray();
        using var client = new OpennessWorkerClient(new ProjectSessionBinding(null));

        Assert.True(PlcWritePayloadBudget.MeasureProtectedCore(items) > StructuredOperationBatchPayloadBudget.MaxDocumentChars);
        Assert.False(new PlcWriteDomain(client).Validate(items, McpAccessMode.ReadWrite).IsValid);
    }

    [Fact]
    public void NonEligibleCall_BudgetBehaviorUnchanged()
    {
        var response = WithDiagnostics("applied", true, null);

        var bounded = PlcWritePayloadBudget.Apply(response);

        Assert.Equal("per-operation", bounded.CompilationMode);
        Assert.Null(bounded.SharedCompilations);
        var document = JsonNode.Parse(CanonicalJson.Serialize(bounded))!.AsObject();
        document.Remove("compilationMode");
        document.Remove("sharedCompilations");
        Assert.Equal("""{"batch":null,"contractVersion":"2.1","effects":[],"error":null,"guards":[],"omission":null,"phase":"applied","success":true,"tool":"plc_write","verification":null,"warnings":[]}""", document.ToJsonString());
    }
}
