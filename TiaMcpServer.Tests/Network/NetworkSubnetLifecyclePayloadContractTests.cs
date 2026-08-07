using System.Text.Json;
using TiaMcpServer.Contracts;
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
    private const string LeakToken = "payload-leak-canary";

    private static StructuredOperationItem Project(string operation, string payload)
        => NetworkPayloadContract.Project(
            new NetworkOperationRequest { OperationId = "op-1", Operation = operation },
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
        var validPayload = """{"subnetId":"subnet-1","name":"Ethernet","networkDeviceCount":0,"networkDeviceCountUnchanged":true}""";
        var item = Project(operation, validPayload);

        // After registration, should succeed with JsonElement result
        Assert.Equal(OperationBatchStatus.Succeeded, item.Status);
        Assert.Null(item.Failure);
        Assert.NotNull(item.Result);
        Assert.Equal(JsonValueKind.Object, item.Result!.Value.ValueKind);

        // Verify all four members are present in the result
        Assert.True(item.Result.Value.TryGetProperty("subnetId", out var subnetId));
        Assert.Equal("subnet-1", subnetId.GetString());
        Assert.True(item.Result.Value.TryGetProperty("name", out var name));
        Assert.Equal("Ethernet", name.GetString());
        Assert.True(item.Result.Value.TryGetProperty("networkDeviceCount", out var count));
        Assert.Equal(0, count.GetInt32());
        Assert.True(item.Result.Value.TryGetProperty("networkDeviceCountUnchanged", out var unchanged));
        Assert.True(unchanged.GetBoolean());
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
        var payload = """{"name":"Ethernet","networkDeviceCount":0,"networkDeviceCountUnchanged":true}""";
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
        var payload = """{"subnetId":null,"name":"Ethernet","networkDeviceCount":0,"networkDeviceCountUnchanged":true}""";
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
        var payload = """{"subnetId":"   ","name":"Ethernet","networkDeviceCount":0,"networkDeviceCountUnchanged":true}""";
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
        var payload = """{"subnetId":"subnet-1","networkDeviceCount":0,"networkDeviceCountUnchanged":true}""";
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
        var payload = """{"subnetId":"subnet-1","name":null,"networkDeviceCount":0,"networkDeviceCountUnchanged":true}""";
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
        var payload = """{"subnetId":"subnet-1","name":"  ","networkDeviceCount":0,"networkDeviceCountUnchanged":true}""";
        var item = Project(operation, payload);

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.NotNull(item.Failure);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
    }

    /// <summary>
    /// Negative networkDeviceCount must be rejected during validation.
    /// </summary>
    [Theory]
    [InlineData("create_subnet")]
    [InlineData("update_subnet")]
    [InlineData("delete_subnet")]
    public void Project_RejectsPayload_WhenNetworkDeviceCountNegative(string operation)
    {
        var payload = """{"subnetId":"subnet-1","name":"Ethernet","networkDeviceCount":-1,"networkDeviceCountUnchanged":true}""";
        var item = Project(operation, payload);

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.NotNull(item.Failure);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
    }

    /// <summary>
    /// Missing networkDeviceCountUnchanged must be rejected during validation.
    /// </summary>
    [Theory]
    [InlineData("create_subnet")]
    [InlineData("update_subnet")]
    [InlineData("delete_subnet")]
    public void Project_RejectsPayload_WhenNetworkDeviceCountUnchangedMissing(string operation)
    {
        var payload = """{"subnetId":"subnet-1","name":"Ethernet","networkDeviceCount":0}""";
        var item = Project(operation, payload);

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.NotNull(item.Failure);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
    }

    /// <summary>
    /// False networkDeviceCountUnchanged must be rejected during validation.
    /// </summary>
    [Theory]
    [InlineData("create_subnet")]
    [InlineData("update_subnet")]
    [InlineData("delete_subnet")]
    public void Project_RejectsPayload_WhenNetworkDeviceCountUnchangedFalse(string operation)
    {
        var payload = """{"subnetId":"subnet-1","name":"Ethernet","networkDeviceCount":0,"networkDeviceCountUnchanged":false}""";
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
        var payload = """{"subnetId":"subnet-1","name":"Ethernet","networkDeviceCount":0,"networkDeviceCountUnchanged":true,"networkType":"Ethernet"}""";
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
        var payload = """{"subnetId":"subnet-1","name":"Ethernet","networkDeviceCount":0,"networkDeviceCountUnchanged":true,"highestAddress":126}""";
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
        var payload = """{"subnetId":"subnet-1","name":"Ethernet","networkDeviceCount":0,"networkDeviceCountUnchanged":true,"devices":[]}""";
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
        var payload = """{"subnetId":"payload-leak-canary","name":"Ethernet","networkDeviceCount":-1,"networkDeviceCountUnchanged":true}""";
        var item = Project(operation, payload);

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.NotNull(item.Failure);
        Assert.DoesNotContain(LeakToken, item.Failure.Message);
    }
}
