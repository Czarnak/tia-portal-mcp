using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Hmi;
using TiaMcpServer.Json;
using TiaMcpServer.Worker;
using TiaMcpServer.Safety;
using Xunit;

namespace TiaMcpServer.Tests.Hmi;

public class HmiGuardedWriteDomainTests
{
    [Fact]
    public void FailedCreatePreservesTypedPartialOutcomeAlongsideFailure()
    {
        var outcome = new HmiWriteOutcomeInfo { ObjectName = "T", AppliedProperties = ["Name", "Connection"], FailedProperty = "PlcTag", Cleanup = "failed", CleanupMessage = "identity changed" };
        var item = HmiWritePayloadContract.Project(Item(), new WorkerCallResult(false, "", "setter rejected", "worker_operation_failed", []) { HmiWriteOutcome = outcome });
        Assert.Equal("failed", item.Status);
        Assert.Equal("setter rejected", item.Failure!.Message);
        Assert.Equal(CanonicalJson.Serialize(outcome), item.Result!.Value.GetRawText());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"objectName\":\"secret\",\"appliedProperties\":null,\"failedProperty\":null,\"cleanup\":null,\"cleanupMessage\":null}")]
    [InlineData("null")]
    public void MalformedSuccessIsProtocolErrorWithoutPayloadEcho(string payload)
    {
        var item = HmiWritePayloadContract.Project(Item(), new WorkerCallResult(true, payload, null, null, []));
        Assert.Equal("protocol_error", item.Failure!.Category);
        Assert.Null(item.Result);
        Assert.DoesNotContain("secret", CanonicalJson.Serialize(item));
    }

    [Fact]
    public void SuccessUsesStrictTypedOutcome()
    {
        var outcome = new HmiWriteOutcomeInfo { ObjectName = "T", AppliedProperties = ["Name"] };
        var item = HmiWritePayloadContract.Project(Item(), new WorkerCallResult(true, CanonicalJson.Serialize(outcome), null, null, []));
        Assert.Equal("succeeded", item.Status);
        Assert.Equal("T", item.Result!.Value.GetProperty("objectName").GetString());
    }

    [Fact]
    public void ReadOnlyDomainDeniesBeforeAnyWorkerIsStarted()
    {
        using var client = new OpennessWorkerClient(new ProjectSessionBinding(null), logger: null,
            accessPolicy: new OperationAccessPolicy(McpAccessMode.ReadOnly));
        var domain = new HmiWriteDomain(client);
        Assert.False(domain.ConfirmsEveryCall);
        Assert.Equal("access_denied", domain.Validate([Item()], McpAccessMode.ReadOnly).Error!.Category);
        Assert.False(domain.VerificationSucceeded(null));
    }

    internal static HmiWriteOperationRequest Item(string id = "a") => new() { OperationId = id, Operation = "create_tag_table", TableName = "T" };
}
