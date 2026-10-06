using TiaMcpServer.Contracts;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Plc;
using TiaMcpServer.Safety.Pipeline;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

/// <summary>The verifier against a bound plc-write-roundtrip FakeWorker; batches are built by hand.</summary>
[Collection(RealWorkerProcessCollection.Name)]
public sealed class PlcWriteVerifierTests
{
    private const string Scenario = "plc-write-roundtrip";

    private static StructuredOperationBatch Succeeded(params PlcOperationRequest[] items)
        => StructuredOperationBatch.FromItems(items.Select(i => new StructuredOperationItem(i.OperationId, i.Operation,
            OperationBatchStatus.Succeeded, null, null, null, null, Array.Empty<string>())).ToArray());

    /// <summary>Plans the items first (the effects carry the resolved PLC), then optionally mutates them.</summary>
    private static async Task<PlcWriteVerification> VerifyAsync(PlcGuardedWriteFixture fixture, bool mutate, params PlcOperationRequest[] items)
    {
        var plan = await new PlcWritePlanner(fixture.Client).PlanAsync(Scenario, items);
        Assert.True(plan.Plan.Success, plan.Plan.Error?.Message);
        if (mutate)
            foreach (var item in items)
                Assert.True((await PlcWorkerInvoker.InvokeWriteAsync(fixture.Client, item)).Success);
        var effects = items.Select((item, i) => (item.OperationId, Effect: plan.Plan.Items[i].Effect!))
            .ToDictionary(p => p.OperationId, p => p.Effect);
        return await new PlcWriteVerifier(fixture.Client, items, effects).VerifyAsync(Scenario, Succeeded(items));
    }

