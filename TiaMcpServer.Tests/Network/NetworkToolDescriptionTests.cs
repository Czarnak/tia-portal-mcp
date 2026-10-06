using System.ComponentModel;
using System.Reflection;
using TiaMcpServer.Network;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public class NetworkToolDescriptionTests
{
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

    private static string MethodDescription(Type toolType, string methodName)
    {
        var method = toolType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        var description = method!.GetCustomAttribute<DescriptionAttribute>();
        Assert.NotNull(description);
        return description!.Description;
    }
}
