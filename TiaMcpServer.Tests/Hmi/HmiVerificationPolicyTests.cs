using TiaMcpServer.Hmi;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using Xunit;

namespace TiaMcpServer.Tests.Hmi;

public class HmiVerificationPolicyTests
{
    [Fact]
    public async Task DistinctObjects_AreTrusted()
    {
        var checks = await Verify(
            (Item("table", "create_tag_table"), Effect("TagTable", "T")),
            (Item("tag", "create_tag"), Effect("Tag", "T")),
            (Item("alarm", "create_alarm"), Effect("Alarm", "T", "discrete")));
        Assert.All(checks, c => Assert.Equal(WriteVerificationChecks.Trusted, c));
    }

    [Fact]
    public async Task DiscreteAndAnalogSameName_AreIndependent()
    {
        var checks = await Verify(
            (Item("discrete", "update_alarm"), Effect("Alarm", "A", "discrete")),
            (Item("analog", "update_alarm"), Effect("Alarm", "A", "analog")));
        Assert.Equal([WriteVerificationChecks.Trusted, WriteVerificationChecks.Trusted], checks);
    }

    [Fact]
    public async Task Rename_OwnsOldAndNewName()
    {
        var checks = await Verify(
            (Item("old", "update_tag"), Effect("Tag", "Old")),
            (Item("rename", "update_tag", newName: "New"), Effect("Tag", "Old")),
            (Item("new", "update_tag"), Effect("Tag", "New")));
        Assert.Equal([WriteVerificationChecks.Superseded, WriteVerificationChecks.Superseded, WriteVerificationChecks.Trusted], checks);
        var policy = new HmiVerificationPolicy(new Dictionary<string, HmiWriteEffect> { ["rename"] = Effect("Tag", "Old") });
        Assert.Equal(["hmi\nHMI_1\ntag\nOLD", "hmi\nHMI_1\ntag\nNEW"], policy.ObjectKeys(Item("rename", "update_tag", newName: "New"), false));
    }

    [Fact]
    public async Task CaseOnlyRename_KeysMatchCaseInsensitively()
    {
        var checks = await Verify(
            (Item("create", "create_tag"), Effect("Tag", "motor")),
            (Item("rename", "update_tag", newName: "Motor"), Effect("Tag", "motor")));
        Assert.Equal([WriteVerificationChecks.Superseded, WriteVerificationChecks.Trusted], checks);
        var policy = new HmiVerificationPolicy(new Dictionary<string, HmiWriteEffect> { ["rename"] = Effect("Tag", "motor") });
        Assert.Equal(["hmi\nHMI_1\ntag\nMOTOR"], policy.ObjectKeys(Item("rename", "update_tag", newName: "Motor"), true));
    }

    [Fact]
    public async Task DeleteTable_SupersedesTagsItRemoves()
    {
        var checks = await Verify(
            (Item("tag", "create_tag"), Effect("Tag", "InCall")),
            (Item("other", "update_tag"), Effect("Tag", "Elsewhere")),
            (Item("delete", "delete_tag_table"), Effect("TagTable", "T", removes: ["incall"])));
        Assert.Equal([WriteVerificationChecks.Superseded, WriteVerificationChecks.Trusted, WriteVerificationChecks.Trusted], checks);
    }

    [Fact]
    public async Task DeleteThenRecreate_SupersedesEarlier()
    {
        var checks = await Verify(
            (Item("delete", "delete_tag"), Effect("Tag", "X")),
            (Item("create", "create_tag"), Effect("Tag", "x")));
        Assert.Equal([WriteVerificationChecks.Superseded, WriteVerificationChecks.Trusted], checks);
    }

    [Fact]
    public async Task LaterTagRename_DoesNotSupersedeEarlierAlarm()
    {
        var checks = await Verify(
            (Item("alarm", "create_alarm"), Effect("Alarm", "Trigger", "discrete")),
            (Item("rename", "update_tag", newName: "Renamed"), Effect("Tag", "Trigger")),
            (Item("table", "update_tag_table", newName: "Moved"), Effect("TagTable", "Trigger")));
        Assert.All(checks, c => Assert.Equal(WriteVerificationChecks.Trusted, c));
    }

