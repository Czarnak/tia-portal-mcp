using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using TiaMcpServer.Batch;
using TiaMcpServer.Network;
using TiaMcpServer.Safety;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public class BatchToolMetadataTests
{
    private static string MethodDescription(string methodName)
    {
        var method = typeof(BatchTools).GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        var description = method!.GetCustomAttribute<DescriptionAttribute>();
        Assert.NotNull(description);
        return description!.Description;
    }

    [Fact]
    public void PreviewWriteBatchDescription_ListsEveryWriteOperation()
    {
        var description = MethodDescription("PreviewWriteBatch");
        foreach (var operation in BatchOperationCatalog.WriteOperationNames)
        {
            Assert.Contains(operation, description);
        }
    }

    [Fact]
    public void ApplyWriteBatchDescription_ListsEveryWriteOperation()
    {
        var description = MethodDescription("ApplyWriteBatch");
        foreach (var operation in BatchOperationCatalog.WriteOperationNames)
        {
            Assert.Contains(operation, description);
        }
    }

    [Fact]
    public void OperationPropertyDescription_ListsEveryBatchableOperation()
    {
        var property = typeof(BatchOperationRequest).GetProperty(nameof(BatchOperationRequest.Operation));
        Assert.NotNull(property);
        var description = property!.GetCustomAttribute<DescriptionAttribute>();
        Assert.NotNull(description);

        foreach (var operation in BatchOperationCatalog.WriteOperationNames)
        {
            Assert.Contains(operation, description!.Description);
        }
    }

    [Fact]
    public void PreviewWriteBatchDescription_StatesTheActualTokenLifetime()
    {
        var expected = $"{WriteSafetyService.DefaultTokenLifetime.TotalMinutes:N0} minutes";
        Assert.Contains(expected, MethodDescription("PreviewWriteBatch"));
    }

    private static string PropertyDescription(string propertyName)
    {
        var property = typeof(BatchOperationRequest).GetProperty(propertyName);
        Assert.NotNull(property);
        var description = property!.GetCustomAttribute<DescriptionAttribute>();
        Assert.NotNull(description);
        return description!.Description;
    }

    [Fact]
    public void BlockPathDescription_CoversBatchBlockOperations()
    {
        var description = PropertyDescription(nameof(BatchOperationRequest.BlockPath));
        Assert.Contains("create_block", description);
        Assert.Contains("delete_block", description);
        Assert.DoesNotContain("compile_check", description);
    }

    [Fact]
    public void NewNameDescription_DoesNotClaimUserConstantRename()
    {
        var description = PropertyDescription(nameof(BatchOperationRequest.NewName));
        Assert.Contains("update_tag", description);
        Assert.DoesNotMatch(
        new Regex(@"\buser constant\b", RegexOptions.IgnoreCase),
        description
    );
    }

    [Fact]
    public void PlcNameDescription_NamesTheOperationsThatHonorIt()
    {
        var description = PropertyDescription(nameof(BatchOperationRequest.PlcName));
        Assert.Contains("start_plc", description);
        Assert.DoesNotContain("compile_check", description);
    }

    [Fact]
    public void BatchRequestDescriptions_OmitStandaloneProjectOperations()
    {
        foreach (var propertyName in new[]
        {
            nameof(BatchOperationRequest.Operation),
            nameof(BatchOperationRequest.BlockPath),
            nameof(BatchOperationRequest.PlcName)
        })
        {
            var description = PropertyDescription(propertyName);
            Assert.DoesNotContain("get_project_status", description);
            Assert.DoesNotContain("browse_project_tree", description);
            Assert.DoesNotContain("compile_check", description);
        }
    }

    [Fact]
    public void GenericBatchDescriptions_OmitDedicatedNetworkOperations()
    {
        var descriptions = new[]
        {
            MethodDescription(typeof(BatchTools), "PreviewWriteBatch"),
            MethodDescription(typeof(BatchTools), "ApplyWriteBatch"),
            MethodDescription(typeof(WriteBatchTools), "PreviewWriteBatch"),
            MethodDescription(typeof(WriteBatchTools), "ApplyWriteBatch"),
            PropertyDescription(nameof(BatchOperationRequest.Operation)),
        };

        foreach (var description in descriptions)
        {
            Assert.DoesNotContain("read_hardware_config", description);
            Assert.DoesNotContain("search_equipment_catalog", description);
            Assert.DoesNotContain("add_network_device", description);
            Assert.DoesNotContain("configure_network_device", description);
            Assert.DoesNotContain("create_subnet", description);
            Assert.DoesNotContain("update_subnet", description);
            Assert.DoesNotContain("delete_subnet", description);
        }
    }

    [Fact]
    public void NetworkWriteDescription_StatesConnectedSubnetDeletionIsAllowedAndScopesTheDeviceCountClaim()
    {
        var description = MethodDescription(typeof(NetworkWriteTools), "NetworkWrite");

        Assert.Contains("Connected subnet deletion removes the listed connections", description);
        Assert.Contains("preserving devices and nodes", description);

        // The claim must be scoped to what networkDeviceCountUnchanged actually checks (the root
        // device collection), not overstated as leaving every device untouched — nested device
        // user group members are outside that count.
        Assert.Contains("networkDeviceCountUnchanged", description);
        Assert.Contains("root device count", description);
    }

    [Fact]
    public void NetworkWriteDescription_RequiresExplicitSubnetKindForLifecycleTargets()
    {
        var description = MethodDescription(typeof(NetworkWriteTools), "NetworkWrite");

        Assert.Contains("target.kind='subnet'", description);
        Assert.Contains("target.subnetId", description);
    }

    [Fact]
    public void NetworkWriteDescription_StatesNoBatchWideRollbackAndSeparateSaveAndCompile()
    {
        var description = MethodDescription(typeof(NetworkWriteTools), "NetworkWrite");

        Assert.Contains("no rollback or automatic replay", description);
        Assert.Contains("never saves, compiles, downloads, or controls a PLC", description);
        Assert.Contains("network_read before retrying", description);
    }

    [Fact]
    public void NetworkWriteDescription_OmitsDependencyInventoryAndConnectionAndDeviceDeletionWording()
    {
        var description = MethodDescription(typeof(NetworkWriteTools), "NetworkWrite");

        Assert.DoesNotContain("dependency inventory", description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connection deletion", description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("device deletion", description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NetworkReadDescription_DoesNotAdvertiseSubnetLifecycleWrites()
    {
        var description = MethodDescription(typeof(NetworkReadTools), "NetworkRead");

        Assert.DoesNotContain("create_subnet", description);
        Assert.DoesNotContain("update_subnet", description);
        Assert.DoesNotContain("delete_subnet", description);
        foreach (var operation in NetworkOperationCatalog.WriteOperationNames)
        {
            Assert.DoesNotContain(operation, description);
        }
    }

    [Fact]
    public void NetworkReadDescription_ListsEveryNetworkReadOperation()
    {
        var description = MethodDescription(typeof(NetworkReadTools), "NetworkRead");
        foreach (var operation in NetworkOperationCatalog.ReadOperationNames)
        {
            Assert.Contains(operation, description);
        }
    }

    [Fact]
    public void NetworkWriteDescription_ListsEveryNetworkWriteOperation()
    {
        var description = MethodDescription(typeof(NetworkWriteTools), "NetworkWrite");
        foreach (var operation in NetworkOperationCatalog.WriteOperationNames)
        {
            Assert.Contains(operation, description);
        }
    }

    [Theory]
    [InlineData(nameof(BatchOperationRequest.OperationId))]
    [InlineData(nameof(BatchOperationRequest.Operation))]
    [InlineData(nameof(BatchOperationRequest.ProjectPath))]
    [InlineData(nameof(BatchOperationRequest.BlockPath))]
    [InlineData(nameof(BatchOperationRequest.YamlContent))]
    [InlineData(nameof(BatchOperationRequest.TableName))]
    [InlineData(nameof(BatchOperationRequest.Name))]
    [InlineData(nameof(BatchOperationRequest.DataType))]
    [InlineData(nameof(BatchOperationRequest.Value))]
    public void KeyRequestFieldsHaveDescriptions(string propertyName)
    {
        var property = typeof(BatchOperationRequest).GetProperty(propertyName);
        Assert.NotNull(property);
        var description = property!.GetCustomAttribute<DescriptionAttribute>();
        Assert.NotNull(description);
        Assert.False(string.IsNullOrWhiteSpace(description!.Description));
    }

    private static string MethodDescription(Type toolType, string methodName)
    {
        var method = toolType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        var description = method!.GetCustomAttribute<DescriptionAttribute>();
        Assert.NotNull(description);
        return description!.Description;
    }
}
