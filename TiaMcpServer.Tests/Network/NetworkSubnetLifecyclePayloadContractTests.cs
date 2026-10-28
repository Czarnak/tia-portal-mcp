using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Network;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Network;

/// <summary>
/// Contract tests for the three subnet lifecycle operations: create_subnet, update_subnet,
/// delete_subnet. Each operation declares SubnetLifecycleResultInfo as its result type and must
/// reject anything else as protocol_error without echoing the payload.
/// </summary>
public class NetworkSubnetLifecyclePayloadContractTests
{
    [Theory]
    [InlineData("create_subnet")]
    [InlineData("update_subnet")]
    [InlineData("delete_subnet")]
    public void SubnetResult_RequiresOnlyIdentityAndName(string operation)
    {
        var item = Project(operation, """{"subnetId":"subnet-1","name":"Ethernet"}""");
        Assert.Equal(OperationBatchStatus.Succeeded, item.Status);
        Assert.Equal(new[] { "name", "subnetId" }, item.Result!.Value.EnumerateObject().Select(p => p.Name).OrderBy(n => n));
    }

    private const string LeakToken = "payload-leak-canary";

    private static StructuredOperationItem Project(string operation, string payload)
        => NetworkPayloadContract.Project(
            new NetworkOperationRequest { OperationId = "op-1", Operation = operation, Target = new() { SubnetId = "subnet-1" } },
            WorkerCallResult.Ok(payload));

    /// <summary>
    /// Valid minimal payload for subnet lifecycle operations should decode into a succeeded item.
    /// Before the decoder is registered, this will fail with protocol_error (RED phase).
    /// </summary>
    [Theory]
    [InlineData("create_subnet")]
    [InlineData("update_subnet")]
    [InlineData("delete_subnet")]
    public void Project_DecodesValidSubnetLifecyclePayload_IntoSucceededItem(string operation)
    {
        var validPayload = """{"subnetId":"subnet-1","name":"Ethernet"}""";
        var item = Project(operation, validPayload);

        // After registration, should succeed with JsonElement result
        Assert.Equal(OperationBatchStatus.Succeeded, item.Status);
        Assert.Null(item.Failure);
        Assert.NotNull(item.Result);
        Assert.Equal(JsonValueKind.Object, item.Result!.Value.ValueKind);

        // Verify the minimal result members.
        Assert.True(item.Result.Value.TryGetProperty("subnetId", out var subnetId));
        Assert.Equal("subnet-1", subnetId.GetString());
        Assert.True(item.Result.Value.TryGetProperty("name", out var name));
        Assert.Equal("Ethernet", name.GetString());
    }

    /// <summary>
    /// Missing subnetId must be rejected during validation.
    /// </summary>
    [Theory]
    [InlineData("create_subnet")]
    [InlineData("update_subnet")]
    [InlineData("delete_subnet")]
    public void Project_RejectsPayload_WhenSubnetIdMissing(string operation)
    {
        var payload = """{"name":"Ethernet"}""";
        var item = Project(operation, payload);

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.NotNull(item.Failure);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
    }

    /// <summary>
    /// Null subnetId must be rejected during validation.
    /// </summary>
    [Theory]
    [InlineData("create_subnet")]
    [InlineData("update_subnet")]
    [InlineData("delete_subnet")]
    public void Project_RejectsPayload_WhenSubnetIdNull(string operation)
    {
        var payload = """{"subnetId":null,"name":"Ethernet"}""";
        var item = Project(operation, payload);

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.NotNull(item.Failure);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
    }

    /// <summary>
    /// Blank (whitespace-only) subnetId must be rejected during validation.
    /// </summary>
    [Theory]
    [InlineData("create_subnet")]
    [InlineData("update_subnet")]
    [InlineData("delete_subnet")]
    public void Project_RejectsPayload_WhenSubnetIdBlank(string operation)
    {
        var payload = """{"subnetId":"   ","name":"Ethernet"}""";
        var item = Project(operation, payload);

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.NotNull(item.Failure);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
    }

