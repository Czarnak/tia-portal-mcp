using System.Text.Json;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Plc;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;
using static TiaMcpServer.Tests.Plc.PlcGuardedWriteFixture;

namespace TiaMcpServer.Tests.Plc;

/// <summary>
/// plc_write through the guarded pipeline against the stateful plc-write FakeWorker scenarios: a
/// root PLC (software PLC_1, device Station_1) and a grouped PLC (software PLC_2, device PLC_1).
/// </summary>
[Collection(RealWorkerProcessCollection.Name)]
public sealed class PlcGuardedWriteDomainTests
{
    private const string Roundtrip = "plc-write-roundtrip";
    private static readonly string[] WriteMethods = PlcOperationCatalog.WriteOperationNames.ToArray();

    [Fact]
    public async Task Readonly_TwoSourceImports_ReportSharedModeBeforeGate()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip, McpAccessMode.ReadOnly);
        var first = UpdateBlock("first", "PLC_2/Main", "source:sha256:" + new string('0', 64), "source");
        var second = UpdateBlock("second", "Station_1/Main", "source:sha256:" + new string('0', 64), "source");
        var before = requests.Methods();

        var response = await fixture.RunAsync(false, first, second);

        Assert.Equal(WorkerFailureCategories.AccessDenied, response.Error!.Category);
        Assert.Equal("shared-per-plc", response.CompilationMode);
        Assert.Null(response.SharedCompilations);
        Assert.Equal(before, requests.Methods());
    }

    [Fact]
    public async Task AppliedSharedMode_WithoutCommittedImports_ReportsEmptyCompilations()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-import-rejected");
        var first = UpdateBlock("first", "PLC_2/Main", await fixture.ReadHashAsync("PLC_2/Main", "source"), "source");
        var second = UpdateBlock("second", "Station_1/Main", await fixture.ReadHashAsync("Station_1/Main", "source"), "source");

        var response = await fixture.RunAsync(false, first, second);

        Assert.Equal("applied", response.Phase);
        Assert.False(response.Success);
        Assert.Equal("shared-per-plc", response.CompilationMode);
        Assert.NotNull(response.SharedCompilations);
        Assert.Empty(response.SharedCompilations);
    }

    [Fact]
    public async Task Readonly_DeniesBeforeGate()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip, McpAccessMode.ReadOnly);
        var before = requests.Methods().Length;
        fixture.Binding.Clear(null, out _);

        var response = await fixture.RunAsync(false, CreateTag("tag", "Fresh"));

        Assert.Equal("error", response.Phase);
        Assert.Equal(WorkerFailureCategories.AccessDenied, response.Error!.Category);
        Assert.Equal(before, requests.Methods().Length);
    }

    [Fact]
    public async Task PortalRejectionOfItemTwo_IsTypedWithSiemensEvidence_AndTextEqualsStructured()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);

        var result = await fixture.RunRawAsync(false, CreateTag("a", "First"), CreateTag("b", "RejectedByPortal"), CreateTag("c", "Third"));

        var structured = (JsonElement)result.StructuredContent!;
        Assert.Equal(structured.GetRawText(), Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(Assert.Single(result.Content)).Text);
        Assert.False(result.IsError);
        var response = Document(result);
        Assert.Equal("applied", response.Phase);
        Assert.False(response.Success);
        Assert.Null(response.Error);
        var items = response.Batch!.Operations;
        Assert.Equal(new[] { "succeeded", "failed", "skipped" }, items.Select(item => item.Status));
        Assert.Null(items[0].Failure);
        var failure = items[1].Failure!;
        Assert.Equal(WorkerFailureCategories.EngineeringRejected, failure.Category);
        var siemens = failure.EngineeringError!;
        Assert.Equal("EngineeringTargetInvocationException", siemens.Type);
        Assert.Equal("scripted rejection", siemens.Message);
        var inner = Assert.Single(siemens.Inner);
        Assert.Equal(("InvalidOperationException", "scripted cause"), (inner.Type, inner.Message));
    }

    [Fact]
    public async Task HostSideGuardRejection_CarriesNoEngineeringError()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);

        var result = await fixture.RunRawAsync(false, CreateTag("a", "First", table: "No such table"));

        Assert.DoesNotContain("engineeringError", ((JsonElement)result.StructuredContent!).GetRawText());
    }

    [Fact]
    public async Task BindingIdentityChange_PreventsDispatch()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);
        var identity = (await fixture.Client.ListTagTablesAsync(plcName: null)).SessionIdentity!;
        fixture.AfterGate = () => Assert.True(fixture.Binding.BindVerified(new WorkerSessionIdentity
        {
            WorkerSessionId = identity.WorkerSessionId, SessionGeneration = identity.SessionGeneration + 1,
            PortalProcessId = identity.PortalProcessId, ProjectPath = identity.ProjectPath,
        }, forceRebind: true, out _));

        var response = await fixture.RunAsync(false, CreateTag("tag", "Fresh"));

        Assert.Equal(WorkerFailureCategories.BindingConflict, response.Error!.Category);
        Assert.Null(response.Batch);
        Assert.Empty(requests.Methods().Intersect(WriteMethods));
    }

    [Fact]
    public async Task BindingRevisionChange_PreventsDispatch()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);
        var identity = (await fixture.Client.ListTagTablesAsync(plcName: null)).SessionIdentity!;
        fixture.AfterGate = () =>
        {
            fixture.Binding.Invalidate("test revision change");
            Assert.True(fixture.Binding.BindVerified(identity, forceRebind: true, out _));
        };

        var response = await fixture.RunAsync(false, CreateTag("tag", "Fresh"));

        Assert.Equal(WorkerFailureCategories.BindingConflict, response.Error!.Category);
        Assert.Null(response.Batch);
        Assert.Empty(requests.Methods().Intersect(WriteMethods));
    }

    [Fact]
    public async Task CreateTableThenTagInOneCall_Applies()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);
        var table = CreateTable("table");

        var response = await fixture.RunAsync(false, table, CreateTag("tag", "Valve1", table: "Valves"));

        Assert.Equal("applied", response.Phase);
        Assert.True(response.Success, JsonSerializer.Serialize(response));
        Assert.Null(response.Error);
        Assert.Equal(2, response.Batch!.Counts.Succeeded);
        Assert.Equal("table", response.Effects[1].Effect!.DependsOn);
        Assert.True(response.Verification!.Success);
        Assert.Equal(new[] { "trusted", "trusted" }, response.Verification.Operations.Select(o => o.Check));
        Assert.All(response.Verification.Operations, operation => Assert.Null(operation.ReadBack));
        Assert.DoesNotContain("\"readBack\"", CanonicalJson.Serialize(response));
        Assert.Contains("Valve1", await fixture.InventoryAsync());
    }

    [Fact]
    public async Task DryRun_ReportsEffectsAndDependsOnWithoutMutation()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);

        var response = await fixture.RunAsync(true, CreateTable("table"), CreateTag("tag", "Valve1", table: "Valves"));

        Assert.Equal("preview", response.Phase);
        Assert.True(response.Success);
        Assert.Null(response.Batch);
        Assert.Null(response.Verification);
        Assert.DoesNotContain("\"readBack\"", CanonicalJson.Serialize(response));
        Assert.Equal(new[] { "table", "tag" }, response.Effects.Select(e => e.OperationId));
        Assert.Null(response.Effects[0].Effect!.DependsOn);
        Assert.Equal("table", response.Effects[1].Effect!.DependsOn);
        Assert.Equal("Valves", response.Effects[1].Effect!.Target.TableName);
        Assert.Empty(requests.Methods().Intersect(WriteMethods));
        Assert.DoesNotContain("Valves", await fixture.InventoryAsync());
    }

    [Fact]
    public async Task DuplicateBlock_PreviewsCleanThenPortalRejectionIsForwarded()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);

        var preview = await fixture.RunAsync(true, CreateBlock("block", "PLC_2/Main"));
        var actual = await fixture.RunRawAsync(false, CreateBlock("block", "PLC_2/Main"));
        var applied = Document(actual);

        Assert.Equal("preview", preview.Phase);
        Assert.True(preview.Success);
        Assert.Empty(preview.Guards);
        Assert.False(actual.IsError);
        Assert.Equal("applied", applied.Phase);
        Assert.False(applied.Success);
        Assert.Null(applied.Error);
        var failure = Assert.Single(applied.Batch!.Operations).Failure!;
        Assert.Equal(WorkerFailureCategories.EngineeringRejected, failure.Category);
        Assert.Equal("scripted cause", Assert.Single(failure.EngineeringError!.Inner).Message);
        Assert.Contains("create_block", requests.Methods());
    }

    private static PlcOperationRequest CreateOb(string id, string blockPath, string? obEventClass = null)
        => new() { OperationId = id, Operation = "create_block", BlockPath = blockPath, BlockType = "OB", ObEventClass = obEventClass };

    [Fact]
    public async Task DryRunListsSingletonGuardThenActualCallBlocks()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);
        var items = new[] { CreateOb("first", "PLC_2/ProgErr1", "ProgrammingError"), CreateOb("second", "PLC_2/ProgErr2", "ProgrammingError") };

        var preview = await fixture.RunAsync(true, items);
        var actual = await fixture.RunRawAsync(false, items);
        var blocked = Document(actual);

        Assert.Equal("preview", preview.Phase);
        var guard = Assert.Single(preview.Guards, g => g.Id == PlcGuardDefinitions.ObSingletonExists);
        Assert.Equal(("second", "block"), (guard.OperationId, guard.Severity));
        Assert.True(actual.IsError);
        Assert.Equal("blocked", blocked.Phase);
        Assert.Contains(blocked.Guards, g => g.Id == PlcGuardDefinitions.ObSingletonExists);
        Assert.Null(blocked.Batch);
        Assert.DoesNotContain("create_block", requests.Methods());
    }

    [Fact]
    public async Task ObCreatePreviewNumberMatchesAppliedResult()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);
        var items = new[] { CreateOb("cycle", "PLC_2/Cycle2"), CreateOb("cyclic", "PLC_2/Cyclic1", "CyclicInterrupt"), CreateBlock("fc", "PLC_2/FC_New") };

        var preview = await fixture.RunAsync(true, items);
        var applied = await fixture.RunAsync(false, items);

        Assert.True(applied.Success, JsonSerializer.Serialize(applied));
        var planned = preview.Effects.Select(e => e.Effect!.Changes.SingleOrDefault(c => c.Field == "number")?.Requested).ToArray();
        Assert.Equal(new[] { "123", "30", null }, planned);
        var results = applied.Batch!.Operations.Select(o => o.Result!.Value).ToArray();
        Assert.Equal(new[] { "123", "30" }, results.Take(2).Select(r => r.GetProperty("number").GetInt32().ToString()));
        Assert.Equal(new[] { "ProgramCycle", "CyclicInterrupt", null }, results.Select(r => r.GetProperty("obEventClass").GetString()));
        Assert.Equal(JsonValueKind.Number, results[2].GetProperty("number").ValueKind);
    }

    [Fact]
    public async Task NameCollision_ReachesPortalAndEarlierItemsStay()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);

        var response = await fixture.RunAsync(false, CreateTag("fresh", "Fresh"), CreateTable("table", "Default tag table"), CreateTag("after", "After"));

        Assert.Equal("applied", response.Phase);
        Assert.False(response.Success);
        Assert.DoesNotContain(response.Guards, g => g.Severity == "block");
        Assert.Equal(new[] { "succeeded", "failed", "skipped" }, response.Batch!.Operations.Select(o => o.Status));
        Assert.Equal(WorkerFailureCategories.EngineeringRejected, response.Batch.Operations[1].Failure!.Category);
        Assert.Equal(2, requests.Methods().Count(m => m is "create_tag" or "create_tag_table"));
        var inventory = await fixture.InventoryAsync();
        Assert.Contains("Fresh", inventory);
        Assert.DoesNotContain("After", inventory);
    }

    [Fact]
    public async Task LateReplanFailure_StopsWithPartialWriteGuard()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-late-block");

        var response = await fixture.RunAsync(false, CreateTable("table"), CreateTag("tag", "Valve1", table: "Valves"));

        Assert.Equal("applied", response.Phase);
        Assert.False(response.Success);
        Assert.Null(response.Error);
        var failed = response.Batch!.Operations[1];
        Assert.Equal("failed", failed.Status);
        Assert.Equal(WorkerFailureCategories.GuardBlocked, failed.Failure!.Category);
        Assert.Contains(response.Guards, g => g.Id == WriteGuardCatalog.PartialWriteGuardId && g.OperationId == "tag");
        Assert.Contains(response.Guards, g => g.Id == PlcGuardDefinitions.StateUnverifiable && g.OperationId == "tag");
        Assert.Contains("create_tag_table", requests.Methods());
        Assert.DoesNotContain("create_tag", requests.Methods());
    }

    [Fact]
    public async Task WorkerStateChangedFailsItemEarlierItemsStay()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-content-drift");
        var hash = await fixture.ReadHashAsync("Station_1/Main", "xml");

        var response = await fixture.RunAsync(false, CreateTag("a", "TagA"), CreateTag("b", "TagB"),
            UpdateBlock("content", "Station_1/Main", hash), CreateTag("d", "TagD"), CreateTag("e", "TagE"));

        Assert.Equal("applied", response.Phase);
        Assert.False(response.Success);
        Assert.Null(response.Error);
        Assert.Equal(new[] { "succeeded", "succeeded", "failed", "skipped", "skipped" }, response.Batch!.Operations.Select(o => o.Status));
        Assert.Equal(WorkerFailureCategories.StateChanged, response.Batch.Operations[2].Failure!.Category);
        var partial = Assert.Single(response.Guards, g => g.Id == WriteGuardCatalog.PartialWriteGuardId);
        Assert.Equal("content", partial.OperationId);
        // Verification covers the attempted items: a, b and the failed content item, never the skipped ones.
        Assert.Equal(new[] { "a", "b", "content" }, response.Verification!.Operations.Select(o => o.OperationId));
        Assert.True(response.Verification.Success);
        var observed = response.Verification.Operations[2];
        Assert.Equal("contentHash", observed.Check);
        Assert.Equal(ContentHashRules.Compute("xml", "<Block edited-in-tia-ui=\"true\"/>"), observed.ContentHash);
        Assert.DoesNotContain(WriteExecution.VerificationFailureMessage, response.Warnings);
        var inventory = await fixture.InventoryAsync();
        Assert.Contains("TagB", inventory);
        Assert.DoesNotContain("TagD", inventory);
        Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path));
    }

    [Theory]
    [InlineData("delete_block", "Station_1/Blocks/Motors/FB_Motor", true)]
    [InlineData("delete_block_group", "Station_1/Blocks/Motors", true)]
    [InlineData("delete_block", "Station_1/Blocks/Empty_DB", false)]
    public async Task ShortPathUpdateThenDeleteInOneCall_SupersededOnlyWhenItMayHitTheBlock(string operation, string path, bool superseded)
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);
        var hash = await fixture.ReadHashAsync("Station_1/FB_Motor", "xml");

        var response = await fixture.RunAsync(false, UpdateBlock("update", "Station_1/FB_Motor", hash),
            new PlcOperationRequest { OperationId = "delete", Operation = operation, BlockPath = path });

        Assert.True(response.Success, JsonSerializer.Serialize(response));
        Assert.Equal(superseded ? "superseded" : "contentHash", response.Verification!.Operations[0].Check);
    }

    [Theory]
    [InlineData("xml")]
    [InlineData("source")]
    public async Task PlcReadHashIsAcceptedThenDriftIsRejected(string format)
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);
        // PLC_2 sits on device PLC_1 inside a device group.
        var hash = await fixture.ReadHashAsync("PLC_2/Main", format);

        var accepted = await fixture.RunAsync(false, UpdateBlock("first", "PLC_2/Main", hash, format));
        var updates = requests.Methods().Count(m => m == "update_block_logic");
        var rejected = await fixture.RunRawAsync(false, UpdateBlock("second", "PLC_2/Main", hash, format));

        Assert.True(accepted.Success, JsonSerializer.Serialize(accepted));
        var check = Assert.Single(accepted.Verification!.Operations);
        Assert.Equal("contentHash", check.Check);
        Assert.Equal(ContentHashRules.Compute(format, "<Block written=\"first\"/>"), check.ContentHash);
        Assert.True(rejected.IsError);
        Assert.Equal(WorkerFailureCategories.StateChanged, Document(rejected).Error!.Category);
        Assert.Equal(updates, requests.Methods().Count(m => m == "update_block_logic"));
    }

    [Fact]
    public async Task ReadBackMismatchDoesNotFailSuccessfulImport()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-readback-normalizes");
        const string submitted = "<ConstantType>LReal</ConstantType>";
        var item = new PlcOperationRequest { OperationId = "content", Operation = "update_block_logic",
            BlockPath = "PLC_2/Main", Format = "xml", Content = submitted,
            ExpectedContentHash = await fixture.ReadHashAsync("PLC_2/Main", "xml") };

        var response = await fixture.RunAsync(false, item);

        Assert.True(response.Success, CanonicalJson.Serialize(response));
        Assert.Equal("succeeded", Assert.Single(response.Batch!.Operations).Status);
        Assert.True(response.Verification!.Success);
        var check = Assert.Single(response.Verification.Operations);
        Assert.True(check.Success);
        Assert.Null(check.Expected);
        Assert.Equal(check.Actual, check.ContentHash);
        Assert.Equal(await fixture.ReadHashAsync("PLC_2/Main", "xml"), check.ContentHash);
        var readBack = Assert.IsType<PlcReadBackEvidence>(check.ReadBack);
        Assert.Equal("xmlDocument", readBack.ComparisonScope);
        Assert.False(readBack.RawTextEqual);
        Assert.False(readBack.NormalizedLinesEqual);
        Assert.Equal(submitted, Assert.Single(readBack.Submitted.Excerpt.Lines).Text);
        Assert.Equal("<ConstantType>Real</ConstantType>", Assert.Single(readBack.Observed.Excerpt.Lines).Text);
    }

    [Fact]
    public async Task ReadBackEqualContentKeepsHashAndEmptyExcerpts()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);
        const string submitted = "FUNCTION Main : Void\nBEGIN\nEND_FUNCTION";
        var item = new PlcOperationRequest { OperationId = "content", Operation = "update_block_logic",
            BlockPath = "PLC_2/Main", Format = "source", Content = submitted,
            ExpectedContentHash = await fixture.ReadHashAsync("PLC_2/Main", "source") };

        var response = await fixture.RunAsync(false, item);

        Assert.True(response.Success);
        var check = Assert.Single(response.Verification!.Operations);
        Assert.Equal("contentHash", check.Check);
        Assert.Equal(await fixture.ReadHashAsync("PLC_2/Main", "source"), check.ContentHash);
        var readBack = Assert.IsType<PlcReadBackEvidence>(check.ReadBack);
        Assert.True(readBack.RawTextEqual);
        Assert.True(readBack.NormalizedLinesEqual);
        Assert.False(readBack.LineEndingOnly);
        Assert.Empty(readBack.Submitted.Excerpt.Lines);
        Assert.Empty(readBack.Observed.Excerpt.Lines);
        Assert.Null(readBack.Omission);
    }

    [Theory]
    [InlineData("xml", "xmlDocument")]
    [InlineData("source", "wholeText")]
    public async Task TypeReadBackUsesSubmittedExecutionContent(string format, string scope)
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);
        const string path = "Station_1/Types/UDT_Settings";
        var read = new PlcOperationRequest { OperationId = "read", Operation = "get_type_content", TypePath = path, Format = format };
        var initial = PlcPayloadContract.Project(read, await PlcWorkerInvoker.InvokeReadAsync(fixture.Client, read));
        var submitted = format == "xml" ? "<SW.Types.PlcStruct><Name>UDT_Settings</Name></SW.Types.PlcStruct>"
            : "TYPE \"UDT_Settings\"\nSTRUCT\nValue : Bool;\nEND_STRUCT;\nEND_TYPE";
        var item = new PlcOperationRequest { OperationId = "type", Operation = "update_type_content", TypePath = path,
            Format = format, Content = submitted, ExpectedContentHash = initial.Result!.Value.GetProperty("contentHash").GetString() };

        var response = await fixture.RunAsync(false, item);

        Assert.True(response.Success, CanonicalJson.Serialize(response));
        var check = Assert.Single(response.Verification!.Operations);
        var readBack = Assert.IsType<PlcReadBackEvidence>(check.ReadBack);
        Assert.Equal(scope, readBack.ComparisonScope);
        Assert.True(readBack.RawTextEqual);
        Assert.Equal(submitted.Length, readBack.Submitted.CharacterCount);
        var fresh = PlcPayloadContract.Project(read, await PlcWorkerInvoker.InvokeReadAsync(fixture.Client, read));
        Assert.Equal(fresh.Result!.Value.GetProperty("contentHash").GetString(), check.ContentHash);
    }

    [Fact]
    public async Task FailedContentAttemptReportsObservedComparison()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-content-drift");
        var item = UpdateBlock("content", "PLC_2/Main", await fixture.ReadHashAsync("PLC_2/Main", "xml"));

        var response = await fixture.RunAsync(false, item);

        Assert.False(response.Success);
        Assert.Equal("failed", Assert.Single(response.Batch!.Operations).Status);
        Assert.Equal(WorkerFailureCategories.StateChanged, response.Batch.Operations[0].Failure!.Category);
        Assert.True(response.Verification!.Success);
        var check = Assert.Single(response.Verification.Operations);
        Assert.True(check.Success);
        Assert.Equal(await fixture.ReadHashAsync("PLC_2/Main", "xml"), check.ContentHash);
        var readBack = Assert.IsType<PlcReadBackEvidence>(check.ReadBack);
        Assert.False(readBack.RawTextEqual);
        Assert.Equal(item.Content, Assert.Single(readBack.Submitted.Excerpt.Lines).Text);
        Assert.Equal("<Block edited-in-tia-ui=\"true\"/>", Assert.Single(readBack.Observed.Excerpt.Lines).Text);
    }

    [Fact]
    public async Task ReadBackSharesPlanningAndReplanningExcerptAllowance()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-readback-normalizes");
        var item = new PlcOperationRequest { OperationId = "content", Operation = "update_block_logic", BlockPath = "PLC_2/Main",
            Format = "xml", Content = "<ConstantType>LReal</ConstantType>",
            ExpectedContentHash = await fixture.ReadHashAsync("PLC_2/Main", "xml") };
        var budget = new PlcContentDiffBudget { RemainingLines = 4, RemainingChars = 1000 };
        var planner = new PlcWritePlanner(fixture.Client);

        // An earlier same-name tag marks the content item dependent in the initial working state.
        var plan = await planner.PlanAsync(new[] { CreateTag("tag", "Main"), item }, budget);
        Assert.True(plan.Plan.Success);
        Assert.Equal("tag", plan.Plan.Items[1].DependsOn);
        Assert.Equal(2, budget.RemainingLines);
        Assert.True((await PlcWorkerInvoker.InvokeWriteAsync(fixture.Client, CreateTag("tag", "Main"))).Success);
        var replan = await planner.ReplanAsync(item, budget);
        Assert.True(replan.Replan.Success);
        Assert.Equal(0, budget.RemainingLines);
        var result = await PlcWorkerInvoker.InvokeWriteAsync(fixture.Client, item);
        var batch = StructuredOperationBatch.FromItems(new[] { PlcPayloadContract.ProjectWrite(item, result) });
        var verification = await PlcVerificationPolicy.VerifyAsync(fixture.Client, new[] { item },
            new Dictionary<string, PlcWriteEffect> { [item.OperationId] = replan.Replan.Plan!.Effect! }, batch, budget);

        Assert.True(verification.Success);
        var check = Assert.Single(verification.Operations);
        Assert.Equal(await fixture.ReadHashAsync("PLC_2/Main", "xml"), check.ContentHash);
        var readBack = Assert.IsType<PlcReadBackEvidence>(check.ReadBack);
        Assert.False(readBack.RawTextEqual);
        Assert.True(readBack.BudgetExhausted);
        Assert.Equal(1, readBack.SubmittedChangedSpanLineCount);
        Assert.Equal(1, readBack.ObservedChangedSpanLineCount);
        Assert.Empty(readBack.Submitted.Excerpt.Lines);
        Assert.Empty(readBack.Observed.Excerpt.Lines);
        Assert.Equal("succeeded", Assert.Single(batch.Operations).Status);

        item.ExpectedContentHash = check.ContentHash;
        var next = await fixture.RunAsync(false, item);
        Assert.True(next.Success);
        var fresh = Assert.IsType<PlcReadBackEvidence>(Assert.Single(next.Verification!.Operations).ReadBack);
        Assert.False(fresh.BudgetExhausted);
        Assert.Single(fresh.Submitted.Excerpt.Lines);
        Assert.Single(fresh.Observed.Excerpt.Lines);
    }

    [Theory]
    [InlineData("xml", "failed")]
    [InlineData("source", "failed")]
    [InlineData("xml", "unavailable")]
    [InlineData("source", "unavailable")]
    public async Task CompletedImportWithCompileFailureKeepsEvidenceAndContinuesBatch(string format, string compileStage)
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-compile-" + compileStage);
        var hash = await fixture.ReadHashAsync("PLC_2/Main", format);

        var response = await fixture.RunAsync(false,
            UpdateBlock("import", "PLC_2/Main", hash, format),
            CreateTag("next", "AfterImport"));

        Assert.True(response.Success, JsonSerializer.Serialize(response));
        Assert.Equal(new[] { "succeeded", "succeeded" }, response.Batch!.Operations.Select(item => item.Status));
        var outcome = response.Batch.Operations[0].Result!.Value.GetProperty("importOutcome");
        Assert.Equal("success", outcome.GetProperty("importResultState").GetString());
        Assert.True(outcome.GetProperty("targetMutationCommitted").GetBoolean());
        Assert.Equal(compileStage, outcome.GetProperty("compileStage").GetString());
        if (compileStage == "failed")
            Assert.Equal(1, outcome.GetProperty("compileReport").GetProperty("totalErrorCount").GetInt32());
        else
            Assert.Equal(JsonValueKind.Null, outcome.GetProperty("compileReport").ValueKind);
        Assert.True(response.Verification!.Success);
        Assert.Equal("contentHash", response.Verification.Operations[0].Check);
        Assert.Equal(ContentHashRules.Compute(format, "<Block written=\"import\"/>"),
            response.Verification.Operations[0].ContentHash);
        Assert.Contains("AfterImport", await fixture.InventoryAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnverifiableEvidenceBlocks(bool dryRun)
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-unverifiable");

        // A legacy-name delete could hit the unreadable block, so the lookup cannot be trusted.
        var response = await fixture.RunAsync(dryRun, new PlcOperationRequest { OperationId = "block", Operation = "delete_block", BlockPath = "Station_1/Main" });

        Assert.Equal(dryRun ? "preview" : "blocked", response.Phase);
        Assert.Contains(response.Guards, g => g.Id == PlcGuardDefinitions.StateUnverifiable && g.Severity == "block");
        Assert.Null(response.Batch);
        Assert.DoesNotContain("delete_block", requests.Methods());
    }

    [Fact]
    public async Task VerificationFailure_IsSuccessFalseWithWarning()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-export-lost");
        var hash = await fixture.ReadHashAsync("Station_1/Main", "xml");

        var response = await fixture.RunAsync(false, CreateTag("tag", "Fresh"), UpdateBlock("content", "Station_1/Main", hash));

        Assert.Equal("applied", response.Phase);
        Assert.False(response.Success);
        Assert.Null(response.Error);
        Assert.Equal(new[] { "succeeded", "succeeded" }, response.Batch!.Operations.Select(o => o.Status));
        Assert.False(response.Verification!.Success);
        Assert.Equal(new[] { ("trusted", true), ("contentHash", false) }, response.Verification.Operations.Select(o => (o.Check, o.Success)));
        Assert.Contains(WriteExecution.VerificationFailureMessage, response.Warnings);
        Assert.All(response.Verification.Operations, operation => Assert.Null(operation.ReadBack));
        Assert.DoesNotContain("\"readBack\"", CanonicalJson.Serialize(response));
    }

    private static PlcOperationRequest Patch(string id, string path, string hash, string? format = null,
        string oldText = "Main", string newText = "Patched")
        => new() { OperationId = id, Operation = "patch_block_logic", BlockPath = path, Format = format,
            ExpectedContentHash = hash, Edits = new[] { new PlcTextEdit { OldText = oldText, NewText = newText } } };

    private static async Task<string> ExportAsync(PlcGuardedWriteFixture fixture, string path, string format)
    {
        var result = await PlcWorkerInvoker.InvokeReadAsync(fixture.Client,
            new PlcOperationRequest { OperationId = "read", Operation = "get_block_content", BlockPath = path, Format = format });
        Assert.True(result.Success, result.Error);
        return result.Payload;
    }

    [Theory]
    [InlineData(null, "xml")]
    [InlineData("source", "source")]
    public async Task PatchExecutesExistingImportAndVerifiesResolvedFormat(string? requestedFormat, string format)
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);
        var original = await ExportAsync(fixture, "PLC_2/Main", format);
        var hash = await fixture.ReadHashAsync("PLC_2/Main", format);
        var patch = Patch("patch", "PLC_2/Main", hash, requestedFormat);

        var raw = await fixture.RunRawAsync(false, patch);

        Assert.False(raw.IsError);
        var response = Document(raw);
        Assert.True(response.Success, JsonSerializer.Serialize(response));
        Assert.Equal("patch_block_logic", Assert.Single(response.Batch!.Operations).Operation);
        Assert.Equal("patch_block_logic", Assert.Single(response.Effects).Effect!.Operation);
        var outcome = response.Batch.Operations[0].Result!.Value.GetProperty("importOutcome");
        Assert.Equal("success", outcome.GetProperty("importResultState").GetString());
        Assert.True(outcome.GetProperty("targetMutationCommitted").GetBoolean());
        var expected = original.Replace("Main", "Patched", StringComparison.Ordinal);
        Assert.Equal(expected, await ExportAsync(fixture, "PLC_2/Main", format));
        var verification = Assert.Single(response.Verification!.Operations);
        Assert.Equal("contentHash", verification.Check);
        Assert.Equal(ContentHashRules.Compute(format, expected), verification.ContentHash);
        Assert.True(Assert.IsType<PlcReadBackEvidence>(verification.ReadBack).RawTextEqual);
        Assert.Single(requests.Methods(), method => method == "update_block_logic");
        Assert.DoesNotContain("patch_block_logic", requests.Methods());
        Assert.Null(patch.Content);
        Assert.Equal("patch_block_logic", patch.Operation);
        Assert.Equal(requestedFormat, patch.Format);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DependentPatchReplansBeforeImportAndDriftRetainsEarlierTag(bool drift)
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit,
            drift ? "plc-write-replan-drift" : Roundtrip);
        var hash = await fixture.ReadHashAsync("PLC_2/Main", "xml");
        var before = requests.Methods().Count(method => method == "get_block_content");

        var response = await fixture.RunAsync(false, CreateTag("tag", "Main"), Patch("patch", "PLC_2/Main", hash));

        Assert.Equal("applied", response.Phase);
        Assert.Equal("tag", response.Effects[1].Effect!.DependsOn);
        Assert.Equal("succeeded", response.Batch!.Operations[0].Status);
        Assert.Contains("Main", await fixture.InventoryAsync());
        Assert.Equal(drift ? "failed" : "succeeded", response.Batch.Operations[1].Status);
        Assert.Equal(!drift, response.Success);
        Assert.Equal(drift ? 0 : 1, requests.Methods().Count(method => method == "update_block_logic"));
        // Initial resolution plus one late resolution; successful imports additionally re-export for verification.
        Assert.Equal(drift ? 2 : 3, requests.Methods().Count(method => method == "get_block_content") - before);
        if (drift)
        {
            Assert.Equal(WorkerFailureCategories.StateChanged, response.Batch.Operations[1].Failure!.Category);
            Assert.Contains(response.Guards, guard => guard.Id == WriteGuardCatalog.PartialWriteGuardId && guard.OperationId == "patch");
            using var record = JsonDocument.Parse(Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path)));
            Assert.DoesNotContain("resolvedOperation", record.RootElement.GetProperty("items").GetRawText());
        }
    }

    [Fact]
    public async Task FailedPatchReplanDiscardsPreviouslyRunnableResolution()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);
        var hash = await fixture.ReadHashAsync("PLC_2/Main", "xml");
        var patch = Patch("patch", "PLC_2/Main", hash);
        var domain = new PlcWriteDomain(fixture.Client);
        Assert.True((await domain.PlanAsync(new[] { patch })).Success);
        Assert.True((await fixture.RunAsync(false, UpdateBlock("outside", "PLC_2/Main", hash))).Success);

        var replan = await domain.ReplanAsync(patch);

        Assert.False(replan.Success);
        var before = requests.Methods().Length;
        await Assert.ThrowsAsync<InvalidOperationException>(() => domain.MutateAsync(patch));
        Assert.Equal(before, requests.Methods().Length);
    }

    [Fact]
    public async Task SameBlockPatchesKeepWorkerHashRecheckWithoutInventedDependencies()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);
        var hash = await fixture.ReadHashAsync("PLC_2/Main", "xml");

        var response = await fixture.RunAsync(false, Patch("first", "PLC_2/Main", hash),
            Patch("second", "PLC_2/Main", hash, newText: "Second"));

        Assert.False(response.Success);
        Assert.Equal(new[] { "succeeded", "failed" }, response.Batch!.Operations.Select(item => item.Status));
        Assert.Equal(WorkerFailureCategories.StateChanged, response.Batch.Operations[1].Failure!.Category);
        Assert.Null(response.Effects[1].Effect!.DependsOn);
        Assert.Equal(2, requests.Methods().Count(method => method == "update_block_logic"));
        Assert.All(response.Batch.Operations, item => Assert.Equal("patch_block_logic", item.Operation));
    }

    [Fact]
    public async Task PatchThenSameTargetDeleteUsesExistingSupersededVerification()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);
        var hash = await fixture.ReadHashAsync("PLC_2/Main", "xml");

        var response = await fixture.RunAsync(false, Patch("patch", "PLC_2/Main", hash),
            new PlcOperationRequest { OperationId = "delete", Operation = "delete_block", BlockPath = "PLC_2/Blocks/Main" });

        Assert.True(response.Success, JsonSerializer.Serialize(response));
        Assert.Equal("superseded", response.Verification!.Operations[0].Check);
        Assert.Null(response.Verification.Operations[0].ReadBack);
        Assert.DoesNotContain("\"readBack\"", CanonicalJson.Serialize(response));
        Assert.Equal("patch_block_logic", response.Batch!.Operations[0].Operation);
    }

    [Theory]
    [InlineData("xml", "failed")]
    [InlineData("source", "unavailable")]
    public async Task PatchKeepsTypedImportAndIndependentCompileEvidence(string format, string compileStage)
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-compile-" + compileStage);
        var hash = await fixture.ReadHashAsync("PLC_2/Main", format);

        var response = await fixture.RunAsync(false, Patch("patch", "PLC_2/Main", hash, format), CreateTag("next", "AfterPatch"));

        Assert.True(response.Success, JsonSerializer.Serialize(response));
        Assert.Equal(new[] { "succeeded", "succeeded" }, response.Batch!.Operations.Select(item => item.Status));
        var outcome = response.Batch.Operations[0].Result!.Value.GetProperty("importOutcome");
        Assert.Equal("success", outcome.GetProperty("importResultState").GetString());
        Assert.Equal(compileStage, outcome.GetProperty("compileStage").GetString());
        Assert.Equal("patch_block_logic", response.Batch.Operations[0].Operation);
        Assert.Contains("AfterPatch", await fixture.InventoryAsync());
    }

    [Fact]
    public async Task FailedPatchImportRetainsTypedAttemptAndPublicNameWithoutReplay()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-content-drift");
        var hash = await fixture.ReadHashAsync("PLC_2/Main", "xml");

        var raw = await fixture.RunRawAsync(false, Patch("patch", "PLC_2/Main", hash), CreateTag("after", "AfterPatch"));

        Assert.False(raw.IsError);
        var response = Document(raw);
        Assert.False(response.Success);
        Assert.Null(response.Error);
        Assert.Equal(new[] { "failed", "skipped" }, response.Batch!.Operations.Select(item => item.Status));
        var failed = response.Batch.Operations[0];
        Assert.Equal("patch_block_logic", failed.Operation);
        Assert.Equal(WorkerFailureCategories.StateChanged, failed.Failure!.Category);
        Assert.Null(failed.Result);
        Assert.Equal("not_started", failed.Failure.BlockImportOutcome!.ImportStage);
        Assert.Single(requests.Methods(), method => method == "update_block_logic");
        Assert.Equal("contentHash", Assert.Single(response.Verification!.Operations).Check);
    }

    [Theory]
    [InlineData("plc-write-import-rejected", "engineering_rejected")]
    [InlineData("plc-write-import-portal-lost", "portal_lost")]
    [InlineData("plc-write-invalid-import", "protocol_error")]
    public async Task PatchImportFailuresKeepPublicIdentityAndStopWithoutReplay(string scenario, string category)
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, scenario);
        var hash = await fixture.ReadHashAsync("PLC_2/Main", "xml");

        var raw = await fixture.RunRawAsync(false, CreateTag("before", "BeforePatch"),
            Patch("patch", "PLC_2/Main", hash), CreateTag("after", "AfterPatch"));

        Assert.False(raw.IsError);
        var response = Document(raw);
        Assert.False(response.Success);
        Assert.Null(response.Error);
        Assert.Equal(new[] { "succeeded", "failed", "skipped" }, response.Batch!.Operations.Select(item => item.Status));
        var failed = response.Batch.Operations[1];
        Assert.Equal("patch_block_logic", failed.Operation);
        Assert.Equal(category, failed.Failure!.Category);
        if (category == "engineering_rejected")
        {
            Assert.Equal("EngineeringTargetInvocationException", failed.Failure.EngineeringError!.Type);
            Assert.Null(failed.Result);
            Assert.Equal("not_started", failed.Failure.BlockImportOutcome!.ImportStage);
        }
        Assert.Single(requests.Methods(), method => method == "update_block_logic");
        Assert.Single(requests.Methods(), method => method == "create_tag");
        Assert.DoesNotContain("patch_block_logic", requests.Methods());
        using var record = JsonDocument.Parse(Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path)));
        Assert.True(record.RootElement.GetProperty("items")[1].TryGetProperty("resolvedOperation", out _));
        Assert.False(record.RootElement.GetProperty("items")[2].TryGetProperty("resolvedOperation", out _));
    }

    [Fact]
    public async Task WholeSourceDeletionReachesExistingImporterWithEmptyContent()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);
        var original = await ExportAsync(fixture, "PLC_2/Main", "source");
        var hash = await fixture.ReadHashAsync("PLC_2/Main", "source");

        var response = await fixture.RunAsync(false,
            Patch("delete-content", "PLC_2/Main", hash, "source", original, ""));

        Assert.Null(response.Error);
        Assert.Equal("succeeded", Assert.Single(response.Batch!.Operations).Status);
        Assert.Single(requests.Methods(), method => method == "update_block_logic");
        Assert.Equal("", await ExportAsync(fixture, "PLC_2/Main", "source"));
        Assert.Null(Assert.Single(response.Verification!.Operations).ReadBack);
        Assert.DoesNotContain("\"readBack\"", CanonicalJson.Serialize(response));
        using var record = JsonDocument.Parse(Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path)));
        Assert.Equal("", record.RootElement.GetProperty("items")[0].GetProperty("resolvedOperation").GetProperty("content").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PatchAuditKeepsCallerEditsAndExactResolvedAttemptOnlyOnApply(bool dryRun)
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);
        var hash = await fixture.ReadHashAsync("PLC_2/Main", "xml");
        var original = await ExportAsync(fixture, "PLC_2/Main", "xml");

        var response = await fixture.RunAsync(dryRun, Patch("patch", "PLC_2/Main", hash));

        Assert.True(response.Success, JsonSerializer.Serialize(response));
        using var record = JsonDocument.Parse(Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path)));
        var requested = Assert.Single(record.RootElement.GetProperty("requestedOperations").EnumerateArray());
        Assert.Equal("patch_block_logic", requested.GetProperty("operation").GetString());
        Assert.Equal("Main", requested.GetProperty("edits")[0].GetProperty("oldText").GetString());
        var item = Assert.Single(record.RootElement.GetProperty("items").EnumerateArray());
        if (dryRun)
        {
            Assert.False(item.TryGetProperty("resolvedOperation", out _));
            return;
        }
        var resolved = item.GetProperty("resolvedOperation");
        Assert.Equal("update_block_logic", resolved.GetProperty("operation").GetString());
        Assert.Equal("patch", resolved.GetProperty("operationId").GetString());
        Assert.Equal("PLC_2/Main", resolved.GetProperty("blockPath").GetString());
        Assert.Equal("xml", resolved.GetProperty("format").GetString());
        Assert.Equal(hash, resolved.GetProperty("expectedContentHash").GetString());
        Assert.False(resolved.TryGetProperty("edits", out _));
        Assert.Equal(original.Replace("Main", "Patched", StringComparison.Ordinal), resolved.GetProperty("content").GetString());
        Assert.Equal(await ExportAsync(fixture, "PLC_2/Main", "xml"), resolved.GetProperty("content").GetString());
    }

    [Fact]
    public async Task PatchAuditRetainsExactResolvedLineEndingsAfterLaterFailure()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);
        var hash = await fixture.ReadHashAsync("PLC_2/Main", "source");
        const string source = "first\r\nanchor\r\nlast\n";
        var seed = UpdateBlock("seed", "PLC_2/Main", hash, "source");
        seed.Content = source;
        Assert.True((await fixture.RunAsync(false, seed)).Success);
        hash = await fixture.ReadHashAsync("PLC_2/Main", "source");

        var response = await fixture.RunAsync(false,
            Patch("patch", "PLC_2/Main", hash, "source", "anchor\n", "changed\nmore\n"),
            CreateTag("failure", "RejectedByPortal"));

        Assert.False(response.Success);
        Assert.Equal(new[] { "succeeded", "failed" }, response.Batch!.Operations.Select(item => item.Status));
        using var record = JsonDocument.Parse(NetworkGuardedWriteMcpTests.AuditLines(audit.Path).Last());
        var resolved = record.RootElement.GetProperty("items")[0].GetProperty("resolvedOperation");
        const string expected = "first\r\nchanged\r\nmore\r\nlast\n";
        Assert.Equal(expected, resolved.GetProperty("content").GetString());
        Assert.Equal(await ExportAsync(fixture, "PLC_2/Main", "source"), resolved.GetProperty("content").GetString());
        Assert.Equal(hash, resolved.GetProperty("expectedContentHash").GetString());
        Assert.False(record.RootElement.GetProperty("items")[1].TryGetProperty("resolvedOperation", out _));
    }

    [Theory]
    [InlineData("validation", McpAccessMode.ReadWrite, "error", "none")]
    [InlineData("blocked", McpAccessMode.ReadWrite, "blocked", "none")]
    [InlineData("dryRun", McpAccessMode.ReadWrite, "preview", "none")]
    [InlineData("applied", McpAccessMode.ReadWrite, "applied", "none")]
    [InlineData("applied", McpAccessMode.Full, "applied", "policy")]
    public async Task ExactlyOneAuditPerCall(string kind, McpAccessMode mode, string phase, string confirmation)
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip, mode);
        var items = kind switch
        {
            "validation" => new[] { new PlcOperationRequest { OperationId = "bad", Operation = "create_tag", TableName = "Default tag table" } },
            "blocked" => new[] { CreateOb("first", "PLC_2/ProgErr1", "ProgrammingError"), CreateOb("second", "PLC_2/ProgErr2", "ProgrammingError") },
            _ => new[] { CreateTag("tag", "Fresh") },
        };

        var response = await fixture.RunAsync(kind == "dryRun", items);

        Assert.Equal(phase, response.Phase);
        using var record = JsonDocument.Parse(Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path)));
        Assert.Equal("plc_write", record.RootElement.GetProperty("tool").GetString());
        Assert.Equal(phase, record.RootElement.GetProperty("phase").GetString());
        Assert.Equal(confirmation, record.RootElement.GetProperty("confirmation").GetProperty("by").GetString());
    }
}