    [Fact]
    public async Task PreconditionRefusal_DoesNotSupersede()
    {
        var items = new[] { Item("create", "create_tag"), Item("update", "update_tag") };
        var effects = new Dictionary<string, HmiWriteEffect> { ["create"] = Effect("Tag", "X"), ["update"] = Effect("Tag", "X") };
        var batch = StructuredOperationBatch.FromItems([Succeeded(items[0]),
            StructuredOperationItem.Failed(items[1], "target_not_found", "gone", [])]);
        var verification = await new TrustedWriteVerifier<HmiWriteOperationRequest>(items, new HmiVerificationPolicy(effects)).VerifyAsync(batch);
        var entry = Assert.Single(verification.Operations);
        Assert.Equal(("create", WriteVerificationChecks.Trusted), (entry.OperationId, entry.Check));
        Assert.True(verification.Success);
    }

    [Fact]
    public async Task MissingCultureAfterRename_SupersedesEarlierWriteAndKeepsFailure()
    {
        var items = new[] { Item("first", "update_tag"), Item("rename", "update_tag", newName: "Renamed") };
        var effects = items.ToDictionary(i => i.OperationId, _ => Effect("Tag", "X"));
        var failure = HmiWritePayloadContract.Project(items[1],
            TiaMcpServer.Worker.WorkerCallResult.Fail("target_not_found", "Text culture 'zu-ZA' was not found.") with
            {
                HmiWriteOutcome = new() { ObjectName = "Renamed", AppliedProperties = ["Name"], FailedProperty = "Comment" }
            });
        var batch = StructuredOperationBatch.FromItems([Succeeded(items[0]), failure]);
        var verification = await new TrustedWriteVerifier<HmiWriteOperationRequest>(items, new HmiVerificationPolicy(effects))
            .VerifyAsync(batch);
        var entry = Assert.Single(verification.Operations);
        Assert.Equal(("first", WriteVerificationChecks.Superseded), (entry.OperationId, entry.Check));
        Assert.Contains("the later operation failed and may have changed the object", entry.Message);
        Assert.Equal("target_not_found", failure.Failure!.Category);
        Assert.Equal("Renamed", failure.Result!.Value.GetProperty("objectName").GetString());
        Assert.True(verification.Success);
    }

    private static async Task<string[]> Verify(params (HmiWriteOperationRequest Item, HmiWriteEffect Effect)[] steps)
    {
        var items = steps.Select(s => s.Item).ToArray();
        var effects = steps.ToDictionary(s => s.Item.OperationId, s => s.Effect);
        var verification = await new TrustedWriteVerifier<HmiWriteOperationRequest>(items, new HmiVerificationPolicy(effects))
            .VerifyAsync(StructuredOperationBatch.FromItems(items.Select(Succeeded).ToArray()));
        Assert.True(verification.Success);
        Assert.Equal(items.Select(i => i.OperationId), verification.Operations.Select(o => o.OperationId));
        return verification.Operations.Select(o => o.Check).ToArray();
    }

    private static StructuredOperationItem Succeeded(HmiWriteOperationRequest item)
        => new(item.OperationId, item.Operation, OperationBatchStatus.Succeeded, null, null, null, null, []);

    private static HmiWriteOperationRequest Item(string id, string operation, string? newName = null)
        => new() { OperationId = id, Operation = operation, NewName = newName };

    private static HmiWriteEffect Effect(string kind, string name, string? alarmKind = null, string[]? removes = null)
        => new("op", new HmiTargetIdentity(kind, "HMI_1", name, alarmKind, kind == "Tag" ? "T" : null, null), [],
            removes is null ? null : new HmiRemoval(removes, []), null);
}
