using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Network;
using TiaMcpServer.Safety;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests;

/// <summary>
/// Task 3: proves the three Phase 4 subnet lifecycle operations (<c>create_subnet</c>,
/// <c>update_subnet</c>, <c>delete_subnet</c>) are classified, denied in read-only mode before any
/// worker call, and forward exactly the fields the brief specifies through the real
/// <see cref="NetworkWorkerInvoker.InvokeWriteAsync"/> -&gt; <see cref="OpennessWorkerClient"/> ->
/// echo FakeWorker path — never mocks of the invoker itself.
/// </summary>
public class NetworkPhase4SubnetForwardingTests
{
    private static OpennessWorkerClient CreateClient()
        => new(new ProjectSessionBinding(null), logger: null, workerExecutablePath: FakeWorkerLocator.Locate());

    #region Step 1: Access classification

    [Theory]
    [InlineData("create_subnet")]
    [InlineData("update_subnet")]
    [InlineData("delete_subnet")]
    public void SubnetLifecycleOperations_AreClassifiedAsProjectMutation(string operation)
    {
        Assert.Equal(OperationCapability.ProjectMutation, OperationPolicyCatalog.GetCapability(operation));
    }

    [Theory]
    [InlineData("create_subnet")]
    [InlineData("update_subnet")]
    [InlineData("delete_subnet")]
    public void SubnetLifecycleOperations_AreDeniedInReadOnlyMode(string operation)
    {
        Assert.False(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, operation));
    }

    [Theory]
    [InlineData("create_subnet")]
    [InlineData("update_subnet")]
    [InlineData("delete_subnet")]
    public void SubnetLifecycleOperations_AreAllowedInReadWriteMode(string operation)
    {
        Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadWrite, operation));
    }

    #endregion

    #region Step 1: Denied before any worker call

    [Fact]
    public async Task OpennessWorkerClient_ReadOnly_DeniesCreateSubnetBeforeWorkerInvocation()
    {
        var binding = new ProjectSessionBinding(null);
        var policy = new OperationAccessPolicy(McpAccessMode.ReadOnly);
        var client = new OpennessWorkerClient(
            binding,
            workerExecutablePath: "/nonexistent/path",
            accessPolicy: policy);

        var result = await client.CreateSubnetAsync(
            "Subnet_1", SubnetLifecycleContract.Ethernet, highestAddress: null, transmissionSpeed: null, projectPath: null);

        Assert.False(result.Success);
        Assert.Equal(WorkerFailureCategories.AccessDenied, result.FailureCategory);
        Assert.Contains("create_subnet", result.Error);
        Assert.Contains("read-only", result.Error);
    }

    [Fact]
    public async Task OpennessWorkerClient_ReadOnly_DeniesUpdateSubnetBeforeWorkerInvocation()
    {
        var binding = new ProjectSessionBinding(null);
        var policy = new OperationAccessPolicy(McpAccessMode.ReadOnly);
        var client = new OpennessWorkerClient(
            binding,
            workerExecutablePath: "/nonexistent/path",
            accessPolicy: policy);

        var result = await client.UpdateSubnetAsync(
            "subnet-1", name: "Renamed", highestAddress: null, transmissionSpeed: null, projectPath: null);

        Assert.False(result.Success);
        Assert.Equal(WorkerFailureCategories.AccessDenied, result.FailureCategory);
        Assert.Contains("update_subnet", result.Error);
        Assert.Contains("read-only", result.Error);
    }

    [Fact]
    public async Task OpennessWorkerClient_ReadOnly_DeniesDeleteSubnetBeforeWorkerInvocation()
    {
        var binding = new ProjectSessionBinding(null);
        var policy = new OperationAccessPolicy(McpAccessMode.ReadOnly);
        var client = new OpennessWorkerClient(
            binding,
            workerExecutablePath: "/nonexistent/path",
            accessPolicy: policy);

        var result = await client.DeleteSubnetAsync("subnet-1", projectPath: null);

        Assert.False(result.Success);
        Assert.Equal(WorkerFailureCategories.AccessDenied, result.FailureCategory);
        Assert.Contains("delete_subnet", result.Error);
        Assert.Contains("read-only", result.Error);
    }

    #endregion

    #region Step 1: Invocation through the echo FakeWorker does not report validation_error

    [Fact]
    public async Task InvokeWriteAsync_CreateSubnet_DoesNotReturnValidationError()
    {
        var operation = new NetworkOperationRequest
        {
            OperationId = "create-1",
            Operation = "create_subnet",
            ProjectPath = "echo",
            Subnet = new NetworkSubnetDefinition
            {
                Name = "Subnet_1",
                NetworkType = SubnetLifecycleContract.Ethernet,
            },
        };

        using var client = CreateClient();
        var result = await NetworkWorkerInvoker.InvokeWriteAsync(client, operation, commonProjectPath: "echo");

        Assert.True(result.Success, result.Error);
        Assert.NotEqual(WorkerFailureCategories.ValidationError, result.FailureCategory);
    }

    [Fact]
    public async Task InvokeWriteAsync_UpdateSubnet_DoesNotReturnValidationError()
    {
        var operation = new NetworkOperationRequest
        {
            OperationId = "update-1",
            Operation = "update_subnet",
            ProjectPath = "echo",
            Target = new NetworkObjectTarget { SubnetId = "subnet-42" },
            SubnetChanges = new NetworkSubnetChanges { Name = "Renamed" },
        };

        using var client = CreateClient();
        var result = await NetworkWorkerInvoker.InvokeWriteAsync(client, operation, commonProjectPath: "echo");

        Assert.True(result.Success, result.Error);
        Assert.NotEqual(WorkerFailureCategories.ValidationError, result.FailureCategory);
    }

    [Fact]
    public async Task InvokeWriteAsync_DeleteSubnet_DoesNotReturnValidationError()
    {
        var operation = new NetworkOperationRequest
        {
            OperationId = "delete-1",
            Operation = "delete_subnet",
            ProjectPath = "echo",
            Target = new NetworkObjectTarget { SubnetId = "subnet-7" },
        };

        using var client = CreateClient();
        var result = await NetworkWorkerInvoker.InvokeWriteAsync(client, operation, commonProjectPath: "echo");

        Assert.True(result.Success, result.Error);
        Assert.NotEqual(WorkerFailureCategories.ValidationError, result.FailureCategory);
    }

    #endregion

    #region Step 4: Exact serialized worker request

    [Fact]
    public async Task CreateSubnet_ForwardsNameTypeAndOptionalProfibusFieldsWithNoTargetId()
    {
        var operation = new NetworkOperationRequest
        {
            OperationId = "create-2",
            Operation = "create_subnet",
            ProjectPath = "echo",
            Subnet = new NetworkSubnetDefinition
            {
                Name = "Profibus_1",
                NetworkType = SubnetLifecycleContract.Profibus,
                HighestAddress = 31,
                TransmissionSpeed = "Baud1500000",
            },
        };

        using var client = CreateClient();
        var result = await NetworkWorkerInvoker.InvokeWriteAsync(client, operation, commonProjectPath: "echo");

        Assert.True(result.Success, result.Error);
        using var document = JsonDocument.Parse(result.Payload);
        var root = document.RootElement;

        Assert.Equal("create_subnet", root.GetProperty("method").GetString());
        Assert.Equal("echo", root.GetProperty("projectPath").GetString());
        Assert.Equal("Profibus_1", root.GetProperty("subnetName").GetString());
        Assert.Equal(SubnetLifecycleContract.Profibus, root.GetProperty("subnetNetworkType").GetString());
        Assert.Equal(31, root.GetProperty("subnetHighestAddress").GetInt32());
        Assert.Equal("Baud1500000", root.GetProperty("subnetTransmissionSpeed").GetString());
        Assert.True(root.GetProperty("confirm").GetBoolean());
        Assert.True(root.GetProperty("allowTiaConfirmations").GetBoolean());

        // create_subnet never forwards a target id — a new subnet's id is assigned by Openness.
        Assert.Equal(JsonValueKind.Null, root.GetProperty("subnetId").ValueKind);

        AssertNoProbeFieldsPopulated(root);
    }

    [Fact]
    public async Task UpdateSubnet_ForwardsTargetIdAndOnlyTheOneRequestedChange()
    {
        var operation = new NetworkOperationRequest
        {
            OperationId = "update-2",
            Operation = "update_subnet",
            ProjectPath = "echo",
            Target = new NetworkObjectTarget { SubnetId = "subnet-42" },
            SubnetChanges = new NetworkSubnetChanges { Name = "Renamed" },
        };

        using var client = CreateClient();
        var result = await NetworkWorkerInvoker.InvokeWriteAsync(client, operation, commonProjectPath: "echo");

        Assert.True(result.Success, result.Error);
        using var document = JsonDocument.Parse(result.Payload);
        var root = document.RootElement;

        Assert.Equal("update_subnet", root.GetProperty("method").GetString());
        Assert.Equal("echo", root.GetProperty("projectPath").GetString());
        Assert.Equal("subnet-42", root.GetProperty("subnetId").GetString());
        Assert.Equal("Renamed", root.GetProperty("subnetName").GetString());
        Assert.True(root.GetProperty("confirm").GetBoolean());
        Assert.True(root.GetProperty("allowTiaConfirmations").GetBoolean());

        // Only 'name' was requested; the other two change fields and the (not-updatable) network
        // type must stay unset.
        Assert.Equal(JsonValueKind.Null, root.GetProperty("subnetHighestAddress").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("subnetTransmissionSpeed").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("subnetNetworkType").ValueKind);

        AssertNoProbeFieldsPopulated(root);
    }

    [Fact]
    public async Task UpdateSubnet_ForwardsEveryRequestedChangeWhenAllThreeAreSupplied()
    {
        var operation = new NetworkOperationRequest
        {
            OperationId = "update-3",
            Operation = "update_subnet",
            ProjectPath = "echo",
            Target = new NetworkObjectTarget { SubnetId = "subnet-99" },
            SubnetChanges = new NetworkSubnetChanges
            {
                Name = "Renamed2",
                HighestAddress = 12,
                TransmissionSpeed = "Baud93750",
            },
        };

        using var client = CreateClient();
        var result = await NetworkWorkerInvoker.InvokeWriteAsync(client, operation, commonProjectPath: "echo");

        Assert.True(result.Success, result.Error);
        using var document = JsonDocument.Parse(result.Payload);
        var root = document.RootElement;

        Assert.Equal("subnet-99", root.GetProperty("subnetId").GetString());
        Assert.Equal("Renamed2", root.GetProperty("subnetName").GetString());
        Assert.Equal(12, root.GetProperty("subnetHighestAddress").GetInt32());
        Assert.Equal("Baud93750", root.GetProperty("subnetTransmissionSpeed").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("subnetNetworkType").ValueKind);

        AssertNoProbeFieldsPopulated(root);
    }

    [Fact]
    public async Task DeleteSubnet_ForwardsOnlyTheTargetId()
    {
        var operation = new NetworkOperationRequest
        {
            OperationId = "delete-2",
            Operation = "delete_subnet",
            ProjectPath = "echo",
            Target = new NetworkObjectTarget { SubnetId = "subnet-7" },
        };

        using var client = CreateClient();
        var result = await NetworkWorkerInvoker.InvokeWriteAsync(client, operation, commonProjectPath: "echo");

        Assert.True(result.Success, result.Error);
        using var document = JsonDocument.Parse(result.Payload);
        var root = document.RootElement;

        Assert.Equal("delete_subnet", root.GetProperty("method").GetString());
        Assert.Equal("echo", root.GetProperty("projectPath").GetString());
        Assert.Equal("subnet-7", root.GetProperty("subnetId").GetString());
        Assert.True(root.GetProperty("confirm").GetBoolean());
        Assert.True(root.GetProperty("allowTiaConfirmations").GetBoolean());

        Assert.Equal(JsonValueKind.Null, root.GetProperty("subnetName").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("subnetNetworkType").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("subnetHighestAddress").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("subnetTransmissionSpeed").ValueKind);

        AssertNoProbeFieldsPopulated(root);
    }

    /// <summary>
    /// Every production-call assertion in this file also proves the flip side of the brief's
    /// constraint: none of the three new worker-client methods ever populates a <c>Probe*</c>
    /// member — those stay reserved for <c>probe_subnet_lifecycle_mutations</c> alone.
    /// </summary>
    private static void AssertNoProbeFieldsPopulated(JsonElement root)
    {
        Assert.Equal(JsonValueKind.Null, root.GetProperty("probeRunId").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("probeConnectedEthernetSubnetId").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("probeConnectedProfibusSubnetId").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("probeProfibusHighestAddress").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("probeProfibusTransmissionSpeed").ValueKind);
    }

    #endregion
}
