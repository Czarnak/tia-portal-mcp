using TiaMcpServer.Contracts;
using TiaMcpServer.Network;
using Xunit;

namespace TiaMcpServer.Tests;

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

    [Fact]
    public void All_MatchesTheDedicatedNetworkContract()
    {
        var expected = new Dictionary<string, (NetworkOperationCategory Category, string[] Required, string[] Optional)>
        {
            ["read_hardware_config"] = (NetworkOperationCategory.Read, Array.Empty<string>(), Array.Empty<string>()),
            ["search_equipment_catalog"] = (NetworkOperationCategory.Read, new[] { "query" }, new[] { "maxResults" }),
            ["add_network_device"] = (NetworkOperationCategory.Write, new[] { "typeIdentifier", "deviceName" }, new[] { "deviceItemName" }),
            ["configure_network_device"] = (NetworkOperationCategory.Write, new[] { "deviceName" }, new[] { "ipAddress", "subnetMask", "pnDeviceName", "subnetName", "ioSystemName" })
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
            Op("a", "configure_network_device", operation =>
            {
                operation.DeviceName = "PLC_1";
                operation.ProjectPath = @"C:\\Projects\\One.ap21";
            }),
            Op("b", "configure_network_device", operation =>
            {
                operation.DeviceName = "PLC_2";
                operation.ProjectPath = @"C:\\Projects\\Two.ap21";
            }),
        });

        Assert.False(result.IsValid);
        Assert.Contains("same project", result.Error);
    }

    [Fact]
    public void ValidateWrite_AcceptsNoSettingsConfigureNetworkDevice()
    {
        var result = NetworkOperationCatalog.ValidateWrite(new[]
        {
            Op("w1", "configure_network_device", operation => operation.DeviceName = "PLC_1"),
        });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void ValidateAccessMode_ReturnsOneDeniedItemError()
    {
        var errors = NetworkOperationCatalog.ValidateAccessMode(new[]
        {
            Op("read", "read_hardware_config"),
            Op("write", "configure_network_device", operation => operation.DeviceName = "PLC_1"),
        }, McpAccessMode.ReadOnly);

        Assert.Single(errors);
        Assert.Contains("configure_network_device", errors[0]);
        Assert.Contains("write", errors[0]);
    }
}
