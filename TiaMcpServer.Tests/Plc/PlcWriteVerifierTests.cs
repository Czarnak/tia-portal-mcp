using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Plc;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tests.TestSupport;
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

    /// <summary>
    /// Plans the items first (the effects carry the resolved PLC), optionally mutates them, then
    /// verifies and returns the worker methods the verification itself issued.
    /// </summary>
    private static async Task<(PlcWriteVerification Verification, string[] PostWriteMethods)> VerifyAsync(
        PlcGuardedWriteFixture fixture, FakeWorkerRequestLog requests, bool mutate, params PlcOperationRequest[] items)
    {
        var plan = await new PlcWritePlanner(fixture.Client).PlanAsync(items);
        Assert.True(plan.Plan.Success, plan.Plan.Error?.Message);
        if (mutate)
            foreach (var item in items)
                Assert.True((await PlcWorkerInvoker.InvokeWriteAsync(fixture.Client, item)).Success);
        var effects = items.Select((item, i) => (item.OperationId, Effect: plan.Plan.Items[i].Effect!))
            .ToDictionary(p => p.OperationId, p => p.Effect);
        var before = requests.Methods().Length;
        var verification = await new PlcWriteVerifier(fixture.Client, items, effects).VerifyAsync(Succeeded(items));
        return (verification, requests.Methods().Skip(before).ToArray());
    }

    [Fact]
    public async Task ContentVerificationReportsNewHashWithExactlyOneReExport()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Scenario);
        var item = PlcGuardedWriteFixture.UpdateBlock("content", "PLC_2/Main", await fixture.ReadHashAsync("PLC_2/Main", "xml"));

        var (verification, postWrite) = await VerifyAsync(fixture, requests, mutate: true, item);

        Assert.True(verification.Success);
        var check = Assert.Single(verification.Operations);
        Assert.Equal("contentHash", check.Check);
        Assert.Equal(ContentHashRules.Compute("xml", item.Content!), check.ContentHash);
        Assert.NotEqual(item.ExpectedContentHash, check.ContentHash);
        Assert.Equal(new[] { "get_block_content" }, postWrite);
    }

    [Fact]
    public async Task TypeContentVerificationReExportsTheTypeOnce()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Scenario);
        var read = await PlcWorkerInvoker.InvokeReadAsync(fixture.Client,
            new PlcOperationRequest { OperationId = "r", Operation = "get_type_content", TypePath = TypePath });
        var hash = PlcPayloadContract.Project(new PlcOperationRequest { OperationId = "r", Operation = "get_type_content", TypePath = TypePath }, read)
            .Result!.Value.GetProperty("contentHash").GetString()!;
        var item = new PlcOperationRequest { OperationId = "type", Operation = "update_type_content", TypePath = TypePath, Content = "TYPE x", ExpectedContentHash = hash };

        var (verification, postWrite) = await VerifyAsync(fixture, requests, mutate: true, item);

        Assert.True(verification.Success, string.Join("; ", verification.Operations.Select(o => o.Message)));
        Assert.Equal("contentHash", Assert.Single(verification.Operations).Check);
        Assert.Equal(new[] { "get_type_content" }, postWrite);
    }

    private const string TypePath = "Station_1/Types/UDT_Settings";

    [Fact]
    public async Task NonContentItemsAreTrustedWithoutAnyPostWriteRead()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Scenario);
        var items = new[]
        {
            PlcGuardedWriteFixture.CreateTable("table"),
            PlcGuardedWriteFixture.CreateTag("tag", "Fresh"),
            new PlcOperationRequest { OperationId = "delete", Operation = "delete_tag", PlcName = "PLC_2", TableName = "Default tag table", Name = "Start" },
            PlcGuardedWriteFixture.CreateBlock("block", "PLC_2/FC_Fresh"),
        };

        var (verification, postWrite) = await VerifyAsync(fixture, requests, mutate: true, items);

        Assert.True(verification.Success);
        Assert.All(verification.Operations, o =>
        {
            Assert.Equal("trusted", o.Check);
            Assert.True(o.Success);
            Assert.Contains("without rejection", o.Message);
        });
        Assert.Equal(4, verification.Operations.Count);
        Assert.Empty(postWrite);
    }

    [Fact]
    public async Task UnwrittenItemIsStillTrustedBecauseOnlyTheOpennessCallIsEvidence()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Scenario);

        var (verification, postWrite) = await VerifyAsync(fixture, requests, mutate: false, PlcGuardedWriteFixture.CreateTag("ghost", "Ghost"));

        Assert.True(verification.Success);
        Assert.Equal("trusted", Assert.Single(verification.Operations).Check);
        Assert.Empty(postWrite);
    }

    [Fact]
    public async Task ContentReExportFailureFailsVerification()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Scenario);
        // Empty_DB exports empty text, so the written content cannot be hashed.
        var item = PlcGuardedWriteFixture.UpdateBlock("content", "Station_1/Empty_DB", "unused");
        var target = new PlcTargetIdentity("Block", "Station_1", null, null, null, "Empty_DB", "Station_1/Empty_DB", null);
        var effect = new PlcWriteEffect("update_block_logic", target, Array.Empty<PlcFieldChange>(), null, null, null, null);

        var verification = await new PlcWriteVerifier(fixture.Client, new[] { item }, new Dictionary<string, PlcWriteEffect> { ["content"] = effect })
            .VerifyAsync(Succeeded(item));

        Assert.False(verification.Success);
        var check = Assert.Single(verification.Operations);
        Assert.Equal(("contentHash", false), (check.Check, check.Success));
        Assert.Contains("could not be re-exported", check.Message);
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

    [Theory]
    [InlineData(WorkerFailureCategories.StateChanged, "trusted")]
    [InlineData(WorkerFailureCategories.EngineeringRejected, "superseded")]
    [InlineData(WorkerFailureCategories.PostconditionFailed, "superseded")]
    public async Task FailedNonContentItemIsOmittedAndPossibleMutationLeavesFinalStateUnverified(string category, string expectedCheck)
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Scenario);
        var create = Tag("create", "create_tag", "Temp");
        var update = Tag("update", "update_tag", "Temp", i => i.DataType = "Int");
        var plan = await new PlcWritePlanner(fixture.Client).PlanAsync(new[] { create, update });
        Assert.True((await PlcWorkerInvoker.InvokeWriteAsync(fixture.Client, create)).Success);
        var batch = StructuredOperationBatch.FromItems(new[]
        {
            new StructuredOperationItem("create", "create_tag", OperationBatchStatus.Succeeded, null, null, null, null, Array.Empty<string>()),
            new StructuredOperationItem("update", "update_tag", OperationBatchStatus.Failed, null,
                new StructuredOperationFailure(category, "The update failed."), null, null, Array.Empty<string>()),
        });
        var before = requests.Methods().Length;

        var verification = await new PlcWriteVerifier(fixture.Client, new[] { create, update }, new Dictionary<string, PlcWriteEffect>
        {
            ["create"] = plan.Plan.Items[0].Effect!, ["update"] = plan.Plan.Items[1].Effect!,
        }).VerifyAsync(batch);

        Assert.True(verification.Success);
        var check = Assert.Single(verification.Operations);
        Assert.Equal(("create", expectedCheck), (check.OperationId, check.Check));
        if (expectedCheck == "superseded")
        {
            Assert.Contains("not verified", check.Message);
            Assert.Contains("plc_read", check.Message);
            Assert.DoesNotContain("the final state is verified there", check.Message);
        }
        Assert.Equal(before, requests.Methods().Length);
    }

    [Fact]
    public async Task RejectedDuplicateSupersedesEarlierCreateAndStopsBeforeTheNextItem()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Scenario);

        var response = await fixture.RunAsync(false, Tag("create", "create_tag", "Temp"),
            Tag("duplicate", "create_tag", "Temp"), Tag("after", "create_tag", "After"));

        Assert.False(response.Success);
        Assert.Equal(new[] { "succeeded", "failed", "skipped" }, response.Batch!.Operations.Select(o => o.Status));
        Assert.Equal(WorkerFailureCategories.EngineeringRejected, response.Batch.Operations[1].Failure!.Category);
        var check = Assert.Single(response.Verification!.Operations);
        Assert.Equal(("create", "superseded"), (check.OperationId, check.Check));
        Assert.True(check.Success);
        Assert.Null(check.ContentHash);
        Assert.Contains("not verified", check.Message);
        Assert.Contains("plc_read", check.Message);
        var inventory = await fixture.InventoryAsync();
        Assert.Contains("Temp", inventory);
        Assert.DoesNotContain("After", inventory);
    }
}
