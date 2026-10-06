using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Plc;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tests.Network;
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
    public async Task BindingIdentityChange_PreventsDispatch()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);
        var identity = (await fixture.Client.ListTagTablesAsync(null, Roundtrip)).SessionIdentity!;
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
        var identity = (await fixture.Client.ListTagTablesAsync(null, Roundtrip)).SessionIdentity!;
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
    public async Task MismatchedProjectPath_IsBindingConflictBeforeWorker()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);
        var before = requests.Methods().Length;
        var item = CreateTag("tag", "Fresh");
        item.ProjectPath = @"C:\Projects\Other.ap21";

        var response = await fixture.RunAsync(false, item);

        Assert.Equal("error", response.Phase);
        Assert.Equal(WorkerFailureCategories.BindingConflict, response.Error!.Category);
        Assert.Empty(requests.Methods().Skip(before).Intersect(WriteMethods.Append("list_tag_tables")));
    }

    [Fact]
    public async Task CreateTableThenTagInOneCall_Applies()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);

        var response = await fixture.RunAsync(false, CreateTable("table"), CreateTag("tag", "Valve1", table: "Valves"));

        Assert.Equal("applied", response.Phase);
        Assert.True(response.Success, JsonSerializer.Serialize(response));
        Assert.Null(response.Error);
        Assert.Equal(2, response.Batch!.Counts.Succeeded);
        Assert.Equal("table", response.Effects[1].Effect!.DependsOn);
        Assert.True(response.Verification!.Success);
        Assert.Equal(new[] { "exists", "values" }, response.Verification.Operations.Select(o => o.Check));
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
        Assert.Equal(new[] { "table", "tag" }, response.Effects.Select(e => e.OperationId));
        Assert.Null(response.Effects[0].Effect!.DependsOn);
        Assert.Equal("table", response.Effects[1].Effect!.DependsOn);
        Assert.Equal("Valves", response.Effects[1].Effect!.Target.TableName);
        Assert.Empty(requests.Methods().Intersect(WriteMethods));
        Assert.DoesNotContain("Valves", await fixture.InventoryAsync());
    }

    [Fact]
    public async Task DryRunListsBlockGuardThenActualCallBlocks()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);

        var preview = await fixture.RunAsync(true, CreateBlock("block", "PLC_2/Main"));
        var actual = await fixture.RunRawAsync(false, CreateBlock("block", "PLC_2/Main"));
        var blocked = Document(actual);

        Assert.Equal("preview", preview.Phase);
        Assert.True(preview.Success);
        Assert.Contains(preview.Guards, g => g.Id == PlcGuardDefinitions.BlockExists && g.Severity == "block");
        Assert.True(actual.IsError);
        Assert.Equal("blocked", blocked.Phase);
        Assert.Equal(WorkerFailureCategories.GuardBlocked, blocked.Error!.Category);
        Assert.Null(blocked.Batch);
        Assert.DoesNotContain("create_block", requests.Methods());
    }

    [Fact]
    public async Task CollisionBlocksWholeCall_NothingRuns()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);

        var response = await fixture.RunAsync(false, CreateTag("fresh", "Fresh"), CreateTag("start", "start"));

        Assert.Equal("blocked", response.Phase);
        Assert.Contains(response.Guards, g => g.Id == PlcGuardDefinitions.NameCollision && g.OperationId == "start");
        Assert.Null(response.Batch);
        Assert.DoesNotContain("create_tag", requests.Methods());
        Assert.DoesNotContain("Fresh", await fixture.InventoryAsync());
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
        Assert.Equal(new[] { "a", "b" }, response.Verification!.Operations.Select(o => o.OperationId));
        Assert.True(response.Verification.Success);
        var inventory = await fixture.InventoryAsync();
        Assert.Contains("TagB", inventory);
        Assert.DoesNotContain("TagD", inventory);
        Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path));
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnverifiableEvidenceBlocks(bool dryRun)
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-unverifiable");

        var response = await fixture.RunAsync(dryRun, CreateBlock("block", "Station_1/NewFC"));

        Assert.Equal(dryRun ? "preview" : "blocked", response.Phase);
        Assert.Contains(response.Guards, g => g.Id == PlcGuardDefinitions.StateUnverifiable && g.Severity == "block");
        Assert.Null(response.Batch);
        Assert.DoesNotContain("create_block", requests.Methods());
    }

    [Fact]
    public async Task VerificationFailure_IsSuccessFalseWithWarning()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-late-block");

        var response = await fixture.RunAsync(false, CreateTable("table"));

        Assert.Equal("applied", response.Phase);
        Assert.False(response.Success);
        Assert.Null(response.Error);
        Assert.Equal("succeeded", response.Batch!.Operations[0].Status);
        Assert.False(response.Verification!.Success);
        Assert.False(Assert.Single(response.Verification.Operations).Success);
        Assert.Contains(WriteExecution.VerificationFailureMessage, response.Warnings);
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
        var item = kind switch
        {
            "validation" => new PlcOperationRequest { OperationId = "bad", Operation = "create_tag", TableName = "Default tag table" },
            "blocked" => CreateTag("tag", "Start"),
            _ => CreateTag("tag", "Fresh"),
        };

        var response = await fixture.RunAsync(kind == "dryRun", item);

        Assert.Equal(phase, response.Phase);
        using var record = JsonDocument.Parse(Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path)));
        Assert.Equal("plc_write", record.RootElement.GetProperty("tool").GetString());
        Assert.Equal(phase, record.RootElement.GetProperty("phase").GetString());
        Assert.Equal(confirmation, record.RootElement.GetProperty("confirmation").GetProperty("by").GetString());
    }
}
