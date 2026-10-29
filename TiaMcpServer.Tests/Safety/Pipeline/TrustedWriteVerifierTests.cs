using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using Xunit;

namespace TiaMcpServer.Tests.Safety.Pipeline;

public sealed class TrustedWriteVerifierTests
{
    private sealed class FakePolicy : IWriteVerificationPolicy<FakeWriteItem>
    {
        public Dictionary<string, string[]> Keys { get; } = new();
        public HashSet<string> ReadBack { get; } = new();
        public List<string> ReadBackCalls { get; } = new();
        public StructuredOperationFailure? ObservedFailure { get; private set; }
        public WriteOperationVerification Result { get; set; } = new("a", true, "read", null, "observed", null);
        public string ReadTool => "fake_read";
        public IReadOnlyList<string> ObjectKeys(FakeWriteItem item, bool asLater) => Keys[item.OperationId];
        public bool HasReadBack(FakeWriteItem item) => ReadBack.Contains(item.OperationId);
        public Task<WriteOperationVerification> ReadBackAsync(FakeWriteItem item, StructuredOperationFailure? failure)
        {
            ReadBackCalls.Add(item.OperationId);
            ObservedFailure = failure;
            return Task.FromResult(Result);
        }
    }

    private static StructuredOperationItem Outcome(string id, string status = "succeeded", StructuredOperationFailure? failure = null)
        => new(id, "fake_write", status, null, failure, null, null, Array.Empty<string>());

    private static Task<WriteVerification> Verify(FakePolicy policy, params StructuredOperationItem[] outcomes)
        => new TrustedWriteVerifier<FakeWriteItem>(policy.Keys.Keys.Select(id => new FakeWriteItem(id)).ToArray(), policy)
            .VerifyAsync(StructuredOperationBatch.FromItems(outcomes));

    private static FakePolicy Policy(params string[] keys)
    {
        var policy = new FakePolicy();
        for (var i = 0; i < keys.Length; i++) policy.Keys[((char)('a' + i)).ToString()] = new[] { keys[i] };
        return policy;
    }

    [Fact]
    public async Task SucceededItemsOnDistinctObjects_AreTrusted()
    {
        var result = await Verify(Policy("a", "b"), Outcome("b"), Outcome("a"));
        Assert.True(result.Success);
        Assert.Null(result.Omission);
        Assert.Equal(new[] { "a", "b" }, result.Operations.Select(o => o.OperationId));
        Assert.All(result.Operations, o => { Assert.True(o.Success); Assert.Equal("trusted", o.Check); });
        Assert.Equal("Operation 'a': the Openness call completed without rejection; nothing was read back.", result.Operations[0].Message);
    }

    [Fact]
    public async Task LaterItemOnSameObject_SupersedesEarlier()
    {
        var result = await Verify(Policy("x", "x", "x"), Outcome("a"), Outcome("b"), Outcome("c"));
        Assert.Equal("superseded", result.Operations[0].Check);
        Assert.Equal("Operation 'a': superseded by later operation 'c' on the same object; the final state is verified there.", result.Operations[0].Message);
        Assert.Equal("trusted", result.Operations[2].Check);
    }

    [Fact]
    public async Task ContainerKey_SupersedesMemberKeys()
    {
        var result = await Verify(Policy("x\ny\nz", "x\ny"), Outcome("a"), Outcome("b"));
        Assert.Equal("superseded", result.Operations[0].Check);
        result = await Verify(Policy("x\ny\nz", "x\nyz"), Outcome("a"), Outcome("b"));
        Assert.Equal("trusted", result.Operations[0].Check);
    }

    [Fact]
    public async Task LaterPreconditionRefusal_DoesNotSupersede()
    {
        var result = await Verify(Policy("x", "x"), Outcome("a"), Outcome("b", "failed", new("target_not_found", "missing")));
        Assert.Equal("trusted", Assert.Single(result.Operations).Check);
    }

    [Fact]
    public async Task LaterEngineeringRejected_SupersedesWithUncertaintyMessage()
    {
        var result = await Verify(Policy("x", "x"), Outcome("a"), Outcome("b", "failed", new("engineering_rejected", "rejected")));
        var entry = Assert.Single(result.Operations);
        Assert.Equal("superseded", entry.Check);
        Assert.Equal("Operation 'a': superseded by later operation 'b' on the same object; the later operation failed and may have changed the object; its final state is not verified. Inspect it with fake_read before retrying.", entry.Message);
    }

    [Fact]
    public async Task ReadBackItems_AreReadBackEvenWhenFailed()
    {
        var policy = Policy("x");
        policy.ReadBack.Add("a");
        var failure = new StructuredOperationFailure("engineering_rejected", "rejected");
        var result = await Verify(policy, Outcome("a", "failed", failure));
        Assert.Same(policy.Result, Assert.Single(result.Operations));
        Assert.Same(failure, policy.ObservedFailure);
        Assert.Equal(new[] { "a" }, policy.ReadBackCalls);
    }

    [Fact]
    public async Task SupersededWins_OverReadBackForASucceededItem()
    {
        var policy = Policy("x", "x");
        policy.ReadBack.Add("a");
        var result = await Verify(policy, Outcome("a"), Outcome("b"));
        Assert.Equal("superseded", result.Operations[0].Check);
        Assert.Empty(policy.ReadBackCalls);
    }

    [Fact]
    public async Task SkippedAndFailedItemsWithoutReadBack_HaveNoEntry()
    {
        var result = await Verify(Policy("x", "x", "x"), Outcome("a", "skipped"), Outcome("b", "failed", new("engineering_rejected", "rejected")));
        Assert.Empty(result.Operations);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task Success_IsTheConjunctionOfEntries()
    {
        var policy = Policy("a", "b");
        policy.ReadBack.Add("a");
        policy.Result = policy.Result with { Success = false };
        var result = await Verify(policy, Outcome("a"), Outcome("b"));
        Assert.False(result.Success);
        Assert.Equal(2, result.Operations.Count);
    }
}
