using TiaMcpServer.Contracts;
using TiaMcpServer.Network;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public class NetworkOperationCatalogTests
{
    private static NetworkOperationRequest Op(
        string id,
        string operation,
        Action<NetworkOperationRequest>? configure = null)
    {
        var request = new NetworkOperationRequest { OperationId = id, Operation = operation };
        configure?.Invoke(request);
        return request;
    }

    /// <summary>A minimal valid configure_network_device operation: exact target, one change.</summary>
    private static NetworkOperationRequest Configure(
        string id = "cfg",
        string? deviceName = "PLC_1",
        string? nodeId = "7",
        Action<NetworkOperationRequest>? adjust = null)
    {
        var request = new NetworkOperationRequest
        {
            OperationId = id,
            Operation = "configure_network_device",
            Target = new NetworkObjectTarget { DeviceName = deviceName, NodeId = nodeId },
            Changes = new NetworkDeviceChanges { IpAddress = "192.168.0.10" },
        };
        adjust?.Invoke(request);
        return request;
    }

    [Fact]
    public void All_MatchesTheDedicatedNetworkContract()
    {
        var expected = new Dictionary<string, (NetworkOperationCategory Category, string[] Required, string[] Optional)>
        {
            ["read_hardware_config"] = (NetworkOperationCategory.Read, Array.Empty<string>(), Array.Empty<string>()),
            ["search_equipment_catalog"] = (NetworkOperationCategory.Read, new[] { "query" }, new[] { "maxResults" }),
            ["add_network_device"] = (NetworkOperationCategory.Write, new[] { "typeIdentifier", "deviceName" }, new[] { "deviceItemName" }),
            ["configure_network_device"] = (NetworkOperationCategory.Write, new[] { "target", "changes" }, Array.Empty<string>()),
            ["list_network_objects"] = (NetworkOperationCategory.Read, new[] { "objectKinds" }, new[] { "deviceName", "pageSize", "cursor" }),
            ["inspect_network_object"] = (NetworkOperationCategory.Read, new[] { "target" }, new[] { "attributeNames" }),
            ["create_subnet"] = (NetworkOperationCategory.Write, new[] { "subnet" }, Array.Empty<string>()),
            ["update_subnet"] = (NetworkOperationCategory.Write, new[] { "target", "subnetChanges" }, Array.Empty<string>()),
            ["delete_subnet"] = (NetworkOperationCategory.Write, new[] { "target" }, Array.Empty<string>()),
        };

        var actual = NetworkOperationCatalog.All.ToDictionary(spec => spec.Name, StringComparer.Ordinal);

        Assert.Equal(expected.Keys.OrderBy(name => name), actual.Keys.OrderBy(name => name));
        foreach (var (name, expectedSpec) in expected)
        {
            var actualSpec = actual[name];
            Assert.Equal(expectedSpec.Category, actualSpec.Category);
            Assert.Equal(expectedSpec.Required, actualSpec.RequiredFields);
            Assert.Equal(expectedSpec.Optional, actualSpec.OptionalFields);
        }
    }

    [Fact]
    public void ValidateRead_RejectsEmptyAndOverFiftyItemBatches()
    {
        var empty = NetworkOperationCatalog.ValidateRead(Array.Empty<NetworkOperationRequest>());
        var oversized = NetworkOperationCatalog.ValidateRead(
            Enumerable.Range(0, NetworkOperationCatalog.MaxBatchSize + 1)
                .Select(index => Op($"id{index}", "read_hardware_config"))
                .ToArray());

        Assert.False(empty.IsValid);
        Assert.Contains("at least one", empty.Error);
        Assert.False(oversized.IsValid);
        Assert.Contains("50", oversized.Error);
    }

    [Fact]
    public void ValidateRead_RejectsDuplicateIdsAndWriteOperations()
    {
        var duplicates = NetworkOperationCatalog.ValidateRead(new[]
        {
            Op("same", "read_hardware_config"),
            Op("same", "search_equipment_catalog", operation => operation.Query = "CPU"),
        });
        var wrongCategory = NetworkOperationCatalog.ValidateRead(new[]
        {
            Op("w1", "add_network_device", operation =>
            {
                operation.TypeIdentifier = "OrderNumber:6ES7";
                operation.DeviceName = "PLC_1";
            }),
        });

        Assert.False(duplicates.IsValid);
        Assert.Contains("same", duplicates.Error);
        Assert.False(wrongCategory.IsValid);
        Assert.Contains("write", wrongCategory.Error);
    }

    [Fact]
    public void ValidateRead_RejectsMissingAndInapplicableFieldsAndZeroMaxResults()
    {
        var result = NetworkOperationCatalog.ValidateRead(new[]
        {
            Op("missing", "search_equipment_catalog"),
            Op("inapplicable", "read_hardware_config", operation => operation.Query = "CPU"),
            Op("bounds", "search_equipment_catalog", operation =>
            {
                operation.Query = "CPU";
                operation.MaxResults = 0;
            }),
        });

        Assert.False(result.IsValid);
        Assert.Contains("query", result.Error);
        Assert.Contains("not valid", result.Error);
        Assert.Contains("maxResults", result.Error);
        Assert.Contains("1 or greater", result.Error);
    }

    [Fact]
    public void ValidateWrite_RejectsMixedNormalizedProjectPaths()
    {
        var result = NetworkOperationCatalog.ValidateWrite(new[]
        {
            Configure("a", "PLC_1", adjust: operation => operation.ProjectPath = @"C:\\Projects\\One.ap21"),
            Configure("b", "PLC_2", adjust: operation => operation.ProjectPath = @"C:\\Projects\\Two.ap21"),
        });

        Assert.False(result.IsValid);
        Assert.Contains("same project", result.Error);
    }

    [Fact]
    public void ValidateWrite_AcceptsAnExactTargetWithOneChange()
    {
        var result = NetworkOperationCatalog.ValidateWrite(new[] { Configure() });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void ValidateWrite_RejectsConfigureWithoutTargetOrChanges()
    {
        var result = NetworkOperationCatalog.ValidateWrite(new[]
        {
            Op("w1", "configure_network_device"),
        });

        Assert.False(result.IsValid);
        Assert.Contains("target", result.Error);
        Assert.Contains("changes", result.Error);
    }

    [Fact]
    public void ValidateWrite_RequiresTargetAndChangesOnlyForConfigure()
    {
        var result = NetworkOperationCatalog.ValidateWrite(new[]
        {
            Op("add", "add_network_device", operation =>
            {
                operation.TypeIdentifier = "OrderNumber:6ES7";
                operation.DeviceName = "PLC_1";
            }),
        });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void ValidateWrite_RejectsTargetAndChangesOnAddNetworkDevice()
    {
        var result = NetworkOperationCatalog.ValidateWrite(new[]
        {
            Op("add", "add_network_device", operation =>
            {
                operation.TypeIdentifier = "OrderNumber:6ES7";
                operation.DeviceName = "PLC_1";
                operation.Target = new NetworkObjectTarget { DeviceName = "PLC_1", NodeId = "7" };
                operation.Changes = new NetworkDeviceChanges { IpAddress = "192.168.0.10" };
            }),
        });

        Assert.False(result.IsValid);
        Assert.Contains("'target' is not valid", result.Error);
        Assert.Contains("'changes' is not valid", result.Error);
    }

    [Theory]
    [InlineData(null, "7", "deviceName")]
    [InlineData("   ", "7", "deviceName")]
    [InlineData("PLC_1", null, "nodeId")]
    [InlineData("PLC_1", "   ", "nodeId")]
    public void ValidateWrite_RejectsBlankTargetIdentities(string? deviceName, string? nodeId, string expected)
    {
        var result = NetworkOperationCatalog.ValidateWrite(new[]
        {
            Configure(adjust: operation =>
                operation.Target = new NetworkObjectTarget { DeviceName = deviceName, NodeId = nodeId }),
        });

        Assert.False(result.IsValid);
        Assert.Contains(expected, result.Error);
    }

    [Fact]
    public void ValidateWrite_RejectsChangesThatRequestNothing()
    {
        var result = NetworkOperationCatalog.ValidateWrite(new[]
        {
            Configure(adjust: operation => operation.Changes = new NetworkDeviceChanges()),
        });

        Assert.False(result.IsValid);
        Assert.Contains("at least one change", result.Error);
    }

    [Fact]
    public void ValidateWrite_RejectsBlankScalarChanges()
    {
        var result = NetworkOperationCatalog.ValidateWrite(new[]
        {
            Configure(adjust: operation => operation.Changes = new NetworkDeviceChanges { IpAddress = "  " }),
        });

        Assert.False(result.IsValid);
        Assert.Contains("ipAddress", result.Error);
    }

    [Fact]
    public void ValidateWrite_RejectsSubnetSelectorWithoutSubnetId()
    {
        var result = NetworkOperationCatalog.ValidateWrite(new[]
        {
            Configure(adjust: operation => operation.Changes = new NetworkDeviceChanges
            {
                Subnet = new NetworkSubnetTarget { SubnetId = "  " },
            }),
        });

        Assert.False(result.IsValid);
        Assert.Contains("subnet.subnetId", result.Error);
    }

    [Fact]
    public void ValidateWrite_RejectsIoSystemSelectorMissingSubnetIdOrNumber()
    {
        var result = NetworkOperationCatalog.ValidateWrite(new[]
        {
            Configure(adjust: operation => operation.Changes = new NetworkDeviceChanges
            {
                IoSystem = new NetworkIoSystemTarget(),
            }),
        });

        Assert.False(result.IsValid);
        Assert.Contains("ioSystem.subnetId", result.Error);
        Assert.Contains("ioSystem.number", result.Error);
    }

    [Fact]
    public void ValidateWrite_RejectsIoSystemSubnetIdThatDisagreesWithTheSubnetChange()
    {
        var result = NetworkOperationCatalog.ValidateWrite(new[]
        {
            Configure(adjust: operation => operation.Changes = new NetworkDeviceChanges
            {
                Subnet = new NetworkSubnetTarget { SubnetId = "S1" },
                IoSystem = new NetworkIoSystemTarget { SubnetId = "S2", Number = 100 },
            }),
        });

        Assert.False(result.IsValid);
        Assert.Contains("same subnet", result.Error);
    }

    [Fact]
    public void ValidateWrite_AcceptsMatchingSubnetAndIoSystemSelectors()
    {
        var result = NetworkOperationCatalog.ValidateWrite(new[]
        {
            Configure(adjust: operation => operation.Changes = new NetworkDeviceChanges
            {
                Subnet = new NetworkSubnetTarget { SubnetId = "S1" },
                IoSystem = new NetworkIoSystemTarget { SubnetId = "S1", Number = 100 },
            }),
        });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void ValidateAccessMode_ReturnsOneDeniedItemError()
    {
        var errors = NetworkOperationCatalog.ValidateAccessMode(new[]
        {
            Op("read", "read_hardware_config"),
            Configure("write"),
        }, McpAccessMode.ReadOnly);

        Assert.Single(errors);
        Assert.Contains("configure_network_device", errors[0]);
        Assert.Contains("write", errors[0]);
    }
}