    /// <summary>
    /// Missing name must be rejected during validation.
    /// </summary>
    [Theory]
    [InlineData("create_subnet")]
    [InlineData("update_subnet")]
    [InlineData("delete_subnet")]
    public void Project_RejectsPayload_WhenNameMissing(string operation)
    {
        var payload = """{"subnetId":"subnet-1"}""";
        var item = Project(operation, payload);

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.NotNull(item.Failure);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
    }

    /// <summary>
    /// Null name must be rejected during validation.
    /// </summary>
    [Theory]
    [InlineData("create_subnet")]
    [InlineData("update_subnet")]
    [InlineData("delete_subnet")]
    public void Project_RejectsPayload_WhenNameNull(string operation)
    {
        var payload = """{"subnetId":"subnet-1","name":null}""";
        var item = Project(operation, payload);

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.NotNull(item.Failure);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
    }

    /// <summary>
    /// Blank (whitespace-only) name must be rejected during validation.
    /// </summary>
    [Theory]
    [InlineData("create_subnet")]
    [InlineData("update_subnet")]
    [InlineData("delete_subnet")]
    public void Project_RejectsPayload_WhenNameBlank(string operation)
    {
        var payload = """{"subnetId":"subnet-1","name":"  "}""";
        var item = Project(operation, payload);

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.NotNull(item.Failure);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
    }

    /// <summary>
    /// Extra members like networkType must be rejected (JsonUnmappedMemberHandling.Disallow).
    /// </summary>
    [Theory]
    [InlineData("create_subnet")]
    [InlineData("update_subnet")]
    [InlineData("delete_subnet")]
    public void Project_RejectsPayload_WhenExtraNetworkTypeMember(string operation)
    {
        var payload = """{"subnetId":"subnet-1","name":"Ethernet","networkType":"Ethernet"}""";
        var item = Project(operation, payload);

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.NotNull(item.Failure);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
        // Payload must not be echoed
        Assert.DoesNotContain(payload, item.Failure.Message);
    }

    /// <summary>
    /// Extra members like highestAddress must be rejected.
    /// </summary>
    [Theory]
    [InlineData("create_subnet")]
    [InlineData("update_subnet")]
    [InlineData("delete_subnet")]
    public void Project_RejectsPayload_WhenExtraHighestAddressMember(string operation)
    {
        var payload = """{"subnetId":"subnet-1","name":"Ethernet","highestAddress":126}""";
        var item = Project(operation, payload);

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.NotNull(item.Failure);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
    }

    /// <summary>
    /// Extra members like devices must be rejected.
    /// </summary>
    [Theory]
    [InlineData("create_subnet")]
    [InlineData("update_subnet")]
    [InlineData("delete_subnet")]
    public void Project_RejectsPayload_WhenExtraDevicesMember(string operation)
    {
        var payload = """{"subnetId":"subnet-1","name":"Ethernet","devices":[]}""";
        var item = Project(operation, payload);

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.NotNull(item.Failure);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
    }

    /// <summary>
    /// Wrong root type (array instead of object) must be rejected.
    /// </summary>
    [Theory]
    [InlineData("create_subnet")]
    [InlineData("update_subnet")]
    [InlineData("delete_subnet")]
    public void Project_RejectsPayload_WhenRootIsArray(string operation)
    {
        var payload = """[{"subnetId":"subnet-1"}]""";
        var item = Project(operation, payload);

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.NotNull(item.Failure);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
    }

    /// <summary>
    /// The payload must not be echoed into the failure message (test with invalid payload).
    /// </summary>
    [Theory]
    [InlineData("create_subnet")]
    [InlineData("update_subnet")]
    [InlineData("delete_subnet")]
    public void Project_DoesNotEchoPayload_InFailureMessage(string operation)
    {
        var payload = """{"subnetId":"payload-leak-canary","name":"Ethernet","unexpected":true}""";
        var item = Project(operation, payload);

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.NotNull(item.Failure);
        Assert.DoesNotContain(LeakToken, item.Failure.Message);
    }
}