    [Fact]
    public async Task ContentVerificationReportsNewHash()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Scenario);
        var item = PlcGuardedWriteFixture.UpdateBlock("content", "PLC_2/Main", await fixture.ReadHashAsync("PLC_2/Main", "xml"));

        var verification = await VerifyAsync(fixture, mutate: true, item);

        Assert.True(verification.Success);
        var check = Assert.Single(verification.Operations);
        Assert.Equal("contentHash", check.Check);
        Assert.Equal(ContentHashRules.Compute("xml", item.Content!), check.ContentHash);
        Assert.NotEqual(item.ExpectedContentHash, check.ContentHash);
    }

    [Fact]
    public async Task MissingCreatedTagFailsVerification()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Scenario);

        // The batch claims success, but the tag was never created.
        var verification = await VerifyAsync(fixture, mutate: false, PlcGuardedWriteFixture.CreateTag("ghost", "Ghost"));

        Assert.False(verification.Success);
        var check = Assert.Single(verification.Operations);
        Assert.False(check.Success);
        Assert.Equal("values", check.Check);
        Assert.Equal("absent", check.Actual);
        Assert.Contains("Ghost", check.Message);
    }

    private static PlcOperationRequest Tag(string id, string operation, string name, Action<PlcOperationRequest>? set = null)
    {
        var item = new PlcOperationRequest { OperationId = id, Operation = operation, PlcName = "PLC_2", TableName = "Default tag table", Name = name };
        if (operation == "create_tag") item.DataType = "Bool";
        set?.Invoke(item);
        return item;
    }

    public static TheoryData<string> Sequences => new() { "create-delete", "create-update", "delete-recreate", "rename-use" };

    private static PlcOperationRequest[] Sequence(string name) => name switch
    {
        "create-delete" => new[] { Tag("first", "create_tag", "Temp"), Tag("second", "delete_tag", "Temp") },
        "create-update" => new[] { Tag("first", "create_tag", "Temp"), Tag("second", "update_tag", "Temp", i => i.DataType = "Int") },
        "delete-recreate" => new[] { Tag("first", "delete_tag", "Start"), Tag("second", "create_tag", "Start") },
        _ => new[] { Tag("first", "update_tag", "Start", i => i.NewName = "Go"), Tag("second", "update_tag", "Go", i => i.LogicalAddress = "%I0.7") },
    };

    [Theory]
    [MemberData(nameof(Sequences))]
    public async Task SameObjectSequenceVerifiesEffectiveFinalState(string sequence)
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Scenario);

        var response = await fixture.RunAsync(false, Sequence(sequence));

        Assert.True(response.Success, System.Text.Json.JsonSerializer.Serialize(response));
        Assert.True(response.Verification!.Success);
        Assert.DoesNotContain(WriteExecution.VerificationFailureMessage, response.Warnings);
        var superseded = response.Verification.Operations[0];
        Assert.True(superseded.Success);
        Assert.Equal("superseded", superseded.Check);
        Assert.Contains("'second'", superseded.Message);
        Assert.NotEqual("superseded", response.Verification.Operations[1].Check);
    }

    [Fact]
    public async Task FailedItemThatNeverRanIsObservedWithoutFailingOrSuperseding()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Scenario);
        var create = Tag("create", "create_tag", "Temp");
        var update = Tag("update", "update_tag", "Temp", i => i.DataType = "Int");
        var plan = await new PlcWritePlanner(fixture.Client).PlanAsync(Scenario, new[] { create, update });
        Assert.True((await PlcWorkerInvoker.InvokeWriteAsync(fixture.Client, create)).Success);
        var batch = StructuredOperationBatch.FromItems(new[]
        {
            new StructuredOperationItem("create", "create_tag", OperationBatchStatus.Succeeded, null, null, null, null, Array.Empty<string>()),
            new StructuredOperationItem("update", "update_tag", OperationBatchStatus.Failed, null,
                new StructuredOperationFailure(WorkerFailureCategories.StateChanged, "The tag changed."), null, null, Array.Empty<string>()),
        });

        var verification = await new PlcWriteVerifier(fixture.Client, new[] { create, update }, new Dictionary<string, PlcWriteEffect>
        {
            ["create"] = plan.Plan.Items[0].Effect!, ["update"] = plan.Plan.Items[1].Effect!,
        }).VerifyAsync(Scenario, batch);

        Assert.True(verification.Success, string.Join("; ", verification.Operations.Select(o => o.Message)));
        Assert.Equal(new[] { "values", "observed" }, verification.Operations.Select(o => o.Check));
        Assert.Contains("dataType=Bool", verification.Operations[1].Actual);
        Assert.Contains("never ran", verification.Operations[1].Message);
    }

    [Fact]
    public async Task CreatedTableAndDeletedTagVerify()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Scenario);
        var delete = new PlcOperationRequest { OperationId = "delete", Operation = "delete_tag", PlcName = "PLC_2", TableName = "Default tag table", Name = "Start" };

        var verification = await VerifyAsync(fixture, mutate: true, delete, PlcGuardedWriteFixture.CreateTable("table"));

        Assert.True(verification.Success, string.Join("; ", verification.Operations.Select(o => o.Message)));
        Assert.Equal(new[] { "absent", "exists" }, verification.Operations.Select(o => o.Check));
    }

    [Fact]
    public async Task DeleteProbeUsesResolvedTargetNotCallerPath()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Scenario);
        // "PLC_2/Main" resolves to PLC_2/Blocks/Main; the later create makes a different Main in a group.
        var items = new[]
        {
            new PlcOperationRequest { OperationId = "del", Operation = "delete_block", BlockPath = "PLC_2/Main" },
            new PlcOperationRequest { OperationId = "grp", Operation = "create_block_group", BlockPath = "PLC_2/Blocks/F" },
            PlcGuardedWriteFixture.CreateBlock("add", "PLC_2/Blocks/F/Main"),
        };

        var verification = await VerifyAsync(fixture, mutate: true, items);

        Assert.True(verification.Success, string.Join("; ", verification.Operations.Select(o => o.Message)));
        Assert.Equal("absent", verification.Operations[0].Check);
    }
}
