using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public class HardwarePageDescriptorSetTests
{
    [Fact]
    public void Descriptors_OrderDevicesThenSubnetsByPublicIdentityAndSourceOrder()
    {
        var descriptors = new[]
        {
            Device("Z_Device", "devices/0", 0),
            Subnet("subnet-b", "subnets/0", 0),
            Device("A_Device", "deviceGroups/0/devices/0", 2),
            Subnet("subnet-a", "subnets/1", 1),
            Device("A_Device", "devices/1", 1),
        };

        var result = new HardwarePageDescriptorSet(descriptors);

        Assert.Equal(
            new[]
            {
                "device:A_Device:devices/1",
                "device:A_Device:deviceGroups/0/devices/0",
                "device:Z_Device:devices/0",
                "subnet:subnet-a:subnets/1",
                "subnet:subnet-b:subnets/0",
            },
            Describe(result.Descriptors));
        Assert.Equal(3, result.TotalDevices);
        Assert.Equal(2, result.TotalSubnets);
    }

    [Fact]
    public void Descriptors_KeepDuplicatePublicNamesDistinctAndHashTheirLocators()
    {
        var original = new HardwarePageDescriptorSet(new[]
        {
            Device("PLC", "devices/0", 0),
            Device("PLC", "deviceGroups/0/groups/2/devices/1", 1),
        });
        var regrouped = new HardwarePageDescriptorSet(new[]
        {
            Device("PLC", "devices/0", 0),
            Device("PLC", "deviceGroups/0/groups/3/devices/1", 1),
        });

        Assert.Equal(
            new[]
            {
                "device:PLC:devices/0",
                "device:PLC:deviceGroups/0/groups/2/devices/1",
            },
            Describe(original.Descriptors));
        Assert.NotEqual(original.SnapshotHash, regrouped.SnapshotHash);
    }

    [Fact]
    public void Descriptors_SnapshotChangesWhenEntitiesAreRegroupedReorderedAddedOrRemoved()
    {
        var baseline = new HardwarePageDescriptorSet(new[]
        {
            Device("A", "devices/0", 0),
            Device("B", "devices/1", 1),
            Subnet("s1", "subnets/0", 0),
        });

        var regrouped = new HardwarePageDescriptorSet(new[]
        {
            Device("A", "deviceGroups/0/devices/0", 0),
            Device("B", "devices/1", 1),
            Subnet("s1", "subnets/0", 0),
        });
        var reordered = new HardwarePageDescriptorSet(new[]
        {
            Device("A", "devices/1", 1),
            Device("B", "devices/0", 0),
            Subnet("s1", "subnets/0", 0),
        });
        var added = new HardwarePageDescriptorSet(new[]
        {
            Device("A", "devices/0", 0),
            Device("B", "devices/1", 1),
            Device("C", "devices/2", 2),
            Subnet("s1", "subnets/0", 0),
        });
        var removed = new HardwarePageDescriptorSet(new[]
        {
            Device("A", "devices/0", 0),
            Subnet("s1", "subnets/0", 0),
        });

        Assert.NotEqual(baseline.SnapshotHash, regrouped.SnapshotHash);
        Assert.NotEqual(baseline.SnapshotHash, reordered.SnapshotHash);
        Assert.NotEqual(baseline.SnapshotHash, added.SnapshotHash);
        Assert.NotEqual(baseline.SnapshotHash, removed.SnapshotHash);
    }

    [Fact]
    public void Descriptors_UseStructuralLocatorsWithoutLexicallySortingThem()
    {
        var result = new HardwarePageDescriptorSet(new[]
        {
            Device("PLC", "devices/10", 10),
            Device("PLC", "devices/2", 2),
        });

        Assert.Equal(
            new[] { "device:PLC:devices/2", "device:PLC:devices/10" },
            Describe(result.Descriptors));
    }

    [Fact]
    public void Descriptors_KeepOrderingWhenDetailFlagsChangeButQueryHashChanges()
    {
        var result = new HardwarePageDescriptorSet(new[]
        {
            Device("PLC", "devices/0", 0),
            Subnet("subnet", "subnets/0", 0),
        });

        var noDetails = HardwarePageEvidence.CreateQueryHash("PLC", "PLC_Main", false, false);
        var withDetails = HardwarePageEvidence.CreateQueryHash("PLC", "PLC_Main", true, true);

        Assert.Equal(
            new[] { "device:PLC:devices/0", "subnet:subnet:subnets/0" },
            Describe(result.Descriptors));
        Assert.NotEqual(noDetails, withDetails);
    }

    [Fact]
    public void Descriptors_FilteredSequencesPreserveDeterministicDeviceAndPlcMatches()
    {
        var result = new HardwarePageDescriptorSet(new[]
        {
            Device("PLC_A", "devices/0", 0),
            Device("HMI", "devices/1", 1),
            Device("PLC_B", "deviceGroups/0/devices/0", 2),
            Subnet("subnet-b", "subnets/1", 1),
            Subnet("subnet-a", "subnets/0", 0),
        });

        var deviceFiltered = result.Filter(descriptor =>
            descriptor.Kind != HardwarePageDescriptorKind.Device
            || string.Equals(descriptor.PublicIdentity, "HMI", StringComparison.Ordinal));
        var plcFiltered = result.Filter(descriptor =>
            descriptor.Kind != HardwarePageDescriptorKind.Device
            || string.Equals(descriptor.PublicIdentity, "PLC_B", StringComparison.Ordinal));

        Assert.Equal(
            new[] { "device:HMI:devices/1", "subnet:subnet-a:subnets/0", "subnet:subnet-b:subnets/1" },
            Describe(deviceFiltered.Descriptors));
        Assert.Equal(
            new[] { "device:PLC_B:deviceGroups/0/devices/0", "subnet:subnet-a:subnets/0", "subnet:subnet-b:subnets/1" },
            Describe(plcFiltered.Descriptors));
    }

    [Fact]
    public void Descriptors_PageWindowsSliceTheCombinedSequenceWithoutGapsOrDuplicates()
    {
        var result = new HardwarePageDescriptorSet(new[]
        {
            Device("A", "devices/0", 0),
            Device("B", "devices/1", 1),
            Device("C", "devices/2", 2),
            Subnet("s1", "subnets/0", 0),
            Subnet("s2", "subnets/1", 1),
        });

        var first = result.GetWindow(0, 2);
        var second = result.GetWindow(2, 2);
        var third = result.GetWindow(4, 2);

        Assert.Equal(new[] { "device:A:devices/0", "device:B:devices/1" }, Describe(first));
        Assert.Equal(new[] { "device:C:devices/2", "subnet:s1:subnets/0" }, Describe(second));
        Assert.Equal(new[] { "subnet:s2:subnets/1" }, Describe(third));
        Assert.Equal(Describe(result.Descriptors), Describe(first.Concat(second).Concat(third)));
    }

    private static HardwarePageDescriptor Device(string name, string locator, int sourceOrder)
        => new(HardwarePageDescriptorKind.Device, name, locator, sourceOrder);

    private static HardwarePageDescriptor Subnet(string subnetId, string locator, int sourceOrder)
        => new(HardwarePageDescriptorKind.Subnet, subnetId, locator, sourceOrder);

    private static IReadOnlyList<string> Describe(IEnumerable<HardwarePageDescriptor> descriptors)
        => descriptors.Select(descriptor =>
            $"{descriptor.Kind.ToString().ToLowerInvariant()}:{descriptor.PublicIdentity}:{descriptor.StructuralLocator}").ToArray();
}
